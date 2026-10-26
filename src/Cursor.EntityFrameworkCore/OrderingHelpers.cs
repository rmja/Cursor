using System.Linq.Expressions;
using System.Reflection;

namespace Cursor.EntityFrameworkCore;

internal readonly record struct OrderingKey(LambdaExpression KeySelector, bool Descending)
{
    public Type KeyType => KeySelector.Body.Type;
}

/// <summary>
/// Carries the result of inspecting a query expression tree for its trailing
/// ordering chain.
/// </summary>
/// <param name="OrderingChainNode">
/// The outermost <c>OrderBy</c>/<c>OrderByDescending</c>/<c>ThenBy</c>/<c>ThenByDescending</c>
/// node in the user's expression tree (i.e. the topmost ordering operator).
/// The cursor <c>Where</c> filter is wedged in immediately above this node.
/// </param>
/// <param name="Keys">The ordering keys, in primary-first order.</param>
/// <param name="SourceType">
/// The element type of the ordered query (the entity type, before any
/// projection that may sit on top of the ordering chain).
/// </param>
internal sealed record OrderingInfo(
    Expression OrderingChainNode,
    IReadOnlyList<OrderingKey> Keys,
    Type SourceType
);

/// <summary>
/// Helpers for inspecting an <see cref="IQueryable"/> expression tree to extract
/// its trailing <c>OrderBy</c> / <c>OrderByDescending</c> / <c>ThenBy</c> / <c>ThenByDescending</c>
/// chain and for building the cursor comparison filter from those keys.
/// </summary>
internal static class OrderingHelpers
{
    private static readonly MethodInfo StringCompareMethod = typeof(string).GetMethod(
        nameof(string.Compare),
        [typeof(string), typeof(string)]
    )!;

    /// <summary>
    /// Walks the outermost method-call chain of <paramref name="expression"/>, allowing
    /// passthrough projection operators (<c>Select</c>) to sit above the ordering chain,
    /// and returns information about the ordering keys.
    /// </summary>
    /// <returns><c>null</c> when no ordering operator is found.</returns>
    /// <remarks>
    /// This supports the common pattern of <c>query.OrderBy(...).Select(...).ToCursorPageAsync(...)</c>
    /// where a projection sits between the ordering and the pagination call. The cursor filter
    /// is injected at the source-entity level (immediately above the outermost ordering node)
    /// so it applies before any projection.
    /// </remarks>
    public static OrderingInfo? ExtractOrderingInfo(Expression expression)
    {
        var current = expression;

        while (current is MethodCallExpression mc && mc.Method.DeclaringType == typeof(Queryable))
        {
            var name = mc.Method.Name;
            var isOrderBy =
                name == nameof(Queryable.OrderBy) || name == nameof(Queryable.OrderByDescending);
            var isThenBy =
                name == nameof(Queryable.ThenBy) || name == nameof(Queryable.ThenByDescending);

            if (isOrderBy || isThenBy)
            {
                var keys = ExtractOrderingChain(current);
                if (keys.Count == 0)
                    return null;

                // For OrderBy/ThenBy, the first generic argument is TSource (the element type
                // of the ordered query before any projection on top).
                var sourceType = mc.Method.GetGenericArguments()[0];
                return new OrderingInfo(current, keys, sourceType);
            }

            // Allow Select projections above the ordering chain. The cursor filter will be
            // injected below them, at the source-entity level.
            if (name == nameof(Queryable.Select))
            {
                current = mc.Arguments[0];
                continue;
            }

            // Anything else (Where, Distinct, GroupBy, Take, ...) on the outermost chain
            // is unsupported: it either changes the row set or can't sit above an ordering
            // chain in a way we know how to safely rewrite.
            return null;
        }

        return null;
    }

    /// <summary>
    /// Returns a copy of <paramref name="tree"/> in which the node at the same reference
    /// as <paramref name="target"/> is replaced with <paramref name="replacement"/>.
    /// </summary>
    public static Expression ReplaceNode(
        Expression tree,
        Expression target,
        Expression replacement
    ) => new NodeReplacer(target, replacement).Visit(tree)!;

    /// <summary>
    /// Rewrites each key selector body so that all bodies share <paramref name="targetParameter"/>.
    /// User-supplied lambdas have their own parameter instances; we need a single shared
    /// parameter to assemble a combined <c>Where</c> predicate.
    /// </summary>
    public static List<Expression> RewriteKeyBodies(
        IReadOnlyList<OrderingKey> keys,
        ParameterExpression targetParameter
    )
    {
        var bodies = new List<Expression>(keys.Count);
        foreach (var key in keys)
        {
            var originalParam = key.KeySelector.Parameters[0];
            var rewriter = new ParameterRewriter(originalParam, targetParameter);
            bodies.Add(rewriter.Visit(key.KeySelector.Body)!);
        }
        return bodies;
    }

