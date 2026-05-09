using HoldFast.Analytics.Models;
using HoldFast.Data.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HoldFast.Data.Tests.Postgres;

/// <summary>
/// HOL-33: unit tests for PostgresMetricStore aggregator + column whitelist.
/// </summary>
public class PostgresMetricStoreTests
{
    [Theory]
    [InlineData("COUNT", "count(*)")]
    [InlineData("count", "count(*)")]
    [InlineData("COUNT_DISTINCT", "count(DISTINCT metric_value)")]
    [InlineData("countdistinct", "count(DISTINCT metric_value)")]
    [InlineData("SUM", "sum(metric_value)")]
    [InlineData("AVG", "avg(metric_value)")]
    [InlineData("MIN", "min(metric_value)")]
    [InlineData("MAX", "max(metric_value)")]
    [InlineData("P50", "percentile_cont(0.50) WITHIN GROUP (ORDER BY metric_value)")]
    [InlineData("P90", "percentile_cont(0.90) WITHIN GROUP (ORDER BY metric_value)")]
    [InlineData("P95", "percentile_cont(0.95) WITHIN GROUP (ORDER BY metric_value)")]
    [InlineData("P99", "percentile_cont(0.99) WITHIN GROUP (ORDER BY metric_value)")]
    public void BuildAggregatorExpression_translates_known_aggregators(string aggregator, string expected)
    {
        Assert.Equal(expected, PostgresMetricStore.BuildAggregatorExpression(aggregator, "metric_value"));
    }

    [Theory]
    [InlineData("UNKNOWN")]
    [InlineData("DROP TABLE")]
    [InlineData("")]
    [InlineData(null)]
    public void BuildAggregatorExpression_falls_back_to_count_for_unknown(string? aggregator)
    {
        Assert.Equal("count(*)", PostgresMetricStore.BuildAggregatorExpression(aggregator!, "metric_value"));
    }

    [Theory]
    [InlineData("metric_value", "metric_value")]
    [InlineData("value", "metric_value")]
    [InlineData("count", "metric_value")]
    [InlineData(null, "metric_value")]
    [InlineData("", "metric_value")]
    [InlineData("user_password", "metric_value")] // unknown -> safe default
    [InlineData("'; DROP TABLE x", "metric_value")] // SQLi attempt -> safe default
    public void ResolveColumn_whitelists_known_columns(string? input, string expected)
    {
        Assert.Equal(expected, PostgresMetricStore.ResolveColumn(input));
    }
}

/// <summary>
/// HOL-33: live PG integration tests for metric write/read.
/// </summary>
[Trait("Category", "PgIntegration")]
public class PostgresMetricStoreIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

    private PostgresMetricStore _store = null!;
    private bool _pgReachable;

    public async Task InitializeAsync()
    {
        try
        {
            await using var probe = new NpgsqlConnection(ConnectionString);
            await probe.OpenAsync();
            await using var cmd = probe.CreateCommand();
            cmd.CommandText = "SELECT to_regclass('analytics.metrics') IS NOT NULL";
            _pgReachable = (bool)(await cmd.ExecuteScalarAsync())!;
        }
        catch { _pgReachable = false; }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSQL"] = ConnectionString,
            })
            .Build();
        _store = new PostgresMetricStore(
            Options.Create(new PostgresAnalyticsOptions { Schema = "analytics" }),
            config,
            NullLogger<PostgresMetricStore>.Instance);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static int NextProjectId() => 999_998_000 + (int)(DateTime.UtcNow.Ticks % 1_000);

    private static MetricRowInput MetricRow(int pid, string name, double value, DateTime ts) =>
        new()
        {
            ProjectId = pid,
            MetricName = name,
            Value = value,
            Kind = MetricKind.Gauge,
            Timestamp = ts,
        };

    private static async Task CleanupAsync(int projectId)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM analytics.metrics WHERE project_id = @p", conn);
        cmd.Parameters.AddWithValue("p", projectId);
        await cmd.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task WriteMetric_then_ReadMetrics_aggregates_with_sum()
    {
        Skip.IfNot(_pgReachable, "Postgres + analytics schema not reachable");
        var pid = NextProjectId();
        try
        {
            var ts = DateTime.UtcNow;
            // 3 metric points: 10, 20, 30 — sum should be 60, count 3, avg 20
            await _store.WriteMetricAsync(MetricRow(pid, "request_count", 10, ts));
            await _store.WriteMetricAsync(MetricRow(pid, "request_count", 20, ts));
            await _store.WriteMetricAsync(MetricRow(pid, "request_count", 30, ts));

            var queryInput = new QueryInput
            {
                DateRange = new DateRangeRequiredInput
                {
                    StartDate = ts.AddMinutes(-1),
                    EndDate = ts.AddMinutes(1),
                },
            };

            var sumResult = await _store.ReadMetricsAsync(pid, queryInput, "none", null, "SUM", "metric_value");
            Assert.Single(sumResult.Buckets);
            Assert.Equal(60.0, sumResult.Buckets[0].Value);
            Assert.Equal(3, sumResult.Buckets[0].Count);

            var avgResult = await _store.ReadMetricsAsync(pid, queryInput, "none", null, "AVG", "metric_value");
            Assert.Equal(20.0, avgResult.Buckets[0].Value);

            var p50Result = await _store.ReadMetricsAsync(pid, queryInput, "none", null, "P50", "metric_value");
            Assert.Equal(20.0, p50Result.Buckets[0].Value);
        }
        finally
        {
            await CleanupAsync(pid);
        }
    }

    [SkippableFact]
    public async Task WriteMetric_with_tags_persists_them_as_jsonb()
    {
        Skip.IfNot(_pgReachable, "Postgres + analytics schema not reachable");
        var pid = NextProjectId();
        try
        {
            await _store.WriteMetricAsync(new MetricRowInput
            {
                ProjectId = pid,
                MetricName = "latency",
                Value = 123.4,
                Kind = MetricKind.Gauge,
                Timestamp = DateTime.UtcNow,
                Attributes = new Dictionary<string, string>
                {
                    ["service.name"] = "frontend",
                    ["http.method"] = "GET",
                },
                SecureSessionId = "abc-secure",
            });

            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT tags ->> 'service.name', tags ->> 'http.method', secure_session_id
                FROM analytics.metrics WHERE project_id = @p", conn);
            cmd.Parameters.AddWithValue("p", pid);
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            Assert.Equal("frontend", reader.GetString(0));
            Assert.Equal("GET", reader.GetString(1));
            Assert.Equal("abc-secure", reader.GetString(2));
        }
        finally
        {
            await CleanupAsync(pid);
        }
    }
}
