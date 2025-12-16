using Cursor;

namespace Example;

public record CustomCursorPage<T> : ICursorPage<T>
{
    public required List<T> Data { get; init; }
    public string? Cursor { get; init; }

    List<T> ICursorPage<T>.Items => Data;
    string? ICursorPage<T>.NextCursor => Cursor;
    bool ICursorPage<T>.HasMore => Cursor is not null;
}
