using Xunit;

namespace Cursor.Tests;

public class CursorPageTests
{
    [Fact]
    public void HasMore_ReturnsTrueWhenNextCursorIsNotNull()
    {
        // Arrange & Act
        var page = new CursorPage<int>
        {
            Items = [1, 2, 3],
            NextCursor = "next"
        };

        // Assert
        Assert.True(page.HasMore);
    }

    [Fact]
    public void HasMore_ReturnsFalseWhenNextCursorIsNull()
    {
        // Arrange & Act
        var page = new CursorPage<int>
        {
            Items = [1, 2, 3],
            NextCursor = null
        };

        // Assert
        Assert.False(page.HasMore);
    }

    [Fact]
    public void CursorPage_AllowsEmptyItems()
    {
        // Arrange & Act
        var page = new CursorPage<int>
        {
            Items = [],
            NextCursor = "next"
        };

        // Assert
        Assert.Empty(page.Items);
        Assert.Equal("next", page.NextCursor);
    }

    [Fact]
    public void CursorPage_CanBeCompared()
    {
        // Arrange
        var items = new List<int> { 1, 2, 3 };
        var page1 = new CursorPage<int>
        {
            Items = items,
            NextCursor = "cursor"
        };

        var page2 = new CursorPage<int>
        {
            Items = items,
            NextCursor = "cursor"
        };

        // Act & Assert
        Assert.Equal(page1, page2);
        Assert.Equal("cursor", page1.NextCursor);
        Assert.Equal("cursor", page2.NextCursor);
    }
}
