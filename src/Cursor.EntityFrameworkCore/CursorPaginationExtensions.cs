using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Cursor;

public static class CursorPaginationExtensions
{
    // Simple key overload
    public static Task<CursorPage<T>> ToCursorPageAsync<T, TKey>(
        this IQueryable<T> query,
        Expression<Func<T, TKey>> keySelector,
        int limit,
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull, IComparable<TKey> =>
        ToCursorPageSimpleAsync(
            query,
            keySelector,
            limit,
            cursor,
            descending: false,
            cancellationToken
        );

    public static Task<CursorPage<T>> ToCursorPageDescendingAsync<T, TKey>(
        this IQueryable<T> query,
        Expression<Func<T, TKey>> keySelector,
        int limit,
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull, IComparable<TKey> =>
        ToCursorPageSimpleAsync(
            query,
            keySelector,
            limit,
            cursor,
            descending: true,
            cancellationToken
        );

    // Compound key overload (anonymous types, tuples, etc.)
    public static Task<CursorPage<T>> ToCursorPageAsync<T>(
        this IQueryable<T> query,
        Expression<Func<T, object>> keySelector,
        int limit,
        string? cursor = null,
        CancellationToken cancellationToken = default
    ) =>
        ToCursorPageCompoundAsync(
            query,
            keySelector,
            limit,
            cursor,
            descending: false,
            cancellationToken
        );

    public static Task<CursorPage<T>> ToCursorPageDescendingAsync<T>(
        this IQueryable<T> query,
        Expression<Func<T, object>> keySelector,
        int limit,
        string? cursor = null,
        CancellationToken cancellationToken = default
    ) =>
        ToCursorPageCompoundAsync(
            query,
            keySelector,
            limit,
            cursor,
            descending: true,
            cancellationToken
        );

    private static async Task<CursorPage<T>> ToCursorPageSimpleAsync<T, TKey>(
        IQueryable<T> query,
        Expression<Func<T, TKey>> keySelector,
        int limit,
        string? cursor,
        bool descending,
        CancellationToken cancellationToken
    )
        where TKey : notnull, IComparable<TKey>
    {
        var parameter = keySelector.Parameters[0];
        var keySelectorBody = keySelector.Body;

        // Only apply cursor filter if cursor is provided
        if (cursor is not null)
        {
            var lastKey = DecodeCursor<TKey>(cursor);
            var lastKeyConstant = Expression.Constant(lastKey, typeof(TKey));

            // Use LessThan for descending, GreaterThan for ascending
            var comparison = descending
                ? Expression.LessThan(keySelectorBody, lastKeyConstant)
                : Expression.GreaterThan(keySelectorBody, lastKeyConstant);

            var wherePredicate = Expression.Lambda<Func<T, bool>>(comparison, parameter);
            query = query.Where(wherePredicate);
        }

        // Apply ordering
        query = descending ? query.OrderByDescending(keySelector) : query.OrderBy(keySelector);

        query = query.Take(limit + 1);

        var items = await query.ToListAsync(cancellationToken);
        var hasMore = items.Count > limit;

        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        var nextCursor =
            hasMore && items.Count > 0 ? EncodeCursor(keySelector.Compile()(items[^1])) : null;

        return new CursorPage<T> { Items = items, NextCursor = nextCursor };
    }

    private static async Task<CursorPage<T>> ToCursorPageCompoundAsync<T>(
        IQueryable<T> query,
        Expression<Func<T, object>> keySelector,
        int limit,
        string? cursor,
        bool descending,
        CancellationToken cancellationToken
    )
    {
        var parameter = keySelector.Parameters[0];

        // Unwrap Convert expression if present (happens with anonymous types)
        var keySelectorBody = keySelector.Body;
        if (keySelectorBody is UnaryExpression { NodeType: ExpressionType.Convert } unary)
        {
            keySelectorBody = unary.Operand;
        }

        // Detect compound key (anonymous type or tuple)
        if (!IsCompoundKey(keySelectorBody, out var keyProperties) || keyProperties == null)
        {
            throw new ArgumentException(
                "Key selector must return an anonymous type or tuple for compound keys, e.g., x => new { x.Key1, x.Key2 } or x => (x.Key1, x.Key2)",
                nameof(keySelector)
            );
        }

        // Apply cursor filter for compound keys
        if (cursor is not null)
        {
            var lastKeyValues = DecodeCompoundCursor(cursor, keyProperties);
            var comparison = BuildCompoundComparison(keyProperties, lastKeyValues, descending);

            var wherePredicate = Expression.Lambda<Func<T, bool>>(comparison, parameter);
            query = query.Where(wherePredicate);
        }

        // Apply ordering
        query = ApplyCompoundOrdering(query, keyProperties, parameter, descending);

        query = query.Take(limit + 1);

        var items = await query.ToListAsync(cancellationToken);
        var hasMore = items.Count > limit;

        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        var nextCursor =
            hasMore && items.Count > 0
                ? EncodeCompoundCursor(keySelector.Compile()(items[^1]))
                : null;

        return new CursorPage<T> { Items = items, NextCursor = nextCursor };
    }

    private static bool IsCompoundKey(Expression expression, out List<Expression>? keyProperties)
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

    private static Expression BuildCompoundComparison(
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

    private static IQueryable<T> ApplyCompoundOrdering<T>(
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
                    ? (descending ? "OrderByDescending" : "OrderBy")
                    : (descending ? "ThenByDescending" : "ThenBy");

            var orderByMethod = typeof(Queryable)
                .GetMethods()
                .First(m => m.Name == method && m.GetParameters().Length == 2)
                .MakeGenericMethod(typeof(T), keyExpr.Type);

            query = (IQueryable<T>)orderByMethod.Invoke(null, new object[] { query, lambda })!;
        }

        return query;
    }

    private static List<object?> ExtractKeyValues(object obj)
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

    private static string EncodeCursor<TKey>(TKey key)
        where TKey : notnull
    {
        var json = JsonSerializer.Serialize(key);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static TKey DecodeCursor<TKey>(string cursor)
        where TKey : notnull
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        return JsonSerializer.Deserialize<TKey>(json)!;
    }

    private static string EncodeCompoundCursor(object key)
    {
        // Extract the values as a list and serialize as array
        var values = ExtractKeyValues(key);
        var json = JsonSerializer.Serialize(values);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static List<object?> DecodeCompoundCursor(string cursor, List<Expression> keyProperties)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        var elements = JsonSerializer.Deserialize<JsonElement[]>(json)!;

        if (elements.Length != keyProperties.Count)
        {
            throw new InvalidOperationException("Cursor key count mismatch");
        }

        var values = new List<object?>();
        for (int i = 0; i < keyProperties.Count; i++)
        {
            var targetType = keyProperties[i].Type;
            var jsonElement = elements[i];
            var value = jsonElement.Deserialize(targetType);
            values.Add(value);
        }

        return values;
    }
}
