using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Private;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Tests for GraphQL response/DTO record types — positional construction,
/// equality, immutability, and edge case values.
/// </summary>
public class ResponseTypeTests
{
    // ══════════════════════════════════════════════════════════════════
    // IntegrationStatus
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void IntegrationStatus_Construction()
    {
        var status = new IntegrationStatus(true, "Project", DateTime.UtcNow);

        Assert.True(status.Integrated);
        Assert.Equal("Project", status.ResourceType);
        Assert.NotNull(status.CreatedAt);
    }

    [Fact]
    public void IntegrationStatus_DefaultCreatedAt_IsNull()
    {
        var status = new IntegrationStatus(false, "Workspace");

        Assert.Null(status.CreatedAt);
    }

    [Fact]
    public void IntegrationStatus_Equality()
    {
        var a = new IntegrationStatus(true, "Project", null);
        var b = new IntegrationStatus(true, "Project", null);

        Assert.Equal(a, b);
    }

    // ══════════════════════════════════════════════════════════════════
    // SessionsReportRow
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void SessionsReportRow_AllFieldsSet()
    {
        var row = new SessionsReportRow(
            "key1", "user@test.com", 5,
            new DateTime(2026, 1, 1), new DateTime(2026, 3, 1),
            10, 3, 2.5, 10.0, 12.5, 3.0, 15.0, 15.0, "NYC, US");

        Assert.Equal("key1", row.Key);
        Assert.Equal("user@test.com", row.Email);
        Assert.Equal(5, row.NumSessions);
        Assert.Equal(10, row.NumDaysVisited);
        Assert.Equal(3, row.NumMonthsVisited);
        Assert.Equal(2.5, row.AvgActiveLengthMins);
        Assert.Equal("NYC, US", row.Location);
    }

    [Fact]
    public void SessionsReportRow_ZeroValues()
    {
        var row = new SessionsReportRow(
            "", "", 0,
            DateTime.MinValue, DateTime.MinValue,
            0, 0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, "");

        Assert.Equal(0, row.NumSessions);
        Assert.Equal(0.0, row.AvgActiveLengthMins);
    }

    // ══════════════════════════════════════════════════════════════════
    // MatchedErrorTag
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MatchedErrorTag_Construction()
    {
        var tag = new MatchedErrorTag(42, "NPE", "NullPointerException in UserService", 0.95);

        Assert.Equal(42, tag.Id);
        Assert.Equal("NPE", tag.Title);
        Assert.Equal(0.95, tag.Score);
    }

    [Fact]
    public void MatchedErrorTag_PerfectScore()
    {
        var tag = new MatchedErrorTag(1, "Match", "Exact match", 1.0);
        Assert.Equal(1.0, tag.Score);
    }

    [Fact]
    public void MatchedErrorTag_ZeroScore()
    {
        var tag = new MatchedErrorTag(1, "NoMatch", "No match", 0.0);
        Assert.Equal(0.0, tag.Score);
    }

