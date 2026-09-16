using System.Buffers.Text;
using System.Text;
using Cursor.Serialization;
using Xunit;

namespace Cursor.Tests;

public class PrimitiveCursorSerializerTests
{
    private readonly PrimitiveCursorSerializer _serializer = new();

    [Fact]
    public void EncodeCursor_And_DecodeCursor_Int_RoundTrips() => AssertRoundTrips(42);

    [Fact]
    public void EncodeCursor_And_DecodeCursor_NegativeLong_RoundTrips() =>
        AssertRoundTrips(long.MinValue);

    [Fact]
    public void EncodeCursor_And_DecodeCursor_Bool_RoundTrips()
    {
        AssertRoundTrips(true);
        AssertRoundTrips(false);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("")]
    [InlineData("3:121")] // Looks like a compound token.
    [InlineData("-")] // Looks like the null marker.
    [InlineData("a:b-c:d")]
    [InlineData("æøå 漢字")]
    public void EncodeCursor_And_DecodeCursor_String_RoundTrips(string value) =>
        AssertRoundTrips(value);

    [Fact]
    public void EncodeCursor_And_DecodeCursor_Guid_RoundTrips() =>
        AssertRoundTrips(Guid.Parse("7b4f0c2e-1d3a-4f5b-8c6d-9e0f1a2b3c4d"));

    [Fact]
    public void EncodeCursor_And_DecodeCursor_Decimal_RoundTrips() => AssertRoundTrips(1.50m);

    [Fact]
    public void EncodeCursor_And_DecodeCursor_Double_RoundTrips()
    {
        AssertRoundTrips(0.1 + 0.2);
        AssertRoundTrips(1e-300);
    }

    [Fact]
    public void EncodeCursor_And_DecodeCursor_DateTime_PreservesKind()
    {
        var value = new DateTime(2024, 1, 15, 10, 30, 0, 123, DateTimeKind.Utc).AddTicks(4567);

        var decoded = _serializer.DecodeCursor<DateTime>(_serializer.EncodeCursor(value));

        Assert.Equal(value, decoded);
        Assert.Equal(value.Kind, decoded.Kind);
    }

