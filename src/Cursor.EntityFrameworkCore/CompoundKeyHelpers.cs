using System.Linq.Expressions;

namespace Cursor.EntityFrameworkCore;

internal static class CompoundKeyHelpers
{
    public static bool IsCompoundKey(Expression expression, out List<Expression>? keyProperties)
    {
        keyProperties = null;

        // Check for anonymous type (NewExpression with members)
        if (expression is NewExpression newExpr && newExpr.Members != null)
        {
            keyProperties = newExpr.Arguments.ToList();
            return true;
        }

        // Check for tuple (NewExpression without explicit members)
        if (
            expression is NewExpression tupleExpr
            && tupleExpr.Type.FullName?.StartsWith("System.ValueTuple`") == true
        )
        {
            keyProperties = tupleExpr.Arguments.ToList();
            return true;
        }

        return false;
    }

    public static Expression BuildCompoundComparison(
        List<Expression> keyProperties,
        List<object?> lastKeyValues,
        bool descending
    )
    {
        if (lastKeyValues.Count != keyProperties.Count)
        {
            throw new InvalidOperationException("Cursor key count mismatch");
        }

        // Build: (key1 op last1) OR (key1 == last1 AND (key2 op last2)) OR (key1 == last1 AND key2 == last2 AND key3 op last3) ...
        Expression? result = null;

        for (int i = 0; i < keyProperties.Count; i++)
        {
            var keyExpr = keyProperties[i];
            var lastValue = lastKeyValues[i];
            var constant = Expression.Constant(lastValue, keyExpr.Type);

            // Build equality conditions for all PREVIOUS keys (0 to i-1)
            Expression? equalityChain = null;
            for (int j = 0; j < i; j++)
            {
                var eqKeyExpr = keyProperties[j];
                var eqConstant = Expression.Constant(lastKeyValues[j], eqKeyExpr.Type);
                var equality = Expression.Equal(eqKeyExpr, eqConstant);

                equalityChain =
                    equalityChain == null ? equality : Expression.AndAlso(equalityChain, equality);
            }

            // Current key comparison
            var comparison = descending
                ? Expression.LessThan(keyExpr, constant)
                : Expression.GreaterThan(keyExpr, constant);

            // Combine: previous keys equal AND current key compared
            Expression condition =
                equalityChain == null ? comparison : Expression.AndAlso(equalityChain, comparison);

            result = result == null ? condition : Expression.OrElse(result, condition);
        }

        return result!;
    }

    public static IQueryable<T> ApplyCompoundOrdering<T>(
        IQueryable<T> query,
        List<Expression> keyProperties,
        ParameterExpression parameter,
        bool descending
    )
    {
        for (int i = 0; i < keyProperties.Count; i++)
        {
            var keyExpr = keyProperties[i];
            var lambda = Expression.Lambda(keyExpr, parameter);

            var method =
                i == 0
                    ? (
                        descending
                            ? nameof(Enumerable.OrderByDescending)
                            : nameof(Enumerable.OrderBy)
                    )
                    : (
                        descending ? nameof(Enumerable.ThenByDescending) : nameof(Enumerable.ThenBy)
                    );

            var orderByMethod = typeof(Queryable)
                .GetMethods()
                .First(m => m.Name == method && m.GetParameters().Length == 2)
                .MakeGenericMethod(typeof(T), keyExpr.Type);

            query = (IQueryable<T>)orderByMethod.Invoke(null, [query, lambda])!;
        }

        return query;
    }

    public static List<object?> ExtractKeyValues(object obj)
    {
        var values = new List<object?>();
        var type = obj.GetType();

        // Check if it's a tuple
        if (type.FullName?.StartsWith("System.ValueTuple`") == true)
        {
            var fields = type.GetFields();
            foreach (var field in fields)
            {
                values.Add(field.GetValue(obj));
            }
        }
        // Otherwise assume it's an anonymous type
        else
        {
            var properties = type.GetProperties();
            foreach (var property in properties)
            {
                values.Add(property.GetValue(obj));
            }
        }

        return values;
    }
}
