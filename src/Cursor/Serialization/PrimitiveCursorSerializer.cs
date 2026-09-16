using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Cursor.Serialization;

/// <summary>
/// An <see cref="ICursorSerializer"/> for the primitive types that keys are made of.
/// </summary>
/// <remarks>
/// <para>
/// Every supported type is formatted and parsed through a closed switch over known types, so no reflection,
/// no runtime code generation and no serialization metadata is involved. That makes this serializer statically
/// trim and native AOT safe, and it produces noticeably shorter cursors than <see cref="JsonCursorSerializer"/>
/// because there are no JSON quotes or escapes.
/// </para>
/// <para>
/// The supported types are <see cref="string"/>, <see cref="bool"/>, <see cref="char"/>, the integral and
/// floating point types, <see cref="decimal"/>, <see cref="Guid"/>, <see cref="DateTime"/>,
/// <see cref="DateTimeOffset"/>, <see cref="DateOnly"/>, <see cref="TimeOnly"/>, <see cref="TimeSpan"/>,
/// enums, and nullable variants of these. A key of any other type throws <see cref="NotSupportedException"/> -
/// use <see cref="JsonCursorSerializer"/> for those.
/// </para>
/// <para>
/// Cursors are opaque: the encoding is an implementation detail and may change between versions.
/// A malformed cursor throws <see cref="FormatException"/>.
/// </para>
/// </remarks>
public sealed class PrimitiveCursorSerializer : ICursorSerializer
{
    // A compound cursor is the concatenation of one token per key value, where a token is either
    // NullToken, or the value length in characters, LengthSeparator, and the formatted value.
    // Length prefixing keeps values that contain the separator unambiguous without any escaping.
    private const char NullToken = '-';
    private const char LengthSeparator = ':';

    /// <inheritdoc />
    public string EncodeCursor<TKey>(TKey keyValue)
        where TKey : notnull => Encode(Format(keyValue));

    /// <inheritdoc />
    public TKey DecodeCursor<TKey>(string cursor)
        where TKey : notnull => (TKey)Parse(typeof(TKey), Decode(cursor));

    /// <inheritdoc />
    public string EncodeCompoundCursor(List<object?> keyValues)
    {
        var builder = new StringBuilder();
        foreach (var value in keyValues)
        {
            if (value is null)
            {
                builder.Append(NullToken);
            }
            else
            {
                var text = Format(value);
                builder.Append(text.Length).Append(LengthSeparator).Append(text);
            }
        }

        return Encode(builder.ToString());
    }

    /// <inheritdoc />
    public List<object?> DecodeCompoundCursor(string cursor, List<Type> keyPropertyTypes)
    {
        var text = Decode(cursor);
        var values = new List<object?>(keyPropertyTypes.Count);
        var position = 0;

        foreach (var keyPropertyType in keyPropertyTypes)
        {
            if (position == text.Length)
            {
                throw new FormatException("Cursor key count mismatch");
            }

            if (text[position] == NullToken)
            {
                position++;
                values.Add(null);
                continue;
            }

            var separator = text.IndexOf(LengthSeparator, position);
            if (
                separator < 0
                || !int.TryParse(
                    text.AsSpan(position, separator - position),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var length
                )
                || length > text.Length - (separator + 1)
            )
            {
                throw new FormatException("Malformed cursor");
            }

            position = separator + 1;
            values.Add(Parse(keyPropertyType, text.AsSpan(position, length)));
            position += length;
        }

        if (position != text.Length)
        {
            throw new FormatException("Cursor key count mismatch");
        }

        return values;
    }

    private static string Encode(string text) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text));

    private static string Decode(string cursor) =>
        Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));

    private static string Format(object value)
    {
        if (value.GetType().IsEnum)
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture);
        }

        return value switch
        {
            string v => v,
            bool v => v ? "true" : "false",
            char v => v.ToString(),
            byte v => v.ToString(CultureInfo.InvariantCulture),
            sbyte v => v.ToString(CultureInfo.InvariantCulture),
            short v => v.ToString(CultureInfo.InvariantCulture),
            ushort v => v.ToString(CultureInfo.InvariantCulture),
            int v => v.ToString(CultureInfo.InvariantCulture),
            uint v => v.ToString(CultureInfo.InvariantCulture),
            long v => v.ToString(CultureInfo.InvariantCulture),
            ulong v => v.ToString(CultureInfo.InvariantCulture),
            float v => v.ToString("R", CultureInfo.InvariantCulture),
            double v => v.ToString("R", CultureInfo.InvariantCulture),
            decimal v => v.ToString(CultureInfo.InvariantCulture),
            Guid v => v.ToString("N", CultureInfo.InvariantCulture),
            DateTime v => v.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset v => v.ToString("O", CultureInfo.InvariantCulture),
            DateOnly v => v.ToString("O", CultureInfo.InvariantCulture),
            TimeOnly v => v.ToString("O", CultureInfo.InvariantCulture),
            TimeSpan v => v.ToString("c", CultureInfo.InvariantCulture),
            _ => throw Unsupported(value.GetType()),
        };
    }

    private static object Parse(Type keyType, ReadOnlySpan<char> text)
    {
        var type = Nullable.GetUnderlyingType(keyType) ?? keyType;

        if (type.IsEnum)
        {
            return Enum.ToObject(
                type,
                long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)
            );
        }

        if (type == typeof(string))
        {
            return text.ToString();
        }
        if (type == typeof(bool))
        {
            return bool.Parse(text);
        }
        if (type == typeof(char))
        {
            return text.Length == 1 ? text[0] : throw new FormatException("Malformed cursor");
        }
        if (type == typeof(byte))
        {
            return byte.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
        if (type == typeof(sbyte))
        {
            return sbyte.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
        if (type == typeof(short))
        {
            return short.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
        if (type == typeof(ushort))
        {
            return ushort.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
        if (type == typeof(int))
        {
            return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
        if (type == typeof(uint))
        {
            return uint.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
        if (type == typeof(long))
        {
            return long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
        if (type == typeof(ulong))
        {
            return ulong.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
        if (type == typeof(float))
        {
            return float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        if (type == typeof(double))
        {
            return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        if (type == typeof(decimal))
        {
            return decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);
        }
        if (type == typeof(Guid))
        {
            return Guid.Parse(text, CultureInfo.InvariantCulture);
        }
        if (type == typeof(DateTime))
        {
            return DateTime.Parse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind
            );
        }
        if (type == typeof(DateTimeOffset))
        {
            return DateTimeOffset.Parse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind
            );
        }
        if (type == typeof(DateOnly))
        {
            return DateOnly.Parse(text, CultureInfo.InvariantCulture);
        }
        if (type == typeof(TimeOnly))
        {
            return TimeOnly.Parse(text, CultureInfo.InvariantCulture);
        }
        if (type == typeof(TimeSpan))
        {
            return TimeSpan.ParseExact(text, "c", CultureInfo.InvariantCulture);
        }

        throw Unsupported(keyType);
    }

    private static NotSupportedException Unsupported(Type type) =>
        new(
            $"'{type}' is not a supported cursor key type. Use {nameof(JsonCursorSerializer)} for key types beyond the primitive ones."
        );
}
