using HoldFast.Data.Postgres;

namespace HoldFast.Data.Tests.Postgres;

/// <summary>
/// HOL-26 unit tests for the PostgresMigrationService pure helpers.
///
/// Note: there's no live-Postgres test here — the runner's actual SQL
/// execution is exercised end-to-end by the smoke test (compose up,
/// observe "Analytics PG migrations: 3 applied" in logs). Future PRs
/// may add a Testcontainers-based integration test once we have a
/// reason to hit migration edge cases (e.g. non-transactional DDL).
///
/// Per the project's "OVER TEST" .NET testing rule, this file covers
/// happy paths AND deliberate failure cases for both pure helpers.
/// </summary>
public class PostgresMigrationServiceTests
{
    // ── ParseVersion ─────────────────────────────────────────────────

    [Theory]
    [InlineData("0001_create_analytics_schema.up.sql", 1)]
    [InlineData("0042_add_logs_table.up.sql", 42)]
    [InlineData("999999_huge_version.up.sql", 999999)]
    [InlineData("0_zero_version.up.sql", 0)]
    public void ParseVersion_extracts_numeric_prefix(string filename, long expected)
    {
        Assert.Equal(expected, PostgresMigrationService.ParseVersion(filename));
    }

    [Theory]
    [InlineData("no_underscore.sql")]                         // no _ at all
    [InlineData("abc_text_prefix.up.sql")]                    // non-numeric prefix
    [InlineData("_starts_with_underscore.up.sql")]            // _ at index 0
    [InlineData("12.5_decimal_prefix.up.sql")]                // floats not allowed (long.TryParse rejects)
    [InlineData("")]                                          // empty filename
    public void ParseVersion_returns_null_when_prefix_invalid(string filename)
    {
        Assert.Null(PostgresMigrationService.ParseVersion(filename));
    }

    [Fact]
    public void ParseVersion_handles_filename_with_extra_underscores_in_description()
    {
        // Real migrations often have multiple underscores in their description
        // ("0042_add_user_table_for_oauth.up.sql"). Parser only cares about
        // the prefix before the FIRST underscore.
        Assert.Equal(42, PostgresMigrationService.ParseVersion("0042_add_user_table_for_oauth.up.sql"));
    }

    // ── SanitizeIdentifier ───────────────────────────────────────────

    [Theory]
    [InlineData("analytics", "analytics")]
    [InlineData("Analytics_v2", "Analytics_v2")]
    [InlineData("snake_case_name", "snake_case_name")]
    [InlineData("PascalCase", "PascalCase")]
    [InlineData("digits123", "digits123")]
    [InlineData("0_starts_with_digit", "0_starts_with_digit")]
    public void SanitizeIdentifier_passes_safe_identifiers_through(string raw, string expected)
    {
        Assert.Equal(expected, PostgresMigrationService.SanitizeIdentifier(raw));
    }

    [Theory]
    [InlineData("ana lytics", "analytics")]                   // strips spaces
    [InlineData("ana-lytics", "analytics")]                   // strips hyphens
    [InlineData("ana.lytics", "analytics")]                   // strips dots
    [InlineData("ana\"lytics", "analytics")]                  // strips quotes
    [InlineData("ana'lytics", "analytics")]                   // strips apostrophes
    [InlineData("ana;DROP TABLE foo;--", "anaDROPTABLEfoo")]  // strips SQLi attempt
    [InlineData("ana/lytics\\bar", "analyticsbar")]           // strips slashes
    public void SanitizeIdentifier_strips_unsafe_characters(string raw, string expected)
    {
        // The schema name comes from operator config, not user input, but stripping
        // anything that isn't [A-Za-z0-9_] guarantees we can't produce SQL injection
        // even on a typo or misconfiguration.
        Assert.Equal(expected, PostgresMigrationService.SanitizeIdentifier(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void SanitizeIdentifier_throws_on_empty_or_whitespace(string raw)
    {
        Assert.Throws<InvalidOperationException>(
            () => PostgresMigrationService.SanitizeIdentifier(raw));
    }

    [Theory]
    [InlineData(";--")]                                       // pure SQL injection
    [InlineData("()")]                                        // pure punctuation
    [InlineData("[]<>")]
    [InlineData(@"\\\\")]
    public void SanitizeIdentifier_throws_when_no_valid_chars_remain(string raw)
    {
        // Edge case: a config value that's entirely punctuation strips down to
        // empty string — we throw rather than silently use "" as the schema name
        // (which would make CREATE SCHEMA "" fail with a confusing PG error).
        Assert.Throws<InvalidOperationException>(
            () => PostgresMigrationService.SanitizeIdentifier(raw));
    }
}
