using System.Text.Json;
using System.Text.Json.Serialization;
using Cursor.EntityFrameworkCore;
using Xunit;

namespace Cursor.Tests.EntityFrameworkCore;

[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
internal partial class TestJsonContext : JsonSerializerContext;

public class JsonCursorSerializerTests
{
    private static readonly JsonSerializerOptions StrictOptions =
        new(TestJsonContext.Default.Options);

    [Fact]
    public void EncodeCursor_And_DecodeCursor_Int_RoundTrips()
    {
        var serializer = new JsonCursorSerializer(StrictOptions);
        var cursor = serializer.EncodeCursor(42);
        var decoded = serializer.DecodeCursor<int>(cursor);
        Assert.Equal(42, decoded);
    }

    [Fact]
    public void EncodeCursor_And_DecodeCursor_String_RoundTrips()
    {
        var serializer = new JsonCursorSerializer(StrictOptions);
        var cursor = serializer.EncodeCursor("hello");
        var decoded = serializer.DecodeCursor<string>(cursor);
        Assert.Equal("hello", decoded);
    }

    [Fact]
    public void EncodeCursor_And_DecodeCursor_DateTimeOffset_RoundTrips()
    {
        var serializer = new JsonCursorSerializer(StrictOptions);
        var value = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var cursor = serializer.EncodeCursor(value);
        var decoded = serializer.DecodeCursor<DateTimeOffset>(cursor);
        Assert.Equal(value, decoded);
    }

    [Fact]
    public void EncodeCompoundCursor_And_DecodeCompoundCursor_RoundTrips()
    {
        var serializer = new JsonCursorSerializer(StrictOptions);
        var keyValues = new List<object?> { 42, "hello" };
        var keyTypes = new List<Type> { typeof(int), typeof(string) };

        var cursor = serializer.EncodeCompoundCursor(keyValues);
        var decoded = serializer.DecodeCompoundCursor(cursor, keyTypes);

        Assert.Equal(2, decoded.Count);
        Assert.Equal(42, decoded[0]);
        Assert.Equal("hello", decoded[1]);
    }

    [Fact]
    public void EncodeCompoundCursor_And_DecodeCompoundCursor_WithNull_RoundTrips()
    {
        var serializer = new JsonCursorSerializer(StrictOptions);
        var keyValues = new List<object?> { 1, null };
        var keyTypes = new List<Type> { typeof(int), typeof(string) };

        var cursor = serializer.EncodeCompoundCursor(keyValues);
        var decoded = serializer.DecodeCompoundCursor(cursor, keyTypes);

        Assert.Equal(2, decoded.Count);
        Assert.Equal(1, decoded[0]);
        Assert.Null(decoded[1]);
    }

    [Fact]
    public void EncodeCompoundCursor_And_DecodeCompoundCursor_IntAndDateTimeOffset_RoundTrips()
    {
        var serializer = new JsonCursorSerializer(StrictOptions);
        var date = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var keyValues = new List<object?> { 7, date };
        var keyTypes = new List<Type> { typeof(int), typeof(DateTimeOffset) };

        var cursor = serializer.EncodeCompoundCursor(keyValues);
        var decoded = serializer.DecodeCompoundCursor(cursor, keyTypes);

        Assert.Equal(2, decoded.Count);
        Assert.Equal(7, decoded[0]);
        Assert.Equal(date, decoded[1]);
    }
}
