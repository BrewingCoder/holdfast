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
/// Tests for MetricAlertWatcherWorker.EvaluateAlertAsync — threshold evaluation,
/// cooldown enforcement, state recording, and webhook notification dispatch.
/// </summary>
public class MetricAlertWatcherWorkerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly MetricAlertWatcherWorker _worker;
    private readonly FakeMetricClickHouseService _fakeClickHouse;
    private readonly FakeMetricNotificationService _fakeNotifications;
    private readonly Workspace _workspace;
    private readonly Project _project;
    private readonly IServiceScopeFactory _scopeFactory;

    public MetricAlertWatcherWorkerTests()
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

        _fakeClickHouse = new FakeMetricClickHouseService();
        _fakeNotifications = new FakeMetricNotificationService();

        var services = new ServiceCollection();
        services.AddSingleton(new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options);
        services.AddScoped(sp => new HoldFastDbContext(
            sp.GetRequiredService<DbContextOptions<HoldFastDbContext>>()));
        services.AddSingleton<IClickHouseService>(_fakeClickHouse);
        services.AddSingleton<INotificationService>(_fakeNotifications);

        _scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        _worker = new MetricAlertWatcherWorker(
            _scopeFactory,
            NullLogger<MetricAlertWatcherWorker>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private Alert MakeAlert(
        string productType = "METRICS",
        double? aboveThreshold = 100.0,
        double? belowThreshold = null,
        int? cooldown = null,
        int? thresholdWindow = null) => new Alert
    {
        ProjectId = _project.Id,
        Name = "Metric Alert",
        ProductType = productType,
        Disabled = false,
        AboveThreshold = aboveThreshold,
        BelowThreshold = belowThreshold,
        ThresholdCooldown = cooldown,
        ThresholdWindow = thresholdWindow,
        FunctionType = "Count",
    };

    private AlertDestination MakeWebhookDest(int alertId, string url) => new AlertDestination
    {
        AlertId = alertId,
        DestinationType = "webhook",
        TypeId = url,
    };

    // ── Above-threshold tests ────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAlertAsync_MetricAboveThreshold_WritesAlertingState()
    {
        var alert = MakeAlert(aboveThreshold: 100.0);
        _fakeClickHouse.MetricsValue = 150.0; // above threshold

        await _worker.EvaluateAlertAsync(alert, _db, _fakeClickHouse, _fakeNotifications, CancellationToken.None);

        Assert.Single(_fakeClickHouse.WrittenStateChanges);
        Assert.Equal("Alerting", _fakeClickHouse.WrittenStateChanges[0].State);
    }

    [Fact]
    public async Task EvaluateAlertAsync_MetricBelowThreshold_WritesNormalState()
    {
        var alert = MakeAlert(aboveThreshold: 100.0);
        _fakeClickHouse.MetricsValue = 50.0; // below threshold

        await _worker.EvaluateAlertAsync(alert, _db, _fakeClickHouse, _fakeNotifications, CancellationToken.None);

        Assert.Single(_fakeClickHouse.WrittenStateChanges);
        Assert.Equal("Normal", _fakeClickHouse.WrittenStateChanges[0].State);
    }

    [Fact]
    public async Task EvaluateAlertAsync_MetricAtThreshold_IsAlerting()
    {
        var alert = MakeAlert(aboveThreshold: 100.0);
        _fakeClickHouse.MetricsValue = 100.0; // exactly at threshold

        await _worker.EvaluateAlertAsync(alert, _db, _fakeClickHouse, _fakeNotifications, CancellationToken.None);

        Assert.Equal("Alerting", _fakeClickHouse.WrittenStateChanges[0].State);
    }

    // ── Below-threshold condition ────────────────────────────────────────

    [Fact]
    public async Task EvaluateAlertAsync_BelowThresholdCondition_AlertsWhenLow()
    {
        var alert = MakeAlert(aboveThreshold: 100.0, belowThreshold: 1.0); // alert when metric <= 100
        _fakeClickHouse.MetricsValue = 50.0;

        await _worker.EvaluateAlertAsync(alert, _db, _fakeClickHouse, _fakeNotifications, CancellationToken.None);

        Assert.Equal("Alerting", _fakeClickHouse.WrittenStateChanges[0].State);
    }

    [Fact]
    public async Task EvaluateAlertAsync_BelowThresholdCondition_NormalWhenHigh()
    {
        var alert = MakeAlert(aboveThreshold: 100.0, belowThreshold: 1.0);
        _fakeClickHouse.MetricsValue = 200.0; // above, so below-condition is NOT met

        await _worker.EvaluateAlertAsync(alert, _db, _fakeClickHouse, _fakeNotifications, CancellationToken.None);

        Assert.Equal("Normal", _fakeClickHouse.WrittenStateChanges[0].State);
    }

    // ── Notification dispatch ────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAlertAsync_WhenAlerting_SendsWebhookNotification()
    {
        var alert = MakeAlert(aboveThreshold: 10.0);
        _db.Set<Alert>().Add(alert);
        _db.SaveChanges();
        _db.Set<AlertDestination>().Add(MakeWebhookDest(alert.Id, "https://hook.example.com"));
        _db.SaveChanges();

        _fakeClickHouse.MetricsValue = 50.0;

        await _worker.EvaluateAlertAsync(alert, _db, _fakeClickHouse, _fakeNotifications, CancellationToken.None);

        Assert.Single(_fakeNotifications.SentWebhooks);
        Assert.Equal("https://hook.example.com", _fakeNotifications.SentWebhooks[0]);
    }

    [Fact]
    public async Task EvaluateAlertAsync_WhenNormal_DoesNotSendNotification()
    {
        var alert = MakeAlert(aboveThreshold: 10.0);
        _db.Set<Alert>().Add(alert);
        _db.SaveChanges();
        _db.Set<AlertDestination>().Add(MakeWebhookDest(alert.Id, "https://hook.example.com"));
        _db.SaveChanges();

        _fakeClickHouse.MetricsValue = 5.0; // below threshold

        await _worker.EvaluateAlertAsync(alert, _db, _fakeClickHouse, _fakeNotifications, CancellationToken.None);

        Assert.Empty(_fakeNotifications.SentWebhooks);
    }

    [Fact]
    public async Task EvaluateAlertAsync_NoAlertDestinations_NoNotificationSent()
    {
        var alert = MakeAlert(aboveThreshold: 10.0);
        _db.Set<Alert>().Add(alert);
        _db.SaveChanges();
        // No AlertDestination added

        _fakeClickHouse.MetricsValue = 100.0;

        await _worker.EvaluateAlertAsync(alert, _db, _fakeClickHouse, _fakeNotifications, CancellationToken.None);

        Assert.Empty(_fakeNotifications.SentWebhooks);
    }

    [Fact]
    public async Task EvaluateAlertAsync_DestinationTypeNotWebhook_Skipped()
    {
        var alert = MakeAlert(aboveThreshold: 10.0);
        _db.Set<Alert>().Add(alert);
        _db.SaveChanges();
        _db.Set<AlertDestination>().Add(new AlertDestination
        {
            AlertId = alert.Id,
            DestinationType = "slack", // not webhook
            TypeId = "C123",
        });
        _db.SaveChanges();

        _fakeClickHouse.MetricsValue = 100.0;

        await _worker.EvaluateAlertAsync(alert, _db, _fakeClickHouse, _fakeNotifications, CancellationToken.None);

        Assert.Empty(_fakeNotifications.SentWebhooks);
    }

    // ── Cooldown enforcement ─────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAlertAsync_WithinCooldown_DoesNotFire()
    {
        // cooldown = 3600s, fake says already alerting within cooldown window
        var alert = MakeAlert(aboveThreshold: 10.0, cooldown: 3600);
        _db.Set<Alert>().Add(alert);
        _db.SaveChanges();
        _db.Set<AlertDestination>().Add(MakeWebhookDest(alert.Id, "https://hook.example.com"));
        _db.SaveChanges();

        _fakeClickHouse.MetricsValue = 100.0;
        _fakeClickHouse.LastAlertingStates = [new AlertStateChangeRow
        {
            ProjectId = _project.Id,
            AlertId = alert.Id,
            State = "Alerting",
            Timestamp = DateTime.UtcNow.AddMinutes(-5),
        }];

        await _worker.EvaluateAlertAsync(alert, _db, _fakeClickHouse, _fakeNotifications, CancellationToken.None);

        // Suppressed by cooldown — no state written, no notification
        Assert.Empty(_fakeClickHouse.WrittenStateChanges);
        Assert.Empty(_fakeNotifications.SentWebhooks);
    }

    [Fact]
    public async Task EvaluateAlertAsync_CooldownExpired_Fires()
    {
        var alert = MakeAlert(aboveThreshold: 10.0, cooldown: 300);
        // No recent alerting states — cooldown not active
        _fakeClickHouse.LastAlertingStates = [];
        _fakeClickHouse.MetricsValue = 100.0;

        await _worker.EvaluateAlertAsync(alert, _db, _fakeClickHouse, _fakeNotifications, CancellationToken.None);

        Assert.Single(_fakeClickHouse.WrittenStateChanges);
        Assert.Equal("Alerting", _fakeClickHouse.WrittenStateChanges[0].State);
    }

    [Fact]
    public async Task EvaluateAlertAsync_NoCooldown_AlwaysEvaluates()
    {
        var alert = MakeAlert(aboveThreshold: 10.0, cooldown: null);
        _fakeClickHouse.MetricsValue = 100.0;

        await _worker.EvaluateAlertAsync(alert, _db, _fakeClickHouse, _fakeNotifications, CancellationToken.None);

        Assert.Single(_fakeClickHouse.WrittenStateChanges);
    }

    // ── Log alert routing ────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAlertAsync_LogsProductType_UsesCountLogs()
    {
        var alert = MakeAlert(productType: "LOGS", aboveThreshold: 5.0);
        _fakeClickHouse.LogCountResult = 10;

        await _worker.EvaluateAlertAsync(alert, _db, _fakeClickHouse, _fakeNotifications, CancellationToken.None);

        Assert.True(_fakeClickHouse.CountLogsWasCalled);
        Assert.False(_fakeClickHouse.ReadMetricsWasCalled);
        Assert.Equal("Alerting", _fakeClickHouse.WrittenStateChanges[0].State);
    }

    [Fact]
    public async Task EvaluateAlertAsync_LogsProductTypeLowercase_UsesCountLogs()
    {
        var alert = MakeAlert(productType: "logs", aboveThreshold: 5.0);
        _fakeClickHouse.LogCountResult = 10;

        await _worker.EvaluateAlertAsync(alert, _db, _fakeClickHouse, _fakeNotifications, CancellationToken.None);

        Assert.True(_fakeClickHouse.CountLogsWasCalled);
    }

    [Fact]
    public async Task EvaluateAlertAsync_MetricsProductType_UsesReadMetrics()
    {
        var alert = MakeAlert(productType: "METRICS", aboveThreshold: 5.0);
        _fakeClickHouse.MetricsValue = 10.0;

        await _worker.EvaluateAlertAsync(alert, _db, _fakeClickHouse, _fakeNotifications, CancellationToken.None);

        Assert.True(_fakeClickHouse.ReadMetricsWasCalled);
        Assert.False(_fakeClickHouse.CountLogsWasCalled);
    }

    // ── State change fields ──────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAlertAsync_StateChangeHasCorrectFields()
    {
        var alert = MakeAlert(aboveThreshold: 10.0);
        alert.ProjectId = _project.Id;
        _fakeClickHouse.MetricsValue = 100.0;

        await _worker.EvaluateAlertAsync(alert, _db, _fakeClickHouse, _fakeNotifications, CancellationToken.None);

        var state = _fakeClickHouse.WrittenStateChanges[0];
        Assert.Equal(alert.ProjectId, state.ProjectId);
        Assert.Equal(alert.Id, state.AlertId);
        Assert.Equal("Alerting", state.State);
    }

    // ── Worker constant ──────────────────────────────────────────────────

    [Fact]
    public void Worker_HasCorrectEvalInterval()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), MetricAlertWatcherWorker.EvalInterval);
    }
}

