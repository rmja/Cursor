namespace Cursor.EntityFrameworkCore;

/// <summary>
/// Defines serialization and deserialization of cursor values used for cursor-based pagination.
/// </summary>
public interface ICursorSerializer
{
    /// <summary>
    /// Encodes a single key value into a cursor string.
    /// </summary>
    /// <typeparam name="TKey">The type of the key value.</typeparam>
    /// <param name="keyValue">The key value to encode.</param>
    /// <returns>A cursor string representing the key value.</returns>
    string EncodeCursor<TKey>(TKey keyValue)
        where TKey : notnull;

    /// <summary>
    /// Decodes a cursor string back into a single key value.
    /// </summary>
    /// <typeparam name="TKey">The type of the key value.</typeparam>
    /// <param name="cursor">The cursor string to decode.</param>
    /// <returns>The decoded key value.</returns>
    TKey DecodeCursor<TKey>(string cursor)
        where TKey : notnull;

    /// <summary>
    /// Encodes a list of key values from a compound key into a cursor string.
    /// </summary>
    /// <param name="keyValues">The ordered list of key values that make up the compound key.</param>
    /// <returns>A cursor string representing the compound key values.</returns>
    string EncodeCompoundCursor(List<object?> keyValues);

    /// <summary>
    /// Decodes a cursor string back into a list of key values for a compound key.
    /// </summary>
    /// <param name="cursor">The cursor string to decode.</param>
    /// <param name="keyPropertyTypes">The ordered list of types corresponding to each key property, used to deserialize each value correctly.</param>
    /// <returns>The decoded list of key values in the same order as <paramref name="keyPropertyTypes"/>.</returns>
    List<object?> DecodeCompoundCursor(string cursor, List<Type> keyPropertyTypes);
}
