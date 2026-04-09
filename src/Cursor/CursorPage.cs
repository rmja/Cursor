using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Cursor;

/// <summary>
/// Represents a page of items with cursor-based pagination.
/// </summary>
[CollectionBuilder(typeof(CursorPageBuilder), nameof(CursorPageBuilder.Create))]
public record CursorPage<T> : ICursorPage<T>
{
    private bool? _hasMore;

    /// <inheritdoc />
    public List<T> Items { get; set; } = [];

    /// <inheritdoc />
    public string? NextCursor { get; set; }

    /// <inheritdoc />
    public bool HasMore
    {
        get => _hasMore ?? NextCursor is not null;
        set => _hasMore = value;
    }

    /// <summary>
    /// Gets the total number of items available in the collection, if known.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TotalCount { get; set; }

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