/// <summary>
/// Fake IClickHouseService for MetricAlertWatcherWorker tests.
/// Records method calls and returns configurable values.
/// </summary>
internal class FakeMetricClickHouseService : IClickHouseService
{
    public double MetricsValue { get; set; }
    public long LogCountResult { get; set; }
    public List<AlertStateChangeRow> LastAlertingStates { get; set; } = [];
    public List<AlertStateChangeRow> WrittenStateChanges { get; } = [];
    public bool CountLogsWasCalled { get; private set; }
    public bool ReadMetricsWasCalled { get; private set; }

    public Task<long> CountLogsAsync(int projectId, string? query, DateTime startDate, DateTime endDate, CancellationToken ct)
    {
        CountLogsWasCalled = true;
        return Task.FromResult(LogCountResult);
    }

    public Task<MetricsBuckets> ReadMetricsAsync(int projectId, QueryInput query, string bucketBy, List<string>? groupBy, string aggregator, string? column, CancellationToken ct)
    {
        ReadMetricsWasCalled = true;
        return Task.FromResult(new MetricsBuckets
        {
            Buckets = [new MetricsBucket { MetricValue = MetricsValue }],
        });
    }

    public Task<List<AlertStateChangeRow>> GetLastAlertingStatesAsync(
        int projectId, int alertId, DateTime startDate, DateTime endDate, CancellationToken ct) =>
        Task.FromResult(LastAlertingStates);

