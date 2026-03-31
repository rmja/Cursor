using System.Linq.Expressions;

namespace Cursor;

public static partial class CursorPaginationExtensions
{
    /// <summary>
    /// Converts an IQueryable to a cursor-paginated result set ordered by a single key in ascending order.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <typeparam name="TKey">The type of the key used for pagination.</typeparam>
    /// <param name="query">The IQueryable to paginate.</param>
    /// <param name="keySelector">An expression that selects the key property to use for pagination (e.g., x => x.Id).</param>
    /// <param name="limit">The maximum number of items to return per page.</param>
    /// <param name="cursor">Optional. The cursor from a previous page to continue pagination from.</param>
    /// <param name="computeTotalCount">Optional. Whether to compute the total count of items across all pages. This can be expensive for large datasets.</param>
    /// <param name="cancellationToken">Optional. A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation, containing a <see cref="CursorPage{T}"/> with the results.</returns>
    /// <remarks>
    /// This method applies ascending ordering based on the key selector. For descending order, use <see cref="ToCursorPageDescendingAsync{T, TKey}"/>.
    /// The cursor is an opaque Base64-encoded token that should be passed back to subsequent calls to retrieve the next page.
    /// </remarks>
    /// <example>
    /// <code>
    /// var page = await dbContext.Users
    ///     .ToCursorPageAsync(x => x.Id, limit: 20, cursor: null);
    /// </code>
    /// </example>
    public static Task<CursorPage<T>> ToCursorPageAsync<T, TKey>(
        this IQueryable<T> query,
        Expression<Func<T, TKey>> keySelector,
        int limit,
        string? cursor = null,
        bool computeTotalCount = false,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull, IComparable<TKey> =>
        query
            .CursorPage(keySelector, limit, cursor)
            .ToCursorPageAsync(computeTotalCount, cancellationToken);

    /// <summary>
    /// Converts an IQueryable to a cursor-paginated result set ordered by a single key in descending order.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <typeparam name="TKey">The type of the key used for pagination.</typeparam>
    /// <param name="query">The IQueryable to paginate.</param>
    /// <param name="keySelector">An expression that selects the key property to use for pagination (e.g., x => x.CreatedAt).</param>
    /// <param name="limit">The maximum number of items to return per page.</param>
    /// <param name="cursor">Optional. The cursor from a previous page to continue pagination from.</param>
    /// <param name="computeTotalCount">Optional. Whether to compute the total count of items across all pages. This can be expensive for large datasets.</param>
    /// <param name="cancellationToken">Optional. A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation, containing a <see cref="CursorPage{T}"/> with the results.</returns>
    /// <remarks>
    /// This method applies descending ordering based on the key selector. For ascending order, use <see cref="ToCursorPageAsync{T, TKey}"/>.
    /// This is useful for retrieving the most recent items first (e.g., sorting by creation date descending).
    /// </remarks>
    /// <example>
    /// <code>
    /// var page = await dbContext.Posts
    ///     .ToCursorPageDescendingAsync(x => x.CreatedAt, limit: 20);
    /// </code>
    /// </example>
    public static Task<CursorPage<T>> ToCursorPageDescendingAsync<T, TKey>(
        this IQueryable<T> query,
        Expression<Func<T, TKey>> keySelector,
        int limit,
        string? cursor = null,
        bool computeTotalCount = false,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull, IComparable<TKey> =>
        query
            .CursorPageDescending(keySelector, limit, cursor)
            .ToCursorPageAsync(computeTotalCount, cancellationToken);

    /// <summary>
    /// Converts an IQueryable to a cursor-paginated result set ordered by a compound key (multiple properties) in ascending order.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="query">The IQueryable to paginate.</param>
    /// <param name="keySelector">An expression that selects multiple key properties using an anonymous type or tuple (e.g., x => new { x.Category, x.Id } or x => (x.Category, x.Id)).</param>
    /// <param name="limit">The maximum number of items to return per page.</param>
    /// <param name="cursor">Optional. The cursor from a previous page to continue pagination from.</param>
    /// <param name="computeTotalCount">Optional. Whether to compute the total count of items across all pages. This can be expensive for large datasets.</param>
    /// <param name="cancellationToken">Optional. A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation, containing a <see cref="CursorPage{T}"/> with the results.</returns>
    /// <remarks>
    /// Use this method when you need to order by multiple properties to ensure stable pagination.
    /// The compound key is treated as a lexicographic ordering (first key takes precedence, then second, etc.).
    /// You must use either an anonymous type (new { x.Prop1, x.Prop2 }) or a tuple ((x.Prop1, x.Prop2)).
    /// </remarks>
    /// <example>
    /// <code>
    /// // Using anonymous type
    /// var page = await dbContext.Products
    ///     .ToCursorPageAsync(x => new { x.Category, x.Id }, limit: 20);
    ///
    /// // Using tuple
    /// var page = await dbContext.Products
    ///     .ToCursorPageAsync(x => (x.Category, x.Id), limit: 20);
    /// </code>
    /// </example>
    public static Task<CursorPage<T>> ToCursorPageAsync<T>(
        this IQueryable<T> query,
        Expression<Func<T, object>> keySelector,
        int limit,
        string? cursor = null,
        bool computeTotalCount = false,
        CancellationToken cancellationToken = default
    ) =>
        query
            .CursorPage(keySelector, limit, cursor)
            .ToCursorPageAsync(computeTotalCount, cancellationToken);

    /// <summary>
    /// Converts an IQueryable to a cursor-paginated result set ordered by a compound key (multiple properties) in descending order.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="query">The IQueryable to paginate.</param>
    /// <param name="keySelector">An expression that selects multiple key properties using an anonymous type or tuple (e.g., x => new { x.Category, x.Priority } or x => (x.Category, x.Priority)).</param>
    /// <param name="limit">The maximum number of items to return per page.</param>
    /// <param name="cursor">Optional. The cursor from a previous page to continue pagination from.</param>
    /// <param name="computeTotalCount">Optional. Whether to compute the total count of items across all pages. This can be expensive for large datasets.</param>
    /// <param name="cancellationToken">Optional. A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation, containing a <see cref="CursorPage{T}"/> with the results.</returns>
    /// <remarks>
    /// Use this method when you need to order by multiple properties in descending order to ensure stable pagination.
    /// All properties in the compound key will be ordered in descending order.
    /// The compound key is treated as a lexicographic ordering (first key takes precedence, then second, etc.).
    /// </remarks>
    /// <example>
    /// <code>
    /// var page = await dbContext.Tasks
    ///     .ToCursorPageDescendingAsync(x => new { x.Priority, x.CreatedAt }, limit: 20);
    /// </code>
    /// </example>
    public static Task<CursorPage<T>> ToCursorPageDescendingAsync<T>(
        this IQueryable<T> query,
        Expression<Func<T, object>> keySelector,
        int limit,
        string? cursor = null,
        bool computeTotalCount = false,
        CancellationToken cancellationToken = default
    ) =>
        query
            .CursorPageDescending(keySelector, limit, cursor)
            .ToCursorPageAsync(computeTotalCount, cancellationToken);
}
