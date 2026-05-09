using HoldFast.Analytics.Models;
using HoldFast.Data.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HoldFast.Data.Tests.Postgres;

/// <summary>
/// HOL-31: live integration tests for PostgresSessionAnalyticsStore.
/// </summary>
[Trait("Category", "PgIntegration")]
public class PostgresSessionAnalyticsStoreIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

    private PostgresSessionAnalyticsStore _store = null!;
    private bool _pgReachable;

    public async Task InitializeAsync()
    {
        try
        {
            await using var probe = new NpgsqlConnection(ConnectionString);
            await probe.OpenAsync();
            await using var cmd = probe.CreateCommand();
            cmd.CommandText = "SELECT to_regclass('analytics.sessions') IS NOT NULL";
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
        _store = new PostgresSessionAnalyticsStore(opts, config, NullLogger<PostgresSessionAnalyticsStore>.Instance);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static int NextProjectId() =>
        999_999_700 + (int)(DateTime.UtcNow.Ticks % 1_000);

    private static async Task CleanupAsync(int projectId)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        foreach (var sql in new[]
        {
            "DELETE FROM analytics.sessions WHERE project_id = @p",
            "DELETE FROM analytics.session_field_values WHERE project_id = @p",
        })
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p", projectId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [SkippableFact]
    public async Task WriteSessions_then_QuerySessionIds_returns_paginated_ids()
    {
        Skip.IfNot(_pgReachable, "Postgres + analytics schema not reachable");
        var pid = NextProjectId();
        try
        {
            var ts = DateTime.UtcNow;
            var batch = Enumerable.Range(1, 5).Select(i => new SessionRowInput
            {
                ProjectId = pid,
                SessionId = i * 100,
                CreatedAt = ts.AddMinutes(-i), // newest at i=1, oldest at i=5
                Identifier = $"user-{i}",
                OSName = "Linux",
                Environment = "test",
            }).ToList();

            await _store.WriteSessionsAsync(batch);

            var (ids, total) = await _store.QuerySessionIdsAsync(
                pid,
                new QueryInput
                {
                    DateRange = new DateRangeRequiredInput
                    {
                        StartDate = ts.AddMinutes(-10),
                        EndDate = ts.AddMinutes(1),
                    },
                },
                count: 3, page: 0,
                sortField: "created_at", sortDesc: true);

            Assert.Equal(5, total);
            Assert.Equal(3, ids.Count);
            // Newest 3 should be sessions 100, 200, 300 (i=1,2,3)
            Assert.Contains(100, ids);
            Assert.Contains(200, ids);
            Assert.Contains(300, ids);
        }
        finally
        {
            await CleanupAsync(pid);
        }
    }

    [SkippableFact]
    public async Task WriteSessions_populates_session_field_values_for_reserved_keys()
    {
        Skip.IfNot(_pgReachable, "Postgres + analytics schema not reachable");
        var pid = NextProjectId();
        try
        {
            var ts = DateTime.UtcNow;
            await _store.WriteSessionsAsync(
            [
                new SessionRowInput
                {
                    ProjectId = pid,
                    SessionId = 1,
                    CreatedAt = ts,
                    Identifier = "alice@example.com",
                    OSName = "macOS",
                    BrowserName = "Safari",
                    Environment = "prod",
                    Country = "US",
                },
                new SessionRowInput
                {
                    ProjectId = pid,
                    SessionId = 2,
                    CreatedAt = ts,
                    Identifier = "bob@example.com",
                    OSName = "Windows",
                    BrowserName = "Chrome",
                    Environment = "prod",
                    Country = "CA",
                },
            ]);

            var identifiers = await _store.GetSessionsKeyValuesAsync(
                pid, "identifier", ts.Date, ts.Date.AddDays(1), null, 100);
            Assert.Equal(2, identifiers.Count);
            Assert.Contains("alice@example.com", identifiers);
            Assert.Contains("bob@example.com", identifiers);

            var oses = await _store.GetSessionsKeyValuesAsync(
                pid, "os_name", ts.Date, ts.Date.AddDays(1), null, 100);
            Assert.Contains("macOS", oses);
            Assert.Contains("Windows", oses);

            // Empty/null values shouldn't appear in the catalog
            var states = await _store.GetSessionsKeyValuesAsync(
                pid, "state", ts.Date, ts.Date.AddDays(1), null, 100);
            Assert.Empty(states);
        }
        finally
        {
            await CleanupAsync(pid);
        }
    }

    [SkippableFact]
    public async Task ReadSessionsHistogram_buckets_by_hour()
    {
        Skip.IfNot(_pgReachable, "Postgres + analytics schema not reachable");
        var pid = NextProjectId();
        try
        {
            var hour1 = DateTime.UtcNow.Date.AddHours(10);
            var hour2 = DateTime.UtcNow.Date.AddHours(11);

            await _store.WriteSessionsAsync(
            [
                new SessionRowInput { ProjectId = pid, SessionId = 1, CreatedAt = hour1.AddMinutes(5) },
                new SessionRowInput { ProjectId = pid, SessionId = 2, CreatedAt = hour1.AddMinutes(20) },
                new SessionRowInput { ProjectId = pid, SessionId = 3, CreatedAt = hour2.AddMinutes(15) },
            ]);

            var buckets = await _store.ReadSessionsHistogramAsync(
                pid,
                new QueryInput
                {
                    DateRange = new DateRangeRequiredInput
                    {
                        StartDate = hour1.AddMinutes(-1),
                        EndDate = hour2.AddHours(1),
                    },
                });

            Assert.Equal(2, buckets.Count);
            Assert.Equal(2, buckets[0].Count);
            Assert.Equal(1, buckets[1].Count);
        }
        finally
        {
            await CleanupAsync(pid);
        }
    }

    [SkippableFact]
    public async Task GetSessionsKeys_returns_reserved_keys_filtered_by_query()
    {
        Skip.IfNot(_pgReachable, "Postgres + analytics schema not reachable");
        // GetSessionsKeysAsync doesn't hit the DB at all — it's a hardcoded list.
        // This test still runs without PG, but we keep it under PgIntegration
        // to keep the related tests grouped.

        var allKeys = await _store.GetSessionsKeysAsync(1, default, default, null);
        Assert.Contains(allKeys, k => k.Name == "identifier");
        Assert.Contains(allKeys, k => k.Name == "os_name");
        Assert.Contains(allKeys, k => k.Name == "browser_name");

        var browserOnly = await _store.GetSessionsKeysAsync(1, default, default, "browser");
        Assert.All(browserOnly, k => Assert.Contains("browser", k.Name, StringComparison.OrdinalIgnoreCase));
    }
}
