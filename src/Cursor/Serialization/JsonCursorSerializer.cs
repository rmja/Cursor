using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Cursor.Serialization;

/// <summary>
/// An <see cref="ICursorSerializer"/> that represents cursor values as base64url encoded JSON.
/// </summary>
/// <remarks>
/// Key type metadata is resolved through <see cref="JsonSerializerOptions.TypeInfoResolver"/> rather than
/// through the reflection based <see cref="JsonSerializer"/> overloads, which is what keeps this serializer
/// trim and native AOT safe. The supplied options must therefore be able to resolve every key type, which in
/// a trimmed or AOT compiled application means supplying a source generated <see cref="JsonSerializerContext"/>.
/// A key type that cannot be resolved fails with a <see cref="NotSupportedException"/> when a cursor is encoded
/// or decoded. Consider <see cref="PrimitiveCursorSerializer"/>, which needs no metadata at all for the usual
/// key types and produces shorter cursors.
/// </remarks>
public class JsonCursorSerializer : ICursorSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Creates a serializer that resolves key type metadata using <paramref name="options"/>.
    /// </summary>
    /// <param name="options">
    /// The options used to serialize cursor values. Its <see cref="JsonSerializerOptions.TypeInfoResolver"/>
    /// must be able to resolve the key types, for example by using a source generated <see cref="JsonSerializerContext"/>.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="options"/> does not specify a <see cref="JsonSerializerOptions.TypeInfoResolver"/>.
    /// </exception>
    public JsonCursorSerializer(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Locking the options up front fails fast on a missing resolver, and lets the repeated
        // GetTypeInfo lookups below hit the options cache instead of re-resolving on every cursor.
        options.MakeReadOnly();

        _options = options;
    }

    /// <summary>
    /// Creates a serializer that resolves key type metadata using reflection.
    /// </summary>
    [RequiresUnreferencedCode(
        "JSON serialization of cursor values might require types that cannot be statically analyzed. Use the constructor that takes JsonSerializerOptions with a source generated JsonSerializerContext, or use PrimitiveCursorSerializer."
    )]
    [RequiresDynamicCode(
        "JSON serialization of cursor values might require runtime code generation. Use the constructor that takes JsonSerializerOptions with a source generated JsonSerializerContext, or use PrimitiveCursorSerializer."
    )]
    public JsonCursorSerializer()
        : this(JsonSerializerOptions.Default) { }

    /// <inheritdoc />
    public string EncodeCursor<TKey>(TKey keyValue)
        where TKey : notnull =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(keyValue, GetTypeInfo<TKey>()));

    /// <inheritdoc />
    public TKey DecodeCursor<TKey>(string cursor)
        where TKey : notnull =>
        JsonSerializer.Deserialize(Base64Url.DecodeFromChars(cursor), GetTypeInfo<TKey>())!;

    /// <inheritdoc />
    public string EncodeCompoundCursor(List<object?> keyValues)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
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
                    JsonSerializer.Serialize(writer, value, _options.GetTypeInfo(value.GetType()));
                }
            }
            writer.WriteEndArray();
        }

        return Base64Url.EncodeToString(buffer.WrittenSpan);
    }

    /// <inheritdoc />
    public List<object?> DecodeCompoundCursor(string cursor, List<Type> keyPropertyTypes)
    {
        var utf8Json = Base64Url.DecodeFromChars(cursor);
        using var document = JsonDocument.Parse(utf8Json);
        var root = document.RootElement;
        if (root.GetArrayLength() != keyPropertyTypes.Count)
        {
            throw new InvalidOperationException("Cursor key count mismatch");
        }

        var values = new List<object?>(keyPropertyTypes.Count);
        foreach (var (type, valueElement) in keyPropertyTypes.Zip(root.EnumerateArray()))
        {
            values.Add(valueElement.Deserialize(_options.GetTypeInfo(type)));
        }

        return values;
    }

    private JsonTypeInfo<TKey> GetTypeInfo<TKey>() =>
        (JsonTypeInfo<TKey>)_options.GetTypeInfo(typeof(TKey));
}
