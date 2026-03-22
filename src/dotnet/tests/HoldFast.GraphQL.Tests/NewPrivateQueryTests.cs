using System.Security.Claims;
using System.Text;
using HoldFast.Data;
using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HoldFast.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Comprehensive tests for the new PrivateQuery methods added in the session/error detail,
/// suggestion, integration status, and API key resolution areas.
/// Uses SQLite in-memory database, hand-rolled fakes for IAuthorizationService,
/// IStorageService, and IClickHouseService.
/// </summary>
public class NewPrivateQueryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly PrivateQuery _query;
    private readonly Workspace _workspace;
    private readonly Project _project;
    private readonly Session _session;
    private readonly ClaimsPrincipal _principal;
    private readonly FakeAuthorizationService _authz;
    private readonly FakeStorageService _storage;
    private readonly FakeClickHouseService _clickHouse;

    public NewPrivateQueryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _workspace = new Workspace { Name = "TestWS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _project = new Project { Name = "TestProj", WorkspaceId = _workspace.Id, Secret = "test-api-key-123" };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _session = new Session
        {
            SecureId = "sess-secure-001",
            ProjectId = _project.Id,
            CreatedAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
        };
        _db.Sessions.Add(_session);
        _db.SaveChanges();

        _query = new PrivateQuery();

        // Create a ClaimsPrincipal with a uid claim
        var claims = new[] { new Claim(HoldFastClaimTypes.Uid, "test-uid-001") };
        var identity = new ClaimsIdentity(claims, "Test");
        _principal = new ClaimsPrincipal(identity);

        _authz = new FakeAuthorizationService();
        _storage = new FakeStorageService();
        _clickHouse = new FakeClickHouseService();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ══════════════════════════════════════════════════════════════════
    // GetErrors
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetErrors_ReturnsEmptyList_WhenNoErrors()
    {
        var result = await _query.GetErrors(
            _session.SecureId, _principal, _authz, _db, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetErrors_SessionNotFound_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _query.GetErrors("nonexistent-session", _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task GetErrors_ReturnsErrorsForSession()
    {
        var errorGroup = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-001" };
        _db.Set<ErrorGroup>().Add(errorGroup);
        _db.SaveChanges();

        _db.Set<ErrorObject>().Add(new ErrorObject
        {
            ProjectId = _project.Id,
            SessionId = _session.Id,
            ErrorGroupId = errorGroup.Id,
            Event = "TypeError",
            CreatedAt = new DateTime(2026, 1, 15, 10, 5, 0, DateTimeKind.Utc),
        });
        _db.SaveChanges();

        var result = await _query.GetErrors(
            _session.SecureId, _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("TypeError", result[0].Event);
    }

    [Fact]
    public async Task GetErrors_MultipleErrors_OrderedByCreatedAt()
    {
        var errorGroup = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-002" };
        _db.Set<ErrorGroup>().Add(errorGroup);
        _db.SaveChanges();

        _db.Set<ErrorObject>().AddRange(
            new ErrorObject
            {
                ProjectId = _project.Id, SessionId = _session.Id, ErrorGroupId = errorGroup.Id,
                Event = "Second", CreatedAt = new DateTime(2026, 1, 15, 10, 10, 0, DateTimeKind.Utc),
            },
            new ErrorObject
            {
                ProjectId = _project.Id, SessionId = _session.Id, ErrorGroupId = errorGroup.Id,
                Event = "First", CreatedAt = new DateTime(2026, 1, 15, 10, 5, 0, DateTimeKind.Utc),
            },
            new ErrorObject
            {
                ProjectId = _project.Id, SessionId = _session.Id, ErrorGroupId = errorGroup.Id,
                Event = "Third", CreatedAt = new DateTime(2026, 1, 15, 10, 15, 0, DateTimeKind.Utc),
            });
        _db.SaveChanges();

        var result = await _query.GetErrors(
            _session.SecureId, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal("First", result[0].Event);
        Assert.Equal("Second", result[1].Event);
        Assert.Equal("Third", result[2].Event);
    }

    [Fact]
    public async Task GetErrors_DoesNotReturnErrorsFromOtherSessions()
    {
        var otherSession = new Session
        {
            SecureId = "sess-other-001", ProjectId = _project.Id,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Sessions.Add(otherSession);
        _db.SaveChanges();

        var errorGroup = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-003" };
        _db.Set<ErrorGroup>().Add(errorGroup);
        _db.SaveChanges();

        _db.Set<ErrorObject>().Add(new ErrorObject
        {
            ProjectId = _project.Id, SessionId = otherSession.Id, ErrorGroupId = errorGroup.Id,
            Event = "OtherSessionError", CreatedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();

        var result = await _query.GetErrors(
            _session.SecureId, _principal, _authz, _db, CancellationToken.None);
        Assert.Empty(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetResources
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetResources_SessionNotFound_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _query.GetResources("no-such-session", _principal, _authz, _db, _storage, CancellationToken.None));
    }

    [Fact]
    public async Task GetResources_NoStorageData_ReturnsNull()
    {
        var result = await _query.GetResources(
            _session.SecureId, _principal, _authz, _db, _storage, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetResources_WithStorageData_ReturnsContent()
    {
        var key = $"{_session.ProjectId}/{_session.Id}/resources";
        _storage.Store["sessions:" + key] = "[{\"name\":\"resource1\"}]";

        var result = await _query.GetResources(
            _session.SecureId, _principal, _authz, _db, _storage, CancellationToken.None);

        Assert.Equal("[{\"name\":\"resource1\"}]", result);
    }

    [Fact]
    public async Task GetResources_EmptyStorageContent_ReturnsEmptyString()
    {
        var key = $"{_session.ProjectId}/{_session.Id}/resources";
        _storage.Store["sessions:" + key] = "";

        var result = await _query.GetResources(
            _session.SecureId, _principal, _authz, _db, _storage, CancellationToken.None);

        Assert.Equal("", result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetTimelineIndicatorEvents
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetTimelineIndicatorEvents_SessionNotFound_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _query.GetTimelineIndicatorEvents("bad-id", _principal, _authz, _db, _storage, CancellationToken.None));
    }

    [Fact]
    public async Task GetTimelineIndicatorEvents_NoData_ReturnsNull()
    {
        var result = await _query.GetTimelineIndicatorEvents(
            _session.SecureId, _principal, _authz, _db, _storage, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetTimelineIndicatorEvents_WithData_ReturnsContent()
    {
        var key = $"{_session.ProjectId}/{_session.Id}/timeline-indicator-events";
        _storage.Store["sessions:" + key] = "[{\"ts\":1234}]";

        var result = await _query.GetTimelineIndicatorEvents(
            _session.SecureId, _principal, _authz, _db, _storage, CancellationToken.None);

        Assert.Equal("[{\"ts\":1234}]", result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetWebsocketEvents
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetWebsocketEvents_SessionNotFound_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _query.GetWebsocketEvents("missing", _principal, _authz, _db, _storage, CancellationToken.None));
    }

    [Fact]
    public async Task GetWebsocketEvents_NoData_ReturnsNull()
    {
        var result = await _query.GetWebsocketEvents(
            _session.SecureId, _principal, _authz, _db, _storage, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetWebsocketEvents_WithData_ReturnsContent()
    {
        var key = $"{_session.ProjectId}/{_session.Id}/websocket-events";
        _storage.Store["sessions:" + key] = "{\"events\":[]}";

        var result = await _query.GetWebsocketEvents(
            _session.SecureId, _principal, _authz, _db, _storage, CancellationToken.None);

        Assert.Equal("{\"events\":[]}", result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetWebVitals
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetWebVitals_SessionNotFound_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _query.GetWebVitals("nope", _principal, _authz, _db, _clickHouse, CancellationToken.None));
    }

    [Fact]
    public async Task GetWebVitals_ReturnsMetricsBucketsFromClickHouse()
    {
        _clickHouse.MetricsResult = new MetricsBuckets
        {
            Buckets = [new MetricsBucket { Value = 0.25, Group = "CLS" }],
            TotalCount = 1,
        };

        var result = await _query.GetWebVitals(
            _session.SecureId, _principal, _authz, _db, _clickHouse, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result.Buckets);
        Assert.Equal("CLS", result.Buckets[0].Group.FirstOrDefault());
    }

    [Fact]
    public async Task GetWebVitals_EmptyMetrics_ReturnsEmptyBuckets()
    {
        _clickHouse.MetricsResult = new MetricsBuckets { Buckets = [], TotalCount = 0 };

        var result = await _query.GetWebVitals(
            _session.SecureId, _principal, _authz, _db, _clickHouse, CancellationToken.None);

        Assert.Empty(result.Buckets);
        Assert.Equal(0L, result.BucketCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetSessionInsight
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSessionInsight_SessionNotFound_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _query.GetSessionInsight(999999, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task GetSessionInsight_NoInsight_ReturnsNull()
    {
        var result = await _query.GetSessionInsight(
            _session.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetSessionInsight_WithInsight_ReturnsIt()
    {
        _db.Set<SessionInsight>().Add(new SessionInsight
        {
            SessionId = _session.Id,
            Insight = "User encountered a TypeError on checkout page.",
        });
        _db.SaveChanges();

        var result = await _query.GetSessionInsight(
            _session.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("User encountered a TypeError on checkout page.", result!.Insight);
    }

    [Fact]
    public async Task GetSessionInsight_ReturnsInsightForCorrectSession()
    {
        var otherSession = new Session
        {
            SecureId = "sess-other-insight", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow,
        };
        _db.Sessions.Add(otherSession);
        _db.SaveChanges();

        _db.Set<SessionInsight>().Add(new SessionInsight
        {
            SessionId = otherSession.Id, Insight = "Other session insight",
        });
        _db.SaveChanges();

        var result = await _query.GetSessionInsight(
            _session.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Null(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetErrorIssue
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetErrorIssue_ErrorGroupNotFound_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _query.GetErrorIssue("nonexistent-eg", _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task GetErrorIssue_NoAttachments_ReturnsEmptyList()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-no-attachments" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        var result = await _query.GetErrorIssue(
            "eg-no-attachments", _principal, _authz, _db, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetErrorIssue_ReturnsAttachments_OrderedByCreatedAtDesc()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-with-issues" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        _db.Set<ExternalAttachment>().AddRange(
            new ExternalAttachment
            {
                ErrorGroupId = eg.Id, IntegrationType = "Linear", Title = "Older",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            new ExternalAttachment
            {
                ErrorGroupId = eg.Id, IntegrationType = "Jira", Title = "Newer",
                CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            });
        _db.SaveChanges();

        var result = await _query.GetErrorIssue(
            "eg-with-issues", _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("Newer", result[0].Title);
        Assert.Equal("Older", result[1].Title);
    }

    [Fact]
    public async Task GetErrorIssue_DoesNotReturnAttachmentsFromOtherGroups()
    {
        var eg1 = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-iso-1" };
        var eg2 = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-iso-2" };
        _db.Set<ErrorGroup>().AddRange(eg1, eg2);
        _db.SaveChanges();

        _db.Set<ExternalAttachment>().Add(new ExternalAttachment
        {
            ErrorGroupId = eg2.Id, IntegrationType = "Slack", Title = "OtherGroup",
            CreatedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();

        var result = await _query.GetErrorIssue(
            "eg-iso-1", _principal, _authz, _db, CancellationToken.None);
        Assert.Empty(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetErrorTags
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetErrorTags_Empty_ReturnsEmptyList()
    {
        var result = await _query.GetErrorTags(_principal, _authz, _db, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetErrorTags_ReturnsAllTags()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-tags" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        _db.Set<ErrorTag>().AddRange(
            new ErrorTag { ErrorGroupId = eg.Id, Title = "NullRef" },
            new ErrorTag { ErrorGroupId = eg.Id, Title = "Timeout" },
            new ErrorTag { ErrorGroupId = eg.Id, Title = "CORS" });
        _db.SaveChanges();

        var result = await _query.GetErrorTags(_principal, _authz, _db, CancellationToken.None);
        Assert.Equal(3, result.Count);
    }

    // ══════════════════════════════════════════════════════════════════
    // MatchErrorTag
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MatchErrorTag_NoMatches_ReturnsEmptyList()
    {
        var result = await _query.MatchErrorTag(
            "zzzzz", _principal, _authz, _db, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task MatchErrorTag_MatchesByTitle()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-match-title" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        _db.Set<ErrorTag>().Add(new ErrorTag { ErrorGroupId = eg.Id, Title = "NullReferenceException" });
        _db.SaveChanges();

        var result = await _query.MatchErrorTag(
            "NullRef", _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("NullReferenceException", result[0].Title);
    }

    [Fact]
    public async Task MatchErrorTag_MatchesByDescription()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-match-desc" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        _db.Set<ErrorTag>().Add(new ErrorTag
        {
            ErrorGroupId = eg.Id, Title = "DatabaseError",
            Description = "Connection timeout to PostgreSQL",
        });
        _db.SaveChanges();

        var result = await _query.MatchErrorTag(
            "PostgreSQL", _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("DatabaseError", result[0].Title);
    }

    [Fact]
    public async Task MatchErrorTag_CaseInsensitive()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-match-case" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        _db.Set<ErrorTag>().Add(new ErrorTag { ErrorGroupId = eg.Id, Title = "CORS Error" });
        _db.SaveChanges();

        var result = await _query.MatchErrorTag(
            "cors", _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task MatchErrorTag_ExactMatch_ScoreIsOne()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-exact-score" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        _db.Set<ErrorTag>().Add(new ErrorTag { ErrorGroupId = eg.Id, Title = "Timeout" });
        _db.SaveChanges();

        var result = await _query.MatchErrorTag(
            "Timeout", _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(1.0, result[0].Score);
    }

    [Fact]
    public async Task MatchErrorTag_PartialMatch_ScoreIsHalf()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-partial-score" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        _db.Set<ErrorTag>().Add(new ErrorTag { ErrorGroupId = eg.Id, Title = "TimeoutError" });
        _db.SaveChanges();

        var result = await _query.MatchErrorTag(
            "Timeout", _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(0.5, result[0].Score);
    }

    [Fact]
    public async Task MatchErrorTag_LimitedTo10Results()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-limit-10" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        for (int i = 0; i < 15; i++)
        {
            _db.Set<ErrorTag>().Add(new ErrorTag
            {
                ErrorGroupId = eg.Id, Title = $"Error_{i}",
            });
        }
        _db.SaveChanges();

        var result = await _query.MatchErrorTag(
            "Error", _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(10, result.Count);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetProjectSuggestion
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetProjectSuggestion_NoMatch_ReturnsEmpty()
    {
        var result = await _query.GetProjectSuggestion(
            "zzzznonexistent", _principal, _authz, _db, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetProjectSuggestion_MatchesByName_CaseInsensitive()
    {
        var result = await _query.GetProjectSuggestion(
            "testproj", _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("TestProj", result[0].Name);
    }

    [Fact]
    public async Task GetProjectSuggestion_PartialMatch()
    {
        var result = await _query.GetProjectSuggestion(
            "Test", _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetProjectSuggestion_LimitedTo20Results()
    {
        for (int i = 0; i < 25; i++)
        {
            _db.Projects.Add(new Project { Name = $"BulkProject_{i}", WorkspaceId = _workspace.Id });
        }
        _db.SaveChanges();

        var result = await _query.GetProjectSuggestion(
            "BulkProject", _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(20, result.Count);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetEnvironmentSuggestion
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetEnvironmentSuggestion_ReturnsFieldsFromClickHouse()
    {
        _clickHouse.SessionKeyValuesResult = ["production", "staging", "development"];

        var result = await _query.GetEnvironmentSuggestion(
            _project.Id, _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.All(result, f =>
        {
            Assert.Equal("environment", f.Name);
            Assert.Equal("session", f.Type);
            Assert.Equal(_project.Id, f.ProjectId);
        });
        Assert.Equal("production", result[0].Value);
    }

    [Fact]
    public async Task GetEnvironmentSuggestion_EmptyValues_ReturnsEmptyList()
    {
        _clickHouse.SessionKeyValuesResult = [];

        var result = await _query.GetEnvironmentSuggestion(
            _project.Id, _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Empty(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetIdentifierSuggestion
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetIdentifierSuggestion_ReturnsValues()
    {
        _clickHouse.SessionKeyValuesResult = ["user@test.com", "admin@test.com"];

        var result = await _query.GetIdentifierSuggestion(
            _project.Id, null, _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains("user@test.com", result);
    }

    [Fact]
    public async Task GetIdentifierSuggestion_EmptyResult()
    {
        _clickHouse.SessionKeyValuesResult = [];

        var result = await _query.GetIdentifierSuggestion(
            _project.Id, "nobody", _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetIdentifierSuggestion_PassesQueryThrough()
    {
        _clickHouse.SessionKeyValuesResult = ["filtered@test.com"];

        var result = await _query.GetIdentifierSuggestion(
            _project.Id, "filtered", _principal, _authz, _clickHouse, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("filtered", _clickHouse.LastQueryParam);
    }

    // ══════════════════════════════════════════════════════════════════
    // IsIntegratedWith
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IsIntegratedWith_Slack_FalseWhenNoToken()
    {
        var result = await _query.IsIntegratedWith(
            IntegrationType.Slack, _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task IsIntegratedWith_Slack_TrueWhenTokenSet()
    {
        _workspace.SlackAccessToken = "xoxb-slack-token";
        _db.SaveChanges();

        var result = await _query.IsIntegratedWith(
            IntegrationType.Slack, _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task IsIntegratedWith_Linear_TrueWhenTokenSet()
    {
        _workspace.LinearAccessToken = "lin-token";
        _db.SaveChanges();

        var result = await _query.IsIntegratedWith(
            IntegrationType.Linear, _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task IsIntegratedWith_Discord_TrueWhenGuildIdSet()
    {
        _workspace.DiscordGuildId = "1234567890";
        _db.SaveChanges();

        var result = await _query.IsIntegratedWith(
            IntegrationType.Discord, _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task IsIntegratedWith_MicrosoftTeams_TrueWhenTenantSet()
    {
        _workspace.MicrosoftTeamsTenantId = "tenant-id";
        _db.SaveChanges();

        var result = await _query.IsIntegratedWith(
            IntegrationType.MicrosoftTeams, _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task IsIntegratedWith_Vercel_TrueWhenTokenSet()
    {
        _workspace.VercelAccessToken = "vercel-token";
        _db.SaveChanges();

        var result = await _query.IsIntegratedWith(
            IntegrationType.Vercel, _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task IsIntegratedWith_ClickUp_TrueWhenTokenSet()
    {
        _workspace.ClickupAccessToken = "clickup-token";
        _db.SaveChanges();

        var result = await _query.IsIntegratedWith(
            IntegrationType.ClickUp, _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task IsIntegratedWith_Zapier_TrueWhenProjectTokenSet()
    {
        _project.ZapierAccessToken = "zapier-token";
        _db.SaveChanges();

        var result = await _query.IsIntegratedWith(
            IntegrationType.Zapier, _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task IsIntegratedWith_FallbackType_UsesProjectMapping()
    {
        _db.Set<IntegrationProjectMapping>().Add(new IntegrationProjectMapping
        {
            ProjectId = _project.Id, IntegrationType = "GitHub", ExternalId = "repo-123",
        });
        _db.SaveChanges();

        var result = await _query.IsIntegratedWith(
            IntegrationType.GitHub, _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task IsIntegratedWith_FallbackType_FalseWhenNoMapping()
    {
        var result = await _query.IsIntegratedWith(
            IntegrationType.Jira, _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task IsIntegratedWith_NonexistentProject_ReturnsFalse()
    {
        var result = await _query.IsIntegratedWith(
            IntegrationType.Slack, 999999, _principal, _authz, _db, CancellationToken.None);
        Assert.False(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // IsWorkspaceIntegratedWith
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IsWorkspaceIntegratedWith_Slack_FalseWhenNoToken()
    {
        var result = await _query.IsWorkspaceIntegratedWith(
            IntegrationType.Slack, _workspace.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task IsWorkspaceIntegratedWith_Slack_TrueWhenTokenSet()
    {
        _workspace.SlackAccessToken = "xoxb-ws-token";
        _db.SaveChanges();

        var result = await _query.IsWorkspaceIntegratedWith(
            IntegrationType.Slack, _workspace.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task IsWorkspaceIntegratedWith_NonexistentWorkspace_ReturnsFalse()
    {
        var result = await _query.IsWorkspaceIntegratedWith(
            IntegrationType.Linear, 999999, _principal, _authz, _db, CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task IsWorkspaceIntegratedWith_FallbackType_UsesWorkspaceMapping()
    {
        _db.Set<IntegrationWorkspaceMapping>().Add(new IntegrationWorkspaceMapping
        {
            WorkspaceId = _workspace.Id, IntegrationType = "GitLab", AccessToken = "gl-token",
        });
        _db.SaveChanges();

        var result = await _query.IsWorkspaceIntegratedWith(
            IntegrationType.GitLab, _workspace.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task IsWorkspaceIntegratedWith_FallbackType_FalseWhenNoMapping()
    {
        var result = await _query.IsWorkspaceIntegratedWith(
            IntegrationType.Heroku, _workspace.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.False(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // IsProjectIntegratedWith
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IsProjectIntegratedWith_NoMapping_ReturnsFalse()
    {
        var result = await _query.IsProjectIntegratedWith(
            IntegrationType.Jira, _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task IsProjectIntegratedWith_WithMapping_ReturnsTrue()
    {
        _db.Set<IntegrationProjectMapping>().Add(new IntegrationProjectMapping
        {
            ProjectId = _project.Id, IntegrationType = "Jira", ExternalId = "PROJ-123",
        });
        _db.SaveChanges();

        var result = await _query.IsProjectIntegratedWith(
            IntegrationType.Jira, _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task IsProjectIntegratedWith_WrongType_ReturnsFalse()
    {
        _db.Set<IntegrationProjectMapping>().Add(new IntegrationProjectMapping
        {
            ProjectId = _project.Id, IntegrationType = "Slack",
        });
        _db.SaveChanges();

        var result = await _query.IsProjectIntegratedWith(
            IntegrationType.Jira, _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.False(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetMetricsIntegration
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetMetricsIntegration_NoSetup_ReturnsNotIntegrated()
    {
        var result = await _query.GetMetricsIntegration(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.False(result.Integrated);
        Assert.Equal("Metric", result.ResourceType);
        Assert.Null(result.CreatedAt);
    }

    [Fact]
    public async Task GetMetricsIntegration_WithSetup_ReturnsIntegrated()
    {
        var setupTime = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
        _db.Set<SetupEvent>().Add(new SetupEvent
        {
            ProjectId = _project.Id, Type = "Metrics", CreatedAt = setupTime,
        });
        _db.SaveChanges();

        var result = await _query.GetMetricsIntegration(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result.Integrated);
        Assert.Equal("Metric", result.ResourceType);
        Assert.Equal(setupTime, result.CreatedAt);
    }

    [Fact]
    public async Task GetMetricsIntegration_WrongSetupType_ReturnsNotIntegrated()
    {
        _db.Set<SetupEvent>().Add(new SetupEvent
        {
            ProjectId = _project.Id, Type = "Client", CreatedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();

        var result = await _query.GetMetricsIntegration(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.False(result.Integrated);
    }

    [Fact]
    public async Task GetMetricsIntegration_SetupForDifferentProject_ReturnsNotIntegrated()
    {
        var otherProject = new Project { Name = "Other", WorkspaceId = _workspace.Id };
        _db.Projects.Add(otherProject);
        _db.SaveChanges();

        _db.Set<SetupEvent>().Add(new SetupEvent
        {
            ProjectId = otherProject.Id, Type = "Metrics", CreatedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();

        var result = await _query.GetMetricsIntegration(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.False(result.Integrated);
    }

    // ══════════════════════════════════════════════════════════════════
    // ApiKeyToOrgId
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ApiKeyToOrgId_ValidKey_ReturnsProjectId()
    {
        var result = await _query.ApiKeyToOrgId("test-api-key-123", _db, CancellationToken.None);
        Assert.Equal(_project.Id, result);
    }

    [Fact]
    public async Task ApiKeyToOrgId_UnknownKey_ReturnsNull()
    {
        var result = await _query.ApiKeyToOrgId("nonexistent-key", _db, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task ApiKeyToOrgId_EmptyString_ReturnsNull()
    {
        var result = await _query.ApiKeyToOrgId("", _db, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task ApiKeyToOrgId_MultipleProjects_ReturnsCorrectOne()
    {
        var project2 = new Project
        {
            Name = "SecondProj", WorkspaceId = _workspace.Id, Secret = "second-key-456",
        };
        _db.Projects.Add(project2);
        _db.SaveChanges();

        var result1 = await _query.ApiKeyToOrgId("test-api-key-123", _db, CancellationToken.None);
        var result2 = await _query.ApiKeyToOrgId("second-key-456", _db, CancellationToken.None);

        Assert.Equal(_project.Id, result1);
        Assert.Equal(project2.Id, result2);
    }

    [Fact]
    public async Task ApiKeyToOrgId_NullSecret_DoesNotMatch()
    {
        var projectNoSecret = new Project { Name = "NoSecret", WorkspaceId = _workspace.Id };
        _db.Projects.Add(projectNoSecret);
        _db.SaveChanges();

        // Searching for empty string should not match a project with null Secret
        var result = await _query.ApiKeyToOrgId("", _db, CancellationToken.None);
        Assert.Null(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // Storage key correctness tests
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task StorageMethods_UseCorrectKeyFormat()
    {
        // Verify storage keys include projectId and sessionId
        var resourceKey = $"{_session.ProjectId}/{_session.Id}/resources";
        var timelineKey = $"{_session.ProjectId}/{_session.Id}/timeline-indicator-events";
        var wsKey = $"{_session.ProjectId}/{_session.Id}/websocket-events";

        _storage.Store["sessions:" + resourceKey] = "r";
        _storage.Store["sessions:" + timelineKey] = "t";
        _storage.Store["sessions:" + wsKey] = "w";

        var r = await _query.GetResources(_session.SecureId, _principal, _authz, _db, _storage, CancellationToken.None);
        var t = await _query.GetTimelineIndicatorEvents(_session.SecureId, _principal, _authz, _db, _storage, CancellationToken.None);
        var w = await _query.GetWebsocketEvents(_session.SecureId, _principal, _authz, _db, _storage, CancellationToken.None);

        Assert.Equal("r", r);
        Assert.Equal("t", t);
        Assert.Equal("w", w);
    }

    // ══════════════════════════════════════════════════════════════════
    // Integration type coverage edge cases
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IsIntegratedWith_EmptyStringToken_ReturnsFalse()
    {
        _workspace.SlackAccessToken = "";
        _db.SaveChanges();

        var result = await _query.IsIntegratedWith(
            IntegrationType.Slack, _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task IsWorkspaceIntegratedWith_EmptyStringToken_ReturnsFalse()
    {
        _workspace.LinearAccessToken = "";
        _db.SaveChanges();

        var result = await _query.IsWorkspaceIntegratedWith(
            IntegrationType.Linear, _workspace.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.False(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // Fakes / Stubs
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fake IAuthorizationService that always succeeds. No-ops for all checks.
    /// </summary>
    private class FakeAuthorizationService : IAuthorizationService
    {
        private readonly Admin _admin = new() { Uid = "test-uid-001", Name = "Test Admin", Email = "test@test.com" };

        public Task<Admin> GetCurrentAdminAsync(string uid, CancellationToken ct = default)
            => Task.FromResult(_admin);

        public Task<Workspace> IsAdminInWorkspaceAsync(int adminId, int workspaceId, CancellationToken ct = default)
            => Task.FromResult(new Workspace { Name = "Test" });

        public Task<Workspace> IsAdminInWorkspaceFullAccessAsync(int adminId, int workspaceId, CancellationToken ct = default)
            => Task.FromResult(new Workspace { Name = "Test" });

        public Task<Project> IsAdminInProjectAsync(int adminId, int projectId, CancellationToken ct = default)
            => Task.FromResult(new Project { Name = "Test" });

        public Task<(string Role, List<int>? ProjectIds)?> GetAdminRoleAsync(int adminId, int workspaceId, CancellationToken ct = default)
            => Task.FromResult<(string Role, List<int>? ProjectIds)?>(("ADMIN", null));

        public Task ValidateAdminRoleAsync(int adminId, int workspaceId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Fake IStorageService backed by an in-memory dictionary.
    /// Keys are stored as "bucket:key".
    /// </summary>
    private class FakeStorageService : IStorageService
    {
        public Dictionary<string, string> Store { get; } = new();

        public Task UploadAsync(string bucket, string key, Stream data, string? contentType = null, CancellationToken ct = default)
        {
            using var reader = new StreamReader(data);
            Store[$"{bucket}:{key}"] = reader.ReadToEnd();
            return Task.CompletedTask;
        }

        public Task<Stream?> DownloadAsync(string bucket, string key, CancellationToken ct = default)
        {
            var compositeKey = $"{bucket}:{key}";
            if (!Store.TryGetValue(compositeKey, out var content))
                return Task.FromResult<Stream?>(null);
            return Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes(content)));
        }

        public Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default)
            => Task.FromResult(Store.ContainsKey($"{bucket}:{key}"));

        public Task DeleteAsync(string bucket, string key, CancellationToken ct = default)
        {
            Store.Remove($"{bucket}:{key}");
            return Task.CompletedTask;
        }

        public Task<string> GetDownloadUrlAsync(string bucket, string key, TimeSpan expiry, CancellationToken ct = default)
            => Task.FromResult($"file://{bucket}/{key}");
    }

    /// <summary>
    /// Fake IClickHouseService that returns configurable results.
    /// </summary>
    private class FakeClickHouseService : IClickHouseService
    {
        public MetricsBuckets MetricsResult { get; set; } = new();
        public List<string> SessionKeyValuesResult { get; set; } = [];
        public string? LastQueryParam { get; private set; }

        public Task<MetricsBuckets> ReadMetricsAsync(int projectId, QueryInput query, string bucketBy, List<string>? groupBy, string aggregator, string? column, CancellationToken ct)
            => Task.FromResult(MetricsResult);

        public Task<List<string>> GetSessionsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, CancellationToken ct)
        {
            LastQueryParam = query;
            return Task.FromResult(SessionKeyValuesResult);
        }

        // Remaining interface methods — not exercised, return empty defaults
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
        public Task<List<QueryKey>> GetSessionsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, CancellationToken ct) => Task.FromResult(new List<QueryKey>());
        public Task<List<QueryKey>> GetErrorsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, CancellationToken ct) => Task.FromResult(new List<QueryKey>());
        public Task<List<string>> GetErrorsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task<List<QueryKey>> GetEventsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, string? eventName, CancellationToken ct) => Task.FromResult(new List<QueryKey>());
        public Task<List<string>> GetEventsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, string? eventName, CancellationToken ct) => Task.FromResult(new List<string>());
        public Task WriteMetricAsync(int projectId, string metricName, double metricValue, string? category, DateTime timestamp, Dictionary<string, string>? tags, string? sessionSecureId, CancellationToken ct) => Task.CompletedTask;
        public Task WriteLogsAsync(IEnumerable<LogRowInput> logs, CancellationToken ct) => Task.CompletedTask;
        public Task WriteTracesAsync(IEnumerable<TraceRowInput> traces, CancellationToken ct) => Task.CompletedTask;
        public Task WriteSessionsAsync(IEnumerable<SessionRowInput> sessions, CancellationToken ct) => Task.CompletedTask;
        public Task WriteErrorGroupsAsync(IEnumerable<ErrorGroupRowInput> errorGroups, CancellationToken ct) => Task.CompletedTask;
        public Task WriteErrorObjectsAsync(IEnumerable<ErrorObjectRowInput> errorObjects, CancellationToken ct) => Task.CompletedTask;

        public Task<long> CountLogsAsync(int projectId, string? query, DateTime startDate, DateTime endDate, CancellationToken ct = default)
            => Task.FromResult(0L);

        public Task<List<AlertStateChangeRow>> GetLastAlertStateChangesAsync(int projectId, int alertId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
            => Task.FromResult(new List<AlertStateChangeRow>());

        public Task<List<AlertStateChangeRow>> GetAlertingAlertStateChangesAsync(int projectId, int alertId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
            => Task.FromResult(new List<AlertStateChangeRow>());

        public Task<List<AlertStateChangeRow>> GetLastAlertingStatesAsync(int projectId, int alertId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
            => Task.FromResult(new List<AlertStateChangeRow>());

        public Task WriteAlertStateChangesAsync(int projectId, IEnumerable<AlertStateChangeRow> rows, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
