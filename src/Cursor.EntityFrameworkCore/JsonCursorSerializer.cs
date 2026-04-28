using System.Text;
using System.Text.Json;

namespace Cursor.EntityFrameworkCore;

public class JsonCursorSerializer(JsonSerializerOptions? options = null) : ICursorSerializer
{
    /// <inheritdoc />
    public string EncodeCursor<TKey>(TKey keyValue)
        where TKey : notnull
    {
        var json = JsonSerializer.Serialize(keyValue, options);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <inheritdoc />
    public TKey DecodeCursor<TKey>(string cursor)
        where TKey : notnull
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        return JsonSerializer.Deserialize<TKey>(json, options)!;
    }

    /// <inheritdoc />
    public string EncodeCompoundCursor(List<object?> keyValues)
    {
        var json = JsonSerializer.Serialize(keyValues, options);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <inheritdoc />
    public List<object?> DecodeCompoundCursor(string cursor, List<Type> keyPropertyTypes)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        var elements = JsonSerializer.Deserialize<JsonElement[]>(json, options)!;
        if (elements.Length != keyPropertyTypes.Count)
        {
            throw new InvalidOperationException("Cursor key count mismatch");
        }

        var values = new List<object?>();
        for (int i = 0; i < keyPropertyTypes.Count; i++)
        {
            var targetType = keyPropertyTypes[i];
            var jsonElement = elements[i];
            var value = jsonElement.Deserialize(targetType, options);
            values.Add(value);
        }

        return values;
    }
}