    [Fact]
    public void EncodeCursor_And_DecodeCursor_DateTimeOffset_RoundTrips() =>
        AssertRoundTrips(new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.FromHours(2)));

    [Fact]
    public void EncodeCursor_And_DecodeCursor_DateOnly_RoundTrips() =>
        AssertRoundTrips(new DateOnly(2024, 2, 29));

    [Fact]
    public void EncodeCursor_And_DecodeCursor_TimeOnly_RoundTrips() =>
        AssertRoundTrips(new TimeOnly(13, 45, 30, 123));

    [Fact]
    public void EncodeCursor_And_DecodeCursor_TimeSpan_RoundTrips() =>
        AssertRoundTrips(TimeSpan.FromTicks(-123456789012345));

    [Fact]
    public void EncodeCursor_And_DecodeCursor_Enum_RoundTrips() =>
        AssertRoundTrips(Status.Archived);

    [Fact]
    public void EncodeCursor_ProducesUrlSafeCursors()
    {
        // Every byte value appears, so any padding or non-url-safe base64 character would show up.
        var value = string.Concat(Enumerable.Range(1, 255).Select(c => (char)c));

        var cursor = _serializer.EncodeCursor(value);

        Assert.DoesNotContain("+", cursor);
        Assert.DoesNotContain("/", cursor);
        Assert.DoesNotContain("=", cursor);
        Assert.Equal(value, _serializer.DecodeCursor<string>(cursor));
    }

    [Fact]
    public void EncodeCursor_UnsupportedType_Throws() =>
        Assert.Throws<NotSupportedException>(() => _serializer.EncodeCursor(new object()));

    [Fact]
    public void DecodeCursor_UnsupportedType_Throws() =>
        Assert.Throws<NotSupportedException>(() =>
            _serializer.DecodeCursor<object>(_serializer.EncodeCursor("x"))
        );

    [Fact]
    public void CompoundCursor_RoundTrips()
    {
        List<object?> keyValues = ["hello", 42, Status.Active];
        List<Type> keyTypes = [typeof(string), typeof(int), typeof(Status)];

        var decoded = _serializer.DecodeCompoundCursor(
            _serializer.EncodeCompoundCursor(keyValues),
            keyTypes
        );

        Assert.Equal(keyValues, decoded);
    }

    [Fact]
    public void CompoundCursor_WithNulls_RoundTrips()
    {
        List<object?> keyValues = [null, 42, null];
        List<Type> keyTypes = [typeof(string), typeof(int?), typeof(Guid?)];

        var decoded = _serializer.DecodeCompoundCursor(
            _serializer.EncodeCompoundCursor(keyValues),
            keyTypes
        );

        Assert.Equal(keyValues, decoded);
    }

    /// <summary>
    /// The compound format is length prefixed precisely so that a value containing the separator
    /// or the null marker needs no escaping.
    /// </summary>
    [Fact]
    public void CompoundCursor_ValuesThatLookLikeTokens_RoundTrip()
    {
        List<object?> keyValues = ["3:121", "-", "", ":", "12:x"];
        List<Type> keyTypes =
        [
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(string),
        ];

        var decoded = _serializer.DecodeCompoundCursor(
            _serializer.EncodeCompoundCursor(keyValues),
            keyTypes
        );

        Assert.Equal(keyValues, decoded);
    }

    [Fact]
    public void CompoundCursor_HasAStableWireFormat()
    {
        // Pins the encoding: "121" and "165" encode as the tokens 3:121 and 3:165, concatenated
        // and base64url encoded. A cursor handed out by an earlier version has to keep decoding.
        List<object?> keyValues = ["121", "165"];

        var cursor = _serializer.EncodeCompoundCursor(keyValues);

        Assert.Equal("MzoxMjEzOjE2NQ", cursor);
        Assert.Equal(
            keyValues,
            _serializer.DecodeCompoundCursor(cursor, [typeof(string), typeof(string)])
        );
    }

    [Theory]
    [InlineData("")] // No tokens at all.
    [InlineData("NTphYg")] // "5:ab" - longer than what follows.
    [InlineData("eDphYg")] // "x:ab" - length is not a number.
    [InlineData("MzphYg")] // "3:ab" - one character short.
    [InlineData("!!!!")] // Not base64url.
    public void DecodeCompoundCursor_MalformedCursor_Throws(string cursor) =>
        Assert.Throws<FormatException>(() =>
            _serializer.DecodeCompoundCursor(cursor, [typeof(string)])
        );

    [Fact]
    public void DecodeCompoundCursor_TooManyKeysInCursor_Throws()
    {
        var cursor = _serializer.EncodeCompoundCursor(["a", "b"]);

        Assert.Throws<FormatException>(() =>
            _serializer.DecodeCompoundCursor(cursor, [typeof(string)])
        );
    }

    [Fact]
    public void DecodeCompoundCursor_TooFewKeysInCursor_Throws()
    {
        var cursor = _serializer.EncodeCompoundCursor(["a"]);

        Assert.Throws<FormatException>(() =>
            _serializer.DecodeCompoundCursor(cursor, [typeof(string), typeof(string)])
        );
    }

    [Fact]
    public void DecodeCursor_MalformedCursor_Throws() =>
        Assert.Throws<FormatException>(() => _serializer.DecodeCursor<int>(Encode("not a number")));

    [Fact]
    public void DefaultCursorSerializer_IsThePrimitiveOne() =>
        Assert.IsType<PrimitiveCursorSerializer>(CursorOptions.Default.CursorSerializer);

    private void AssertRoundTrips<TKey>(TKey value)
        where TKey : notnull =>
        Assert.Equal(value, _serializer.DecodeCursor<TKey>(_serializer.EncodeCursor(value)));

    private static string Encode(string text) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text));

    private enum Status
    {
        Draft = 0,
        Active = 1,
        Archived = 2,
    }
}
