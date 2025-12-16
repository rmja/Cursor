namespace Cursor;

/// <summary>
/// Represents a page of items with cursor-based pagination.
/// </summary>
public record CursorPage<T> : ICursorPage<T>
{
    /// <summary>
    /// Whether there are more items available.
    /// </summary>
    public required List<T> Items { get; init; }

    /// <summary>
    /// The cursor for the next page, or null if there are no more pages.
    /// </summary>
    public string? NextCursor { get; init; }

    /// <summary>
    /// Whether there are more items available.
    /// </summary>
    public bool HasMore => NextCursor is not null;
}
