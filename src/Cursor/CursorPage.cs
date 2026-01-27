using System.Runtime.CompilerServices;

namespace Cursor;

/// <summary>
/// Represents a page of items with cursor-based pagination.
/// </summary>
[CollectionBuilder(typeof(CursorPageBuilder), nameof(CursorPageBuilder.Create))]
public record CursorPage<T> : ICursorPage<T>
{
    /// <summary>
    /// Whether there are more items available.
    /// </summary>
    public List<T> Items { get; set; } = [];

    /// <summary>
    /// The cursor for the next page, or null if there are no more pages.
    /// </summary>
    public string? NextCursor { get; set; }

    /// <summary>
    /// Whether there are more items available.
    /// </summary>
    public bool HasMore => NextCursor is not null;

    public CursorPage() { }

    public CursorPage(int capacity)
    {
        Items.EnsureCapacity(capacity);
    }

    public CursorPage(IEnumerable<T> items)
    {
        Items.AddRange(items);
    }

    // This gives the type an iteration type without implementing IEnumerable<T>
    // We don't want CursorPage<T> to implement IEnumerable<T> as it causes System.Text.Json
    // to serialize the page as an array, instead of the individual properties.
    public List<T>.Enumerator GetEnumerator() => Items.GetEnumerator();
}

public static class CursorPageBuilder
{
    public static CursorPage<T> Create<T>(ReadOnlySpan<T> items)
    {
        var page = new CursorPage<T>(items.Length);
        foreach (var item in items)
        {
            page.Items.Add(item);
        }
        return page;
    }
}
