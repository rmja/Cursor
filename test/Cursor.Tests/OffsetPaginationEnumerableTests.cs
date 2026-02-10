using Xunit;

namespace Cursor.Tests;

public class OffsetPaginationEnumerableTests
{
    [Fact]
    public async Task EnumerateItems_FetchesAllPages()
    {
        // Arrange
        var pages = new[]
        {
            new CursorPage<int> { Items = [1, 2, 3], NextCursor = "3" },
            new CursorPage<int> { Items = [4, 5, 6], NextCursor = "6" },
            new CursorPage<int> { Items = [7, 8], NextCursor = null },
        };

        var currentPage = 0;
        Task<CursorPage<int>> FetchPage(int offset, CancellationToken ct)
        {
            return Task.FromResult(pages[currentPage++]);
        }

        var enumerable = new OffsetPaginationEnumerable<int, CursorPage<int>>(FetchPage);

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
            new CursorPage<int> { Items = [1, 2, 3], NextCursor = "3" },
            new CursorPage<int> { Items = [4, 5, 6], NextCursor = "6" },
            new CursorPage<int> { Items = [7, 8], NextCursor = null },
        };

        var currentPage = 0;
        Task<CursorPage<int>> FetchPage(int offset, CancellationToken ct)
        {
            return Task.FromResult(pages[currentPage++]);
        }

        var enumerable = new OffsetPaginationEnumerable<int, CursorPage<int>>(
            FetchPage,
            maxPages: 2
        );

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
            new CursorPage<int> { Items = [], NextCursor = "0" },
            new CursorPage<int> { Items = [1, 2], NextCursor = null },
        };

        var currentPage = 0;
        Task<CursorPage<int>> FetchPage(int offset, CancellationToken ct)
        {
            return Task.FromResult(pages[currentPage++]);
        }

        var enumerable = new OffsetPaginationEnumerable<int, CursorPage<int>>(FetchPage);

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

        Task<CursorPage<int>> FetchPage(int offset, CancellationToken ct)
        {
            fetchCount++;
            if (fetchCount == 2)
            {
                cts.Cancel();
            }
            return Task.FromResult(
                new CursorPage<int> { Items = [1, 2, 3], NextCursor = offset + 3 + "" }
            );
        }

        var enumerable = new OffsetPaginationEnumerable<int, CursorPage<int>>(FetchPage);

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
            new CursorPage<int> { Items = [1, 2], NextCursor = "2" },
            new CursorPage<int> { Items = [3, 4], NextCursor = null }, // NextCursor = null means HasMore = false
        };

        var currentPage = 0;
        Task<CursorPage<int>> FetchPage(int offset, CancellationToken ct)
        {
            return Task.FromResult(pages[currentPage++]);
        }

        var enumerable = new OffsetPaginationEnumerable<int, CursorPage<int>>(FetchPage);

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
    public async Task EnumerateItems_PassesOffsetCorrectly_WhenNextCursorProvided()
    {
        // Arrange
        var receivedOffsets = new List<int>();

        Task<CursorPage<int>> FetchPage(int offset, CancellationToken ct)
        {
            receivedOffsets.Add(offset);
            return offset switch
            {
                0 => Task.FromResult(new CursorPage<int> { Items = [1], NextCursor = "10" }),
                10 => Task.FromResult(new CursorPage<int> { Items = [2], NextCursor = "20" }),
                20 => Task.FromResult(new CursorPage<int> { Items = [3], NextCursor = null }),
                _ => throw new InvalidOperationException($"Unexpected offset: {offset}"),
            };
        }

        var enumerable = new OffsetPaginationEnumerable<int, CursorPage<int>>(FetchPage);

        // Act
        await foreach (var _ in enumerable) { }

        // Assert
        Assert.Equal([0, 10, 20], receivedOffsets);
    }

    [Fact]
    public async Task EnumerateItems_CalculatesOffsetFromItemCount_WhenNextCursorNotProvided()
    {
        // Arrange
        var receivedOffsets = new List<int>();

        Task<CursorPage<int>> FetchPage(int offset, CancellationToken ct)
        {
            receivedOffsets.Add(offset);
            return offset switch
            {
                0 => Task.FromResult(
                    new CursorPage<int>
                    {
                        Items = [1, 2, 3],
                        NextCursor = null,
                        HasMore = true,
                    }
                ),
                3 => Task.FromResult(new CursorPage<int> { Items = [4, 5], NextCursor = null }),
                _ => throw new InvalidOperationException($"Unexpected offset: {offset}"),
            };
        }

        var enumerable = new OffsetPaginationEnumerable<int, CursorPage<int>>(
            FetchPage,
            maxPages: 2
        );

        // Act
        await foreach (var _ in enumerable) { }

        // Assert
        Assert.Equal([0, 3], receivedOffsets);
    }

    [Fact]
    public async Task EnumerateItems_CalculatesOffsetFromItemCount_WhenNextCursorInvalid()
    {
        // Arrange
        var receivedOffsets = new List<int>();

        Task<CursorPage<int>> FetchPage(int offset, CancellationToken ct)
        {
            receivedOffsets.Add(offset);
            return offset switch
            {
                0 => Task.FromResult(
                    new CursorPage<int> { Items = [1, 2], NextCursor = "invalid" }
                ),
                2 => Task.FromResult(new CursorPage<int> { Items = [3, 4, 5], NextCursor = null }),
                _ => throw new InvalidOperationException($"Unexpected offset: {offset}"),
            };
        }

        var enumerable = new OffsetPaginationEnumerable<int, CursorPage<int>>(
            FetchPage,
            maxPages: 2
        );

        // Act
        await foreach (var _ in enumerable) { }

        // Assert
        Assert.Equal([0, 2], receivedOffsets);
    }
}
