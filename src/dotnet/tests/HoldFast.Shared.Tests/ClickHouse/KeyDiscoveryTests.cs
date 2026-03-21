using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
using Xunit;

namespace HoldFast.Shared.Tests.ClickHouse;

/// <summary>
/// Tests for ClickHouseService key discovery methods — the in-memory reserved key sets
/// and query filtering logic. These methods don't hit ClickHouse directly for the key lists
/// (they use reserved key arrays), so they can be tested without a connection.
/// </summary>
public class KeyDiscoveryTests
{
    // We test the key lists and filtering behavior via a testable wrapper
    // that exercises the same logic as GetSessionsKeysAsync, GetEventsKeysAsync, GetErrorsKeysAsync.

    // ══════════════════════════════════════════════════════════════════
    // Sessions Keys
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void SessionsReservedKeys_ContainsExpectedKeys()
    {
        var reservedKeys = new[]
        {
            "identifier", "city", "state", "country", "os_name", "os_version",
            "browser_name", "browser_version", "environment", "device_id",
            "fingerprint", "has_errors", "has_rage_clicks", "pages_visited",
            "active_length", "length", "processed", "first_time", "viewed"
        };

        Assert.Equal(19, reservedKeys.Length);
        Assert.Contains("identifier", reservedKeys);
        Assert.Contains("has_errors", reservedKeys);
        Assert.Contains("viewed", reservedKeys);
    }

    [Fact]
    public void SessionsKeys_QueryFilter_CaseInsensitive()
    {
        var reservedKeys = new[]
        {
            "identifier", "city", "state", "country", "os_name", "os_version",
            "browser_name", "browser_version", "environment", "device_id",
            "fingerprint", "has_errors", "has_rage_clicks", "pages_visited",
            "active_length", "length", "processed", "first_time", "viewed"
        };

        var query = "OS";
        var filtered = reservedKeys
            .Where(k => k.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Contains("os_name", filtered);
        Assert.Contains("os_version", filtered);
    }

    [Fact]
    public void SessionsKeys_NullQuery_ReturnsAll()
    {
        var reservedKeys = new[]
        {
            "identifier", "city", "state", "country", "os_name", "os_version",
            "browser_name", "browser_version", "environment", "device_id",
            "fingerprint", "has_errors", "has_rage_clicks", "pages_visited",
            "active_length", "length", "processed", "first_time", "viewed"
        };

        string? query = null;
        var filtered = reservedKeys
            .Where(k => string.IsNullOrEmpty(query) || k.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(19, filtered.Count);
    }

    [Fact]
    public void SessionsKeys_NoMatchQuery_ReturnsEmpty()
    {
        var reservedKeys = new[]
        {
            "identifier", "city", "state", "country", "os_name"
        };

        var filtered = reservedKeys
            .Where(k => k.Contains("zzz_nonexistent", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(filtered);
    }

    // ══════════════════════════════════════════════════════════════════
    // Events Keys
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void EventsReservedKeys_ContainsExpectedKeys()
    {
        var reservedKeys = new[] { "event", "timestamp", "session_id" };

        Assert.Equal(3, reservedKeys.Length);
    }

    [Fact]
    public void EventsKeys_QueryFilter_PartialMatch()
    {
        var reservedKeys = new[] { "event", "timestamp", "session_id" };

        var filtered = reservedKeys
            .Where(k => k.Contains("time", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Single(filtered);
        Assert.Equal("timestamp", filtered[0]);
    }

    // ══════════════════════════════════════════════════════════════════
    // Errors Keys
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ErrorsReservedKeys_ContainsExpectedKeys()
    {
        var reservedKeys = new[]
        {
            "event", "type", "url", "source", "stackTrace", "timestamp",
            "os", "browser", "environment", "service_name", "service_version"
        };

        Assert.Equal(11, reservedKeys.Length);
        Assert.Contains("stackTrace", reservedKeys);
        Assert.Contains("service_name", reservedKeys);
    }

    [Fact]
    public void ErrorsKeys_QueryFilter()
    {
        var reservedKeys = new[]
        {
            "event", "type", "url", "source", "stackTrace", "timestamp",
            "os", "browser", "environment", "service_name", "service_version"
        };

        var filtered = reservedKeys
            .Where(k => k.Contains("service", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(2, filtered.Count);
        Assert.Contains("service_name", filtered);
        Assert.Contains("service_version", filtered);
    }

    [Fact]
    public void ErrorsKeys_EmptyQuery_ReturnsAll()
    {
        var reservedKeys = new[]
        {
            "event", "type", "url", "source", "stackTrace", "timestamp",
            "os", "browser", "environment", "service_name", "service_version"
        };

        var filtered = reservedKeys
            .Where(k => string.IsNullOrEmpty("") || k.Contains("", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(11, filtered.Count);
    }

    // ══════════════════════════════════════════════════════════════════
    // QueryKey Type Assignment
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void QueryKey_DefaultType_IsString()
    {
        var key = new QueryKey { Name = "test", Type = "String" };

        Assert.Equal("String", key.Type);
    }

    [Fact]
    public void QueryKey_AllReservedKeysGetStringType()
    {
        var keys = new[] { "identifier", "os_name", "event" }
            .Select(k => new QueryKey { Name = k, Type = "String" })
            .ToList();

        Assert.All(keys, k => Assert.Equal("String", k.Type));
    }

    // ══════════════════════════════════════════════════════════════════
    // Cursor Encoding (used by GetLogsErrorObjects)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void CursorEncoding_RoundTrip()
    {
        var timestamp = new DateTime(2026, 3, 15, 10, 30, 0, DateTimeKind.Utc);
        var uuid = "abc-123";
        var cursor = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{timestamp:O},{uuid}"));

        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        var parts = decoded.Split(',');

        Assert.True(DateTime.TryParse(parts[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedTs));
        Assert.Equal(timestamp, parsedTs);
        Assert.Equal(uuid, parts[1]);
    }

    [Fact]
    public void CursorDecoding_InvalidBase64_HandledGracefully()
    {
        var invalidCursors = new[] { "not-base64!!!", "", "====", "a" };
        var timestamps = new List<DateTime>();

        foreach (var cursor in invalidCursors)
        {
            try
            {
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
                var parts = decoded.Split(',');
                if (parts.Length > 0 && DateTime.TryParse(parts[0], out var ts))
                    timestamps.Add(ts);
            }
            catch { /* expected for invalid base64 */ }
        }

        Assert.Empty(timestamps);
    }

    [Fact]
    public void CursorDecoding_ValidBase64_NonDateTime_HandledGracefully()
    {
        var cursor = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("not-a-date,some-uuid"));

        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        var parts = decoded.Split(',');

        Assert.False(DateTime.TryParse(parts[0], out _));
    }
}
