namespace Cursor;

/// <summary>
/// Provides an async enumerable over cursor-paginated results.
/// </summary>
public class CursorPaginationEnumerable<T, TPage>(
    Func<string?, CancellationToken, Task<TPage>> fetchPage,
    int? maxPages = null
) : IAsyncEnumerable<T>
    where TPage : ICursorPage<T>
{
    public async IAsyncEnumerator<T> GetAsyncEnumerator(
        CancellationToken cancellationToken = default
    )
    {
        string? cursor = null;
        var hasMore = true;
        var pageCount = 0;

        while (hasMore && !cancellationToken.IsCancellationRequested)
        {
            if (maxPages.HasValue && pageCount >= maxPages.Value)
            {
                yield break;
            }

            var page = await fetchPage(cursor, cancellationToken).ConfigureAwait(false);
            pageCount++;

            foreach (var item in page.Items)
            {
                yield return item;
            }

            cursor = page.NextCursor;
            hasMore = page.HasMore;
        }
    }
}

/// <summary>
/// Provides an async enumerable over cursor-paginated results using CursorPage.
/// </summary>
public class CursorPaginationEnumerable<T>(
    Func<string?, CancellationToken, Task<CursorPage<T>>> fetchPage,
    int? maxPages = null
) : CursorPaginationEnumerable<T, CursorPage<T>>(fetchPage, maxPages);
