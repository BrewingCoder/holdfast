using HoldFast.Data;
using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.Shared.Notifications;
using HoldFast.Worker;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoldFast.Worker.Tests;

/// <summary>
/// Tests for LogAlertWatcherWorker.EvaluateAlertAsync — the per-alert evaluation
/// logic that counts logs and fires notifications when thresholds are breached.
/// </summary>
public class LogAlertWatcherWorkerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly LogAlertWatcherWorker _worker;
    private readonly FakeLogClickHouseService _fakeClickHouse;
    private readonly FakeNotificationService _fakeNotifications;
    private readonly Workspace _workspace;
    private readonly Project _project;
    private readonly IServiceScopeFactory _scopeFactory;

    public LogAlertWatcherWorkerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _workspace = new Workspace
        {
            Name = "WS",
            PlanTier = "Enterprise",
            RetentionPeriod = RetentionPeriod.ThirtyDays,
            ErrorsRetentionPeriod = RetentionPeriod.ThirtyDays,
        };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _project = new Project { Name = "Proj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _fakeClickHouse = new FakeLogClickHouseService();
        _fakeNotifications = new FakeNotificationService();

        var services = new ServiceCollection();
        services.AddSingleton(new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options);
        services.AddScoped(sp => new HoldFastDbContext(
            sp.GetRequiredService<DbContextOptions<HoldFastDbContext>>()));
        services.AddSingleton<IClickHouseService>(_fakeClickHouse);
        services.AddSingleton<INotificationService>(_fakeNotifications);

        _scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        _worker = new LogAlertWatcherWorker(
            _scopeFactory,
            NullLogger<LogAlertWatcherWorker>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private LogAlert MakeAlert(
        int countThreshold = 10,
        int? belowThreshold = null,
        string? webhookDest = null,
        string? query = null,
        int? thresholdWindow = null) => new LogAlert
    {
        ProjectId = _project.Id,
        Name = "Test Alert",
        Disabled = false,
        CountThreshold = countThreshold,
        BelowThreshold = belowThreshold,
        WebhookDestinations = webhookDest,
        Query = query,
        ThresholdWindow = thresholdWindow,
    };

    // ── Alert-condition tests ────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAlertAsync_CountAtThreshold_Fires()
    {
        var alert = MakeAlert(countThreshold: 5, webhookDest: "https://hook.example.com");
        _fakeClickHouse.LogCount = 5; // exactly at threshold

        await _worker.EvaluateAlertAsync(alert, CancellationToken.None);

        Assert.Single(_fakeNotifications.SentWebhooks);
        Assert.Equal("https://hook.example.com", _fakeNotifications.SentWebhooks[0].Url);
    }

    [Fact]
    public async Task EvaluateAlertAsync_CountAboveThreshold_Fires()
    {
        var alert = MakeAlert(countThreshold: 5, webhookDest: "https://hook.example.com");
        _fakeClickHouse.LogCount = 100;

        await _worker.EvaluateAlertAsync(alert, CancellationToken.None);

        Assert.Single(_fakeNotifications.SentWebhooks);
    }

    [Fact]
    public async Task EvaluateAlertAsync_CountBelowThreshold_DoesNotFire()
    {
        var alert = MakeAlert(countThreshold: 10, webhookDest: "https://hook.example.com");
        _fakeClickHouse.LogCount = 3; // below threshold

        await _worker.EvaluateAlertAsync(alert, CancellationToken.None);

        Assert.Empty(_fakeNotifications.SentWebhooks);
    }

    [Fact]
    public async Task EvaluateAlertAsync_BelowThresholdCondition_FiresWhenCountLow()
    {
        // BelowThreshold set: alert fires when count <= countThreshold
        var alert = MakeAlert(countThreshold: 5, belowThreshold: 1, webhookDest: "https://hook.example.com");
        _fakeClickHouse.LogCount = 2; // below 5

        await _worker.EvaluateAlertAsync(alert, CancellationToken.None);

        Assert.Single(_fakeNotifications.SentWebhooks);
    }

    [Fact]
    public async Task EvaluateAlertAsync_BelowThresholdCondition_DoesNotFireWhenCountHigh()
    {
        var alert = MakeAlert(countThreshold: 5, belowThreshold: 1, webhookDest: "https://hook.example.com");
        _fakeClickHouse.LogCount = 10;

        await _worker.EvaluateAlertAsync(alert, CancellationToken.None);

        Assert.Empty(_fakeNotifications.SentWebhooks);
    }

    [Fact]
    public async Task EvaluateAlertAsync_ZeroCountThreshold_FiresOnAnyLog()
    {
        var alert = MakeAlert(countThreshold: 0, webhookDest: "https://hook.example.com");
        _fakeClickHouse.LogCount = 1;

        await _worker.EvaluateAlertAsync(alert, CancellationToken.None);

        Assert.Single(_fakeNotifications.SentWebhooks);
    }

    // ── Notification tests ───────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAlertAsync_NoWebhookDest_NoNotificationSent()
    {
        var alert = MakeAlert(countThreshold: 5, webhookDest: null);
        _fakeClickHouse.LogCount = 100;

        await _worker.EvaluateAlertAsync(alert, CancellationToken.None);

        Assert.Empty(_fakeNotifications.SentWebhooks);
    }

    [Fact]
    public async Task EvaluateAlertAsync_EmptyWebhookDest_NoNotificationSent()
    {
        var alert = MakeAlert(countThreshold: 5, webhookDest: "");
        _fakeClickHouse.LogCount = 100;

        await _worker.EvaluateAlertAsync(alert, CancellationToken.None);

        Assert.Empty(_fakeNotifications.SentWebhooks);
    }

    // ── LogAlertEvent recording ──────────────────────────────────────────

    [Fact]
    public async Task EvaluateAlertAsync_WhenFires_RecordsLogAlertEvent()
    {
        var alert = MakeAlert(countThreshold: 5, webhookDest: "https://hook.example.com");
        _db.Set<LogAlert>().Add(alert);
        _db.SaveChanges();

        _fakeClickHouse.LogCount = 10;

        await _worker.EvaluateAlertAsync(alert, CancellationToken.None);

        // Use a fresh context to verify the event was persisted
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoldFastDbContext>();
        var events = db.Set<LogAlertEvent>().ToList();
        Assert.Single(events);
        Assert.Equal(alert.Id, events[0].LogAlertId);
        Assert.Equal(10, events[0].Count);
    }

    [Fact]
    public async Task EvaluateAlertAsync_WhenDoesNotFire_NoLogAlertEvent()
    {
        var alert = MakeAlert(countThreshold: 10);
        _db.Set<LogAlert>().Add(alert);
        _db.SaveChanges();

        _fakeClickHouse.LogCount = 3;

        await _worker.EvaluateAlertAsync(alert, CancellationToken.None);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoldFastDbContext>();
        Assert.Empty(db.Set<LogAlertEvent>().ToList());
    }

    // ── Query and time window ────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAlertAsync_PassesQueryToClickHouse()
    {
        var alert = MakeAlert(countThreshold: 5, query: "level:error service:api");
        _fakeClickHouse.LogCount = 10;

        await _worker.EvaluateAlertAsync(alert, CancellationToken.None);

        Assert.Equal("level:error service:api", _fakeClickHouse.LastQuery);
    }

    [Fact]
    public async Task EvaluateAlertAsync_DefaultsTo5MinThresholdWindow()
    {
        var alert = MakeAlert(countThreshold: 5); // no ThresholdWindow set
        _fakeClickHouse.LogCount = 1;

        await _worker.EvaluateAlertAsync(alert, CancellationToken.None);

        // Default window is 300 seconds (5 min) — verify time range is ~5 min
        Assert.True(_fakeClickHouse.LastWindowSeconds >= 295 && _fakeClickHouse.LastWindowSeconds <= 305,
            $"Expected ~300s window, got {_fakeClickHouse.LastWindowSeconds}s");
    }

    [Fact]
    public async Task EvaluateAlertAsync_CustomThresholdWindow_Used()
    {
        var alert = MakeAlert(countThreshold: 5, thresholdWindow: 600); // 10 min
        _fakeClickHouse.LogCount = 1;

        await _worker.EvaluateAlertAsync(alert, CancellationToken.None);

        Assert.True(_fakeClickHouse.LastWindowSeconds >= 595 && _fakeClickHouse.LastWindowSeconds <= 605,
            $"Expected ~600s window, got {_fakeClickHouse.LastWindowSeconds}s");
    }

    // ── Worker constants ─────────────────────────────────────────────────

    [Fact]
    public void Worker_HasCorrectIntervals()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), LogAlertWatcherWorker.EvalInterval);
        Assert.Equal(TimeSpan.FromMinutes(1), LogAlertWatcherWorker.ReloadInterval);
    }
}