    /// <summary>
    /// Builds the lexicographic cursor-comparison expression for a list of key bodies,
    /// taking each key's direction (ascending or descending) into account.
    /// </summary>
    /// <remarks>
    /// The result has the shape:
    /// <c>(k0 op0 v0) OR (k0 == v0 AND k1 op1 v1) OR (k0 == v0 AND k1 == v1 AND k2 op2 v2) OR ...</c>
    /// where <c>op_i</c> is <c>&gt;</c> when key <c>i</c> is ascending and <c>&lt;</c> when descending.
    /// </remarks>
    public static Expression BuildCursorComparison(
        IReadOnlyList<Expression> keyBodies,
        IReadOnlyList<bool> descendings,
        IReadOnlyList<object?> lastKeyValues
    )
    {
        if (lastKeyValues.Count != keyBodies.Count)
        {
            throw new InvalidOperationException("Cursor key count mismatch");
        }

        // One node per key, shared by every place the key's value is compared against, so each
        // value becomes a single parameter.
        var values = new Expression[keyBodies.Count];
        for (int i = 0; i < keyBodies.Count; i++)
        {
            values[i] = CursorParameter(lastKeyValues[i], keyBodies[i].Type);
        }

        Expression? result = null;
        for (int i = 0; i < keyBodies.Count; i++)
        {
            var keyExpr = keyBodies[i];

            Expression? equalityChain = null;
            for (int j = 0; j < i; j++)
            {
                var eqExpr = keyBodies[j];
                var equality = Expression.Equal(eqExpr, values[j]);
                equalityChain =
                    equalityChain is null
                        ? equality
                        : Expression.AndAlso(equalityChain, equality);
            }

            var comparison = MakeRelationalComparison(keyExpr, values[i], descendings[i]);

            Expression condition =
                equalityChain is null
                    ? comparison
                    : Expression.AndAlso(equalityChain, comparison);

            result = result is null ? condition : Expression.OrElse(result, condition);
        }

        return result!;
    }

    /// <summary>
    /// The expression for a value decoded from a cursor, which EF Core sends to the database as a
    /// parameter.
    /// </summary>
    /// <remarks>
    /// EF Core writes a <see cref="ConstantExpression"/> into the SQL as a literal, but turns a
    /// member read from a constant object - the shape the compiler gives a captured local
    /// variable - into a parameter. With literals every page would be a different SQL statement:
    /// EF Core would compile the query again for each cursor, and the database would cache a
    /// separate plan for each one. So the value is read from a <see cref="CursorValue{T}"/>
    /// holder instead.
    /// </remarks>
    private static Expression CursorParameter(object? value, Type type)
    {
        // Rejects a value that does not fit the key's type, as a plain constant would.
        var checkedValue = Expression.Constant(value, type).Value;

        var holderType = typeof(CursorValue<>).MakeGenericType(type);
        var holder = Activator.CreateInstance(holderType, [checkedValue])!;
        return Expression.Field(
            Expression.Constant(holder),
            holderType.GetField(nameof(CursorValue<object>.cursor))!
        );
    }

    private sealed class CursorValue<T>(T value)
    {
        // EF Core names the parameter after this field, so the SQL reads "@cursor".
#pragma warning disable IDE1006
        public readonly T cursor = value;
#pragma warning restore IDE1006
    }

    /// <summary>
    /// Builds <c>key &gt; value</c>, or <c>key &lt; value</c> when the key is descending.
    /// </summary>
    /// <remarks>
    /// <see cref="Expression.GreaterThan(Expression, Expression)"/> only accepts the numeric
    /// primitives and types that define an <c>op_GreaterThan</c> operator. <see cref="string"/>,
    /// <see cref="Guid"/>, <see cref="bool"/> and enums define no such operator - C# compares them
    /// through other means - so building the comparison directly throws
    /// <see cref="InvalidOperationException"/> for those. Enums are compared as their underlying
    /// integral type, which is what the column stores anyway, and the rest go through
    /// <see cref="string.Compare(string, string)"/> or <c>CompareTo</c>, which relational providers
    /// translate back into a plain SQL comparison.
    /// </remarks>
    private static Expression MakeRelationalComparison(
        Expression key,
        Expression value,
        bool descending
    )
    {
        (key, value) = ConvertEnumToUnderlyingType(key, value);

        if (!HasGreaterThanOperator(key.Type))
        {
            key = MakeCompareCall(key, value);
            value = Expression.Constant(0);
        }

        return descending ? Expression.LessThan(key, value) : Expression.GreaterThan(key, value);
    }

    private static (Expression Key, Expression Value) ConvertEnumToUnderlyingType(
        Expression key,
        Expression value
    )
    {
        var keyType = Nullable.GetUnderlyingType(key.Type) ?? key.Type;
        if (!keyType.IsEnum)
        {
            return (key, value);
        }

        var underlyingType = Enum.GetUnderlyingType(keyType);
        if (keyType != key.Type)
        {
            underlyingType = typeof(Nullable<>).MakeGenericType(underlyingType);
        }

        return (Expression.Convert(key, underlyingType), Expression.Convert(value, underlyingType));
    }

