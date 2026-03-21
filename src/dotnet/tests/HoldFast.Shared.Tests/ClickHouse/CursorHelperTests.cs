using HoldFast.Data.ClickHouse;

namespace HoldFast.Shared.Tests.ClickHouse;

public class CursorHelperTests
{
    // ── Encode ─────────────────────────────────────────────────────

    [Fact]
    public void Encode_ProducesBase64String()
    {
        var ts = new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Utc);
        var cursor = CursorHelper.Encode(ts, "abc123");
        Assert.False(string.IsNullOrEmpty(cursor));
        // Valid base64 should not throw
        var bytes = Convert.FromBase64String(cursor);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void Encode_MatchesGoFormat()
    {
        // Go format: base64("{RFC3339},{uuid}")
        // RFC3339 for Go: 2006-01-02T15:04:05Z07:00
        var ts = new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Utc);
        var cursor = CursorHelper.Encode(ts, "test-uuid");

        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        Assert.Equal("2026-03-20T12:00:00Z,test-uuid", decoded);
    }

    [Fact]
    public void Encode_LocalTimeConvertsToUtc()
    {
        var local = new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Local);
        var utc = local.ToUniversalTime();
        var cursor = CursorHelper.Encode(local, "uuid");

        var (decodedTs, _) = CursorHelper.Decode(cursor);
        Assert.Equal(utc, decodedTs, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Encode_EmptyUuid_StillWorks()
    {
        var ts = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var cursor = CursorHelper.Encode(ts, "");
        var (_, uuid) = CursorHelper.Decode(cursor);
        Assert.Equal("", uuid);
    }

    [Fact]
    public void Encode_UuidWithSpecialChars()
    {
        var ts = DateTime.UtcNow;
        var cursor = CursorHelper.Encode(ts, "uuid-with-dashes-and_underscores");
        var (_, uuid) = CursorHelper.Decode(cursor);
        Assert.Equal("uuid-with-dashes-and_underscores", uuid);
    }

    [Fact]
    public void Encode_MinDateTimeUtc()
    {
        var cursor = CursorHelper.Encode(DateTime.MinValue, "min");
        Assert.NotNull(cursor);
        var (ts, uuid) = CursorHelper.Decode(cursor);
        Assert.Equal("min", uuid);
    }

    // ── Decode ─────────────────────────────────────────────────────

    [Fact]
    public void Decode_RoundTrips()
    {
        var ts = new DateTime(2026, 3, 20, 15, 30, 45, DateTimeKind.Utc);
        var encoded = CursorHelper.Encode(ts, "my-uuid-123");

        var (decodedTs, decodedUuid) = CursorHelper.Decode(encoded);
        Assert.Equal(ts, decodedTs);
        Assert.Equal("my-uuid-123", decodedUuid);
    }

    [Fact]
    public void Decode_InvalidBase64_Throws()
    {
        Assert.Throws<ArgumentException>(() => CursorHelper.Decode("not-base64!!!"));
    }

    [Fact]
    public void Decode_MissingComma_Throws()
    {
        // base64 of "no-comma-here"
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("no-comma-here"));
        Assert.Throws<ArgumentException>(() => CursorHelper.Decode(encoded));
    }

    [Fact]
    public void Decode_TooManyCommas_Throws()
    {
        // base64 of "a,b,c"
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("a,b,c"));
        Assert.Throws<ArgumentException>(() => CursorHelper.Decode(encoded));
    }

    [Fact]
    public void Decode_InvalidTimestamp_Throws()
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("not-a-date,uuid"));
        Assert.Throws<ArgumentException>(() => CursorHelper.Decode(encoded));
    }

    [Fact]
    public void Decode_EmptyString_Throws()
    {
        Assert.Throws<ArgumentException>(() => CursorHelper.Decode(""));
    }

    // ── TryDecode ──────────────────────────────────────────────────

    [Fact]
    public void TryDecode_ValidCursor_ReturnsTrue()
    {
        var encoded = CursorHelper.Encode(DateTime.UtcNow, "uuid-1");
        Assert.True(CursorHelper.TryDecode(encoded, out var ts, out var uuid));
        Assert.Equal("uuid-1", uuid);
        Assert.NotEqual(default, ts);
    }

    [Fact]
    public void TryDecode_Null_ReturnsFalse()
    {
        Assert.False(CursorHelper.TryDecode(null, out _, out _));
    }

    [Fact]
    public void TryDecode_EmptyString_ReturnsFalse()
    {
        Assert.False(CursorHelper.TryDecode("", out _, out _));
    }

    [Fact]
    public void TryDecode_Garbage_ReturnsFalse()
    {
        Assert.False(CursorHelper.TryDecode("definitely-not-a-cursor!!!", out _, out _));
    }

    [Fact]
    public void TryDecode_ValidBase64ButBadFormat_ReturnsFalse()
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("just-one-field"));
        Assert.False(CursorHelper.TryDecode(encoded, out _, out _));
    }

    // ── Cross-compatibility with Go ────────────────────────────────

    [Fact]
    public void Decode_GoGeneratedCursor()
    {
        // Simulate what Go would generate: base64("2026-03-20T12:00:00Z,abc-123")
        var goPayload = "2026-03-20T12:00:00Z,abc-123";
        var goCursor = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(goPayload));

        var (ts, uuid) = CursorHelper.Decode(goCursor);
        Assert.Equal(new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Utc), ts);
        Assert.Equal("abc-123", uuid);
    }

    [Fact]
    public void Encode_ProducesCursorDecodableByGoLogic()
    {
        var ts = new DateTime(2026, 6, 15, 8, 30, 0, DateTimeKind.Utc);
        var cursor = CursorHelper.Encode(ts, "span-456");

        // Verify the raw format is what Go would expect
        var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        var parts = raw.Split(',');
        Assert.Equal(2, parts.Length);
        Assert.Equal("2026-06-15T08:30:00Z", parts[0]);
        Assert.Equal("span-456", parts[1]);
    }

    [Theory]
    [InlineData("2026-01-01T00:00:00Z", "uuid-a")]
    [InlineData("2026-12-31T23:59:59Z", "uuid-b")]
    [InlineData("2026-06-15T12:30:45Z", "")]
    [InlineData("2026-03-20T00:00:00Z", "long-uuid-with-lots-of-characters-1234567890")]
    public void Encode_Decode_Roundtrip_Theory(string timestampStr, string uuid)
    {
        var ts = DateTime.Parse(timestampStr, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var cursor = CursorHelper.Encode(ts, uuid);
        var (decodedTs, decodedUuid) = CursorHelper.Decode(cursor);

        Assert.Equal(ts, decodedTs);
        Assert.Equal(uuid, decodedUuid);
    }
}
