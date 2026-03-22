using HoldFast.Domain.Enums;

namespace HoldFast.Domain.Tests;

public class LogEnumsTests
{
    // ── LogLevel ────────────────────────────────────────────────────

    [Fact]
    public void LogLevel_HasCorrectSeverityNumbers()
    {
        Assert.Equal(1, (int)LogLevel.Trace);
        Assert.Equal(5, (int)LogLevel.Debug);
        Assert.Equal(9, (int)LogLevel.Info);
        Assert.Equal(13, (int)LogLevel.Warn);
        Assert.Equal(17, (int)LogLevel.Error);
        Assert.Equal(21, (int)LogLevel.Fatal);
    }

    [Fact]
    public void LogLevel_SeverityIncreases()
    {
        Assert.True(LogLevel.Trace < LogLevel.Debug);
        Assert.True(LogLevel.Debug < LogLevel.Info);
        Assert.True(LogLevel.Info < LogLevel.Warn);
        Assert.True(LogLevel.Warn < LogLevel.Error);
        Assert.True(LogLevel.Error < LogLevel.Fatal);
    }

    [Fact]
    public void LogLevel_HasSixValues()
    {
        Assert.Equal(6, Enum.GetValues<LogLevel>().Length);
    }

    [Fact]
    public void LogLevel_AllDistinct()
    {
        var values = Enum.GetValues<LogLevel>().Select(v => (int)v).ToList();
        Assert.Equal(values.Count, values.Distinct().Count());
    }

    // ── LogSource ───────────────────────────────────────────────────

    [Fact]
    public void LogSource_HasFrontendAndBackend()
    {
        Assert.Equal(0, (int)LogSource.Frontend);
        Assert.Equal(1, (int)LogSource.Backend);
    }

    [Fact]
    public void LogSource_HasTwoValues()
    {
        Assert.Equal(2, Enum.GetValues<LogSource>().Length);
    }

    // ── SpanKind ────────────────────────────────────────────────────

    [Fact]
    public void SpanKind_HasFiveValues()
    {
        Assert.Equal(5, Enum.GetValues<SpanKind>().Length);
    }

    [Theory]
    [InlineData(SpanKind.Internal)]
    [InlineData(SpanKind.Server)]
    [InlineData(SpanKind.Client)]
    [InlineData(SpanKind.Producer)]
    [InlineData(SpanKind.Consumer)]
    public void SpanKind_AllValuesExist(SpanKind kind)
    {
        Assert.True(Enum.IsDefined(kind));
    }

    // ── MetricAggregator ────────────────────────────────────────────

    [Fact]
    public void MetricAggregator_HasTwelveValues()
    {
        Assert.Equal(12, Enum.GetValues<MetricAggregator>().Length);
    }

    [Theory]
    [InlineData(MetricAggregator.Count)]
    [InlineData(MetricAggregator.CountDistinct)]
    [InlineData(MetricAggregator.Sum)]
    [InlineData(MetricAggregator.Avg)]
    [InlineData(MetricAggregator.Min)]
    [InlineData(MetricAggregator.Max)]
    [InlineData(MetricAggregator.P50)]
    [InlineData(MetricAggregator.P90)]
    [InlineData(MetricAggregator.P95)]
    [InlineData(MetricAggregator.P99)]
    public void MetricAggregator_AllValuesExist(MetricAggregator agg)
    {
        Assert.True(Enum.IsDefined(agg));
    }

    [Fact]
    public void MetricAggregator_AllDistinct()
    {
        var values = Enum.GetValues<MetricAggregator>().Select(v => (int)v).ToList();
        Assert.Equal(values.Count, values.Distinct().Count());
    }

    // ── ProductType ─────────────────────────────────────────────────

    [Fact]
    public void ProductType_HasSixValues()
    {
        Assert.Equal(6, Enum.GetValues<ProductType>().Length);
    }

    [Theory]
    [InlineData(ProductType.Sessions)]
    [InlineData(ProductType.Errors)]
    [InlineData(ProductType.Logs)]
    [InlineData(ProductType.Traces)]
    [InlineData(ProductType.Metrics)]
    [InlineData(ProductType.Events)]
    public void ProductType_AllValuesExist(ProductType type)
    {
        Assert.True(Enum.IsDefined(type));
    }
}
