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
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartArray();
            foreach (var value in keyValues)
            {
                if (value is null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    JsonSerializer.Serialize(writer, value, value.GetType(), options);
                }
            }
            writer.WriteEndArray();
        }
        return Convert.ToBase64String(ms.ToArray());
    }

    /// <inheritdoc />
    public List<object?> DecodeCompoundCursor(string cursor, List<Type> keyPropertyTypes)
    {
        var bytes = Convert.FromBase64String(cursor);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.GetArrayLength() != keyPropertyTypes.Count)
        {
            throw new InvalidOperationException("Cursor key count mismatch");
        }

        var values = new List<object?>();
        foreach (var (type, valueElement) in keyPropertyTypes.Zip(root.EnumerateArray()))
        {
            var value = valueElement.Deserialize(type, options);
            values.Add(value);
        }

        return values;
    }
}
