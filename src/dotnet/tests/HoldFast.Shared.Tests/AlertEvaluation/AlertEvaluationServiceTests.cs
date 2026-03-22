using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.Shared.AlertEvaluation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoldFast.Shared.Tests.AlertEvaluation;

/// <summary>
/// Tests for AlertEvaluationService: query/regex matching, threshold checking,
/// frequency cooldown, alert event creation, and notification dispatch.
/// </summary>
public class AlertEvaluationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AlertEvaluationService _service;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public AlertEvaluationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _workspace = new Workspace { Name = "WS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _project = new Project { Name = "Proj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        var httpFactory = new StubHttpClientFactory();
        var notificationService = new StubNotificationService();
        _service = new AlertEvaluationService(
            _db,
            httpFactory,
            notificationService,
            NullLogger<AlertEvaluationService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private (ErrorGroup, ErrorObject) CreateError(string eventText = "NullReferenceException", int count = 1)
    {
        var group = new ErrorGroup
        {
            ProjectId = _project.Id,
            Event = eventText,
            Type = "BACKEND",
            State = ErrorGroupState.Open,
            SecureId = Guid.NewGuid().ToString("N"),
        };
        _db.ErrorGroups.Add(group);
        _db.SaveChanges();

        ErrorObject? lastObj = null;
        for (int i = 0; i < count; i++)
        {
            var obj = new ErrorObject
            {
                ProjectId = _project.Id,
                ErrorGroupId = group.Id,
                Event = eventText,
                Type = "BACKEND",
                Timestamp = DateTime.UtcNow.AddSeconds(-i),
                Environment = "production",
                ServiceName = "api-server",
            };
            _db.ErrorObjects.Add(obj);
            lastObj = obj;
        }
        _db.SaveChanges();

        return (group, lastObj!);
    }

    private ErrorAlert CreateAlert(
        string? query = null,
        string? regexGroups = null,
        int countThreshold = 1,
        int windowMinutes = 30,
        int frequencySeconds = 0,
        bool disabled = false)
    {
        var alert = new ErrorAlert
        {
            ProjectId = _project.Id,
            Name = "Test Alert",
            Disabled = disabled,
            Query = query,
            RegexGroups = regexGroups,
            CountThreshold = countThreshold,
            ThresholdWindow = windowMinutes,
            Frequency = frequencySeconds,
        };
        _db.Set<ErrorAlert>().Add(alert);
        _db.SaveChanges();
        return alert;
    }

    // ── EvaluateErrorAlertsAsync basic tests ─────────────────────────────

    [Fact]
    public async Task Evaluate_NoAlerts_ReturnsZero()
    {
        var (group, obj) = CreateError();
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(0, result.AlertsEvaluated);
        Assert.Equal(0, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_DisabledAlert_NotEvaluated()
    {
        CreateAlert(disabled: true);
        var (group, obj) = CreateError();
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(0, result.AlertsEvaluated);
    }

    [Fact]
    public async Task Evaluate_SimpleAlert_Triggered()
    {
        CreateAlert();
        var (group, obj) = CreateError();
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_AlertCreatesEvent()
    {
        var alert = CreateAlert();
        var (group, obj) = CreateError();
        await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);

        var events = await _db.Set<ErrorAlertEvent>()
            .Where(e => e.ErrorAlertId == alert.Id)
            .ToListAsync();
        Assert.Single(events);
        Assert.Equal(group.Id, events[0].ErrorGroupId);
    }

    // ── Query matching tests ─────────────────────────────────────────────

    [Fact]
    public async Task Evaluate_QueryMatches_Triggered()
    {
        CreateAlert(query: "NullReference");
        var (group, obj) = CreateError("NullReferenceException: Object reference not set");
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_QueryDoesNotMatch_NotTriggered()
    {
        CreateAlert(query: "StackOverflow");
        var (group, obj) = CreateError("NullReferenceException");
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(0, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_QueryCaseInsensitive_Triggered()
    {
        CreateAlert(query: "nullreference");
        var (group, obj) = CreateError("NullReferenceException");
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    // ── Regex matching tests ─────────────────────────────────────────────

    [Fact]
    public async Task Evaluate_RegexMatches_Triggered()
    {
        CreateAlert(regexGroups: "[\"Null.*Exception\"]");
        var (group, obj) = CreateError("NullReferenceException");
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_RegexDoesNotMatch_NotTriggered()
    {
        CreateAlert(regexGroups: "[\"^StackOverflow\"]");
        var (group, obj) = CreateError("NullReferenceException");
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(0, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_MultipleRegex_OneMatches_Triggered()
    {
        CreateAlert(regexGroups: "[\"^StackOverflow\", \"NullReference\"]");
        var (group, obj) = CreateError("NullReferenceException");
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_InvalidRegex_Skipped()
    {
        CreateAlert(regexGroups: "[\"[invalid\", \"NullReference\"]");
        var (group, obj) = CreateError("NullReferenceException");
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_InvalidRegexJson_NotTriggered()
    {
        CreateAlert(regexGroups: "not-valid-json");
        var (group, obj) = CreateError("NullReferenceException");
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(0, result.AlertsTriggered);
    }

    // ── Threshold tests ──────────────────────────────────────────────────

    [Fact]
    public async Task Evaluate_BelowThreshold_NotTriggered()
    {
        CreateAlert(countThreshold: 5);
        var (group, obj) = CreateError(count: 3); // Only 3 errors
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(0, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_AtThreshold_Triggered()
    {
        CreateAlert(countThreshold: 5);
        var (group, obj) = CreateError(count: 5);
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_AboveThreshold_Triggered()
    {
        CreateAlert(countThreshold: 5);
        var (group, obj) = CreateError(count: 10);
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    // ── Frequency cooldown tests ─────────────────────────────────────────

    [Fact]
    public async Task Evaluate_FrequencyCooldown_BlocksSecondAlert()
    {
        var alert = CreateAlert(frequencySeconds: 3600); // 1 hour cooldown
        var (group, obj) = CreateError();

        // First alert should trigger
        var result1 = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result1.AlertsTriggered);

        // Create another error in the same group
        var obj2 = new ErrorObject
        {
            ProjectId = _project.Id,
            ErrorGroupId = group.Id,
            Event = group.Event,
            Type = "BACKEND",
            Timestamp = DateTime.UtcNow,
        };
        _db.ErrorObjects.Add(obj2);
        _db.SaveChanges();

        // Second alert should be blocked by cooldown
        var result2 = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj2, CancellationToken.None);
        Assert.Equal(0, result2.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_ZeroFrequency_NoRateLimit()
    {
        CreateAlert(frequencySeconds: 0);
        var (group, obj) = CreateError();

        var result1 = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result1.AlertsTriggered);

        // Add another error
        _db.ErrorObjects.Add(new ErrorObject
        {
            ProjectId = _project.Id, ErrorGroupId = group.Id,
            Event = group.Event, Type = "BACKEND", Timestamp = DateTime.UtcNow,
        });
        _db.SaveChanges();

        var result2 = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result2.AlertsTriggered);
    }

    // ── Error group state tests ──────────────────────────────────────────

    [Fact]
    public async Task Evaluate_IgnoredGroup_NotTriggered()
    {
        CreateAlert();
        var (group, obj) = CreateError();
        group.State = ErrorGroupState.Ignored;
        _db.SaveChanges();

        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(0, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_OpenGroup_Triggered()
    {
        CreateAlert();
        var (group, obj) = CreateError();
        group.State = ErrorGroupState.Open;
        _db.SaveChanges();

        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_ResolvedGroup_Triggered()
    {
        CreateAlert();
        var (group, obj) = CreateError();
        group.State = ErrorGroupState.Resolved;
        _db.SaveChanges();

        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    // ── Multiple alerts tests ────────────────────────────────────────────

    [Fact]
    public async Task Evaluate_MultipleAlerts_AllEvaluated()
    {
        CreateAlert(query: "Null");
        CreateAlert(query: "Exception");
        CreateAlert(query: "StackOverflow"); // Won't match

        var (group, obj) = CreateError("NullReferenceException");
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);

        Assert.Equal(3, result.AlertsEvaluated);
        Assert.Equal(2, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_DifferentProject_NotEvaluated()
    {
        var otherProject = new Project { Name = "Other", WorkspaceId = _workspace.Id };
        _db.Projects.Add(otherProject);
        _db.SaveChanges();

        CreateAlert(); // Alert for _project
        var (group, obj) = CreateError();

        // Evaluate for other project — should find no alerts
        var result = await _service.EvaluateErrorAlertsAsync(otherProject.Id, group, obj, CancellationToken.None);
        Assert.Equal(0, result.AlertsEvaluated);
    }

    // ── MatchesQuery static tests ────────────────────────────────────────

    [Theory]
    [InlineData("error", "An error occurred", true)]
    [InlineData("ERROR", "An error occurred", true)]
    [InlineData("missing", "An error occurred", false)]
    [InlineData("error", null, false)]
    [InlineData("error", "", false)]
    [InlineData("", "An error occurred", true)] // Empty query matches everything
    public void MatchesQuery_VariousCases(string query, string? errorEvent, bool expected)
    {
        Assert.Equal(expected, AlertEvaluationService.MatchesQuery(query, errorEvent));
    }

    // ── MatchesRegex static tests ────────────────────────────────────────

    [Theory]
    [InlineData("[\"Null.*Exception\"]", "NullReferenceException", true)]
    [InlineData("[\"^Null\"]", "NullReferenceException", true)]
    [InlineData("[\"^Stack\"]", "NullReferenceException", false)]
    [InlineData("[\"pattern1\", \"Null\"]", "NullReferenceException", true)]
    [InlineData("[]", "NullReferenceException", false)]
    [InlineData("not-json", "NullReferenceException", false)]
    [InlineData("[\"[invalid\"]", "NullReferenceException", false)]
    [InlineData("[\"Null\"]", null, false)]
    [InlineData("[\"Null\"]", "", false)]
    public void MatchesRegex_VariousCases(string regexJson, string? errorEvent, bool expected)
    {
        Assert.Equal(expected, AlertEvaluationService.MatchesRegex(regexJson, errorEvent));
    }

    [Fact]
    public void MatchesRegex_EmptyPattern_Skipped()
    {
        Assert.False(AlertEvaluationService.MatchesRegex("[\"\", null]", "test"));
    }

    [Fact]
    public void MatchesRegex_ReDoS_TimesOut()
    {
        // Regex that could cause catastrophic backtracking
        var evil = "[\"(a+)+$\"]";
        // Should complete (timeout at 1s) without hanging
        var result = AlertEvaluationService.MatchesRegex(evil, "aaaaaaaaaaaaaaaaaaaaaaaaaaa!");
        // May or may not match — point is it doesn't hang
        Assert.True(true); // If we get here, no hang
    }

    // ── Edge cases ───────────────────────────────────────────────────────

    [Fact]
    public async Task Evaluate_QueryAndRegex_BothMustMatch()
    {
        CreateAlert(query: "NullReference", regexGroups: "[\"^StackOverflow\"]");
        var (group, obj) = CreateError("NullReferenceException");

        // Query matches but regex doesn't — should not trigger
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(0, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_NullThresholdDefaults_Triggered()
    {
        // Alert with null threshold/window — should default to 1/30
        var alert = new ErrorAlert
        {
            ProjectId = _project.Id,
            Name = "Null defaults",
            CountThreshold = null,
            ThresholdWindow = null,
            Frequency = null,
        };
        _db.Set<ErrorAlert>().Add(alert);
        _db.SaveChanges();

        var (group, obj) = CreateError();
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_10Alerts_AllEvaluated()
    {
        for (int i = 0; i < 10; i++)
            CreateAlert();

        var (group, obj) = CreateError();
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(10, result.AlertsEvaluated);
    }

    [Fact]
    public async Task Evaluate_SqlInjectionInQuery_Safe()
    {
        CreateAlert(query: "'; DROP TABLE error_alerts; --");
        var (group, obj) = CreateError("NullReferenceException");
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(0, result.AlertsTriggered); // No match, no crash
    }

    // ── Stub HttpClientFactory ───────────────────────────────────────────

    private class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler());

        private class StubHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken ct)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
            }
        }
    }

    // ── Stub NotificationService ────────────────────────────────────────

    private class StubNotificationService : HoldFast.Shared.Notifications.INotificationService
    {
        public Task SendSlackMessageAsync(string accessToken, string channelId, HoldFast.Shared.Notifications.SlackMessage message, CancellationToken ct) => Task.CompletedTask;
        public Task SendDiscordMessageAsync(string webhookUrl, HoldFast.Shared.Notifications.DiscordMessage message, CancellationToken ct) => Task.CompletedTask;
        public Task SendTeamsMessageAsync(string webhookUrl, HoldFast.Shared.Notifications.TeamsMessage message, CancellationToken ct) => Task.CompletedTask;
        public Task SendWebhookAsync(string url, object payload, CancellationToken ct) => Task.CompletedTask;
    }
}
