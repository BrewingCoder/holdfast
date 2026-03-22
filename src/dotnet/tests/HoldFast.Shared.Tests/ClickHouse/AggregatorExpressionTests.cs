using System.Reflection;
using HoldFast.Data.ClickHouse;

namespace HoldFast.Shared.Tests.ClickHouse;

/// <summary>
/// Tests for the BuildAggregatorExpression method via reflection (it's private static).
/// Ensures all MetricAggregator enum values map to valid ClickHouse SQL.
/// </summary>
public class AggregatorExpressionTests
{
    private static string BuildAggregator(string aggregator, string column = "Value")
    {
        var method = typeof(ClickHouseService).GetMethod(
            "BuildAggregatorExpression",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (string)method!.Invoke(null, [aggregator, column])!;
    }

    [Theory]
    [InlineData("COUNT", "count(*)")]
    [InlineData("count", "count(*)")]
    [InlineData("Count", "count(*)")]
    public void Count_CaseInsensitive(string input, string expected)
    {
        Assert.Equal(expected, BuildAggregator(input));
    }

    [Theory]
    [InlineData("COUNT_DISTINCT", "uniq(Value)")]
    [InlineData("COUNTDISTINCT", "uniq(Value)")]
    public void CountDistinct_BothFormats(string input, string expected)
    {
        Assert.Equal(expected, BuildAggregator(input));
    }

    [Fact]
    public void Sum_UsesColumn()
    {
        Assert.Equal("sum(Duration)", BuildAggregator("SUM", "Duration"));
    }

    [Fact]
    public void Avg_UsesColumn()
    {
        Assert.Equal("avg(ResponseTime)", BuildAggregator("AVG", "ResponseTime"));
    }

    [Fact]
    public void Min_UsesColumn()
    {
        Assert.Equal("min(Value)", BuildAggregator("MIN"));
    }

    [Fact]
    public void Max_UsesColumn()
    {
        Assert.Equal("max(Value)", BuildAggregator("MAX"));
    }

    [Theory]
    [InlineData("P50", "quantile(0.50)(Value)")]
    [InlineData("P90", "quantile(0.90)(Value)")]
    [InlineData("P95", "quantile(0.95)(Value)")]
    [InlineData("P99", "quantile(0.99)(Value)")]
    public void Percentiles_UseQuantile(string aggregator, string expected)
    {
        Assert.Equal(expected, BuildAggregator(aggregator));
    }

    [Fact]
    public void P50_CustomColumn()
    {
        Assert.Equal("quantile(0.50)(Latency)", BuildAggregator("P50", "Latency"));
    }

    [Fact]
    public void Unknown_DefaultsToCount()
    {
        Assert.Equal("count(*)", BuildAggregator("INVALID_AGG"));
    }

    [Fact]
    public void Empty_DefaultsToCount()
    {
        Assert.Equal("count(*)", BuildAggregator(""));
    }

    // ── All MetricAggregator enum values produce valid SQL ─────────

    [Theory]
    [InlineData("Count")]
    [InlineData("CountDistinct")]
    [InlineData("Sum")]
    [InlineData("Avg")]
    [InlineData("Min")]
    [InlineData("Max")]
    [InlineData("P50")]
    [InlineData("P90")]
    [InlineData("P95")]
    [InlineData("P99")]
    public void AllEnumValues_ProduceNonEmptyExpression(string aggregator)
    {
        var result = BuildAggregator(aggregator);
        Assert.False(string.IsNullOrEmpty(result));
        // Should be a valid ClickHouse function call or keyword
        Assert.Matches(@"^(count\(\*\)|[a-z]+\([^)]*\)(\([^)]*\))?)$", result);
    }
}
