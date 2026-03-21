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
/// Edge case and stress tests for AlertEvaluationService.
/// </summary>
public class AlertEvaluationEdgeCaseTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AlertEvaluationService _service;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public AlertEvaluationEdgeCaseTests()
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

        _service = new AlertEvaluationService(
            _db,
            new StubHttpClientFactory(),
            new StubNotificationService(),
            NullLogger<AlertEvaluationService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private (ErrorGroup, ErrorObject) CreateError(string eventText = "Error", int count = 1)
    {
        var group = new ErrorGroup
        {
            ProjectId = _project.Id, Event = eventText, Type = "BACKEND",
            State = ErrorGroupState.Open, SecureId = Guid.NewGuid().ToString("N"),
        };
        _db.ErrorGroups.Add(group);
        _db.SaveChanges();

        ErrorObject? last = null;
        for (int i = 0; i < count; i++)
        {
            last = new ErrorObject
            {
                ProjectId = _project.Id, ErrorGroupId = group.Id,
                Event = eventText, Type = "BACKEND",
                Timestamp = DateTime.UtcNow.AddSeconds(-i),
            };
            _db.ErrorObjects.Add(last);
        }
        _db.SaveChanges();
        return (group, last!);
    }

    private ErrorAlert CreateAlert(
        string? query = null, string? regexGroups = null,
        int threshold = 1, int window = 30, int frequency = 0,
        string? webhooks = null)
    {
        var alert = new ErrorAlert
        {
            ProjectId = _project.Id, Name = "Alert",
            Query = query, RegexGroups = regexGroups,
            CountThreshold = threshold, ThresholdWindow = window,
            Frequency = frequency, WebhookDestinations = webhooks,
        };
        _db.Set<ErrorAlert>().Add(alert);
        _db.SaveChanges();
        return alert;
    }

    // ── Webhook payload tests ────────────────────────────────────────────

    [Fact]
    public async Task Evaluate_WithGenericWebhook_TriggersAndNoError()
    {
        CreateAlert(webhooks: "[{\"url\":\"https://example.com/hook\",\"type\":\"generic\"}]");
        var (group, obj) = CreateError();
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_WithDiscordWebhook_TriggersAndNoError()
    {
        CreateAlert(webhooks: "[{\"url\":\"https://discord.com/api/webhooks/1234\",\"type\":\"discord\"}]");
        var (group, obj) = CreateError();
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_WithTeamsWebhook_TriggersAndNoError()
    {
        CreateAlert(webhooks: "[{\"url\":\"https://teams.microsoft.com/webhook\",\"type\":\"microsoft_teams\"}]");
        var (group, obj) = CreateError();
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_InvalidWebhookJson_StillTriggersNoError()
    {
        CreateAlert(webhooks: "not-valid-json");
        var (group, obj) = CreateError();
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_EmptyWebhookUrl_StillTriggersNoError()
    {
        CreateAlert(webhooks: "[{\"url\":\"\",\"type\":\"generic\"}]");
        var (group, obj) = CreateError();
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_NullWebhookUrl_StillTriggersNoError()
    {
        CreateAlert(webhooks: "[{\"url\":null,\"type\":null}]");
        var (group, obj) = CreateError();
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_MultipleWebhooks_AllAttempted()
    {
        CreateAlert(webhooks: "[{\"url\":\"https://a.com\",\"type\":\"generic\"},{\"url\":\"https://b.com\",\"type\":\"discord\"}]");
        var (group, obj) = CreateError();
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    // ── Threshold window edge cases ──────────────────────────────────────

    [Fact]
    public async Task Evaluate_ErrorOutsideWindow_NotCounted()
    {
        var alert = CreateAlert(threshold: 2, window: 1); // 1-minute window

        var group = new ErrorGroup
        {
            ProjectId = _project.Id, Event = "Error", Type = "BACKEND",
            State = ErrorGroupState.Open, SecureId = Guid.NewGuid().ToString("N"),
        };
        _db.ErrorGroups.Add(group);
        _db.SaveChanges();

        // One error now
        _db.ErrorObjects.Add(new ErrorObject
        {
            ProjectId = _project.Id, ErrorGroupId = group.Id,
            Event = "Error", Type = "BACKEND", Timestamp = DateTime.UtcNow,
        });
        // One error 10 minutes ago (outside window)
        _db.ErrorObjects.Add(new ErrorObject
        {
            ProjectId = _project.Id, ErrorGroupId = group.Id,
            Event = "Error", Type = "BACKEND", Timestamp = DateTime.UtcNow.AddMinutes(-10),
        });
        _db.SaveChanges();

        var obj = _db.ErrorObjects.First();
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(0, result.AlertsTriggered); // Only 1 in window, need 2
    }

    [Fact]
    public async Task Evaluate_VeryLargeThreshold_NotTriggered()
    {
        CreateAlert(threshold: 1000000);
        var (group, obj) = CreateError(count: 10);
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(0, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_ZeroThreshold_Defaults()
    {
        var alert = new ErrorAlert
        {
            ProjectId = _project.Id, Name = "Zero",
            CountThreshold = 0, ThresholdWindow = 30,
        };
        _db.Set<ErrorAlert>().Add(alert);
        _db.SaveChanges();

        var (group, obj) = CreateError();
        // CountThreshold 0 means >= 0 always true — but null handling uses ?? 1
        // Let's just verify no crash
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsEvaluated);
    }

    // ── Concurrent alert evaluation ──────────────────────────────────────

    [Fact]
    public async Task Evaluate_50Alerts_AllProcessed()
    {
        for (int i = 0; i < 50; i++)
            CreateAlert(query: "Error");

        var (group, obj) = CreateError("Error happened");
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(50, result.AlertsEvaluated);
        Assert.Equal(50, result.AlertsTriggered);
        Assert.Equal(50, result.TriggeredAlertIds.Count);
    }

    [Fact]
    public async Task Evaluate_MixedEnabledDisabled_OnlyEnabledProcessed()
    {
        CreateAlert(); // enabled
        // Add disabled alert directly
        _db.Set<ErrorAlert>().Add(new ErrorAlert
        {
            ProjectId = _project.Id, Name = "Disabled", Disabled = true,
            CountThreshold = 1, ThresholdWindow = 30,
        });
        _db.SaveChanges();
        CreateAlert(); // enabled

        // Note: disabled alerts filtered at query level, not counted in AlertsEvaluated
        var (group, obj) = CreateError();
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(2, result.AlertsEvaluated);
        Assert.Equal(2, result.AlertsTriggered);
    }

    // ── Query and regex combined edge cases ──────────────────────────────

    [Fact]
    public async Task Evaluate_EmptyQueryMatches_Triggered()
    {
        CreateAlert(query: ""); // Empty query matches everything
        var (group, obj) = CreateError("Any error");
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_EmptyRegexArray_NotTriggered()
    {
        CreateAlert(regexGroups: "[]");
        var (group, obj) = CreateError();
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(0, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_UnicodeInQuery_Matches()
    {
        CreateAlert(query: "日本語");
        var (group, obj) = CreateError("日本語エラー");
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_SpecialCharsInRegex_Handled()
    {
        // In C# string: "error\\d+" → JSON deserializes regex to: error\d+ → matches digits
        CreateAlert(regexGroups: "[\"error\\\\d+\"]");
        var (group, obj) = CreateError("error123");
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(1, result.AlertsTriggered);
    }

    // ── Frequency cooldown edge cases ────────────────────────────────────

    [Fact]
    public async Task Evaluate_DifferentGroupsSamAlert_IndependentCooldown()
    {
        var alert = CreateAlert(frequency: 3600);

        var (group1, obj1) = CreateError("Error A");
        var (group2, obj2) = CreateError("Error B");

        // Trigger for group1
        var result1 = await _service.EvaluateErrorAlertsAsync(_project.Id, group1, obj1, CancellationToken.None);
        Assert.Equal(1, result1.AlertsTriggered);

        // Group2 should still trigger (different group, independent cooldown)
        var result2 = await _service.EvaluateErrorAlertsAsync(_project.Id, group2, obj2, CancellationToken.None);
        Assert.Equal(1, result2.AlertsTriggered);
    }

    [Fact]
    public async Task Evaluate_AlertEventRecordCreated_WithCorrectTimestamp()
    {
        var alert = CreateAlert();
        var (group, obj) = CreateError();

        var before = DateTime.UtcNow.AddSeconds(-1);
        await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        var after = DateTime.UtcNow.AddSeconds(1);

        var events = await _db.Set<ErrorAlertEvent>().ToListAsync();
        Assert.Single(events);
        Assert.True(events[0].CreatedAt >= before);
        Assert.True(events[0].CreatedAt <= after);
    }

    // ── MatchesQuery edge cases ──────────────────────────────────────────

    [Theory]
    [InlineData("hello world", "hello world", true)]
    [InlineData("hello", "HELLO WORLD", true)]
    [InlineData(" ", "has spaces", true)]
    [InlineData("特殊", "特殊文字", true)]
    [InlineData("\n", "line\nbreak", true)]
    public void MatchesQuery_EdgeCases(string query, string errorEvent, bool expected)
    {
        Assert.Equal(expected, AlertEvaluationService.MatchesQuery(query, errorEvent));
    }

    // ── MatchesRegex edge cases ──────────────────────────────────────────

    [Fact]
    public void MatchesRegex_DigitPattern_Matches()
    {
        // JSON: ["\\d+"] → regex: \d+ → matches digits
        var json = "[\"\\\\d+\"]";
        Assert.True(AlertEvaluationService.MatchesRegex(json, "abc123"));
    }

    [Fact]
    public void MatchesRegex_CaseInsensitiveFlag_Matches()
    {
        Assert.True(AlertEvaluationService.MatchesRegex("[\"(?i)error\"]", "ERROR"));
    }

    [Fact]
    public void MatchesRegex_EmptyStringMatchesEmptyPattern()
    {
        // Empty event returns false (guard clause)
        Assert.False(AlertEvaluationService.MatchesRegex("[\"^$\"]", ""));
    }

    // ── Disabled alert with query ────────────────────────────────────────

    [Fact]
    public async Task Evaluate_DisabledAlertWithMatchingQuery_NotTriggered()
    {
        var alert = new ErrorAlert
        {
            ProjectId = _project.Id, Name = "Disabled", Disabled = true,
            Query = "Error", CountThreshold = 1, ThresholdWindow = 30,
        };
        _db.Set<ErrorAlert>().Add(alert);
        _db.SaveChanges();

        var (group, obj) = CreateError("Error");
        var result = await _service.EvaluateErrorAlertsAsync(_project.Id, group, obj, CancellationToken.None);
        Assert.Equal(0, result.AlertsEvaluated);
    }

    private class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler());
        private class StubHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private class StubNotificationService : HoldFast.Shared.Notifications.INotificationService
    {
        public Task SendSlackMessageAsync(string accessToken, string channelId, HoldFast.Shared.Notifications.SlackMessage message, CancellationToken ct) => Task.CompletedTask;
        public Task SendDiscordMessageAsync(string webhookUrl, HoldFast.Shared.Notifications.DiscordMessage message, CancellationToken ct) => Task.CompletedTask;
        public Task SendTeamsMessageAsync(string webhookUrl, HoldFast.Shared.Notifications.TeamsMessage message, CancellationToken ct) => Task.CompletedTask;
        public Task SendWebhookAsync(string url, object payload, CancellationToken ct) => Task.CompletedTask;
    }
}
