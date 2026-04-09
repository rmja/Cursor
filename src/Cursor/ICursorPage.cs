namespace Cursor;

public interface ICursorPage<T>
{
    /// <summary>
    /// The list of items contained in the current page.
    /// </summary>
    List<T> Items { get; }

    /// <summary>
    /// The cursor for the next page, or null if there are no more pages.
    /// If offset-based pagination is used, this may be a string representation of the next offset.
    /// </summary>
    string? NextCursor { get; }

    /// <summary>
    /// Whether there are more items available.
    /// </summary>
    bool HasMore => NextCursor is not null;
}
