using System.Linq.Expressions;
using System.Text;
using System.Text.Json;

namespace Cursor.EntityFrameworkCore;

internal static class CursorSerializer
{
    public static string EncodeCursor<TKey>(TKey keyValue)
        where TKey : notnull
    {
        var json = JsonSerializer.Serialize(keyValue);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static TKey DecodeCursor<TKey>(string cursor)
        where TKey : notnull
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        return JsonSerializer.Deserialize<TKey>(json)!;
    }

    public static string EncodeCompoundCursor(List<object?> keyValues)
    {
        var json = JsonSerializer.Serialize(keyValues);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static List<object?> DecodeCompoundCursor(string cursor, List<Expression> keyProperties)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        var elements = JsonSerializer.Deserialize<JsonElement[]>(json)!;

        if (elements.Length != keyProperties.Count)
        {
            throw new InvalidOperationException("Cursor key count mismatch");
        }

        var values = new List<object?>();
        for (int i = 0; i < keyProperties.Count; i++)
        {
            var targetType = keyProperties[i].Type;
            var jsonElement = elements[i];
            var value = jsonElement.Deserialize(targetType);
            values.Add(value);
        }

        return values;
    }
}
