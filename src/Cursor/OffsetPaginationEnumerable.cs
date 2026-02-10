namespace Cursor;

/// <summary>
/// Provides an async enumerable over offset-paginated results.
/// </summary>
public class OffsetPaginationEnumerable<T, TPage>(
    Func<int, CancellationToken, Task<TPage>> fetchPage,
    int? maxPages = null
) : IAsyncEnumerable<T>
    where TPage : ICursorPage<T>
{
    public async IAsyncEnumerator<T> GetAsyncEnumerator(
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

            foreach (var item in page.Items)
            {
                yield return item;
            }

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