    public Task WriteAlertStateChangesAsync(int projectId, IEnumerable<AlertStateChangeRow> rows, CancellationToken ct = default)
    {
        WrittenStateChanges.AddRange(rows);
        return Task.CompletedTask;
    }

    public Task<List<AlertStateChangeRow>> GetAlertingAlertStateChangesAsync(
        int projectId, int alertId, DateTime startDate, DateTime endDate, CancellationToken ct = default) =>
        Task.FromResult(new List<AlertStateChangeRow>());

    public Task<List<AlertStateChangeRow>> GetLastAlertStateChangesAsync(
        int projectId, int alertId, DateTime startDate, DateTime endDate, CancellationToken ct = default) =>
        Task.FromResult(new List<AlertStateChangeRow>());

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

/// <summary>Fake INotificationService for MetricAlertWatcherWorker tests.</summary>
internal class FakeMetricNotificationService : INotificationService
{
    public List<string> SentWebhooks { get; } = [];

    public Task SendWebhookAsync(string url, object payload, CancellationToken ct)
    {
        SentWebhooks.Add(url);
        return Task.CompletedTask;
    }

    public Task SendSlackMessageAsync(string accessToken, string channelId, SlackMessage message, CancellationToken ct) =>
        Task.CompletedTask;
    public Task SendDiscordMessageAsync(string webhookUrl, DiscordMessage message, CancellationToken ct) =>
        Task.CompletedTask;
    public Task SendTeamsMessageAsync(string webhookUrl, TeamsMessage message, CancellationToken ct) =>
        Task.CompletedTask;
}
