using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Cursor.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Cursor;

/// <summary>
/// Provides extension methods for Entity Framework Core queries to implement cursor-based pagination.
/// </summary>
public static partial class CursorPaginationExtensions
{
    private static readonly ConditionalWeakTable<Expression, CursorPageInfo> _cursorPageInfo = [];

    /// <summary>
    /// Applies cursor-based pagination to the query using a single key in ascending order.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <typeparam name="TKey">The type of the key used for pagination.</typeparam>
    /// <param name="query">The IQueryable to paginate.</param>
    /// <param name="keySelector">An expression that selects the key property to use for pagination (e.g., x => x.Id).</param>
    /// <param name="limit">The maximum number of items to return per page.</param>
    /// <param name="cursor">Optional. The cursor from a previous page to continue pagination from.</param>
    /// <param name="options">Options to control the behavior of the cursor pagination.</param>
    /// <returns>An <see cref="IQueryable{T}"/> with cursor filtering, ordering, and take applied. Call <see cref="ToCursorPageAsync{T}(IQueryable{T}, bool, CancellationToken)"/> to materialize the results.</returns>
    /// <remarks>
    /// This method applies Where (if cursor is provided), OrderBy, and Take to the query.
    /// You can apply additional LINQ operators such as <c>.Select()</c> before calling <see cref="ToCursorPageAsync{T}(IQueryable{T}, bool, CancellationToken)"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// var page = await dbContext.Users
    ///     .CursorPage(x => x.Id, limit: 20, cursor: previousCursor)
    ///     .Select(x => new UserDto { Id = x.Id, Name = x.Name })
    ///     .ToCursorPageAsync();
    /// </code>
    /// </example>
    public static IQueryable<T> CursorPage<T, TKey>(
        this IQueryable<T> query,
        Expression<Func<T, TKey>> keySelector,
        int limit,
        string? cursor = null,
        CursorOptions? options = null
    )
        where TKey : notnull, IComparable<TKey> =>
        CursorPageCore(query, keySelector, limit, cursor, options, descending: false);

    /// <summary>
    /// Applies cursor-based pagination to the query using a single key in descending order.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <typeparam name="TKey">The type of the key used for pagination.</typeparam>
    /// <param name="query">The IQueryable to paginate.</param>
    /// <param name="keySelector">An expression that selects the key property to use for pagination (e.g., x => x.CreatedAt).</param>
    /// <param name="limit">The maximum number of items to return per page.</param>
    /// <param name="cursor">Optional. The cursor from a previous page to continue pagination from.</param>
    /// <param name="options">Options to control the behavior of the cursor pagination.</param>
    /// <returns>An <see cref="IQueryable{T}"/> with cursor filtering, descending ordering, and take applied. Call <see cref="ToCursorPageAsync{T}(IQueryable{T}, bool, CancellationToken)"/> to materialize the results.</returns>
    /// <remarks>
    /// This method applies Where (if cursor is provided), OrderByDescending, and Take to the query.
    /// This is useful for retrieving the most recent items first (e.g., sorting by creation date descending).
    /// You can apply additional LINQ operators such as <c>.Select()</c> before calling <see cref="ToCursorPageAsync{T}(IQueryable{T}, bool, CancellationToken)"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// var page = await dbContext.Posts
    ///     .CursorPageDescending(x => x.CreatedAt, limit: 20)
    ///     .ToCursorPageAsync();
    /// </code>
    /// </example>
    public static IQueryable<T> CursorPageDescending<T, TKey>(
        this IQueryable<T> query,
        Expression<Func<T, TKey>> keySelector,
        int limit,
        string? cursor = null,
        CursorOptions? options = null
    )
        where TKey : notnull, IComparable<TKey> =>
        CursorPageCore(query, keySelector, limit, cursor, options, descending: true);

