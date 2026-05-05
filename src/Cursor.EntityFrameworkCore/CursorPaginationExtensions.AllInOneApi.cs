using Cursor.EntityFrameworkCore;

namespace Cursor;

public static partial class CursorPaginationExtensions
{
    /// <summary>
    /// Converts an ordered query to a cursor-paginated result set in a single call.
    /// </summary>
    /// <typeparam name="T">The type of items in the result set (may be a projection of the originating entity).</typeparam>
    /// <param name="query">
    /// A query whose outermost operators form an ordering chain: <see cref="Queryable.OrderBy"/> or
    /// <see cref="Queryable.OrderByDescending"/>, optionally followed by <see cref="Queryable.ThenBy"/>
    /// or <see cref="Queryable.ThenByDescending"/> calls. Mixed directions are allowed. The query
    /// may be projected (for example with <c>.Select(...)</c>) after the ordering chain.
    /// </param>
    /// <param name="limit">The maximum number of items to return per page.</param>
    /// <param name="cursor">Optional. The cursor from a previous page to continue pagination from.</param>
    /// <param name="options">Options to control the behavior of the cursor pagination.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation, containing a <see cref="CursorPage{T}"/> with the results.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="query"/> does not have an ordering operator applied to its outermost call chain.</exception>
    /// <remarks>
    /// The cursor is an opaque Base64-encoded token that should be passed back to subsequent calls
    /// to retrieve the next page. The direction of pagination is encoded in the ordering applied to
    /// the query, so both ascending and descending pagination use the same method.
    /// <para>
    /// <b>Stable pagination requires a unique ordering.</b> If the ordering keys can contain duplicate
    /// values (for example, <c>OrderBy(x =&gt; x.Name)</c>), append a tiebreaker such as the primary
    /// key (<c>OrderBy(x =&gt; x.Name).ThenBy(x =&gt; x.Id)</c>) to ensure consistent paging.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Single ascending key
    /// var page = await dbContext.Users
    ///     .OrderBy(x => x.Id)
    ///     .ToCursorPageAsync(limit: 20);
    ///
    /// // Descending
    /// var newest = await dbContext.Posts
    ///     .OrderByDescending(x => x.CreatedAt)
    ///     .ThenBy(x => x.Id)
    ///     .ToCursorPageAsync(limit: 20);
    ///
    /// // With projection (the projected query is an IQueryable, which is fine)
    /// var dtos = await dbContext.Users
    ///     .OrderBy(x => x.Id)
    ///     .Select(x => new UserDto { Id = x.Id, Name = x.Name })
    ///     .ToCursorPageAsync(limit: 20);
    /// </code>
    /// </example>
    public static Task<CursorPage<T>> ToCursorPageAsync<T>(
        this IQueryable<T> query,
        int limit,
        string? cursor = null,
        CursorOptions? options = null,
        CancellationToken cancellationToken = default
    ) => query.CursorPage(limit, cursor, options).ToCursorPageAsync(cancellationToken);
}
