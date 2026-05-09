using HoldFast.Data.Postgres;

namespace HoldFast.Data.Tests.Postgres;

/// <summary>
/// HOL-30: unit tests for PostgresTraceStore pure helpers.
/// Same shape as PostgresLogStoreTests — confirms the per-helper guard logic
/// (ClampLimit, DateOnlyOf) is duplicated correctly between the two stores
/// rather than silently diverging in cleanup.
/// </summary>
public class PostgresTraceStoreTests
{
    [Theory]
    [InlineData(0, 50)]
    [InlineData(-1, 50)]
    [InlineData(int.MinValue, 50)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(10_000, 10_000)]
    [InlineData(10_001, 10_000)]
    [InlineData(int.MaxValue, 10_000)]
    public void ClampLimit_matches_log_store_behavior(int input, int expected)
    {
        // The two stores should clamp identically — if they ever diverge,
        // GraphQL pagination behavior would too.
        Assert.Equal(expected, PostgresTraceStore.ClampLimit(input));
        Assert.Equal(expected, PostgresLogStore.ClampLimit(input));
    }

    [Fact]
    public void DateOnlyOf_default_returns_epoch_sentinel()
    {
        Assert.Equal(new DateOnly(1970, 1, 1), PostgresTraceStore.DateOnlyOf(default));
    }

    [Fact]
    public void DateOnlyOf_utc_truncates_correctly()
    {
        var ts = new DateTime(2026, 5, 9, 23, 59, 59, DateTimeKind.Utc);
        Assert.Equal(new DateOnly(2026, 5, 9), PostgresTraceStore.DateOnlyOf(ts));
    }
}