    /// <summary>
    /// Applies cursor-based pagination to the query using a compound key (multiple properties) in ascending order.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="query">The IQueryable to paginate.</param>
    /// <param name="keySelector">An expression that selects multiple key properties using an anonymous type or tuple (e.g., x => new { x.Category, x.Id } or x => (x.Category, x.Id)).</param>
    /// <param name="limit">The maximum number of items to return per page.</param>
    /// <param name="cursor">Optional. The cursor from a previous page to continue pagination from.</param>
    /// <param name="options">Options to control the behavior of the cursor pagination.</param>
    /// <returns>An <see cref="IQueryable{T}"/> with cursor filtering, compound ordering, and take applied. Call <see cref="ToCursorPageAsync{T}(IQueryable{T}, bool, CancellationToken)"/> to materialize the results.</returns>
    /// <remarks>
    /// This method applies Where (if cursor is provided), compound OrderBy/ThenBy, and Take to the query.
    /// The compound key is treated as a lexicographic ordering (first key takes precedence, then second, etc.).
    /// You can apply additional LINQ operators such as <c>.Select()</c> before calling <see cref="ToCursorPageAsync{T}(IQueryable{T}, bool, CancellationToken)"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// var page = await dbContext.Products
    ///     .CursorPage(x => new { x.Category, x.Id }, limit: 20)
    ///     .Select(x => new ProductDto { Id = x.Id })
    ///     .ToCursorPageAsync();
    /// </code>
    /// </example>
    public static IQueryable<T> CursorPage<T>(
        this IQueryable<T> query,
        Expression<Func<T, object>> keySelector,
        int limit,
        string? cursor = null,
        CursorOptions? options = null
    ) => CursorPageCompoundCore(query, keySelector, limit, cursor, options, descending: false);

    /// <summary>
    /// Applies cursor-based pagination to the query using a compound key (multiple properties) in descending order.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="query">The IQueryable to paginate.</param>
    /// <param name="keySelector">An expression that selects multiple key properties using an anonymous type or tuple (e.g., x => new { x.Category, x.Priority } or x => (x.Category, x.Priority)).</param>
    /// <param name="limit">The maximum number of items to return per page.</param>
    /// <param name="cursor">Optional. The cursor from a previous page to continue pagination from.</param>
    /// <param name="options">Options to control the behavior of the cursor pagination.</param>
    /// <returns>An <see cref="IQueryable{T}"/> with cursor filtering, compound descending ordering, and take applied. Call <see cref="ToCursorPageAsync{T}(IQueryable{T}, bool, CancellationToken)"/> to materialize the results.</returns>
    /// <remarks>
    /// This method applies Where (if cursor is provided), compound OrderByDescending/ThenByDescending, and Take to the query.
    /// All properties in the compound key will be ordered in descending order.
    /// You can apply additional LINQ operators such as <c>.Select()</c> before calling <see cref="ToCursorPageAsync{T}(IQueryable{T}, bool, CancellationToken)"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// var page = await dbContext.Tasks
    ///     .CursorPageDescending(x => new { x.Priority, x.CreatedAt }, limit: 20)
    ///     .ToCursorPageAsync();
    /// </code>
    /// </example>
    public static IQueryable<T> CursorPageDescending<T>(
        this IQueryable<T> query,
        Expression<Func<T, object>> keySelector,
        int limit,
        string? cursor = null,
        CursorOptions? options = null
    ) => CursorPageCompoundCore(query, keySelector, limit, cursor, options, descending: true);

