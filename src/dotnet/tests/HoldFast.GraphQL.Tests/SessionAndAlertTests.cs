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
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Comprehensive tests for session payload, error group instances, session user reports,
/// comment mention suggestions, AI/error stubs, OAuth metadata queries, and alert/integration
/// mutations. Over-tests with edge cases, forced failures, and boundary conditions.
/// </summary>
public class SessionAndAlertTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly PrivateQuery _query;
    private readonly PrivateMutation _mutation;
    private readonly Workspace _workspace;
    private readonly Project _project;
    private readonly Session _session;
    private readonly Admin _admin;
    private readonly ClaimsPrincipal _principal;
    private readonly FakeAuthorizationService _authz;
    private readonly FakeStorageService _storage;
    private readonly FakeClickHouseService _clickHouse;

    public SessionAndAlertTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _admin = new Admin { Uid = "test-uid-001", Name = "Test Admin", Email = "admin@test.com" };
        _db.Set<Admin>().Add(_admin);
        _db.SaveChanges();

        _workspace = new Workspace { Name = "TestWS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        // Link admin to workspace
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = _admin.Id,
            WorkspaceId = _workspace.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();

        _project = new Project { Name = "TestProj", WorkspaceId = _workspace.Id, Secret = "test-secret" };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _session = new Session
        {
            SecureId = "sess-001",
            ProjectId = _project.Id,
            CreatedAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
        };
        _db.Sessions.Add(_session);
        _db.SaveChanges();

        _query = new PrivateQuery();
        _mutation = new PrivateMutation();

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
    // GetSessionPayload
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSessionPayload_SessionNotFound_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _query.GetSessionPayload("nonexistent", false, _principal, _authz, _db, _storage, CancellationToken.None));
    }

    [Fact]
    public async Task GetSessionPayload_NoEventsInStorage_ReturnsEmptyJsonArray()
    {
        var result = await _query.GetSessionPayload(
            _session.SecureId, false, _principal, _authz, _db, _storage, CancellationToken.None);

        Assert.Equal("[]", result.Events);
    }

    [Fact]
    public async Task GetSessionPayload_SkipEventsTrue_DoesNotFetchFromStorage()
    {
        // Even if storage has events, skipEvents=true should skip them
        var key = $"{_session.ProjectId}/{_session.Id}/events";
        _storage.Store[$"sessions:{key}"] = "[{\"type\":\"click\"}]";

        var result = await _query.GetSessionPayload(
            _session.SecureId, true, _principal, _authz, _db, _storage, CancellationToken.None);

        Assert.Equal("[]", result.Events);
    }

    [Fact]
    public async Task GetSessionPayload_SkipEventsFalse_ReturnsEventsFromStorage()
    {
        var key = $"{_session.ProjectId}/{_session.Id}/events";
        _storage.Store[$"sessions:{key}"] = "[{\"type\":\"click\",\"ts\":1234}]";

        var result = await _query.GetSessionPayload(
            _session.SecureId, false, _principal, _authz, _db, _storage, CancellationToken.None);

        Assert.Equal("[{\"type\":\"click\",\"ts\":1234}]", result.Events);
    }

    [Fact]
    public async Task GetSessionPayload_IncludesErrors()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-sp-1" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        _db.Set<ErrorObject>().AddRange(
            new ErrorObject { ProjectId = _project.Id, SessionId = _session.Id, ErrorGroupId = eg.Id, Event = "Err1", CreatedAt = DateTime.UtcNow },
            new ErrorObject { ProjectId = _project.Id, SessionId = _session.Id, ErrorGroupId = eg.Id, Event = "Err2", CreatedAt = DateTime.UtcNow.AddSeconds(1) });
        _db.SaveChanges();

        var result = await _query.GetSessionPayload(
            _session.SecureId, true, _principal, _authz, _db, _storage, CancellationToken.None);

        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public async Task GetSessionPayload_ErrorsOrderedByCreatedAt()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-sp-order" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        _db.Set<ErrorObject>().AddRange(
            new ErrorObject { ProjectId = _project.Id, SessionId = _session.Id, ErrorGroupId = eg.Id, Event = "Second", CreatedAt = new DateTime(2026, 1, 15, 11, 0, 0, DateTimeKind.Utc) },
            new ErrorObject { ProjectId = _project.Id, SessionId = _session.Id, ErrorGroupId = eg.Id, Event = "First", CreatedAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc) });
        _db.SaveChanges();

        var result = await _query.GetSessionPayload(
            _session.SecureId, true, _principal, _authz, _db, _storage, CancellationToken.None);

        Assert.Equal("First", result.Errors[0].Event);
        Assert.Equal("Second", result.Errors[1].Event);
    }

    [Fact]
    public async Task GetSessionPayload_IncludesRageClicks()
    {
        _db.Set<RageClickEvent>().Add(new RageClickEvent
        {
            ProjectId = _project.Id, SessionId = _session.Id,
            TotalClicks = 42, Selector = "#rage", StartTimestamp = 100, EndTimestamp = 200,
        });
        _db.SaveChanges();

        var result = await _query.GetSessionPayload(
            _session.SecureId, true, _principal, _authz, _db, _storage, CancellationToken.None);

        Assert.Single(result.RageClicks);
        Assert.Equal(42, result.RageClicks[0].TotalClicks);
    }

    [Fact]
    public async Task GetSessionPayload_IncludesComments()
    {
        _db.SessionComments.Add(new SessionComment
        {
            ProjectId = _project.Id, SessionId = _session.Id,
            AdminId = _admin.Id, Text = "Hello", CreatedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();

        var result = await _query.GetSessionPayload(
            _session.SecureId, true, _principal, _authz, _db, _storage, CancellationToken.None);

        Assert.Single(result.SessionComments);
        Assert.Equal("Hello", result.SessionComments[0].Text);
    }

    [Fact]
    public async Task GetSessionPayload_UsesLastUserInteractionTime_WhenSet()
    {
        _session.LastUserInteractionTime = "2026-01-15T12:00:00.000Z";
        _db.SaveChanges();

        var result = await _query.GetSessionPayload(
            _session.SecureId, true, _principal, _authz, _db, _storage, CancellationToken.None);

        Assert.Equal("2026-01-15T12:00:00.000Z", result.LastUserInteractionTime);
    }

    [Fact]
    public async Task GetSessionPayload_FallsBackToCreatedAt_WhenLastInteractionNull()
    {
        _session.LastUserInteractionTime = null;
        _db.SaveChanges();

        var result = await _query.GetSessionPayload(
            _session.SecureId, true, _principal, _authz, _db, _storage, CancellationToken.None);

        Assert.Equal(_session.CreatedAt.ToString("O"), result.LastUserInteractionTime);
    }

    [Fact]
    public async Task GetSessionPayload_DoesNotReturnErrorsFromOtherSessions()
    {
        var other = new Session { SecureId = "sess-other-sp", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        _db.Sessions.Add(other);
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-sp-other" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        _db.Set<ErrorObject>().Add(new ErrorObject
        {
            ProjectId = _project.Id, SessionId = other.Id, ErrorGroupId = eg.Id,
            Event = "Other", CreatedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();

        var result = await _query.GetSessionPayload(
            _session.SecureId, true, _principal, _authz, _db, _storage, CancellationToken.None);

        Assert.Empty(result.Errors);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetErrorGroupInstances
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetErrorGroupInstances_ErrorGroupNotFound_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _query.GetErrorGroupInstances("nonexistent-eg", 10, 0, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task GetErrorGroupInstances_EmptyGroup_ReturnsZero()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-empty" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        var result = await _query.GetErrorGroupInstances(
            "eg-empty", 10, 0, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result.ErrorObjects);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task GetErrorGroupInstances_Pagination_FirstPage()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-pag" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        for (int i = 0; i < 5; i++)
        {
            _db.Set<ErrorObject>().Add(new ErrorObject
            {
                ProjectId = _project.Id, SessionId = _session.Id, ErrorGroupId = eg.Id,
                Event = $"Err{i}", CreatedAt = DateTime.UtcNow.AddMinutes(i),
            });
        }
        _db.SaveChanges();

        var result = await _query.GetErrorGroupInstances(
            "eg-pag", 2, 0, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, result.ErrorObjects.Count);
        Assert.Equal(5, result.TotalCount);
    }

    [Fact]
    public async Task GetErrorGroupInstances_Pagination_SecondPage()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-pag2" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        for (int i = 0; i < 5; i++)
        {
            _db.Set<ErrorObject>().Add(new ErrorObject
            {
                ProjectId = _project.Id, SessionId = _session.Id, ErrorGroupId = eg.Id,
                Event = $"Err{i}", CreatedAt = DateTime.UtcNow.AddMinutes(i),
            });
        }
        _db.SaveChanges();

        var result = await _query.GetErrorGroupInstances(
            "eg-pag2", 2, 1, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, result.ErrorObjects.Count);
        Assert.Equal(5, result.TotalCount);
    }

    [Fact]
    public async Task GetErrorGroupInstances_Pagination_BeyondLastPage_ReturnsEmpty()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-beyond" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        _db.Set<ErrorObject>().Add(new ErrorObject
        {
            ProjectId = _project.Id, SessionId = _session.Id, ErrorGroupId = eg.Id,
            Event = "Only", CreatedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();

        var result = await _query.GetErrorGroupInstances(
            "eg-beyond", 10, 100, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result.ErrorObjects);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task GetErrorGroupInstances_OrderedByCreatedAtDescending()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-ord" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        _db.Set<ErrorObject>().AddRange(
            new ErrorObject { ProjectId = _project.Id, SessionId = _session.Id, ErrorGroupId = eg.Id, Event = "Oldest", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new ErrorObject { ProjectId = _project.Id, SessionId = _session.Id, ErrorGroupId = eg.Id, Event = "Newest", CreatedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc) },
            new ErrorObject { ProjectId = _project.Id, SessionId = _session.Id, ErrorGroupId = eg.Id, Event = "Middle", CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc) });
        _db.SaveChanges();

        var result = await _query.GetErrorGroupInstances(
            "eg-ord", 10, 0, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("Newest", result.ErrorObjects[0].Event);
        Assert.Equal("Middle", result.ErrorObjects[1].Event);
        Assert.Equal("Oldest", result.ErrorObjects[2].Event);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetSessionUsersReports
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSessionUsersReports_EmptyResults_WhenNoSessions()
    {
        var emptyProject = new Project { Name = "Empty", WorkspaceId = _workspace.Id, Secret = "empty" };
        _db.Projects.Add(emptyProject);
        _db.SaveChanges();

        var result = await _query.GetSessionUsersReports(
            emptyProject.Id, new QueryInput(), _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSessionUsersReports_ExcludedSessionsFiltered()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "sess-excluded", ProjectId = _project.Id,
            Identifier = "excluded@user.com", Excluded = true,
            CreatedAt = DateTime.UtcNow, ActiveLength = 60000, Length = 120000,
        });
        _db.SaveChanges();

        var result = await _query.GetSessionUsersReports(
            _project.Id, new QueryInput { DateRange = new DateRangeRequiredInput { StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(1) } },
            _principal, _authz, _db, CancellationToken.None);

        Assert.DoesNotContain(result, r => r.Email == "excluded@user.com");
    }

    [Fact]
    public async Task GetSessionUsersReports_NullIdentifierFiltered()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "sess-noid", ProjectId = _project.Id,
            Identifier = null,
            CreatedAt = DateTime.UtcNow, ActiveLength = 60000, Length = 120000,
        });
        _db.SaveChanges();

        var result = await _query.GetSessionUsersReports(
            _project.Id, new QueryInput { DateRange = new DateRangeRequiredInput { StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(1) } },
            _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSessionUsersReports_AggregatesByUser()
    {
        var now = DateTime.UtcNow;
        _db.Sessions.AddRange(
            new Session { SecureId = "sess-u1a", ProjectId = _project.Id, Identifier = "user@a.com", CreatedAt = now, ActiveLength = 60000, Length = 120000, City = "NYC", Country = "US" },
            new Session { SecureId = "sess-u1b", ProjectId = _project.Id, Identifier = "user@a.com", CreatedAt = now.AddHours(1), ActiveLength = 120000, Length = 180000, City = "NYC", Country = "US" },
            new Session { SecureId = "sess-u2a", ProjectId = _project.Id, Identifier = "user@b.com", CreatedAt = now, ActiveLength = 30000, Length = 60000, Country = "UK" });
        _db.SaveChanges();

        var result = await _query.GetSessionUsersReports(
            _project.Id, new QueryInput { DateRange = new DateRangeRequiredInput { StartDate = now.AddDays(-1), EndDate = now.AddDays(1) } },
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, result.Count);
        // user@a.com has 2 sessions and should be first (sorted by NumSessions desc)
        Assert.Equal("user@a.com", result[0].Email);
        Assert.Equal(2, result[0].NumSessions);
        Assert.Equal("user@b.com", result[1].Email);
        Assert.Equal(1, result[1].NumSessions);
    }

    [Fact]
    public async Task GetSessionUsersReports_LocationFormatting_CityAndCountry()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "sess-loc-cc", ProjectId = _project.Id,
            Identifier = "loc@test.com", CreatedAt = DateTime.UtcNow,
            City = "Berlin", Country = "DE", ActiveLength = 10000, Length = 20000,
        });
        _db.SaveChanges();

        var result = await _query.GetSessionUsersReports(
            _project.Id, new QueryInput { DateRange = new DateRangeRequiredInput { StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(1) } },
            _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Berlin, DE", result[0].Location);
    }

    [Fact]
    public async Task GetSessionUsersReports_LocationFormatting_CountryOnly()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "sess-loc-co", ProjectId = _project.Id,
            Identifier = "loc2@test.com", CreatedAt = DateTime.UtcNow,
            City = null, Country = "JP", ActiveLength = 10000, Length = 20000,
        });
        _db.SaveChanges();

        var result = await _query.GetSessionUsersReports(
            _project.Id, new QueryInput { DateRange = new DateRangeRequiredInput { StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(1) } },
            _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("JP", result[0].Location);
    }

    [Fact]
    public async Task GetSessionUsersReports_LocationFormatting_NoGeo_EmptyString()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "sess-loc-none", ProjectId = _project.Id,
            Identifier = "loc3@test.com", CreatedAt = DateTime.UtcNow,
            City = null, Country = null, ActiveLength = 10000, Length = 20000,
        });
        _db.SaveChanges();

        var result = await _query.GetSessionUsersReports(
            _project.Id, new QueryInput { DateRange = new DateRangeRequiredInput { StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(1) } },
            _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("", result[0].Location);
    }

    [Fact]
    public async Task GetSessionUsersReports_ActiveLengthConvertedToMinutes()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "sess-mins", ProjectId = _project.Id,
            Identifier = "mins@test.com", CreatedAt = DateTime.UtcNow,
            ActiveLength = 120000, // 2 minutes in ms
            Length = 300000, // 5 minutes in ms
        });
        _db.SaveChanges();

        var result = await _query.GetSessionUsersReports(
            _project.Id, new QueryInput { DateRange = new DateRangeRequiredInput { StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(1) } },
            _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(2.0, result[0].AvgActiveLengthMins, 0.01);
        Assert.Equal(5.0, result[0].AvgLengthMins, 0.01);
    }

    [Fact]
    public async Task GetSessionUsersReports_NullActiveLengthTreatedAsZero()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "sess-null-len", ProjectId = _project.Id,
            Identifier = "nulllen@test.com", CreatedAt = DateTime.UtcNow,
            ActiveLength = null, Length = null,
        });
        _db.SaveChanges();

        var result = await _query.GetSessionUsersReports(
            _project.Id, new QueryInput { DateRange = new DateRangeRequiredInput { StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(1) } },
            _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(0.0, result[0].AvgActiveLengthMins);
        Assert.Equal(0.0, result[0].TotalActiveLengthMins);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetCommentMentionSuggestions
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetCommentMentionSuggestions_ReturnsWorkspaceAdmins()
    {
        var result = await _query.GetCommentMentionSuggestions(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Test Admin", result[0].Name);
    }

    [Fact]
    public async Task GetCommentMentionSuggestions_MultipleAdmins()
    {
        var admin2 = new Admin { Uid = "uid-002", Name = "Admin Two", Email = "two@test.com" };
        _db.Set<Admin>().Add(admin2);
        _db.SaveChanges();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = admin2.Id, WorkspaceId = _workspace.Id,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();

        var result = await _query.GetCommentMentionSuggestions(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetCommentMentionSuggestions_ProjectNotFound_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _query.GetCommentMentionSuggestions(99999, _principal, _authz, _db, CancellationToken.None));
    }

    // ══════════════════════════════════════════════════════════════════
    // GetAIQuerySuggestion (stub)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAIQuerySuggestion_ReturnsEmptyString()
    {
        var result = await _query.GetAIQuerySuggestion(
            _project.Id, "Sessions", "some query", "UTC", _principal);

        Assert.Equal("", result);
    }

    [Fact]
    public async Task GetAIQuerySuggestion_ReturnsEmptyForAnyInput()
    {
        var result = await _query.GetAIQuerySuggestion(
            0, "", "", "", _principal);

        Assert.Equal("", result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetErrorResolutionSuggestion (stub)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetErrorResolutionSuggestion_ReturnsEmptyString()
    {
        var result = await _query.GetErrorResolutionSuggestion("eg-001", _principal);

        Assert.Equal("", result);
    }

    [Fact]
    public async Task GetErrorResolutionSuggestion_ReturnsEmptyForInvalidId()
    {
        var result = await _query.GetErrorResolutionSuggestion("", _principal);

        Assert.Equal("", result);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetOAuthClientMetadata
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetOAuthClientMetadata_Found()
    {
        _db.OAuthClientStores.Add(new OAuthClientStore
        {
            ClientId = "client-123", Secret = "secret", AppName = "TestApp",
        });
        _db.SaveChanges();

        var result = await _query.GetOAuthClientMetadata("client-123", _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("TestApp", result!.AppName);
    }

    [Fact]
    public async Task GetOAuthClientMetadata_NotFound_ReturnsNull()
    {
        var result = await _query.GetOAuthClientMetadata("nonexistent", _db, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOAuthClientMetadata_EmptyClientId_ReturnsNull()
    {
        var result = await _query.GetOAuthClientMetadata("", _db, CancellationToken.None);

        Assert.Null(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // CreateAlert
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateAlert_BasicFields()
    {
        var result = await _mutation.CreateAlert(
            _project.Id, "High Error Rate", "ERRORS_ALERT", "Count", null,
            "status:500", null, false, 100.0, 300, 600, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("High Error Rate", result.Name);
        Assert.Equal("ERRORS_ALERT", result.ProductType);
        Assert.Equal("Count", result.FunctionType);
        Assert.Equal("status:500", result.Query);
        Assert.Equal(100.0, result.AboveThreshold);
        Assert.Equal(300, result.ThresholdWindow);
        Assert.Equal(600, result.ThresholdCooldown);
        Assert.False(result.Default);
        Assert.True(result.Id > 0);
    }

    [Fact]
    public async Task CreateAlert_WithDestinations()
    {
        var destinations = new List<AlertDestinationInput>
        {
            new("Slack", "#alerts", "alerts-channel"),
            new("Email", "admin@test.com", null),
        };

        var result = await _mutation.CreateAlert(
            _project.Id, "Test Alert", "SESSIONS_ALERT", null, null,
            null, null, null, null, null, null, destinations,
            _principal, _authz, _db, CancellationToken.None);

        var savedDests = await _db.AlertDestinations.Where(d => d.AlertId == result.Id).ToListAsync();
        Assert.Equal(2, savedDests.Count);
        Assert.Contains(savedDests, d => d.DestinationType == "Slack" && d.TypeId == "#alerts");
        Assert.Contains(savedDests, d => d.DestinationType == "Email" && d.TypeId == "admin@test.com");
    }

    [Fact]
    public async Task CreateAlert_WithoutDestinations_NoDestinationsCreated()
    {
        var result = await _mutation.CreateAlert(
            _project.Id, "No Dests", "LOGS_ALERT", null, null,
            null, null, null, null, null, null, null,
            _principal, _authz, _db, CancellationToken.None);

        var savedDests = await _db.AlertDestinations.Where(d => d.AlertId == result.Id).ToListAsync();
        Assert.Empty(savedDests);
    }

    [Fact]
    public async Task CreateAlert_DefaultTrue()
    {
        var result = await _mutation.CreateAlert(
            _project.Id, "Default Alert", "ERRORS_ALERT", null, null,
            null, null, true, null, null, null, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result.Default);
    }

    [Fact]
    public async Task CreateAlert_SetsLastAdminToEditId()
    {
        var result = await _mutation.CreateAlert(
            _project.Id, "Admin Track", "ERRORS_ALERT", null, null,
            null, null, null, null, null, null, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(result.LastAdminToEditId);
    }

    [Fact]
    public async Task CreateAlert_EmptyDestinationsList_NoDestinationsCreated()
    {
        var result = await _mutation.CreateAlert(
            _project.Id, "Empty Dests", "ERRORS_ALERT", null, null,
            null, null, null, null, null, null, new List<AlertDestinationInput>(),
            _principal, _authz, _db, CancellationToken.None);

        var savedDests = await _db.AlertDestinations.Where(d => d.AlertId == result.Id).ToListAsync();
        Assert.Empty(savedDests);
    }

    // ══════════════════════════════════════════════════════════════════
    // UpdateAlert
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateAlert_PartialUpdate_OnlyChangesSpecifiedFields()
    {
        var alert = new Alert
        {
            ProjectId = _project.Id, Name = "Original", ProductType = "ERRORS_ALERT",
            Query = "original-query", AboveThreshold = 50.0,
        };
        _db.Alerts.Add(alert);
        _db.SaveChanges();

        var result = await _mutation.UpdateAlert(
            _project.Id, alert.Id, "Updated Name", null, null, null, null, null,
            null, null, null, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("Updated Name", result.Name);
        Assert.Equal("ERRORS_ALERT", result.ProductType); // unchanged
        Assert.Equal("original-query", result.Query); // unchanged
        Assert.Equal(50.0, result.AboveThreshold); // unchanged
    }

    [Fact]
    public async Task UpdateAlert_ReplaceDestinations()
    {
        var alert = new Alert { ProjectId = _project.Id, Name = "DestTest", ProductType = "ERRORS_ALERT" };
        _db.Alerts.Add(alert);
        _db.SaveChanges();

        _db.AlertDestinations.Add(new AlertDestination { AlertId = alert.Id, DestinationType = "Slack", TypeId = "#old" });
        _db.SaveChanges();

        var newDests = new List<AlertDestinationInput>
        {
            new("Email", "new@test.com", "New Channel"),
        };

        await _mutation.UpdateAlert(
            _project.Id, alert.Id, null, null, null, null, null, null,
            null, null, null, newDests,
            _principal, _authz, _db, CancellationToken.None);

        var savedDests = await _db.AlertDestinations.Where(d => d.AlertId == alert.Id).ToListAsync();
        Assert.Single(savedDests);
        Assert.Equal("Email", savedDests[0].DestinationType);
        Assert.Equal("new@test.com", savedDests[0].TypeId);
    }

    [Fact]
    public async Task UpdateAlert_NullDestinations_PreservesExisting()
    {
        var alert = new Alert { ProjectId = _project.Id, Name = "PreserveDests", ProductType = "ERRORS_ALERT" };
        _db.Alerts.Add(alert);
        _db.SaveChanges();

        _db.AlertDestinations.Add(new AlertDestination { AlertId = alert.Id, DestinationType = "Slack", TypeId = "#keep" });
        _db.SaveChanges();

        await _mutation.UpdateAlert(
            _project.Id, alert.Id, "Renamed", null, null, null, null, null,
            null, null, null, null, // destinations = null
            _principal, _authz, _db, CancellationToken.None);

        var savedDests = await _db.AlertDestinations.Where(d => d.AlertId == alert.Id).ToListAsync();
        Assert.Single(savedDests);
        Assert.Equal("#keep", savedDests[0].TypeId);
    }

    [Fact]
    public async Task UpdateAlert_AlertNotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateAlert(
                _project.Id, 99999, "Name", null, null, null, null, null,
                null, null, null, null,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAlert_WrongProject_Throws()
    {
        var otherProject = new Project { Name = "Other", WorkspaceId = _workspace.Id, Secret = "other" };
        _db.Projects.Add(otherProject);
        _db.SaveChanges();

        var alert = new Alert { ProjectId = otherProject.Id, Name = "WrongProj", ProductType = "ERRORS_ALERT" };
        _db.Alerts.Add(alert);
        _db.SaveChanges();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateAlert(
                _project.Id, alert.Id, "Rename", null, null, null, null, null,
                null, null, null, null,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAlert_AllFieldsUpdated()
    {
        var alert = new Alert
        {
            ProjectId = _project.Id, Name = "Before", ProductType = "ERRORS_ALERT",
            FunctionType = "Count", FunctionColumn = "col1", Query = "q1",
            GroupByKey = "key1", AboveThreshold = 10, ThresholdWindow = 60, ThresholdCooldown = 120,
        };
        _db.Alerts.Add(alert);
        _db.SaveChanges();

        var result = await _mutation.UpdateAlert(
            _project.Id, alert.Id, "After", "LOGS_ALERT", "Sum", "col2", "q2", "key2",
            99.9, 300, 600, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("After", result.Name);
        Assert.Equal("LOGS_ALERT", result.ProductType);
        Assert.Equal("Sum", result.FunctionType);
        Assert.Equal("col2", result.FunctionColumn);
        Assert.Equal("q2", result.Query);
        Assert.Equal("key2", result.GroupByKey);
        Assert.Equal(99.9, result.AboveThreshold);
        Assert.Equal(300, result.ThresholdWindow);
        Assert.Equal(600, result.ThresholdCooldown);
    }

    // ══════════════════════════════════════════════════════════════════
    // SyncSlackIntegration
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SyncSlackIntegration_StoresToken()
    {
        var result = await _mutation.SyncSlackIntegration(
            _project.Id, "xoxb-slack-token", _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        Assert.Equal("xoxb-slack-token", ws!.SlackAccessToken);
    }

    [Fact]
    public async Task SyncSlackIntegration_NullCode_NoOp()
    {
        _workspace.SlackAccessToken = "existing-token";
        _db.SaveChanges();

        var result = await _mutation.SyncSlackIntegration(
            _project.Id, null, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        Assert.Equal("existing-token", ws!.SlackAccessToken);
    }

    [Fact]
    public async Task SyncSlackIntegration_EmptyCode_NoOp()
    {
        _workspace.SlackAccessToken = "keep-this";
        _db.SaveChanges();

        var result = await _mutation.SyncSlackIntegration(
            _project.Id, "", _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        Assert.Equal("keep-this", ws!.SlackAccessToken);
    }

    [Fact]
    public async Task SyncSlackIntegration_ProjectNotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.SyncSlackIntegration(
                99999, "code", _principal, _authz, _db, CancellationToken.None));
    }

    // ══════════════════════════════════════════════════════════════════
    // CreateIssueForSessionComment
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateIssueForSessionComment_CreatesAttachment()
    {
        var comment = new SessionComment
        {
            ProjectId = _project.Id, SessionId = _session.Id,
            AdminId = _admin.Id, Text = "Bug here", CreatedAt = DateTime.UtcNow,
        };
        _db.SessionComments.Add(comment);
        _db.SaveChanges();

        var result = await _mutation.CreateIssueForSessionComment(
            _project.Id, _session.SecureId, comment.Id, IntegrationType.Linear,
            "Fix the bug", "Description", _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(comment.Id, result.Id);

        var attachment = await _db.ExternalAttachments.FirstAsync(a => a.SessionCommentId == comment.Id);
        Assert.Equal("Linear", attachment.IntegrationType);
        Assert.Equal("Fix the bug", attachment.Title);
    }

    [Fact]
    public async Task CreateIssueForSessionComment_NullTitle_DefaultsToIssue()
    {
        var comment = new SessionComment
        {
            ProjectId = _project.Id, SessionId = _session.Id,
            AdminId = _admin.Id, Text = "Comment", CreatedAt = DateTime.UtcNow,
        };
        _db.SessionComments.Add(comment);
        _db.SaveChanges();

        await _mutation.CreateIssueForSessionComment(
            _project.Id, _session.SecureId, comment.Id, IntegrationType.Jira,
            null, null, _principal, _authz, _db, CancellationToken.None);

        var attachment = await _db.ExternalAttachments.FirstAsync(a => a.SessionCommentId == comment.Id);
        Assert.Equal("Issue", attachment.Title);
    }

    [Fact]
    public async Task CreateIssueForSessionComment_CommentNotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateIssueForSessionComment(
                _project.Id, _session.SecureId, 99999, IntegrationType.GitHub,
                "Title", "Desc", _principal, _authz, _db, CancellationToken.None));
    }

    // ══════════════════════════════════════════════════════════════════
    // LinkIssueForSessionComment
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LinkIssueForSessionComment_CreatesAttachmentWithUrl()
    {
        var comment = new SessionComment
        {
            ProjectId = _project.Id, SessionId = _session.Id,
            AdminId = _admin.Id, Text = "Link here", CreatedAt = DateTime.UtcNow,
        };
        _db.SessionComments.Add(comment);
        _db.SaveChanges();

        var result = await _mutation.LinkIssueForSessionComment(
            _project.Id, comment.Id, IntegrationType.GitHub,
            "https://github.com/org/repo/issues/42", "GitHub Issue #42",
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(comment.Id, result.Id);

        var attachment = await _db.ExternalAttachments.FirstAsync(a => a.SessionCommentId == comment.Id);
        Assert.Equal("GitHub", attachment.IntegrationType);
        Assert.Equal("https://github.com/org/repo/issues/42", attachment.ExternalId);
        Assert.Equal("GitHub Issue #42", attachment.Title);
    }

    [Fact]
    public async Task LinkIssueForSessionComment_CommentNotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.LinkIssueForSessionComment(
                _project.Id, 99999, IntegrationType.Linear,
                "https://linear.app/issue/123", "Linear Issue",
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task LinkIssueForSessionComment_NullTitle_StoredAsNull()
    {
        var comment = new SessionComment
        {
            ProjectId = _project.Id, SessionId = _session.Id,
            AdminId = _admin.Id, Text = "No title", CreatedAt = DateTime.UtcNow,
        };
        _db.SessionComments.Add(comment);
        _db.SaveChanges();

        await _mutation.LinkIssueForSessionComment(
            _project.Id, comment.Id, IntegrationType.Jira,
            "https://jira.example.com/PROJ-1", null,
            _principal, _authz, _db, CancellationToken.None);

        var attachment = await _db.ExternalAttachments.FirstAsync(a => a.SessionCommentId == comment.Id);
        Assert.Null(attachment.Title);
    }

    // ══════════════════════════════════════════════════════════════════
    // CreateIssueForErrorComment
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateIssueForErrorComment_CreatesAttachment()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-err-iss" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        var comment = new ErrorComment { ErrorGroupId = eg.Id, AdminId = _admin.Id, Text = "Error here" };
        _db.ErrorComments.Add(comment);
        _db.SaveChanges();

        var result = await _mutation.CreateIssueForErrorComment(
            _project.Id, "eg-err-iss", comment.Id, IntegrationType.ClickUp,
            "Fix error", "Desc", _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(comment.Id, result.Id);

        var attachment = await _db.ExternalAttachments.FirstAsync(a => a.ErrorGroupId == eg.Id);
        Assert.Equal("ClickUp", attachment.IntegrationType);
        Assert.Equal("Fix error", attachment.Title);
    }

    [Fact]
    public async Task CreateIssueForErrorComment_CommentNotFound_Throws()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-err-nf" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateIssueForErrorComment(
                _project.Id, "eg-err-nf", 99999, IntegrationType.GitHub,
                "Title", "Desc", _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task CreateIssueForErrorComment_ErrorGroupNotFound_Throws()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-exists-tmp" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        var comment = new ErrorComment { ErrorGroupId = eg.Id, AdminId = _admin.Id, Text = "Comment" };
        _db.ErrorComments.Add(comment);
        _db.SaveChanges();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateIssueForErrorComment(
                _project.Id, "nonexistent-eg-xyz", comment.Id, IntegrationType.GitHub,
                "Title", "Desc", _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task CreateIssueForErrorComment_NullTitle_DefaultsToIssue()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-err-def" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        var comment = new ErrorComment { ErrorGroupId = eg.Id, AdminId = _admin.Id, Text = "Comment" };
        _db.ErrorComments.Add(comment);
        _db.SaveChanges();

        await _mutation.CreateIssueForErrorComment(
            _project.Id, "eg-err-def", comment.Id, IntegrationType.Linear,
            null, null, _principal, _authz, _db, CancellationToken.None);

        var attachment = await _db.ExternalAttachments.FirstAsync(a => a.ErrorGroupId == eg.Id);
        Assert.Equal("Issue", attachment.Title);
    }

    // ══════════════════════════════════════════════════════════════════
    // LinkIssueForErrorComment
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LinkIssueForErrorComment_CreatesAttachment()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-link-err" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        var comment = new ErrorComment { ErrorGroupId = eg.Id, AdminId = _admin.Id, Text = "Link error" };
        _db.ErrorComments.Add(comment);
        _db.SaveChanges();

        var result = await _mutation.LinkIssueForErrorComment(
            _project.Id, comment.Id, IntegrationType.GitLab,
            "https://gitlab.com/org/repo/-/issues/7", "GitLab #7",
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(comment.Id, result.Id);

        var attachment = await _db.ExternalAttachments.FirstAsync(a => a.ErrorGroupId == eg.Id);
        Assert.Equal("GitLab", attachment.IntegrationType);
        Assert.Equal("https://gitlab.com/org/repo/-/issues/7", attachment.ExternalId);
        Assert.Equal("GitLab #7", attachment.Title);
    }

    [Fact]
    public async Task LinkIssueForErrorComment_CommentNotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.LinkIssueForErrorComment(
                _project.Id, 99999, IntegrationType.Jira,
                "https://jira.example.com/PROJ-2", "Jira",
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task LinkIssueForErrorComment_NullTitle_StoredAsNull()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-link-null" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        var comment = new ErrorComment { ErrorGroupId = eg.Id, AdminId = _admin.Id, Text = "No title" };
        _db.ErrorComments.Add(comment);
        _db.SaveChanges();

        await _mutation.LinkIssueForErrorComment(
            _project.Id, comment.Id, IntegrationType.Linear,
            "https://linear.app/issue/456", null,
            _principal, _authz, _db, CancellationToken.None);

        var attachment = await _db.ExternalAttachments.FirstAsync(a => a.ErrorGroupId == eg.Id);
        Assert.Null(attachment.Title);
    }

    [Fact]
    public async Task LinkIssueForErrorComment_UsesCommentErrorGroupId()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, SecureId = "eg-link-uses" };
        _db.Set<ErrorGroup>().Add(eg);
        _db.SaveChanges();

        var comment = new ErrorComment { ErrorGroupId = eg.Id, AdminId = _admin.Id, Text = "Check group" };
        _db.ErrorComments.Add(comment);
        _db.SaveChanges();

        await _mutation.LinkIssueForErrorComment(
            _project.Id, comment.Id, IntegrationType.Discord,
            "https://discord.com/channels/1/2", "Discord Issue",
            _principal, _authz, _db, CancellationToken.None);

        var attachment = await _db.ExternalAttachments.FirstAsync(a => a.ErrorGroupId == eg.Id);
        Assert.Equal(eg.Id, attachment.ErrorGroupId);
    }

    // ══════════════════════════════════════════════════════════════════
    // Fakes
    // ══════════════════════════════════════════════════════════════════

    private class FakeAuthorizationService : IAuthorizationService
    {
        private readonly Admin _admin = new() { Uid = "test-uid-001", Name = "Test Admin", Email = "admin@test.com" };

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

    private class FakeClickHouseService : IClickHouseService
    {
        public MetricsBuckets MetricsResult { get; set; } = new();
        public List<string> SessionKeyValuesResult { get; set; } = [];

        public Task<MetricsBuckets> ReadMetricsAsync(int projectId, QueryInput query, string bucketBy, List<string>? groupBy, string aggregator, string? column, CancellationToken ct)
            => Task.FromResult(MetricsResult);

        public Task<List<string>> GetSessionsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, CancellationToken ct)
            => Task.FromResult(SessionKeyValuesResult);

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
