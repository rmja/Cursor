using Xunit;

namespace Cursor.Tests;

public class OffsetPaginationPageEnumerableTests
{
    [Fact]
    public async Task EnumeratePages_FetchesAllPages()
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

        var enumerable = new OffsetPaginationPageEnumerable<int, CursorPage<int>>(FetchPage);

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
            new CursorPage<int> { Items = [1, 2, 3], NextCursor = "3" },
            new CursorPage<int> { Items = [4, 5, 6], NextCursor = "6" },
            new CursorPage<int> { Items = [7, 8], NextCursor = null },
        };

        var currentPage = 0;
        Task<CursorPage<int>> FetchPage(int offset, CancellationToken ct)
        {
            return Task.FromResult(pages[currentPage++]);
        }

        var enumerable = new OffsetPaginationPageEnumerable<int, CursorPage<int>>(
            FetchPage,
            maxPages: 2
        );

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
            new CursorPage<int> { Items = [], NextCursor = "0" },
            new CursorPage<int> { Items = [1, 2], NextCursor = null },
        };

        var currentPage = 0;
        Task<CursorPage<int>> FetchPage(int offset, CancellationToken ct)
        {
            return Task.FromResult(pages[currentPage++]);
        }

        var enumerable = new OffsetPaginationPageEnumerable<int, CursorPage<int>>(FetchPage);

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

        var enumerable = new OffsetPaginationPageEnumerable<int, CursorPage<int>>(FetchPage);

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
        Task<CursorPage<int>> FetchPage(int offset, CancellationToken ct)
        {
            callCount++;
            if (callCount == 1)
            {
                return Task.FromResult(new CursorPage<int> { Items = [1, 2], NextCursor = "2" });
            }
            else
            {
                return Task.FromResult(
                    new CursorPage<int>
                    {
                        Items = [3, 4],
                        NextCursor = null, // NextCursor = null means HasMore = false
                    }
                );
            }
        }

        var enumerable = new OffsetPaginationPageEnumerable<int, CursorPage<int>>(FetchPage);

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

    [Fact]
    public async Task EnumeratePages_PassesOffsetCorrectly_WhenNextCursorProvided()
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

        var enumerable = new OffsetPaginationPageEnumerable<int, CursorPage<int>>(FetchPage);

        // Act
        await foreach (var _ in enumerable) { }

        // Assert
        Assert.Equal([0, 10, 20], receivedOffsets);
    }

    [Fact]
    public async Task EnumeratePages_CalculatesOffsetFromItemCount_WhenNextCursorNotProvided()
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

        var enumerable = new OffsetPaginationPageEnumerable<int, CursorPage<int>>(
            FetchPage,
            maxPages: 2
        );

        // Act
        await foreach (var _ in enumerable) { }

        // Assert
        Assert.Equal([0, 3], receivedOffsets);
    }

    [Fact]
    public async Task EnumeratePages_CalculatesOffsetFromItemCount_WhenNextCursorInvalid()
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

        var enumerable = new OffsetPaginationPageEnumerable<int, CursorPage<int>>(
            FetchPage,
            maxPages: 2
        );

        // Act
        await foreach (var _ in enumerable) { }

        // Assert
        Assert.Equal([0, 2], receivedOffsets);
    }
}
