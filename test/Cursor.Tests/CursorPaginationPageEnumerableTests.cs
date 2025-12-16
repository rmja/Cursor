using Xunit;

namespace Cursor.Tests;

public class CursorPaginationPageEnumerableTests
{
    [Fact]
    public async Task EnumeratePages_FetchesAllPages()
    {
        // Arrange
        var pages = new[]
        {
            new CursorPage<int> { Items = [1, 2, 3], NextCursor = "page2" },
            new CursorPage<int> { Items = [4, 5, 6], NextCursor = "page3" },
            new CursorPage<int> { Items = [7, 8], NextCursor = null }
        };

        var currentPage = 0;
        Task<CursorPage<int>> FetchPage(string? cursor, CancellationToken ct)
        {
            return Task.FromResult(pages[currentPage++]);
        }

        var enumerable = new CursorPaginationPageEnumerable<int, CursorPage<int>>(FetchPage);

        // Act
        var fetchedPages = new List<CursorPage<int>>();
        await foreach (var page in enumerable)
        {
            fetchedPages.Add(page);
        }

        // Assert
        Assert.Equal(3, fetchedPages.Count);
        Assert.Equal([1, 2, 3], fetchedPages[0].Items);
        Assert.Equal([4, 5, 6], fetchedPages[1].Items);
        Assert.Equal([7, 8], fetchedPages[2].Items);
    }

    [Fact]
    public async Task EnumeratePages_RespectsMaxPages()
    {
        // Arrange
        var pages = new[]
        {
            new CursorPage<int> { Items = [1, 2, 3], NextCursor = "page2" },
            new CursorPage<int> { Items = [4, 5, 6], NextCursor = "page3" },
            new CursorPage<int> { Items = [7, 8], NextCursor = null }
        };

        var currentPage = 0;
        Task<CursorPage<int>> FetchPage(string? cursor, CancellationToken ct)
        {
            return Task.FromResult(pages[currentPage++]);
        }

        var enumerable = new CursorPaginationPageEnumerable<int, CursorPage<int>>(FetchPage, maxPages: 2);

        // Act
        var fetchedPages = new List<CursorPage<int>>();
        await foreach (var page in enumerable)
        {
            fetchedPages.Add(page);
        }

        // Assert
        Assert.Equal(2, fetchedPages.Count);
        Assert.Equal([1, 2, 3], fetchedPages[0].Items);
        Assert.Equal([4, 5, 6], fetchedPages[1].Items);
    }

    [Fact]
    public async Task EnumeratePages_HandlesEmptyPages()
    {
        // Arrange
        var pages = new[]
        {
            new CursorPage<int> { Items = [], NextCursor = "page2" },
            new CursorPage<int> { Items = [1, 2], NextCursor = null }
        };

        var currentPage = 0;
        Task<CursorPage<int>> FetchPage(string? cursor, CancellationToken ct)
        {
            return Task.FromResult(pages[currentPage++]);
        }

        var enumerable = new CursorPaginationPageEnumerable<int, CursorPage<int>>(FetchPage);

        // Act
        var fetchedPages = new List<CursorPage<int>>();
        await foreach (var page in enumerable)
        {
            fetchedPages.Add(page);
        }

        // Assert
        Assert.Equal(2, fetchedPages.Count);
        Assert.Empty(fetchedPages[0].Items);
        Assert.Equal([1, 2], fetchedPages[1].Items);
    }

    [Fact]
    public async Task EnumeratePages_RespectsCancellation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var fetchCount = 0;

        Task<CursorPage<int>> FetchPage(string? cursor, CancellationToken ct)
        {
            fetchCount++;
            if (fetchCount == 2)
            {
                cts.Cancel();
            }
            return Task.FromResult(new CursorPage<int>
            {
                Items = [1, 2, 3],
                NextCursor = "next"
            });
        }

        var enumerable = new CursorPaginationPageEnumerable<int, CursorPage<int>>(FetchPage);

        // Act
        var fetchedPages = new List<CursorPage<int>>();
        await foreach (var page in enumerable.WithCancellation(cts.Token))
        {
            fetchedPages.Add(page);
        }

        // Assert
        Assert.Equal(2, fetchedPages.Count);
        Assert.Equal(2, fetchCount);
    }

    [Fact]
    public async Task EnumeratePages_StopsWhenHasMoreIsFalse()
    {
        // Arrange
        var callCount = 0;
        Task<CursorPage<int>> FetchPage(string? cursor, CancellationToken ct)
        {
            callCount++;
            if (callCount == 1)
            {
                return Task.FromResult(new CursorPage<int>
                {
                    Items = [1, 2],
                    NextCursor = "page2"
                });
            }
            else
            {
                return Task.FromResult(new CursorPage<int>
                {
                    Items = [3, 4],
                    NextCursor = null // NextCursor = null means HasMore = false
                });
            }
        }

        var enumerable = new CursorPaginationPageEnumerable<int, CursorPage<int>>(FetchPage);

        // Act
        var fetchedPages = new List<CursorPage<int>>();
        await foreach (var page in enumerable)
        {
            fetchedPages.Add(page);
        }

        // Assert
        Assert.Equal(2, fetchedPages.Count);
        Assert.Equal(2, callCount);
    }
}
