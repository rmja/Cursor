namespace Cursor;

public interface ICursorPage<T>
{
    List<T> Items { get; }
    string? NextCursor { get; }
    bool HasMore => NextCursor is not null;
}
