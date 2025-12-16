using Xunit;

namespace Cursor.Tests;

public class CursorPaginationEnumerableTests
{
    [Fact]
    public async Task EnumerateItems_FetchesAllPages()
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

        var enumerable = new CursorPaginationEnumerable<int, CursorPage<int>>(FetchPage);

        // Act
        var items = new List<int>();
        await foreach (var item in enumerable)
        {
            items.Add(item);
        }

        // Assert
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], items);
        Assert.Equal(3, currentPage);
    }

    [Fact]
    public async Task EnumerateItems_RespectsMaxPages()
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

        var enumerable = new CursorPaginationEnumerable<int, CursorPage<int>>(FetchPage, maxPages: 2);

        // Act
        var items = new List<int>();
        await foreach (var item in enumerable)
        {
            items.Add(item);
        }

        // Assert
        Assert.Equal([1, 2, 3, 4, 5, 6], items); // Only first 2 pages
        Assert.Equal(2, currentPage);
    }

    [Fact]
    public async Task EnumerateItems_HandlesEmptyPages()
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

        var enumerable = new CursorPaginationEnumerable<int, CursorPage<int>>(FetchPage);

        // Act
        var items = new List<int>();
        await foreach (var item in enumerable)
        {
            items.Add(item);
        }

        // Assert
        Assert.Equal([1, 2], items);
        Assert.Equal(2, currentPage);
    }

    [Fact]
    public async Task EnumerateItems_RespectsCancellation()
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

        var enumerable = new CursorPaginationEnumerable<int, CursorPage<int>>(FetchPage);

        // Act
        var items = new List<int>();
        await foreach (var item in enumerable.WithCancellation(cts.Token))
        {
            items.Add(item);
        }

        // Assert
        Assert.Equal([1, 2, 3, 1, 2, 3], items); // 2 pages before cancellation
        Assert.Equal(2, fetchCount);
    }

    [Fact]
    public async Task EnumerateItems_StopsWhenHasMoreIsFalse()
    {
        // Arrange
        var pages = new[]
        {
            new CursorPage<int> { Items = [1, 2], NextCursor = "page2" },
            new CursorPage<int> { Items = [3, 4], NextCursor = null } // NextCursor = null means HasMore = false
        };

        var currentPage = 0;
        Task<CursorPage<int>> FetchPage(string? cursor, CancellationToken ct)
        {
            return Task.FromResult(pages[currentPage++]);
        }

        var enumerable = new CursorPaginationEnumerable<int, CursorPage<int>>(FetchPage);

        // Act
        var items = new List<int>();
        await foreach (var item in enumerable)
        {
            items.Add(item);
        }

        // Assert
        Assert.Equal([1, 2, 3, 4], items);
        Assert.Equal(2, currentPage);
    }

    [Fact]
    public async Task EnumerateItems_PassesCursorCorrectly()
    {
        // Arrange
        var receivedCursors = new List<string?>();

        Task<CursorPage<int>> FetchPage(string? cursor, CancellationToken ct)
        {
            receivedCursors.Add(cursor);
            return cursor switch
            {
                null => Task.FromResult(new CursorPage<int> { Items = [1], NextCursor = "cursor1" }),
                "cursor1" => Task.FromResult(new CursorPage<int> { Items = [2], NextCursor = "cursor2" }),
                "cursor2" => Task.FromResult(new CursorPage<int> { Items = [3], NextCursor = null }),
                _ => throw new InvalidOperationException()
            };
        }

        var enumerable = new CursorPaginationEnumerable<int, CursorPage<int>>(FetchPage);

        // Act
        await foreach (var _ in enumerable)
        {
        }

        // Assert
        Assert.Equal([null, "cursor1", "cursor2"], receivedCursors);
    }
}
