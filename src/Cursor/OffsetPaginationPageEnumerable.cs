namespace Cursor;

/// <summary>
/// Provides an async enumerable over offset-paginated pages.
/// </summary>
public class OffsetPaginationPageEnumerable<T, TPage>(
    Func<int, CancellationToken, Task<TPage>> fetchPage,
    int? maxPages = null
) : IAsyncEnumerable<TPage>
    where TPage : ICursorPage<T>
{
    public async IAsyncEnumerator<TPage> GetAsyncEnumerator(
        CancellationToken cancellationToken = default
    )
    {
        int offset = 0;
        var hasMore = true;
        var pageCount = 0;

        while (hasMore && !cancellationToken.IsCancellationRequested)
        {
            if (maxPages.HasValue && pageCount >= maxPages.Value)
            {
                yield break;
            }

            var page = await fetchPage(offset, cancellationToken).ConfigureAwait(false);
            pageCount++;

            yield return page;

            // Parse NextCursor as offset, or calculate from items returned
            if (page.NextCursor is not null && int.TryParse(page.NextCursor, out var nextOffset))
            {
                offset = nextOffset;
            }
            else if (page.Items.Count > 0)
            {
                offset += page.Items.Count;
            }
            else
            {
                yield break; // No more items, end the enumeration
            }

            hasMore = page.HasMore;
        }
    }
}
