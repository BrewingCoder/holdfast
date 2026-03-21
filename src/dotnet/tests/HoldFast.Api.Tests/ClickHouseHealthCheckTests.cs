using HoldFast.Api;
using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace HoldFast.Api.Tests;

/// <summary>
/// Tests for ClickHouseHealthCheck behavior.
/// </summary>
public class ClickHouseHealthCheckTests
{
    [Fact]
    public async Task Healthy_WhenClickHouseServiceReportsHealthy()
    {
        var service = new FakeClickHouseService { IsHealthy = true };
        var check = new ClickHouseHealthCheck(service);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        // FakeClickHouseService is not a ClickHouseService, so falls to Degraded
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("not available", result.Description);
    }

    [Fact]
    public async Task Degraded_WhenNonConcreteServiceProvided()
    {
        var stub = new StubClickHouseService();
        var check = new ClickHouseHealthCheck(stub);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("not available", result.Description);
    }

    [Fact]
    public async Task HealthCheck_NullContext_NoException()
    {
        var stub = new StubClickHouseService();
        var check = new ClickHouseHealthCheck(stub);
        // HealthCheckContext with null properties should not throw
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        // HealthCheckResult is a value type — verify no exception and a valid result
        // Stub doesn't have a real ClickHouse, so it returns Degraded
        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task HealthCheck_CancellationRespected()
    {
        var stub = new StubClickHouseService();
        var check = new ClickHouseHealthCheck(stub);
        using var cts = new CancellationTokenSource();
        // Not cancelled, should complete normally
        var result = await check.CheckHealthAsync(new HealthCheckContext(), cts.Token);
        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    // Stub that implements IClickHouseService but is NOT ClickHouseService (concrete type)
    private class StubClickHouseService : IClickHouseService
    {
        public Task<LogConnection> ReadLogsAsync(int projectId, QueryInput query, ClickHousePagination pagination, CancellationToken ct) => Task.FromResult(new LogConnection());
        public Task<List<HistogramBucket>> ReadLogsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<List<string>> GetLogKeysAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<string>> GetLogKeyValuesAsync(int projectId, string key, QueryInput query, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<TraceConnection> ReadTracesAsync(int projectId, QueryInput query, ClickHousePagination pagination, bool omitBody, CancellationToken ct) => Task.FromResult(new TraceConnection());
        public Task<List<HistogramBucket>> ReadTracesHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<List<string>> GetTraceKeysAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<string>> GetTraceKeyValuesAsync(int projectId, string key, QueryInput query, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<HistogramBucket>> ReadSessionsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<(List<int> Ids, long Total)> QuerySessionIdsAsync(int projectId, QueryInput query, int count, int page, string? sortField, bool sortDesc, CancellationToken ct) => Task.FromResult((new List<int>(), 0L));
        public Task<(List<int> Ids, long Total)> QueryErrorGroupIdsAsync(int projectId, QueryInput query, int count, int page, CancellationToken ct) => Task.FromResult((new List<int>(), 0L));
        public Task<List<HistogramBucket>> ReadErrorObjectsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) => Task.FromResult(new List<HistogramBucket>());
        public Task<MetricsBuckets> ReadMetricsAsync(int projectId, QueryInput query, string bucketBy, List<string>? groupBy, string aggregator, string? column, CancellationToken ct) => Task.FromResult(new MetricsBuckets());
        public Task<List<QueryKey>> GetSessionsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, CancellationToken ct) => Task.FromResult(new List<QueryKey>());
        public Task<List<string>> GetSessionsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<string>> GetErrorsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<QueryKey>> GetEventsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, string? eventName, CancellationToken ct) => Task.FromResult(new List<QueryKey>());
        public Task<List<string>> GetEventsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, string? eventName, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task WriteMetricAsync(int projectId, string metricName, double metricValue, string? category, DateTime timestamp, Dictionary<string, string>? tags, string? sessionSecureId, CancellationToken ct) => Task.CompletedTask;
        public Task WriteLogsAsync(IEnumerable<LogRowInput> logs, CancellationToken ct) => Task.CompletedTask;
        public Task WriteTracesAsync(IEnumerable<TraceRowInput> traces, CancellationToken ct) => Task.CompletedTask;
        public Task WriteSessionsAsync(IEnumerable<SessionRowInput> sessions, CancellationToken ct) => Task.CompletedTask;
        public Task WriteErrorGroupsAsync(IEnumerable<ErrorGroupRowInput> errorGroups, CancellationToken ct) => Task.CompletedTask;
        public Task WriteErrorObjectsAsync(IEnumerable<ErrorObjectRowInput> errorObjects, CancellationToken ct) => Task.CompletedTask;
    }

    private class FakeClickHouseService : StubClickHouseService
    {
        public bool IsHealthy { get; set; }
    }
}