    /// <summary>
    /// Materializes a cursor-paginated query that was previously prepared with
    /// <see cref="CursorPage{T, TKey}"/> / <see cref="CursorPageDescending{T, TKey}"/> or their compound-key overloads.
    /// </summary>
    /// <typeparam name="T">The type of items in the result set (may be a projection of the original entity type).</typeparam>
    /// <param name="query">The query previously prepared with a CursorPage method, optionally followed by additional operators such as <c>.Select()</c>.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation, containing a <see cref="CursorPage{T}"/> with the results.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the query was not prepared with a CursorPage method.</exception>
    /// <example>
    /// <code>
    /// var page = await dbContext.Users
    ///     .CursorPage(x => x.Id, limit: 20, cursor: previousCursor)
    ///     .Select(x => new UserDto { Id = x.Id, Name = x.Name })
    ///     .ToCursorPageAsync();
    /// </code>
    /// </example>
    public static async Task<CursorPage<T>> ToCursorPageAsync<T>(
        this IQueryable<T> query,
        CancellationToken cancellationToken = default
    )
    {
        var info =
            FindCursorPageInfo(query.Expression)
            ?? throw new InvalidOperationException(
                "ToCursorPageAsync() requires CursorPage() or CursorPageDescending() to be called first on the query."
            );

        long? totalCount = null;
        if (info.Options.ComputeTotalCount)
        {
            totalCount = await CountOriginalQueryAsync(
                query.Provider,
                info.OriginalQueryExpression,
                info.SourceType,
                cancellationToken
            );
        }

        var limit = info.Limit;
        var items = await query.ToListAsync(cancellationToken);
        var hasMore = items.Count > limit;

        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        string? nextCursor = null;
        if ((hasMore || items.Count == int.MaxValue) && items.Count > 0)
        {
            if (typeof(T) == info.SourceType)
            {
                nextCursor = info.EncodeCursorFromSourceEntity(items[^1]!);
            }
            else
            {
                var entity = await SingleSourceEntityAsync(
                    query.Provider,
                    info.OrderedQueryExpression,
                    info.SourceType,
                    items.Count - 1,
                    cancellationToken
                );
                nextCursor = info.EncodeCursorFromSourceEntity(entity);
            }
        }

        return new CursorPage<T>
        {
            Items = items,
            NextCursor = nextCursor,
            TotalCount = totalCount,
        };
    }

    private static IQueryable<T> CursorPageCore<T, TKey>(
        IQueryable<T> query,
        Expression<Func<T, TKey>> keySelector,
        int limit,
        string? cursor,
        CursorOptions? options,
        bool descending
    )
        where TKey : notnull, IComparable<TKey>
    {
        options ??= CursorOptions.Default;

        var originalExpression = query.Expression;
        var parameter = keySelector.Parameters[0];
        var keySelectorBody = keySelector.Body;

        if (cursor is not null)
        {
            var lastKey = options.CursorSerializer.DecodeCursor<TKey>(cursor);
            var lastKeyConstant = Expression.Constant(lastKey, typeof(TKey));

            var comparison = descending
                ? Expression.LessThan(keySelectorBody, lastKeyConstant)
                : Expression.GreaterThan(keySelectorBody, lastKeyConstant);

            var wherePredicate = Expression.Lambda<Func<T, bool>>(comparison, parameter);
            query = query.Where(wherePredicate);
        }

        query = descending ? query.OrderByDescending(keySelector) : query.OrderBy(keySelector);

        var orderedExpression = query.Expression;
        query = query.Take(limit < int.MaxValue ? limit + 1 : int.MaxValue);

        var compiledKeySelector = keySelector.Compile();
        _cursorPageInfo.AddOrUpdate(
            query.Expression,
            new CursorPageInfo
            {
                Limit = limit,
                Options = options,
                OriginalQueryExpression = originalExpression,
                SourceType = typeof(T),
                OrderedQueryExpression = orderedExpression,
                EncodeCursorFromSourceEntity = item =>
                    options.CursorSerializer.EncodeCursor(compiledKeySelector((T)item)),
            }
        );

        return query;
    }

