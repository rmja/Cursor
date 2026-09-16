using System;
using System.Collections.Generic;
using System.Linq;
using Cursor.EntityFrameworkCore;
using Xunit;

namespace Cursor.Tests.EntityFrameworkCore;

public sealed class ToCursorPageInMemoryTests
{
    private sealed record Item(int Id, string Name);

    private static readonly Item[] Items =
    [
        new(1, "A"),
        new(2, "B"),
        new(3, "C"),
        new(4, "D"),
        new(5, "E"),
    ];

    [Fact]
    public void Ascending_FirstPage()
    {
        var page = Items.OrderBy(x => x.Id).ToCursorPage(x => x.Id, limit: 3);

        Assert.Equal([1, 2, 3], page.Items.Select(x => x.Id));
        Assert.NotNull(page.NextCursor);
        Assert.True(page.HasMore);
    }

    [Fact]
    public void Ascending_SecondPage()
    {
        var first = Items.OrderBy(x => x.Id).ToCursorPage(x => x.Id, limit: 3);
        var second = Items.OrderBy(x => x.Id).ToCursorPage(x => x.Id, limit: 3, cursor: first.NextCursor);

        Assert.Equal([4, 5], second.Items.Select(x => x.Id));
        Assert.Null(second.NextCursor);
        Assert.False(second.HasMore);
    }

    // Descending order requires a comparer matching the ordering: the comparer describes the
    // order the sequence is already in, not the natural order of TKey.
    private static readonly IComparer<int> DescendingIdComparer = Comparer<int>.Create((a, b) => b.CompareTo(a));

    [Fact]
    public void Descending_FirstPage()
    {
        var page = Items.OrderByDescending(x => x.Id).ToCursorPage(x => x.Id, DescendingIdComparer, limit: 3);

        Assert.Equal([5, 4, 3], page.Items.Select(x => x.Id));
        Assert.True(page.HasMore);
    }

    [Fact]
    public void Descending_SecondPage()
    {
        var first = Items.OrderByDescending(x => x.Id).ToCursorPage(x => x.Id, DescendingIdComparer, limit: 3);
        var second = Items.OrderByDescending(x => x.Id)
            .ToCursorPage(x => x.Id, DescendingIdComparer, limit: 3, cursor: first.NextCursor);

        Assert.Equal([2, 1], second.Items.Select(x => x.Id));
        Assert.Null(second.NextCursor);
        Assert.False(second.HasMore);
    }

    [Fact]
    public void ExactlyLimit_NoMore()
    {
        var page = Items.OrderBy(x => x.Id).ToCursorPage(x => x.Id, limit: 5);

        Assert.Equal(5, page.Items.Count);
        Assert.Null(page.NextCursor);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void EmptySequence_ReturnsEmptyPage()
    {
        var page = Array.Empty<Item>().OrderBy(x => x.Id).ToCursorPage(x => x.Id, limit: 3);

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void CursorPastEnd_ReturnsEmptyPage()
    {
        var page = Items.OrderBy(x => x.Id)
            .ToCursorPage(x => x.Id, limit: 3, cursor: new CursorOptions().CursorSerializer.EncodeCursor(5));

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void NegativeLimit_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Items.OrderBy(x => x.Id).ToCursorPage(x => x.Id, limit: -1)
        );
    }

    [Fact]
    public void LimitZero_ReturnsEmptyPage_WithoutThrowing()
    {
        var page = Items.OrderBy(x => x.Id).ToCursorPage(x => x.Id, limit: 0);

        Assert.Empty(page.Items);
        // Nothing was handed out, so there is nothing to resume pagination from.
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public void LimitZero_WithComputeTotalCount_ReturnsTotalCountOnly()
    {
        var page = Items.OrderBy(x => x.Id)
            .ToCursorPage(x => x.Id, limit: 0, options: new CursorOptions { ComputeTotalCount = true });

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
        Assert.Equal(5, page.TotalCount);
    }

    [Fact]
    public void ComputeTotalCount_False_ReturnsNull()
    {
        var page = Items.OrderBy(x => x.Id).ToCursorPage(x => x.Id, limit: 3);

        Assert.Null(page.TotalCount);
    }

    [Fact]
    public void ComputeTotalCount_True_ReturnsTotalItemCount()
    {
        var page = Items.OrderBy(x => x.Id)
            .ToCursorPage(x => x.Id, limit: 3, options: new CursorOptions { ComputeTotalCount = true });

        Assert.Equal(5, page.TotalCount);
    }

    [Fact]
    public void ComputeTotalCount_SecondPage_StillReflectsFullSequence()
    {
        var options = new CursorOptions { ComputeTotalCount = true };
        var first = Items.OrderBy(x => x.Id).ToCursorPage(x => x.Id, limit: 3, options: options);
        var second = Items.OrderBy(x => x.Id)
            .ToCursorPage(x => x.Id, limit: 3, cursor: first.NextCursor, options: options);

        Assert.Equal(5, first.TotalCount);
        Assert.Equal(5, second.TotalCount);
    }

    [Fact]
    public void CaseInsensitiveComparer_IsRespected()
    {
        Item[] items = [new(1, "b"), new(2, "A"), new(3, "c")];
        var ordered = items.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase);

        var page = ordered.ToCursorPage(x => x.Name, StringComparer.OrdinalIgnoreCase, limit: 2);

        Assert.Equal(["A", "b"], page.Items.Select(x => x.Name));
    }

    [Fact]
    public void MinimizesAllocations_BuffersExactlyLimitPlusOneUpFront()
    {
        var page = Items.OrderBy(x => x.Id).ToCursorPage(x => x.Id, limit: 3);

        // A single List<T> is allocated up front, sized to limit + 1 (the lookahead item used
        // to detect HasMore), and is never grown or copied afterwards.
        Assert.Equal(4, page.Items.Capacity);
    }
}
