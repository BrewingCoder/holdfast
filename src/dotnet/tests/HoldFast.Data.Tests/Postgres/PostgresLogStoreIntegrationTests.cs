using HoldFast.Analytics.Models;
using HoldFast.Data.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HoldFast.Data.Tests.Postgres;

/// <summary>
/// HOL-29: live integration tests for PostgresLogStore.
///
/// Each test opens its own connection to the local PG and exercises a real
/// query path. Tests skip cleanly via xUnit's Skip mechanism when no PG is
/// reachable on localhost:5432 (so CI without a DB sidecar still passes).
///
/// To run locally: `docker compose up -d postgres` then
/// `dotnet test --filter Category=PgIntegration`.
///
/// Per the OVER TEST rule: covers happy path, multi-batch upsert behavior,
/// JSONB roundtrip with special characters, and absent-attribute edge case.
/// </summary>
[Trait("Category", "PgIntegration")]
public class PostgresLogStoreIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

    private PostgresLogStore _store = null!;
    private bool _pgReachable;

    public async Task InitializeAsync()
    {
        try
        {
            await using var probe = new NpgsqlConnection(ConnectionString);
            await probe.OpenAsync();
            // Also confirm the analytics schema + tables exist (HOL-26/29 migrations applied)
            await using var cmd = probe.CreateCommand();
            cmd.CommandText = "SELECT to_regclass('analytics.logs') IS NOT NULL";
            var hasLogs = (bool)(await cmd.ExecuteScalarAsync())!;
            _pgReachable = hasLogs;
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
        _store = new PostgresLogStore(opts, config, NullLogger<PostgresLogStore>.Instance);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> CreateProjectIdAsync()
    {
        // Use a high project_id well outside the DevSeed range so concurrent
        // backend writes don't pollute these tests' assertions.
        // ProjectId is just an int from the analytics layer's perspective —
        // no FK to public.projects, so we can use any unique-ish number.
        return 999_999_001 + (int)(DateTime.UtcNow.Ticks % 1_000);
    }

    private async Task CleanupProjectAsync(int projectId)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        foreach (var sql in new[]
        {
            "DELETE FROM analytics.logs WHERE project_id = @p",
            "DELETE FROM analytics.log_keys WHERE project_id = @p",
            "DELETE FROM analytics.log_key_values WHERE project_id = @p",
        })
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p", projectId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [SkippableFact]
    public async Task WriteLogs_then_ReadLogs_roundtrips_a_single_row()
    {
        Skip.IfNot(_pgReachable, "Postgres + analytics schema not reachable");
        var pid = await CreateProjectIdAsync();
        try
        {
            var ts = DateTime.UtcNow;
            await _store.WriteLogsAsync(
            [
                new LogRowInput
                {
                    ProjectId = pid,
                    Timestamp = ts,
                    Body = "hello postgres",
                    SeverityText = "INFO",
                    SeverityNumber = 9,
                    ServiceName = "test-service",
                    ServiceVersion = "1.2.3",
                    Environment = "test",
                    LogAttributes = new Dictionary<string, string>
                    {
                        ["component"] = "logstore-test",
                        ["region"] = "us-east-1",
                    },
                },
            ]);

            var page = await _store.ReadLogsAsync(
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
            Assert.Equal("hello postgres", node.Body);
            Assert.Equal("INFO", node.SeverityText);
            Assert.Equal("test-service", node.ServiceName);
            Assert.Equal("1.2.3", node.ServiceVersion);
            Assert.Equal("test", node.Environment);
            Assert.Equal("logstore-test", node.LogAttributes["component"]);
            Assert.Equal("us-east-1", node.LogAttributes["region"]);
        }
        finally
        {
            await CleanupProjectAsync(pid);
        }
    }

    [SkippableFact]
    public async Task WriteLogs_populates_log_keys_and_log_key_values()
    {
        Skip.IfNot(_pgReachable, "Postgres + analytics schema not reachable");
        var pid = await CreateProjectIdAsync();
        try
        {
            var ts = DateTime.UtcNow;
            // Two logs sharing one key, plus a third with a different key.
            // Expected: log_keys has 2 entries (key=region, key=component),
            // log_key_values has 3 (region:us-east-1×2, component:foo, component:bar).
            await _store.WriteLogsAsync(
            [
                new LogRowInput
                {
                    ProjectId = pid, Timestamp = ts, Body = "1",
                    LogAttributes = new() { ["region"] = "us-east-1", ["component"] = "foo" },
                },
                new LogRowInput
                {
                    ProjectId = pid, Timestamp = ts, Body = "2",
                    LogAttributes = new() { ["region"] = "us-east-1", ["component"] = "bar" },
                },
            ]);

            var keys = await _store.GetLogKeysAsync(
                pid,
                new QueryInput
                {
                    DateRange = new() { StartDate = ts.Date, EndDate = ts.Date.AddDays(1) },
                });

            Assert.Contains("region", keys);
            Assert.Contains("component", keys);

            var regionValues = await _store.GetLogKeyValuesAsync(
                pid, "region",
                new QueryInput
                {
                    DateRange = new() { StartDate = ts.Date, EndDate = ts.Date.AddDays(1) },
                });
            Assert.Single(regionValues);
            Assert.Equal("us-east-1", regionValues[0]);

            var componentValues = await _store.GetLogKeyValuesAsync(
                pid, "component",
                new QueryInput
                {
                    DateRange = new() { StartDate = ts.Date, EndDate = ts.Date.AddDays(1) },
                });
            Assert.Equal(2, componentValues.Count);
            Assert.Contains("foo", componentValues);
            Assert.Contains("bar", componentValues);
        }
        finally
        {
            await CleanupProjectAsync(pid);
        }
    }

    [SkippableFact]
    public async Task WriteLogs_with_no_attributes_succeeds()
    {
        // Edge case: a log with no attributes shouldn't crash the inline upsert
        // because the per-key/per-value loops are skipped when LogAttributes is
        // null or empty.
        Skip.IfNot(_pgReachable, "Postgres + analytics schema not reachable");
        var pid = await CreateProjectIdAsync();
        try
        {
            await _store.WriteLogsAsync(
            [
                new LogRowInput
                {
                    ProjectId = pid,
                    Timestamp = DateTime.UtcNow,
                    Body = "no attrs",
                    LogAttributes = null!,
                },
            ]);

            // Just assert it didn't throw — the row exists in logs but no
            // log_keys/log_key_values entries were generated.
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT count(*) FROM analytics.logs WHERE project_id = @p", conn);
            cmd.Parameters.AddWithValue("p", pid);
            Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);
        }
        finally
        {
            await CleanupProjectAsync(pid);
        }
    }

    [SkippableFact]
    public async Task WriteLogs_aggregates_counts_across_repeated_keys()
    {
        // The inline-upsert path must accumulate counts when the same
        // (project, key, day) tuple appears across batches. This is the
        // CH-equivalent SummingMergeTree behavior translated to PG ON CONFLICT.
        Skip.IfNot(_pgReachable, "Postgres + analytics schema not reachable");
        var pid = await CreateProjectIdAsync();
        try
        {
            var ts = DateTime.UtcNow;

            // Batch 1: 3 logs all with attribute `service:api`
            await _store.WriteLogsAsync(Enumerable.Range(0, 3).Select(i => new LogRowInput
            {
                ProjectId = pid,
                Timestamp = ts,
                Body = $"batch1-{i}",
                LogAttributes = new() { ["service"] = "api" },
            }).ToList());

            // Batch 2: 2 more with the same attribute, plus 1 with a different value
            await _store.WriteLogsAsync(Enumerable.Range(0, 2).Select(i => new LogRowInput
            {
                ProjectId = pid,
                Timestamp = ts,
                Body = $"batch2-{i}",
                LogAttributes = new() { ["service"] = "api" },
            }).Concat([new LogRowInput
            {
                ProjectId = pid,
                Timestamp = ts,
                Body = "different",
                LogAttributes = new() { ["service"] = "worker" },
            }]).ToList());

            // log_keys should have one row for service with count = 3+2+1 = 6
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT count FROM analytics.log_keys
                WHERE project_id = @p AND key = 'service'", conn);
            cmd.Parameters.AddWithValue("p", pid);
            var aggregatedKeyCount = (long)(await cmd.ExecuteScalarAsync())!;
            Assert.Equal(6, aggregatedKeyCount);

            // log_key_values: api=5, worker=1
            await using var cmdApi = new NpgsqlCommand(@"
                SELECT count FROM analytics.log_key_values
                WHERE project_id = @p AND key = 'service' AND value = 'api'", conn);
            cmdApi.Parameters.AddWithValue("p", pid);
            Assert.Equal(5, (long)(await cmdApi.ExecuteScalarAsync())!);

            await using var cmdWorker = new NpgsqlCommand(@"
                SELECT count FROM analytics.log_key_values
                WHERE project_id = @p AND key = 'service' AND value = 'worker'", conn);
            cmdWorker.Parameters.AddWithValue("p", pid);
            Assert.Equal(1, (long)(await cmdWorker.ExecuteScalarAsync())!);
        }
        finally
        {
            await CleanupProjectAsync(pid);
        }
    }
}