/// <summary>
/// Fake IClickHouseService for LogAlertWatcherWorker tests.
/// Records CountLogsAsync call parameters for assertion.
/// </summary>
internal class FakeLogClickHouseService : IClickHouseService
{
    public long LogCount { get; set; }
    public string? LastQuery { get; private set; }
    public double LastWindowSeconds { get; private set; }

    public Task<MetricsBuckets> ReadMetricsAsync(int projectId, QueryInput query, string bucketBy, List<string>? groupBy, string aggregator, string? column, CancellationToken ct) =>
        Task.FromResult(new MetricsBuckets());

    // ── Alert state stubs ────────────────────────────────────────────────

    public Task<long> CountLogsAsync(int projectId, string? query, DateTime startDate, DateTime endDate, CancellationToken ct = default)
    {
        LastQuery = query;
        LastWindowSeconds = (endDate - startDate).TotalSeconds;
        return Task.FromResult(LogCount);
    }

    public Task<List<AlertStateChangeRow>> GetLastAlertStateChangesAsync(int projectId, int alertId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
        => Task.FromResult(new List<AlertStateChangeRow>());

    public Task<List<AlertStateChangeRow>> GetAlertingAlertStateChangesAsync(int projectId, int alertId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
        => Task.FromResult(new List<AlertStateChangeRow>());

    public Task<List<AlertStateChangeRow>> GetLastAlertingStatesAsync(int projectId, int alertId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
        => Task.FromResult(new List<AlertStateChangeRow>());

    public Task WriteAlertStateChangesAsync(int projectId, IEnumerable<AlertStateChangeRow> rows, CancellationToken ct = default)
        => Task.CompletedTask;

    // ── Unused stubs ─────────────────────────────────────────────────────

    public Task<LogConnection> ReadLogsAsync(int projectId, QueryInput query, ClickHousePagination pagination, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task<List<HistogramBucket>> ReadLogsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task<List<string>> GetLogKeysAsync(int projectId, QueryInput query, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task<List<string>> GetLogKeyValuesAsync(int projectId, string key, QueryInput query, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task<TraceConnection> ReadTracesAsync(int projectId, QueryInput query, ClickHousePagination pagination, bool omitBody, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task<List<HistogramBucket>> ReadTracesHistogramAsync(int projectId, QueryInput query, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task<List<string>> GetTraceKeysAsync(int projectId, QueryInput query, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task<List<string>> GetTraceKeyValuesAsync(int projectId, string key, QueryInput query, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task<List<HistogramBucket>> ReadSessionsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task<(List<int> Ids, long Total)> QuerySessionIdsAsync(int projectId, QueryInput query, int count, int page, string? sortField, bool sortDesc, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task<(List<int> Ids, long Total)> QueryErrorGroupIdsAsync(int projectId, QueryInput query, int count, int page, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task<List<HistogramBucket>> ReadErrorObjectsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task<List<QueryKey>> GetSessionsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task<List<string>> GetSessionsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task<List<QueryKey>> GetErrorsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task<List<string>> GetErrorsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task<List<QueryKey>> GetEventsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, string? eventName, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task<List<string>> GetEventsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, string? eventName, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task WriteMetricAsync(int projectId, string metricName, double metricValue, string? category, DateTime timestamp, Dictionary<string, string>? tags, string? sessionSecureId, CancellationToken ct) =>
        Task.CompletedTask;
    public Task WriteLogsAsync(IEnumerable<LogRowInput> logs, CancellationToken ct) =>
        Task.CompletedTask;
    public Task WriteTracesAsync(IEnumerable<TraceRowInput> traces, CancellationToken ct) =>
        Task.CompletedTask;
    public Task WriteSessionsAsync(IEnumerable<SessionRowInput> sessions, CancellationToken ct) =>
        Task.CompletedTask;
    public Task WriteErrorGroupsAsync(IEnumerable<ErrorGroupRowInput> errorGroups, CancellationToken ct) =>
        Task.CompletedTask;
    public Task WriteErrorObjectsAsync(IEnumerable<ErrorObjectRowInput> errorObjects, CancellationToken ct) =>
        Task.CompletedTask;
}

/// <summary>Fake INotificationService that records sent webhook calls.</summary>
internal class FakeNotificationService : INotificationService
{
    public record SentWebhook(string Url, object Payload);

    public List<SentWebhook> SentWebhooks { get; } = [];

    public Task SendWebhookAsync(string url, object payload, CancellationToken ct)
    {
        SentWebhooks.Add(new SentWebhook(url, payload));
        return Task.CompletedTask;
    }

    public Task SendSlackMessageAsync(string accessToken, string channelId, SlackMessage message, CancellationToken ct) =>
        Task.CompletedTask;

    public Task SendDiscordMessageAsync(string webhookUrl, DiscordMessage message, CancellationToken ct) =>
        Task.CompletedTask;

    public Task SendTeamsMessageAsync(string webhookUrl, TeamsMessage message, CancellationToken ct) =>
        Task.CompletedTask;
}
