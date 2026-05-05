using System.Linq.Expressions;

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

        Expression? result = null;
        for (int i = 0; i < keyBodies.Count; i++)
        {
            var keyExpr = keyBodies[i];
            var constant = Expression.Constant(lastKeyValues[i], keyExpr.Type);

            Expression? equalityChain = null;
            for (int j = 0; j < i; j++)
            {
                var eqExpr = keyBodies[j];
                var eqConst = Expression.Constant(lastKeyValues[j], eqExpr.Type);
                var equality = Expression.Equal(eqExpr, eqConst);
                equalityChain =
                    equalityChain is null
                        ? equality
                        : Expression.AndAlso(equalityChain, equality);
            }

            var comparison = descendings[i]
                ? Expression.LessThan(keyExpr, constant)
                : Expression.GreaterThan(keyExpr, constant);

            Expression condition =
                equalityChain is null
                    ? comparison
                    : Expression.AndAlso(equalityChain, comparison);

            result = result is null ? condition : Expression.OrElse(result, condition);
        }

        return result!;
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

    private sealed class ParameterRewriter(ParameterExpression source, ParameterExpression target)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == source ? target : base.VisitParameter(node);
    }

    private sealed class NodeReplacer(Expression target, Expression replacement) : ExpressionVisitor
    {
        public override Expression? Visit(Expression? node) =>
            node == target ? replacement : base.Visit(node);
    }
}