    // ══════════════════════════════════════════════════════════════════
    // ErrorGroupInstances
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ErrorGroupInstances_EmptyList()
    {
        var result = new ErrorGroupInstances([], 0);

        Assert.Empty(result.ErrorObjects);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public void ErrorGroupInstances_WithObjects()
    {
        var objects = new List<ErrorObject>
        {
            new() { Event = "Error1" },
            new() { Event = "Error2" },
        };
        var result = new ErrorGroupInstances(objects, 100);

        Assert.Equal(2, result.ErrorObjects.Count);
        Assert.Equal(100, result.TotalCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // SessionPayload
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void SessionPayload_Construction()
    {
        var payload = new SessionPayload(
            "[{\"type\":\"click\"}]",
            [new ErrorObject { Event = "err" }],
            [new RageClickEvent { TotalClicks = 5 }],
            [new SessionComment { Text = "comment" }],
            "2026-01-15T12:00:00Z");

        Assert.Equal("[{\"type\":\"click\"}]", payload.Events);
        Assert.Single(payload.Errors);
        Assert.Single(payload.RageClicks);
        Assert.Single(payload.SessionComments);
        Assert.Equal("2026-01-15T12:00:00Z", payload.LastUserInteractionTime);
    }

    [Fact]
    public void SessionPayload_EmptyPayload()
    {
        var payload = new SessionPayload("[]", [], [], [], "");

        Assert.Equal("[]", payload.Events);
        Assert.Empty(payload.Errors);
        Assert.Empty(payload.RageClicks);
        Assert.Empty(payload.SessionComments);
    }

    // ══════════════════════════════════════════════════════════════════
    // AlertsPagePayload
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void AlertsPagePayload_EmptyPayload()
    {
        var payload = new AlertsPagePayload([], [], [], [], []);

        Assert.Empty(payload.Alerts);
        Assert.Empty(payload.ErrorAlerts);
        Assert.Empty(payload.SessionAlerts);
        Assert.Empty(payload.LogAlerts);
        Assert.Empty(payload.MetricMonitors);
    }

    [Fact]
    public void AlertsPagePayload_WithAllTypes()
    {
        var payload = new AlertsPagePayload(
            [new Alert { Name = "a" }],
            [new ErrorAlert { Name = "ea" }],
            [new SessionAlert { Name = "sa" }],
            [new LogAlert { Name = "la" }],
            [new MetricMonitor { Name = "mm" }]);

        Assert.Single(payload.Alerts);
        Assert.Single(payload.ErrorAlerts);
        Assert.Single(payload.SessionAlerts);
        Assert.Single(payload.LogAlerts);
        Assert.Single(payload.MetricMonitors);
    }

    // ══════════════════════════════════════════════════════════════════
    // SessionResults
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void SessionResults_Construction()
    {
        var result = new SessionResults(
            [new Session { SecureId = "s1" }], 10, 50000, 30000);

        Assert.Single(result.Sessions);
        Assert.Equal(10, result.TotalCount);
        Assert.Equal(50000, result.TotalLength);
        Assert.Equal(30000, result.TotalActiveLength);
    }

    [Fact]
    public void SessionResults_ZeroTotals()
    {
        var result = new SessionResults([], 0, 0, 0);

        Assert.Empty(result.Sessions);
        Assert.Equal(0, result.TotalCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // KeyValueSuggestion / ValueSuggestion
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void KeyValueSuggestion_Construction()
    {
        var suggestion = new KeyValueSuggestion("env",
            [new ValueSuggestion("production", 100, 0),
             new ValueSuggestion("staging", 50, 1)]);

        Assert.Equal("env", suggestion.Key);
        Assert.Equal(2, suggestion.Values.Count);
        Assert.Equal("production", suggestion.Values[0].Value);
        Assert.Equal(100, suggestion.Values[0].Count);
        Assert.Equal(0, suggestion.Values[0].Rank);
    }

    [Fact]
    public void KeyValueSuggestion_EmptyValues()
    {
        var suggestion = new KeyValueSuggestion("empty_key", []);

        Assert.Empty(suggestion.Values);
    }

    [Fact]
    public void ValueSuggestion_Equality()
    {
        var a = new ValueSuggestion("value", 10, 0);
        var b = new ValueSuggestion("value", 10, 0);

        Assert.Equal(a, b);
    }

    // ══════════════════════════════════════════════════════════════════
    // LogLine
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void LogLine_Construction()
    {
        var line = new LogLine(DateTime.UtcNow, "Hello world", "INFO", "{\"key\":\"value\"}");

        Assert.Equal("Hello world", line.Body);
        Assert.Equal("INFO", line.Severity);
        Assert.Contains("key", line.Labels);
    }

    [Fact]
    public void LogLine_NullSeverity()
    {
        var line = new LogLine(DateTime.UtcNow, "body", null, "{}");

        Assert.Null(line.Severity);
    }

    // ══════════════════════════════════════════════════════════════════
    // IssuesSearchResult
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void IssuesSearchResult_Construction()
    {
        var result = new IssuesSearchResult("123", "Fix bug", "https://github.com/org/repo/issues/123");

        Assert.Equal("123", result.Id);
        Assert.Equal("Fix bug", result.Title);
        Assert.Equal("https://github.com/org/repo/issues/123", result.IssueUrl);
    }

    [Fact]
    public void IssuesSearchResult_Equality()
    {
        var a = new IssuesSearchResult("1", "Title", "url");
        var b = new IssuesSearchResult("1", "Title", "url");

        Assert.Equal(a, b);
    }

    // ══════════════════════════════════════════════════════════════════
    // Channel Info Types
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void SanitizedSlackChannel_NullableFields()
    {
        var channel = new SanitizedSlackChannel(null, null);

        Assert.Null(channel.WebhookChannel);
        Assert.Null(channel.WebhookChannelId);
    }

    [Fact]
    public void SanitizedSlackChannel_WithValues()
    {
        var channel = new SanitizedSlackChannel("#general", "C12345");

        Assert.Equal("#general", channel.WebhookChannel);
        Assert.Equal("C12345", channel.WebhookChannelId);
    }

    [Fact]
    public void DiscordChannelInfo_Construction()
    {
        var channel = new DiscordChannelInfo("123456", "general");

        Assert.Equal("123456", channel.Id);
        Assert.Equal("general", channel.Name);
    }

    [Fact]
    public void MicrosoftTeamsChannelInfo_Construction()
    {
        var channel = new MicrosoftTeamsChannelInfo("team-chan-1", "General");

        Assert.Equal("team-chan-1", channel.Id);
        Assert.Equal("General", channel.Name);
    }

    // ══════════════════════════════════════════════════════════════════
    // Other Types
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ReferrerTablePayload_Construction()
    {
        var payload = new ReferrerTablePayload("google.com", 150, 45.5);

        Assert.Equal("google.com", payload.Host);
        Assert.Equal(150, payload.Count);
        Assert.Equal(45.5, payload.Percent);
    }

    [Fact]
    public void TopUsersPayload_Construction()
    {
        var payload = new TopUsersPayload(1, "user@test.com", 3600, 25.5, "{\"role\":\"admin\"}");

        Assert.Equal(1, payload.Id);
        Assert.Equal("user@test.com", payload.Identifier);
        Assert.Equal(3600, payload.TotalActiveTime);
        Assert.Equal(25.5, payload.ActiveTimePercentage);
    }

    [Fact]
    public void AverageSessionLength_Construction()
    {
        var avg = new AverageSessionLength(5.75);
        Assert.Equal(5.75, avg.Length);
    }

    [Fact]
    public void NewUsersCount_Construction()
    {
        var count = new NewUsersCount(42);
        Assert.Equal(42, count.Count);
    }

    [Fact]
    public void UserFingerprintCount_Construction()
    {
        var count = new UserFingerprintCount(100);
        Assert.Equal(100, count.Count);
    }

    [Fact]
    public void ErrorGroupTagAggregation_Construction()
    {
        var agg = new ErrorGroupTagAggregation("browser",
            [new ErrorGroupTagAggregationBucket("Chrome", 500, 75.0),
             new ErrorGroupTagAggregationBucket("Firefox", 166, 25.0)]);

        Assert.Equal("browser", agg.Key);
        Assert.Equal(2, agg.Buckets.Count);
        Assert.Equal(75.0, agg.Buckets[0].Percent);
    }

    [Fact]
    public void ErrorGroupTagAggregationBucket_Equality()
    {
        var a = new ErrorGroupTagAggregationBucket("Chrome", 500, 75.0);
        var b = new ErrorGroupTagAggregationBucket("Chrome", 500, 75.0);

        Assert.Equal(a, b);
    }

    [Fact]
    public void SSOLogin_Construction()
    {
        var login = new SSOLogin("example.com", "client-id-123");

        Assert.Equal("example.com", login.Domain);
        Assert.Equal("client-id-123", login.ClientId);
    }

    [Fact]
    public void AlertDestinationInput_Construction()
    {
        var input = new AlertDestinationInput("Slack", "#alerts", "alerts-channel");

        Assert.Equal("Slack", input.DestinationType);
        Assert.Equal("#alerts", input.TypeId);
        Assert.Equal("alerts-channel", input.TypeName);
    }

    [Fact]
    public void AlertDestinationInput_NullableFields()
    {
        var input = new AlertDestinationInput("Email", null, null);

        Assert.Null(input.TypeId);
        Assert.Null(input.TypeName);
    }

    [Fact]
    public void IntegrationProjectMappingInput_Construction()
    {
        var input = new IntegrationProjectMappingInput(42, "ext-123");

        Assert.Equal(42, input.ProjectId);
        Assert.Equal("ext-123", input.ExternalId);
    }

    [Fact]
    public void IntegrationProjectMappingInput_NullExternalId()
    {
        var input = new IntegrationProjectMappingInput(1, null);

        Assert.Null(input.ExternalId);
    }

    [Fact]
    public void ProjectsAndWorkspacesResult_Construction()
    {
        var result = new ProjectsAndWorkspacesResult(
            [new Project { Name = "P1" }],
            [new Workspace { Name = "W1" }]);

        Assert.Single(result.Projects);
        Assert.Single(result.Workspaces);
    }

    [Fact]
    public void ProjectOrWorkspaceResult_ProjectOnly()
    {
        var result = new ProjectOrWorkspaceResult(new Project { Name = "P" }, null);

        Assert.NotNull(result.Project);
        Assert.Null(result.Workspace);
    }

    [Fact]
    public void ProjectOrWorkspaceResult_WorkspaceOnly()
    {
        var result = new ProjectOrWorkspaceResult(null, new Workspace { Name = "W" });

        Assert.Null(result.Project);
        Assert.NotNull(result.Workspace);
    }

    [Fact]
    public void LogAlertsPagePayload_Construction()
    {
        var payload = new LogAlertsPagePayload([new LogAlert { Name = "LA1" }]);

        Assert.Single(payload.LogAlerts);
    }

    [Fact]
    public void TimelineIndicatorEvent_Construction()
    {
        var evt = new TimelineIndicatorEvent("sess-123", 1234.5, 1.0, null, 4);

        Assert.Equal("sess-123", evt.SessionSecureId);
        Assert.Equal(1234.5, evt.Timestamp);
        Assert.Equal(4, evt.Type);
        Assert.Null(evt.Data);
    }
}
