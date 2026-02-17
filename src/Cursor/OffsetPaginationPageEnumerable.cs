using System.Globalization;
using System.Numerics;

namespace Cursor;

/// <summary>
/// Provides an async enumerable over offset-paginated pages.
/// </summary>
public class OffsetPaginationPageEnumerable<T, TPage, TOffset>(
    Func<TOffset, CancellationToken, Task<TPage>> fetchPage,
    TOffset initialOffset = default,
    int? maxPages = null
) : IAsyncEnumerable<TPage>
    where TPage : ICursorPage<T>
    where TOffset : struct, IBinaryInteger<TOffset>
{
    public async IAsyncEnumerator<TPage> GetAsyncEnumerator(
        CancellationToken cancellationToken = default
    )
    {
        TOffset offset = initialOffset;
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
            if (
                page.NextCursor is not null
                && TOffset.TryParse(
                    page.NextCursor,
                    CultureInfo.InvariantCulture,
                    out var nextOffset
                )
            )
            {
                offset = nextOffset;
            }
            else if (page.Items.Count > 0)
            {
                offset += TOffset.CreateChecked(page.Items.Count);
            }
            else
            {
                yield break; // No more items, end the enumeration
            }

            hasMore = page.HasMore;
        }
    }
}
