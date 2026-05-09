using HoldFast.Analytics;
using HoldFast.Analytics.Models;

namespace HoldFast.Shared.Tests.Analytics;

/// <summary>
/// HOL-25 acceptance test: prove the new analytics interfaces are mockable
/// in unit tests without spinning up a ClickHouse container.
///
/// Before HOL-25, IClickHouseService was a 25-method kitchen-sink — mocking
/// it required stubbing every method even when a test only cared about one.
/// The seven domain interfaces let a test depend on the smallest contract
/// it actually needs.
///
/// These are *seam* tests — they verify the abstraction itself, not any
/// ClickHouse behavior. The full ClickHouse query logic is exercised by
/// the existing 3,000+ tests against IClickHouseService.
/// </summary>
public class MockableStoreTests
{
    [Fact]
    public async Task FakeLogStore_can_substitute_for_real_implementation()
    {
        // Arrange — a fake ILogStore returning deterministic data.
        // Implements only ILogStore (6 methods), not the 25-method IClickHouseService.
        ILogStore store = new FakeLogStore();

        // Act
        var logs = await store.ReadLogsAsync(
            projectId: 42,
            new QueryInput { Query = "level:error" },
            new ClickHousePagination { Limit = 10 });

        // Assert
        Assert.Single(logs.Edges);
        Assert.Equal("test-uuid", logs.Edges[0].Node.UUID);
        Assert.Equal("ERROR", logs.Edges[0].Node.SeverityText);
        Assert.False(logs.PageInfo.HasNextPage);
    }

    [Fact]
    public async Task FakeLogStore_records_writes_so_callers_can_assert()
    {
        // Arrange
        var store = new FakeLogStore();
        var input = new LogRowInput
        {
            ProjectId = 42,
            Timestamp = new DateTime(2026, 5, 9, 0, 0, 0, DateTimeKind.Utc),
            Body = "hello world",
            SeverityText = "INFO",
        };

        // Act
        await store.WriteLogsAsync([input]);

        // Assert
        Assert.Single(store.Written);
        Assert.Equal("hello world", store.Written[0].Body);
        Assert.Equal(42, store.Written[0].ProjectId);
    }

    [Fact]
    public void All_seven_domain_interfaces_compile_independently()
    {
        // Compile-time assertion: each interface stands on its own.
        // If a future refactor accidentally merges two interfaces or pulls
        // an unrelated method into one, this test fails to compile and
        // catches the regression before it lands.
        Assert.True(typeof(ILogStore).IsInterface);
        Assert.True(typeof(ITraceStore).IsInterface);
        Assert.True(typeof(ISessionAnalyticsStore).IsInterface);
        Assert.True(typeof(IErrorAnalyticsStore).IsInterface);
        Assert.True(typeof(IMetricStore).IsInterface);
        Assert.True(typeof(IEventFieldStore).IsInterface);
        Assert.True(typeof(IAlertStateStore).IsInterface);
    }

    [Fact]
    public void Domain_interfaces_live_in_HoldFast_Analytics_assembly()
    {
        // Guards against future refactors that might pull the abstractions
        // back into a backend-specific assembly. The whole point of HOL-25
        // is that these interfaces sit in a project that does NOT depend on
        // any client SDK (ClickHouse.Client, Npgsql, Dapper, …).
        var asm = typeof(ILogStore).Assembly;
        Assert.Equal("HoldFast.Analytics", asm.GetName().Name);
    }

    /// <summary>
    /// Minimal ILogStore stub: returns one synthetic log row, records writes,
    /// throws NotImplementedException for the methods this test doesn't exercise.
    /// </summary>
    private sealed class FakeLogStore : ILogStore
    {
        public List<LogRowInput> Written { get; } = [];

        public Task<LogConnection> ReadLogsAsync(
            int projectId, QueryInput query, ClickHousePagination pagination,
            CancellationToken ct = default) =>
            Task.FromResult(new LogConnection
            {
                Edges =
                [
                    new LogEdge
                    {
                        Node = new LogRow
                        {
                            UUID = "test-uuid",
                            ProjectId = projectId,
                            SeverityText = "ERROR",
                            Body = "synthetic",
                            Timestamp = DateTime.UtcNow,
                        },
                        Cursor = "test-cursor",
                    },
                ],
                PageInfo = new PageInfo
                {
                    HasNextPage = false,
                    HasPreviousPage = false,
                    StartCursor = "test-cursor",
                    EndCursor = "test-cursor",
                },
            });

        public Task WriteLogsAsync(IEnumerable<LogRowInput> logs, CancellationToken ct = default)
        {
            Written.AddRange(logs);
            return Task.CompletedTask;
        }

        public Task<List<HistogramBucket>> ReadLogsHistogramAsync(
            int projectId, QueryInput query, CancellationToken ct = default) =>
            throw new NotImplementedException("not exercised by this test");

        public Task<List<string>> GetLogKeysAsync(
            int projectId, QueryInput query, CancellationToken ct = default) =>
            throw new NotImplementedException("not exercised by this test");

        public Task<List<string>> GetLogKeyValuesAsync(
            int projectId, string key, QueryInput query, CancellationToken ct = default) =>
            throw new NotImplementedException("not exercised by this test");

        public Task<long> CountLogsAsync(
            int projectId, string? query, DateTime startDate, DateTime endDate,
            CancellationToken ct = default) =>
            throw new NotImplementedException("not exercised by this test");
    }
}