    private static IQueryable<T> CursorPageCompoundCore<T>(
        IQueryable<T> query,
        Expression<Func<T, object>> keySelector,
        int limit,
        string? cursor,
        CursorOptions? options,
        bool descending
    )
    {
        options ??= CursorOptions.Default;

        var originalExpression = query.Expression;
        var parameter = keySelector.Parameters[0];

        // Unwrap Convert expression if present (happens with anonymous types)
        var keySelectorBody = keySelector.Body;
        if (keySelectorBody is UnaryExpression { NodeType: ExpressionType.Convert } unary)
        {
            keySelectorBody = unary.Operand;
        }

        // Detect compound key (anonymous type or tuple)
        if (
            !CompoundKeyHelpers.IsCompoundKey(keySelectorBody, out var keyProperties)
            || keyProperties == null
        )
        {
            throw new ArgumentException(
                "Key selector must return an anonymous type or tuple for compound keys, e.g., x => new { x.Key1, x.Key2 } or x => (x.Key1, x.Key2)",
                nameof(keySelector)
            );
        }

        if (cursor is not null)
        {
            var keyPropertyTypes = keyProperties.Select(k => k.Type).ToList();
            var lastKeyValues = options.CursorSerializer.DecodeCompoundCursor(
                cursor,
                keyPropertyTypes
            );
            var comparison = CompoundKeyHelpers.BuildCompoundComparison(
                keyProperties,
                lastKeyValues,
                descending
            );
            var wherePredicate = Expression.Lambda<Func<T, bool>>(comparison, parameter);
            query = query.Where(wherePredicate);
        }

        query = CompoundKeyHelpers.ApplyCompoundOrdering(
            query,
            keyProperties,
            parameter,
            descending
        );

        var orderedExpression = query.Expression;
        query = query.Take(limit < int.MaxValue ? limit + 1 : int.MaxValue);

        var compiledKeySelector = keySelector.Compile();
        _cursorPageInfo.AddOrUpdate(
            query.Expression,
            new CursorPageInfo
            {
                Limit = limit,
                Options = options,
                OriginalQueryExpression = originalExpression,
                SourceType = typeof(T),
                OrderedQueryExpression = orderedExpression,
                EncodeCursorFromSourceEntity = item =>
                {
                    var key = compiledKeySelector((T)item);
                    var keyValues = CompoundKeyHelpers.ExtractKeyValues(key);
                    return options.CursorSerializer.EncodeCompoundCursor(keyValues);
                },
            }
        );

        return query;
    }

    private static CursorPageInfo? FindCursorPageInfo(Expression expression)
    {
        if (_cursorPageInfo.TryGetValue(expression, out var info))
            return info;

        if (expression is MethodCallExpression methodCall)
        {
            foreach (var arg in methodCall.Arguments)
            {
                var found = FindCursorPageInfo(arg);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    private static Task<long> CountOriginalQueryAsync(
        IQueryProvider provider,
        Expression expression,
        Type sourceType,
        CancellationToken cancellationToken
    )
    {
        return (Task<long>)
            typeof(CursorPaginationExtensions)
                .GetMethod(
                    nameof(CountQueryCoreAsync),
                    BindingFlags.NonPublic | BindingFlags.Static
                )!
                .MakeGenericMethod(sourceType)
                .Invoke(null, [provider, expression, cancellationToken])!;
    }

    private static Task<long> CountQueryCoreAsync<TSource>(
        IQueryProvider provider,
        Expression expression,
        CancellationToken cancellationToken
    )
    {
        var query = provider.CreateQuery<TSource>(expression);
        return query.LongCountAsync(cancellationToken);
    }

    private static Task<object> SingleSourceEntityAsync(
        IQueryProvider provider,
        Expression orderedQueryExpression,
        Type sourceType,
        int skip,
        CancellationToken cancellationToken
    )
    {
        var skipExpr = Expression.Call(
            typeof(Queryable),
            nameof(Enumerable.Skip),
            [sourceType],
            orderedQueryExpression,
            Expression.Constant(skip)
        );
        var takeOneExpr = Expression.Call(
            typeof(Queryable),
            nameof(Enumerable.Take),
            [sourceType],
            skipExpr,
            Expression.Constant(1)
        );
        return (Task<object>)
            typeof(CursorPaginationExtensions)
                .GetMethod(
                    nameof(SingleSourceEntityCoreAsync),
                    BindingFlags.NonPublic | BindingFlags.Static
                )!
                .MakeGenericMethod(sourceType)
                .Invoke(null, [provider, takeOneExpr, cancellationToken])!;
    }

    private static async Task<object> SingleSourceEntityCoreAsync<TSource>(
        IQueryProvider provider,
        Expression expression,
        CancellationToken cancellationToken
    )
    {
        var query = provider.CreateQuery<TSource>(expression);
        return (await query.SingleAsync(cancellationToken))!;
    }

    private sealed class CursorPageInfo
    {
        public required int Limit { get; init; }
        public required CursorOptions Options { get; init; }
        public required Expression OriginalQueryExpression { get; init; }
        public required Type SourceType { get; init; }
        public required Expression OrderedQueryExpression { get; init; }
        public required Func<object, string> EncodeCursorFromSourceEntity { get; init; }
    }
}
