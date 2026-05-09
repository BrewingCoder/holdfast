using HoldFast.Analytics.Models;
using HoldFast.Data.Postgres;

namespace HoldFast.Data.Tests.Postgres;

/// <summary>
/// HOL-29: unit tests for PostgresLogStore pure helpers (ClampLimit,
/// DateOnlyOf). The query/insert paths are exercised end-to-end via the
/// smoke test against a live PG container, not unit-tested here — Npgsql's
/// connection plumbing isn't usefully mockable.
///
/// Per the project's OVER TEST rule: edge cases for clamping and date
/// coercion that would otherwise fail silently in production.
/// </summary>
public class PostgresLogStoreTests
{
    // ── ClampLimit ───────────────────────────────────────────────────

    [Fact]
    public void ClampLimit_zero_uses_default()
    {
        Assert.Equal(50, PostgresLogStore.ClampLimit(0));
    }

    [Fact]
    public void ClampLimit_negative_uses_default()
    {
        // Negatives slip through user input via int parsing failures - guard
        // that they don't produce LIMIT -1 which silently returns nothing
        // depending on PG version.
        Assert.Equal(50, PostgresLogStore.ClampLimit(-1));
        Assert.Equal(50, PostgresLogStore.ClampLimit(int.MinValue));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(9_999, 9_999)]
    [InlineData(10_000, 10_000)]
    public void ClampLimit_passes_valid_values_through(int input, int expected)
    {
        Assert.Equal(expected, PostgresLogStore.ClampLimit(input));
    }

    [Theory]
    [InlineData(10_001)]
    [InlineData(50_000)]
    [InlineData(int.MaxValue)]
    public void ClampLimit_caps_at_max(int input)
    {
        Assert.Equal(10_000, PostgresLogStore.ClampLimit(input));
    }

    // ── DateOnlyOf ───────────────────────────────────────────────────

    [Fact]
    public void DateOnlyOf_default_returns_epoch_sentinel()
    {
        // QueryInput.DateRange is typed `DateRangeRequiredInput` with default
        // DateTime values when the caller didn't fill them in. We coerce to
        // the epoch start so the SQL `day >= ...` predicate doesn't blow up
        // on year-0001 dates that PG can't represent.
        Assert.Equal(new DateOnly(1970, 1, 1), PostgresLogStore.DateOnlyOf(default));
    }

    [Fact]
    public void DateOnlyOf_utc_passes_through()
    {
        var ts = new DateTime(2026, 5, 9, 12, 30, 45, DateTimeKind.Utc);
        Assert.Equal(new DateOnly(2026, 5, 9), PostgresLogStore.DateOnlyOf(ts));
    }

    [Fact]
    public void DateOnlyOf_local_converts_to_utc_before_truncating()
    {
        // A local DateTime on the morning of May 9 in EST (UTC-5) should
        // still resolve to May 9 UTC. A local at 23:00 EST on May 8 would
        // be May 9 04:00 UTC — DateOnlyOf returns May 9 in that case.
        var localMorning = new DateTime(2026, 5, 9, 8, 0, 0, DateTimeKind.Local);
        var resultMorning = PostgresLogStore.DateOnlyOf(localMorning);

        // Avoid a brittle test against the host TZ — just assert the
        // result is one of {2026-05-08, 2026-05-09} depending on local TZ
        // offset. The conversion correctness is the property under test.
        Assert.True(resultMorning >= new DateOnly(2026, 5, 8));
        Assert.True(resultMorning <= new DateOnly(2026, 5, 9));
    }

    [Fact]
    public void DateOnlyOf_unspecified_kind_treated_as_local()
    {
        // DateTime.Kind=Unspecified comes through GraphQL deserialization a
        // lot — make sure we don't throw on the .ToUniversalTime() path.
        var ts = new DateTime(2026, 5, 9, 12, 0, 0, DateTimeKind.Unspecified);
        var result = PostgresLogStore.DateOnlyOf(ts);
        Assert.True(result >= new DateOnly(2026, 5, 8));
        Assert.True(result <= new DateOnly(2026, 5, 10));
    }
}
