using HoldFast.Data.Postgres;

namespace HoldFast.Data.Tests.Postgres;

/// <summary>
/// HOL-32: unit tests for PostgresErrorAnalyticsStore.SanitizeColumnName.
///
/// Critical security boundary: keyName comes from GraphQL caller input and
/// resolves to a column name composed directly into SQL (Npgsql can't
/// parameterize column identifiers, only values). Whitelist must reject
/// anything not in the known set.
/// </summary>
public class PostgresErrorAnalyticsStoreTests
{
    [Theory]
    [InlineData("event", "error_groups", "event")]
    [InlineData("type", "error_groups", "type")]
    [InlineData("service_name", "error_groups", "service_name")]
    [InlineData("serviceName", "error_groups", "service_name")]
    [InlineData("url", "error_objects", "url")]
    [InlineData("visitedurl", "error_objects", "url")]    // case-insensitive alias
    [InlineData("os", "error_objects", "os")]
    [InlineData("osname", "error_objects", "os")]
    [InlineData("browser", "error_objects", "browser")]
    [InlineData("environment", "error_objects", "environment")]
    [InlineData("service_version", "error_objects", "service_version")]
    public void SanitizeColumnName_resolves_known_keys(string input, string expectedTable, string expectedColumn)
    {
        var result = PostgresErrorAnalyticsStore.SanitizeColumnName(input);
        Assert.NotNull(result);
        Assert.Equal(expectedTable, result.Value.Table);
        Assert.Equal(expectedColumn, result.Value.Column);
    }

    [Theory]
    [InlineData("EVENT")]                 // uppercase still resolves
    [InlineData("Service_Name")]          // mixed case
    [InlineData("BROWSER")]
    public void SanitizeColumnName_is_case_insensitive(string input)
    {
        Assert.NotNull(PostgresErrorAnalyticsStore.SanitizeColumnName(input));
    }

    [Theory]
    [InlineData("source")]                // in the keys list but not a real column
    [InlineData("stackTrace")]            // listed but not queryable
    [InlineData("timestamp")]             // listed but not text-valued
    [InlineData("user_id")]               // unknown
    [InlineData("password")]              // unknown
    [InlineData("event; DROP TABLE x")]   // SQLi attempt
    [InlineData("(SELECT 1)")]
    [InlineData("")]                      // empty
    [InlineData(null)]                    // null
    public void SanitizeColumnName_returns_null_for_unsafe_or_unsupported_keys(string? input)
    {
        Assert.Null(PostgresErrorAnalyticsStore.SanitizeColumnName(input!));
    }
}
