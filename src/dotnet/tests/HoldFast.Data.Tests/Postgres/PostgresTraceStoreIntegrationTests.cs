using HoldFast.Analytics.Models;
using HoldFast.Data.Postgres;
using HoldFast.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HoldFast.Data.Tests.Postgres;

/// <summary>
/// HOL-30: live integration tests for PostgresTraceStore against
/// localhost:5432. Skips cleanly when no PG is reachable.
/// </summary>
[Trait("Category", "PgIntegration")]
public class PostgresTraceStoreIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

    private PostgresTraceStore _store = null!;
    private bool _pgReachable;

    public async Task InitializeAsync()
    {
        try
        {
            await using var probe = new NpgsqlConnection(ConnectionString);
            await probe.OpenAsync();
            await using var cmd = probe.CreateCommand();
            cmd.CommandText = "SELECT to_regclass('analytics.traces') IS NOT NULL";
            _pgReachable = (bool)(await cmd.ExecuteScalarAsync())!;
        }
        catch
        {
            _pgReachable = false;
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSQL"] = ConnectionString,
            })
            .Build();
        var opts = Options.Create(new PostgresAnalyticsOptions { Schema = "analytics" });
        _store = new PostgresTraceStore(opts, config, NullLogger<PostgresTraceStore>.Instance);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static int NextProjectId() =>
        999_999_500 + (int)(DateTime.UtcNow.Ticks % 1_000);

    private static async Task CleanupAsync(int projectId)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        foreach (var sql in new[]
        {
            "DELETE FROM analytics.traces WHERE project_id = @p",
            "DELETE FROM analytics.trace_keys WHERE project_id = @p",
            "DELETE FROM analytics.trace_key_values WHERE project_id = @p",
        })
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p", projectId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [SkippableFact]
    public async Task WriteTraces_then_ReadTraces_roundtrips_a_span()
    {
        Skip.IfNot(_pgReachable, "Postgres + analytics schema not reachable");
        var pid = NextProjectId();
        try
        {
            var ts = DateTime.UtcNow;
            await _store.WriteTracesAsync(
            [
                new TraceRowInput
                {
                    ProjectId = pid,
                    Timestamp = ts,
                    TraceId = "abc-123",
                    SpanId = "span-1",
                    ParentSpanId = "parent-1",
                    SpanName = "http.GET",
                    SpanKind = "Server",
                    Duration = 12_345_678,
                    ServiceName = "frontend-api",
                    ServiceVersion = "2.1.0",
                    Environment = "prod",
                    StatusCode = "OK",
                    StatusMessage = "",
                    HasErrors = false,
                    TraceAttributes = new Dictionary<string, string>
                    {
                        ["http.method"] = "GET",
                        ["http.route"] = "/api/users",
                    },
                },
            ]);

            var page = await _store.ReadTracesAsync(
                pid,
                new QueryInput
                {
                    DateRange = new DateRangeRequiredInput
                    {
                        StartDate = ts.AddMinutes(-1),
                        EndDate = ts.AddMinutes(1),
                    },
                },
                new ClickHousePagination { Limit = 10 });

            Assert.Single(page.Edges);
            var node = page.Edges[0].Node;
            Assert.Equal("abc-123", node.TraceId);
            Assert.Equal("span-1", node.SpanId);
            Assert.Equal("parent-1", node.ParentSpanId);
            Assert.Equal("http.GET", node.SpanName);
            Assert.Equal(SpanKind.Server, node.SpanKind);
            Assert.Equal(12_345_678, node.Duration);
            Assert.Equal("frontend-api", node.ServiceName);
            Assert.Equal("2.1.0", node.ServiceVersion);
            Assert.Equal("prod", node.Environment);
            Assert.Equal("GET", node.TraceAttributes["http.method"]);
            Assert.Equal("/api/users", node.TraceAttributes["http.route"]);
            // Events + Links are not yet wired (matches CH gap)
            Assert.Empty(node.Events);
            Assert.Empty(node.Links);
        }
        finally
        {
            await CleanupAsync(pid);
        }
    }

    [SkippableFact]
    public async Task WriteTraces_populates_trace_keys_and_trace_key_values()
    {
        Skip.IfNot(_pgReachable, "Postgres + analytics schema not reachable");
        var pid = NextProjectId();
        try
        {
            var ts = DateTime.UtcNow;
            await _store.WriteTracesAsync(
            [
                new TraceRowInput
                {
                    ProjectId = pid, Timestamp = ts, TraceId = "t1", SpanId = "s1",
                    SpanKind = "Server", SpanName = "GET /a",
                    TraceAttributes = new() { ["service.name"] = "api", ["env"] = "dev" },
                },
                new TraceRowInput
                {
                    ProjectId = pid, Timestamp = ts, TraceId = "t2", SpanId = "s2",
                    SpanKind = "Server", SpanName = "GET /b",
                    TraceAttributes = new() { ["service.name"] = "api", ["env"] = "prod" },
                },
            ]);

            var keys = await _store.GetTraceKeysAsync(
                pid,
                new QueryInput
                {
                    DateRange = new() { StartDate = ts.Date, EndDate = ts.Date.AddDays(1) },
                });

            Assert.Contains("service.name", keys);
            Assert.Contains("env", keys);

            var serviceValues = await _store.GetTraceKeyValuesAsync(
                pid, "service.name",
                new QueryInput
                {
                    DateRange = new() { StartDate = ts.Date, EndDate = ts.Date.AddDays(1) },
                });
            Assert.Single(serviceValues);
            Assert.Equal("api", serviceValues[0]);

            var envValues = await _store.GetTraceKeyValuesAsync(
                pid, "env",
                new QueryInput
                {
                    DateRange = new() { StartDate = ts.Date, EndDate = ts.Date.AddDays(1) },
                });
            Assert.Equal(2, envValues.Count);
            Assert.Contains("dev", envValues);
            Assert.Contains("prod", envValues);
        }
        finally
        {
            await CleanupAsync(pid);
        }
    }

    [SkippableFact]
    public async Task ReadTraces_filters_by_has_errors_via_query_filter_substring()
    {
        // Use the AppendQueryFilter path against span_name to verify the query
        // filter is wired correctly. (HasErrors filter itself is not yet
        // exposed on the QueryInput surface — same gap as CH.)
        Skip.IfNot(_pgReachable, "Postgres + analytics schema not reachable");
        var pid = NextProjectId();
        try
        {
            var ts = DateTime.UtcNow;
            await _store.WriteTracesAsync(
            [
                new TraceRowInput
                {
                    ProjectId = pid, Timestamp = ts, TraceId = "t1", SpanId = "s1",
                    SpanName = "GET /healthz", HasErrors = false,
                    TraceAttributes = new(),
                },
                new TraceRowInput
                {
                    ProjectId = pid, Timestamp = ts, TraceId = "t2", SpanId = "s2",
                    SpanName = "POST /api/orders", HasErrors = true,
                    TraceAttributes = new(),
                },
            ]);

            var allPage = await _store.ReadTracesAsync(
                pid,
                new QueryInput
                {
                    DateRange = new() { StartDate = ts.AddMinutes(-1), EndDate = ts.AddMinutes(1) },
                },
                new ClickHousePagination { Limit = 50 });
            Assert.Equal(2, allPage.Edges.Count);

            // Span-name substring filter — only orders.
            var ordersPage = await _store.ReadTracesAsync(
                pid,
                new QueryInput
                {
                    Query = "orders",
                    DateRange = new() { StartDate = ts.AddMinutes(-1), EndDate = ts.AddMinutes(1) },
                },
                new ClickHousePagination { Limit = 50 });
            Assert.Single(ordersPage.Edges);
            Assert.Contains("orders", ordersPage.Edges[0].Node.SpanName);
            Assert.True(ordersPage.Edges[0].Node.HasErrors);
        }
        finally
        {
            await CleanupAsync(pid);
        }
    }

    [SkippableFact]
    public async Task WriteTraces_aggregates_counts_across_batches()
    {
        Skip.IfNot(_pgReachable, "Postgres + analytics schema not reachable");
        var pid = NextProjectId();
        try
        {
            var ts = DateTime.UtcNow;

            // 4 spans each marked service.name=api over two batches (2+2)
            await _store.WriteTracesAsync(Enumerable.Range(0, 2).Select(i => new TraceRowInput
            {
                ProjectId = pid, Timestamp = ts,
                TraceId = $"b1-t{i}", SpanId = $"b1-s{i}",
                TraceAttributes = new() { ["service.name"] = "api" },
            }).ToList());
            await _store.WriteTracesAsync(Enumerable.Range(0, 2).Select(i => new TraceRowInput
            {
                ProjectId = pid, Timestamp = ts,
                TraceId = $"b2-t{i}", SpanId = $"b2-s{i}",
                TraceAttributes = new() { ["service.name"] = "api" },
            }).ToList());

            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT count FROM analytics.trace_keys
                WHERE project_id = @p AND key = 'service.name'", conn);
            cmd.Parameters.AddWithValue("p", pid);
            Assert.Equal(4L, (long)(await cmd.ExecuteScalarAsync())!);
        }
        finally
        {
            await CleanupAsync(pid);
        }
    }
}