    /// <summary>
    /// Whether <see cref="Expression.GreaterThan(Expression, Expression)"/> can compare
    /// <paramref name="type"/> on its own. It can for the numeric primitives, and otherwise only
    /// when the type defines the operator - which <see cref="decimal"/> and the date and time types
    /// do, and <see cref="string"/>, <see cref="Guid"/> and <see cref="bool"/> do not.
    /// </summary>
    private static bool HasGreaterThanOperator(Type type)
    {
        var nonNullableType = Nullable.GetUnderlyingType(type) ?? type;

        return Type.GetTypeCode(nonNullableType) switch
        {
            TypeCode.Char
            or TypeCode.SByte
            or TypeCode.Byte
            or TypeCode.Int16
            or TypeCode.UInt16
            or TypeCode.Int32
            or TypeCode.UInt32
            or TypeCode.Int64
            or TypeCode.UInt64
            or TypeCode.Single
            or TypeCode.Double => !nonNullableType.IsEnum,
            _ => nonNullableType.GetMethod(
                "op_GreaterThan",
                BindingFlags.Public | BindingFlags.Static
            )
                is not null,
        };
    }

    private static Expression MakeCompareCall(Expression key, Expression value)
    {
        if (key.Type == typeof(string))
        {
            return Expression.Call(StringCompareMethod, key, value);
        }

        var compareTo = key.Type.GetMethod(nameof(IComparable.CompareTo), [key.Type]);
        if (compareTo is null || compareTo.ReturnType != typeof(int))
        {
            throw new InvalidOperationException(
                $"Cursor pagination cannot order by '{key.Type}': it defines neither a "
                    + $"greater-than operator nor a CompareTo({key.Type}) method. Order by a key "
                    + "that does, such as the underlying value of a wrapper type."
            );
        }

        return Expression.Call(key, compareTo, value);
    }

    /// <summary>
    /// Walks a node that is known to be an <c>OrderBy</c>/<c>OrderByDescending</c>/
    /// <c>ThenBy</c>/<c>ThenByDescending</c> call and collects the keys in primary-first order.
    /// </summary>
    private static List<OrderingKey> ExtractOrderingChain(Expression orderingNode)
    {
        var keys = new List<OrderingKey>();
        var current = orderingNode;

        while (current is MethodCallExpression mc && mc.Method.DeclaringType == typeof(Queryable))
        {
            var name = mc.Method.Name;
            var isOrderBy =
                name == nameof(Queryable.OrderBy) || name == nameof(Queryable.OrderByDescending);
            var isThenBy =
                name == nameof(Queryable.ThenBy) || name == nameof(Queryable.ThenByDescending);

            if (!isOrderBy && !isThenBy)
                break;

            var lambda = ExtractLambda(mc.Arguments[1]);
            var descending =
                name == nameof(Queryable.OrderByDescending)
                || name == nameof(Queryable.ThenByDescending);
            keys.Add(new OrderingKey(lambda, descending));

            if (isOrderBy)
            {
                // Reached the primary key; anything below is a previous ordering that
                // LINQ semantics consider replaced by this one.
                break;
            }

            current = mc.Arguments[0];
        }

        keys.Reverse();
        return keys;
    }

    private static LambdaExpression ExtractLambda(Expression expression)
    {
        if (
            expression is UnaryExpression
            {
                NodeType: ExpressionType.Quote,
                Operand: LambdaExpression quoted,
            }
        )
        {
            return quoted;
        }

        if (expression is LambdaExpression lambda)
        {
            return lambda;
        }

        throw new InvalidOperationException(
            $"Expected a lambda expression but got {expression.NodeType}."
        );
    }

    /// <summary>
    /// The body of <paramref name="selector"/> applied to <paramref name="argument"/>, with member
    /// accesses on an object initializer or anonymous type in <paramref name="argument"/> replaced by
    /// the assigned expression, so that composed projections stay translatable.
    /// </summary>
    public static Expression Inline(LambdaExpression selector, Expression argument)
    {
        var body = new ParameterRewriter(selector.Parameters[0], argument).Visit(selector.Body)!;
        return new MemberInitAccessSimplifier().Visit(body)!;
    }

    private sealed class ParameterRewriter(ParameterExpression source, Expression target)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == source ? target : base.VisitParameter(node);
    }

    private sealed class MemberInitAccessSimplifier : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node)
        {
            var instance = Visit(node.Expression);
            switch (instance)
            {
                case MemberInitExpression init:
                    foreach (var binding in init.Bindings)
                    {
                        if (
                            binding is MemberAssignment assignment
                            && SameMember(assignment.Member, node.Member)
                        )
                        {
                            return assignment.Expression;
                        }
                    }
                    break;
                case NewExpression { Members: { } members } created:
                    for (var i = 0; i < members.Count; i++)
                    {
                        if (SameMember(members[i], node.Member))
                        {
                            return created.Arguments[i];
                        }
                    }
                    break;
            }
            return node.Update(instance);
        }

        private static bool SameMember(MemberInfo a, MemberInfo b) =>
            a == b || (a.Name == b.Name && a.DeclaringType == b.DeclaringType);
    }

    private sealed class NodeReplacer(Expression target, Expression replacement) : ExpressionVisitor
    {
        public override Expression? Visit(Expression? node) =>
            node == target ? replacement : base.Visit(node);
    }
}
