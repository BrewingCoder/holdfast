using HoldFast.Data.Postgres;

namespace HoldFast.Data.Tests.Postgres;

/// <summary>
/// HOL-31: unit tests for PostgresSessionAnalyticsStore pure helpers
/// (sort-field whitelist + DateOnlyOf).
///
/// The sort-field test is the critical security boundary: GraphQL passes a
/// caller-supplied string straight through, and the resolved column name
/// composes into ORDER BY. A whitelist ensures unknown values fall back to
/// `created_at` rather than being injected.
/// </summary>
public class PostgresSessionAnalyticsStoreTests
{
    [Theory]
    [InlineData("created_at", "created_at")]
    [InlineData("createdAt", "created_at")]
    [InlineData(null, "created_at")]
    [InlineData("", "created_at")]
    [InlineData("active_length", "active_length")]
    [InlineData("activeLength", "active_length")]
    [InlineData("length", "length")]
    [InlineData("pages_visited", "pages_visited")]
    [InlineData("pagesVisited", "pages_visited")]
    public void NormalizeSortField_returns_whitelisted_column(string? input, string expected)
    {
        Assert.Equal(expected, PostgresSessionAnalyticsStore.ResolveSortField(input));
    }

    [Theory]
    [InlineData("user_id")]                        // unknown column
    [InlineData("password")]                       // unknown column
    [InlineData("created_at; DROP TABLE x;--")]    // SQLi attempt
    [InlineData("(SELECT 1)")]                     // expression injection attempt
    [InlineData("1=1")]
    [InlineData("../../etc/passwd")]
    public void NormalizeSortField_falls_back_to_default_for_unsafe_input(string input)
    {
        // Critical: any input not in the whitelist (including SQLi attempts) must
        // resolve to created_at. A failure here would let an attacker control
        // ORDER BY content via GraphQL parameter — unlikely to be exploitable
        // for data extraction (ORDER BY can't dump rows) but a clear hygiene
        // failure.
        Assert.Equal("created_at", PostgresSessionAnalyticsStore.ResolveSortField(input));
    }

    [Fact]
    public void DateOnlyOf_default_returns_epoch_sentinel()
    {
        Assert.Equal(new DateOnly(1970, 1, 1), PostgresSessionAnalyticsStore.DateOnlyOf(default));
    }

    [Fact]
    public void DateOnlyOf_utc_truncates_correctly()
    {
        var ts = new DateTime(2026, 5, 9, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateOnly(2026, 5, 9), PostgresSessionAnalyticsStore.DateOnlyOf(ts));
    }
}
