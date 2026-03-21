using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.Shared.AlertEvaluation;
using HoldFast.Shared.ErrorGrouping;
using HoldFast.Shared.Notifications;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoldFast.Shared.Tests.Integration;

/// <summary>
/// Integration tests that exercise the full error grouping → alert evaluation pipeline.
/// These tests verify the handoff between ErrorGroupingService and AlertEvaluationService
/// working together against a shared in-memory database.
/// </summary>
public class ErrorGroupingAlertIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly ErrorGroupingService _groupingService;
    private readonly AlertEvaluationService _alertService;
    private readonly TrackingNotificationService _notifications;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public ErrorGroupingAlertIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _workspace = new Workspace { Name = "Integration WS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _project = new Project { Name = "Test Project", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _groupingService = new ErrorGroupingService(_db, NullLogger<ErrorGroupingService>.Instance);
        _notifications = new TrackingNotificationService();
        _alertService = new AlertEvaluationService(
            _db,
            new StubHttpClientFactory(),
            _notifications,
            NullLogger<AlertEvaluationService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ── End-to-end: group error → evaluate alerts ───────────────────────

    [Fact]
    public async Task NewError_TriggersAlert_WhenThresholdMet()
    {
        // Create an alert that fires on any error (no query filter, threshold=1)
        _db.Set<ErrorAlert>().Add(new ErrorAlert
        {
            ProjectId = _project.Id,
            Name = "All Errors",
            CountThreshold = 1,
            ThresholdWindow = 60,
            Disabled = false,
        });
        await _db.SaveChangesAsync();

        // Group an error
        var result = await _groupingService.GroupErrorAsync(
            _project.Id, "NullReferenceException", "System.NullReferenceException",
            null, DateTime.UtcNow, null, null, null, "production", "api", "1.0",
            null, null, null, CancellationToken.None);

        Assert.True(result.IsNewGroup);

        // Evaluate alerts against the new error
        var alertResult = await _alertService.EvaluateErrorAlertsAsync(
            _project.Id, result.ErrorGroup, result.ErrorObject, CancellationToken.None);

        Assert.Equal(1, alertResult.AlertsEvaluated);
        Assert.Equal(1, alertResult.AlertsTriggered);
    }

    [Fact]
    public async Task NewError_DoesNotTriggerAlert_WhenThresholdNotMet()
    {
        // Alert requires 5 errors in window
        _db.Set<ErrorAlert>().Add(new ErrorAlert
        {
            ProjectId = _project.Id,
            Name = "High Volume",
            CountThreshold = 5,
            ThresholdWindow = 60,
            Disabled = false,
        });
        await _db.SaveChangesAsync();

        // Group just one error
        var result = await _groupingService.GroupErrorAsync(
            _project.Id, "SomeException", "Exception",
            null, DateTime.UtcNow, null, null, null, null, null, null,
            null, null, null, CancellationToken.None);

        var alertResult = await _alertService.EvaluateErrorAlertsAsync(
            _project.Id, result.ErrorGroup, result.ErrorObject, CancellationToken.None);

        Assert.Equal(1, alertResult.AlertsEvaluated);
        Assert.Equal(0, alertResult.AlertsTriggered);
    }

    [Fact]
    public async Task MultipleErrors_SameGroup_TriggersAlertOnThreshold()
    {
        // Alert requires 3 errors
        _db.Set<ErrorAlert>().Add(new ErrorAlert
        {
            ProjectId = _project.Id,
            Name = "Threshold 3",
            CountThreshold = 3,
            ThresholdWindow = 60,
            Disabled = false,
        });
        await _db.SaveChangesAsync();

        // Same stack trace for fingerprint matching
        var stackTrace = """[{"fileName":"app.js","functionName":"handleRequest","lineNumber":42,"lineContent":"throw new Error('fail')"}]""";

        // Group 3 errors with the same event and fingerprint
        ErrorGroupingResult? lastResult = null;
        for (int i = 0; i < 3; i++)
        {
            lastResult = await _groupingService.GroupErrorAsync(
                _project.Id, "Error: fail", "Error",
                stackTrace, DateTime.UtcNow, null, null, null, "production", "api", "1.0",
                null, null, null, CancellationToken.None);
        }

        // After 3 errors, all should be in the same group
        Assert.False(lastResult!.IsNewGroup);

        // Now evaluate — threshold should be met
        var alertResult = await _alertService.EvaluateErrorAlertsAsync(
            _project.Id, lastResult.ErrorGroup, lastResult.ErrorObject, CancellationToken.None);

        Assert.Equal(1, alertResult.AlertsTriggered);
    }

    [Fact]
    public async Task QueryFilteredAlert_OnlyTriggersOnMatchingErrors()
    {
        // Alert only for "database" errors
        _db.Set<ErrorAlert>().Add(new ErrorAlert
        {
            ProjectId = _project.Id,
            Name = "DB Errors",
            Query = "database",
            CountThreshold = 1,
            ThresholdWindow = 60,
            Disabled = false,
        });
        await _db.SaveChangesAsync();

        // Group a non-matching error
        var result1 = await _groupingService.GroupErrorAsync(
            _project.Id, "network timeout", "TimeoutException",
            null, DateTime.UtcNow, null, null, null, null, null, null,
            null, null, null, CancellationToken.None);

        var alertResult1 = await _alertService.EvaluateErrorAlertsAsync(
            _project.Id, result1.ErrorGroup, result1.ErrorObject, CancellationToken.None);
        Assert.Equal(0, alertResult1.AlertsTriggered);

        // Group a matching error
        var result2 = await _groupingService.GroupErrorAsync(
            _project.Id, "database connection refused", "DbException",
            null, DateTime.UtcNow, null, null, null, null, null, null,
            null, null, null, CancellationToken.None);

        var alertResult2 = await _alertService.EvaluateErrorAlertsAsync(
            _project.Id, result2.ErrorGroup, result2.ErrorObject, CancellationToken.None);
        Assert.Equal(1, alertResult2.AlertsTriggered);
    }

    [Fact]
    public async Task ResolvedError_ReopensOnNewOccurrence()
    {
        // Group an error and manually resolve it
        var result1 = await _groupingService.GroupErrorAsync(
            _project.Id, "transient error", "TransientException",
            null, DateTime.UtcNow, null, null, null, null, null, null,
            null, null, null, CancellationToken.None);

        result1.ErrorGroup.State = ErrorGroupState.Resolved;
        _db.ErrorGroups.Update(result1.ErrorGroup);
        await _db.SaveChangesAsync();

        // Group same error again — should reopen
        var result2 = await _groupingService.GroupErrorAsync(
            _project.Id, "transient error", "TransientException",
            null, DateTime.UtcNow, null, null, null, null, null, null,
            null, null, null, CancellationToken.None);

        Assert.False(result2.IsNewGroup);
        Assert.Equal(ErrorGroupState.Open, result2.ErrorGroup.State);
    }

    [Fact]
    public async Task IgnoredErrorGroup_DoesNotTriggerAlert()
    {
        _db.Set<ErrorAlert>().Add(new ErrorAlert
        {
            ProjectId = _project.Id,
            Name = "All Errors",
            CountThreshold = 1,
            ThresholdWindow = 60,
            Disabled = false,
        });
        await _db.SaveChangesAsync();

        var result = await _groupingService.GroupErrorAsync(
            _project.Id, "ignored error", "IgnoredException",
            null, DateTime.UtcNow, null, null, null, null, null, null,
            null, null, null, CancellationToken.None);

        // Mark group as ignored
        result.ErrorGroup.State = ErrorGroupState.Ignored;
        _db.ErrorGroups.Update(result.ErrorGroup);
        await _db.SaveChangesAsync();

        var alertResult = await _alertService.EvaluateErrorAlertsAsync(
            _project.Id, result.ErrorGroup, result.ErrorObject, CancellationToken.None);

        Assert.Equal(0, alertResult.AlertsTriggered);
    }

    [Fact]
    public async Task DisabledAlert_NotEvaluated()
    {
        _db.Set<ErrorAlert>().Add(new ErrorAlert
        {
            ProjectId = _project.Id,
            Name = "Disabled Alert",
            CountThreshold = 1,
            ThresholdWindow = 60,
            Disabled = true,
        });
        await _db.SaveChangesAsync();

        var result = await _groupingService.GroupErrorAsync(
            _project.Id, "some error", "Exception",
            null, DateTime.UtcNow, null, null, null, null, null, null,
            null, null, null, CancellationToken.None);

        var alertResult = await _alertService.EvaluateErrorAlertsAsync(
            _project.Id, result.ErrorGroup, result.ErrorObject, CancellationToken.None);

        Assert.Equal(0, alertResult.AlertsEvaluated);
        Assert.Equal(0, alertResult.AlertsTriggered);
    }

    [Fact]
    public async Task FrequencyCooldown_PreventsRepeatedAlerts()
    {
        _db.Set<ErrorAlert>().Add(new ErrorAlert
        {
            ProjectId = _project.Id,
            Name = "Cooldown Alert",
            CountThreshold = 1,
            ThresholdWindow = 60,
            Frequency = 3600, // 1 hour cooldown
            Disabled = false,
        });
        await _db.SaveChangesAsync();

        // First error triggers alert
        var result1 = await _groupingService.GroupErrorAsync(
            _project.Id, "repeated error", "RepeatedException",
            null, DateTime.UtcNow, null, null, null, null, null, null,
            null, null, null, CancellationToken.None);

        var alertResult1 = await _alertService.EvaluateErrorAlertsAsync(
            _project.Id, result1.ErrorGroup, result1.ErrorObject, CancellationToken.None);
        Assert.Equal(1, alertResult1.AlertsTriggered);

        // Second error — same group — should be suppressed by cooldown
        var result2 = await _groupingService.GroupErrorAsync(
            _project.Id, "repeated error", "RepeatedException",
            null, DateTime.UtcNow, null, null, null, null, null, null,
            null, null, null, CancellationToken.None);

        var alertResult2 = await _alertService.EvaluateErrorAlertsAsync(
            _project.Id, result2.ErrorGroup, result2.ErrorObject, CancellationToken.None);
        Assert.Equal(0, alertResult2.AlertsTriggered);
    }

    [Fact]
    public async Task MultipleAlerts_EachEvaluatedIndependently()
    {
        // Alert 1: matches "database"
        _db.Set<ErrorAlert>().Add(new ErrorAlert
        {
            ProjectId = _project.Id,
            Name = "DB Alert",
            Query = "database",
            CountThreshold = 1,
            ThresholdWindow = 60,
            Disabled = false,
        });
        // Alert 2: matches everything
        _db.Set<ErrorAlert>().Add(new ErrorAlert
        {
            ProjectId = _project.Id,
            Name = "All Alert",
            CountThreshold = 1,
            ThresholdWindow = 60,
            Disabled = false,
        });
        await _db.SaveChangesAsync();

        // Group an error that matches "database"
        var result = await _groupingService.GroupErrorAsync(
            _project.Id, "database timeout", "DbTimeoutException",
            null, DateTime.UtcNow, null, null, null, null, null, null,
            null, null, null, CancellationToken.None);

        var alertResult = await _alertService.EvaluateErrorAlertsAsync(
            _project.Id, result.ErrorGroup, result.ErrorObject, CancellationToken.None);

        Assert.Equal(2, alertResult.AlertsEvaluated);
        Assert.Equal(2, alertResult.AlertsTriggered);
    }

    [Fact]
    public async Task RegexAlert_TriggersOnPatternMatch()
    {
        _db.Set<ErrorAlert>().Add(new ErrorAlert
        {
            ProjectId = _project.Id,
            Name = "Regex Alert",
            RegexGroups = """["^Error:\\s+\\d+", "timeout"]""",
            CountThreshold = 1,
            ThresholdWindow = 60,
            Disabled = false,
        });
        await _db.SaveChangesAsync();

        var result = await _groupingService.GroupErrorAsync(
            _project.Id, "Error: 42 something broke", "Exception",
            null, DateTime.UtcNow, null, null, null, null, null, null,
            null, null, null, CancellationToken.None);

        var alertResult = await _alertService.EvaluateErrorAlertsAsync(
            _project.Id, result.ErrorGroup, result.ErrorObject, CancellationToken.None);

        Assert.Equal(1, alertResult.AlertsTriggered);
    }

    [Fact]
    public async Task ErrorEvent_TruncatedToMaxLength_StillGroups()
    {
        // Very long error event should be truncated but still create a group
        var longEvent = new string('X', 20_000);

        var result = await _groupingService.GroupErrorAsync(
            _project.Id, longEvent, "LongException",
            null, DateTime.UtcNow, null, null, null, null, null, null,
            null, null, null, CancellationToken.None);

        Assert.True(result.IsNewGroup);
        Assert.Equal(10_000, result.ErrorGroup.Event!.Length);
    }

    [Fact]
    public async Task FingerprintMatching_GroupsSameStackTrace()
    {
        var stackTrace = """[{"fileName":"index.js","functionName":"render","lineNumber":10,"lineContent":"return null;","columnNumber":5}]""";

        var result1 = await _groupingService.GroupErrorAsync(
            _project.Id, "Cannot read property", "TypeError",
            stackTrace, DateTime.UtcNow, null, null, null, null, null, null,
            null, null, null, CancellationToken.None);

        var result2 = await _groupingService.GroupErrorAsync(
            _project.Id, "Cannot read property", "TypeError",
            stackTrace, DateTime.UtcNow, null, null, null, null, null, null,
            null, null, null, CancellationToken.None);

        Assert.True(result1.IsNewGroup);
        Assert.False(result2.IsNewGroup);
        Assert.Equal(result1.ErrorGroup.Id, result2.ErrorGroup.Id);
    }

    [Fact]
    public async Task NoAlerts_ReturnsZeroCounts()
    {
        var result = await _groupingService.GroupErrorAsync(
            _project.Id, "orphan error", "Exception",
            null, DateTime.UtcNow, null, null, null, null, null, null,
            null, null, null, CancellationToken.None);

        var alertResult = await _alertService.EvaluateErrorAlertsAsync(
            _project.Id, result.ErrorGroup, result.ErrorObject, CancellationToken.None);

        Assert.Equal(0, alertResult.AlertsEvaluated);
        Assert.Equal(0, alertResult.AlertsTriggered);
        Assert.Empty(alertResult.TriggeredAlertIds);
    }

    [Fact]
    public async Task ErrorAlertEvent_CreatedForTriggeredAlert()
    {
        var alert = new ErrorAlert
        {
            ProjectId = _project.Id,
            Name = "Track Events",
            CountThreshold = 1,
            ThresholdWindow = 60,
            Disabled = false,
        };
        _db.Set<ErrorAlert>().Add(alert);
        await _db.SaveChangesAsync();

        var result = await _groupingService.GroupErrorAsync(
            _project.Id, "tracked error", "Exception",
            null, DateTime.UtcNow, null, null, null, null, null, null,
            null, null, null, CancellationToken.None);

        await _alertService.EvaluateErrorAlertsAsync(
            _project.Id, result.ErrorGroup, result.ErrorObject, CancellationToken.None);

        // Verify ErrorAlertEvent was created
        var events = await _db.Set<ErrorAlertEvent>().ToListAsync();
        Assert.Single(events);
        Assert.Equal(alert.Id, events[0].ErrorAlertId);
        Assert.Equal(result.ErrorGroup.Id, events[0].ErrorGroupId);
    }

    // ── Session init → error grouping → alert (3-service chain) ─────────

    [Fact]
    public async Task FullPipeline_SessionError_GroupedAndAlerted()
    {
        // Setup: session and alert
        var session = new Session
        {
            SecureId = "integration-sess",
            ProjectId = _project.Id,
            Fingerprint = "fp1",
            ClientID = "client1",
            Environment = "production",
            WithinBillingQuota = true,
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        _db.Set<ErrorAlert>().Add(new ErrorAlert
        {
            ProjectId = _project.Id,
            Name = "Session Error Alert",
            CountThreshold = 1,
            ThresholdWindow = 60,
            Disabled = false,
        });
        await _db.SaveChangesAsync();

        // Group error with session ID
        var result = await _groupingService.GroupErrorAsync(
            _project.Id, "Session crash", "SessionException",
            null, DateTime.UtcNow, "https://app.example.com/dashboard", "client",
            null, "production", "web-app", "2.0",
            session.Id, null, null, CancellationToken.None);

        Assert.True(result.IsNewGroup);
        Assert.Equal(session.Id, result.ErrorObject.SessionId);

        // Alert triggers
        var alertResult = await _alertService.EvaluateErrorAlertsAsync(
            _project.Id, result.ErrorGroup, result.ErrorObject, CancellationToken.None);

        Assert.Equal(1, alertResult.AlertsTriggered);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private class TrackingNotificationService : INotificationService
    {
        public List<string> SentSlack { get; } = [];
        public List<string> SentDiscord { get; } = [];
        public List<string> SentTeams { get; } = [];
        public List<string> SentWebhook { get; } = [];

        public Task SendSlackMessageAsync(string accessToken, string channelId, SlackMessage message, CancellationToken ct)
        {
            SentSlack.Add(channelId);
            return Task.CompletedTask;
        }

        public Task SendDiscordMessageAsync(string webhookUrl, DiscordMessage message, CancellationToken ct)
        {
            SentDiscord.Add(webhookUrl);
            return Task.CompletedTask;
        }

        public Task SendTeamsMessageAsync(string webhookUrl, TeamsMessage message, CancellationToken ct)
        {
            SentTeams.Add(webhookUrl);
            return Task.CompletedTask;
        }

        public Task SendWebhookAsync(string url, object payload, CancellationToken ct)
        {
            SentWebhook.Add(url);
            return Task.CompletedTask;
        }
    }

    private class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
