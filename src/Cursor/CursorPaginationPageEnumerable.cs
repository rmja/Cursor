namespace Cursor;

/// <summary>
/// Provides an async enumerable over cursor-paginated pages.
/// </summary>
public class CursorPaginationPageEnumerable<T, TPage>(
    Func<string?, CancellationToken, Task<TPage>> fetchPage,
    int? maxPages = null
) : IAsyncEnumerable<TPage>
    where TPage : ICursorPage<T>
{
    public async IAsyncEnumerator<TPage> GetAsyncEnumerator(
        CancellationToken cancellationToken = default
    )
    {
        string? cursor = null;
        bool hasMore = true;
        var pageCount = 0;

        while (hasMore && !cancellationToken.IsCancellationRequested)
        {
            if (maxPages.HasValue && pageCount >= maxPages.Value)
            {
                yield break;
            }

            var page = await fetchPage(cursor, cancellationToken).ConfigureAwait(false);
            pageCount++;

            yield return page;

            cursor = page.NextCursor;
            hasMore = page.HasMore;
        }
    }
}
