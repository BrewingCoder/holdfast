using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.Shared.Tests;

/// <summary>
/// Tests for extended PrivateQuery resolvers: admin role, pending invites,
/// session detail, error detail, integration status, comments, rage clicks, alerts.
/// </summary>
public class PrivateQueryExtendedTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateQuery _query;
    private readonly StubClickHouseService _clickHouse;

    public PrivateQueryExtendedTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();
        _authz = new AuthorizationService(_db);
        _query = new PrivateQuery();
        _clickHouse = new StubClickHouseService();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private static ClaimsPrincipal MakePrincipal(string uid) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, uid),
            new Claim(HoldFastClaimTypes.Email, $"{uid}@test.com"),
        }, "Test"));

    private async Task<(Admin admin, Workspace workspace, Project project)> SeedFullStack(
        string uid = "admin-uid", string role = "ADMIN")
    {
        var admin = new Admin { Uid = uid, Email = $"{uid}@test.com" };
        _db.Admins.Add(admin);
        var workspace = new Workspace { Name = "Test WS" };
        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = admin.Id, WorkspaceId = workspace.Id, Role = role,
        });
        var project = new Project { Name = "Test Project", WorkspaceId = workspace.Id };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        return (admin, workspace, project);
    }

    // ── GetAdminRole ───────────────────────────────────────────────

    [Fact]
    public async Task GetAdminRole_ReturnsRole()
    {
        var (_, workspace, _) = await SeedFullStack();

        var role = await _query.GetAdminRole(
            workspace.Id, MakePrincipal("admin-uid"), _authz, CancellationToken.None);

        Assert.Equal("ADMIN", role);
    }

    [Fact]
    public async Task GetAdminRole_NotMember_ReturnsNull()
    {
        var (_, workspace, _) = await SeedFullStack();
        var outsider = new Admin { Uid = "outsider", Email = "outsider@test.com" };
        _db.Admins.Add(outsider);
        await _db.SaveChangesAsync();

        var role = await _query.GetAdminRole(
            workspace.Id, MakePrincipal("outsider"), _authz, CancellationToken.None);

        Assert.Null(role);
    }

    [Fact]
    public async Task GetAdminRole_MemberRole()
    {
        var (_, workspace, _) = await SeedFullStack();
        var member = new Admin { Uid = "member", Email = "member@test.com" };
        _db.Admins.Add(member);
        await _db.SaveChangesAsync();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = member.Id, WorkspaceId = workspace.Id, Role = "MEMBER",
        });
        await _db.SaveChangesAsync();

        var role = await _query.GetAdminRole(
            workspace.Id, MakePrincipal("member"), _authz, CancellationToken.None);

        Assert.Equal("MEMBER", role);
    }

    // ── GetAdminRoleByProject ──────────────────────────────────────

    [Fact]
    public async Task GetAdminRoleByProject_ReturnsRole()
    {
        var (_, _, project) = await SeedFullStack();

        var role = await _query.GetAdminRoleByProject(
            project.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.NotNull(role);
        Assert.Equal("ADMIN", role!.Role);
    }

    [Fact]
    public async Task GetAdminRoleByProject_NonExistentProject_ReturnsNull()
    {
        await SeedFullStack();

        var role = await _query.GetAdminRoleByProject(
            99999, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Null(role);
    }

    // ── GetWorkspacePendingInvites ──────────────────────────────────

    [Fact]
    public async Task GetWorkspacePendingInvites_ReturnsPendingOnly()
    {
        var (_, workspace, _) = await SeedFullStack();

        // Create an invite for a different user
        var invitee = new Admin { Uid = "invitee", Email = "invitee@test.com" };
        _db.Admins.Add(invitee);
        await _db.SaveChangesAsync();

        _db.WorkspaceInviteLinks.Add(new WorkspaceInviteLink
        {
            WorkspaceId = workspace.Id,
            InviteeEmail = "invitee@test.com",
            Secret = Guid.NewGuid().ToString("N"),
            ExpirationDate = DateTime.UtcNow.AddDays(7),
        });
        // Expired invite
        _db.WorkspaceInviteLinks.Add(new WorkspaceInviteLink
        {
            WorkspaceId = workspace.Id,
            InviteeEmail = "invitee@test.com",
            Secret = Guid.NewGuid().ToString("N"),
            ExpirationDate = DateTime.UtcNow.AddDays(-1),
        });
        await _db.SaveChangesAsync();

        var invites = await _query.GetWorkspacePendingInvites(
            MakePrincipal("invitee"), _authz, _db, CancellationToken.None);

        Assert.Single(invites);
    }

    [Fact]
    public async Task GetWorkspacePendingInvites_NoInvites_ReturnsEmpty()
    {
        await SeedFullStack();

        var invites = await _query.GetWorkspacePendingInvites(
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Empty(invites);
    }

    // ── GetJoinableWorkspaces ──────────────────────────────────────

    [Fact]
    public async Task GetJoinableWorkspaces_ReturnsWorkspacesWithInvites()
    {
        var (_, workspace, _) = await SeedFullStack();
        var invitee = new Admin { Uid = "joiner", Email = "joiner@test.com" };
        _db.Admins.Add(invitee);
        await _db.SaveChangesAsync();

        _db.WorkspaceInviteLinks.Add(new WorkspaceInviteLink
        {
            WorkspaceId = workspace.Id,
            InviteeEmail = "joiner@test.com",
            Secret = Guid.NewGuid().ToString("N"),
            ExpirationDate = DateTime.UtcNow.AddDays(7),
        });
        await _db.SaveChangesAsync();

        var workspaces = await _query.GetJoinableWorkspaces(
            MakePrincipal("joiner"), _authz, _db, CancellationToken.None);

        Assert.Single(workspaces);
        Assert.Equal(workspace.Id, workspaces[0].Id);
    }

    // ── IsSessionPending ───────────────────────────────────────────

    [Fact]
    public async Task IsSessionPending_UnprocessedSession_ReturnsTrue()
    {
        var (_, _, project) = await SeedFullStack();
        var session = new Session { SecureId = "s1", ProjectId = project.Id, Processed = false };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var pending = await _query.IsSessionPending(
            session.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(pending);
    }

    [Fact]
    public async Task IsSessionPending_ProcessedSession_ReturnsFalse()
    {
        var (_, _, project) = await SeedFullStack();
        var session = new Session { SecureId = "s2", ProjectId = project.Id, Processed = true };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var pending = await _query.IsSessionPending(
            session.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.False(pending);
    }

    [Fact]
    public async Task IsSessionPending_NullProcessed_ReturnsTrue()
    {
        var (_, _, project) = await SeedFullStack();
        var session = new Session { SecureId = "s3", ProjectId = project.Id, Processed = null };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var pending = await _query.IsSessionPending(
            session.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(pending);
    }

    [Fact]
    public async Task IsSessionPending_NonExistentSession_ReturnsFalse()
    {
        await SeedFullStack();

        var pending = await _query.IsSessionPending(
            99999, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.False(pending);
    }

    // ── GetErrorInstance ────────────────────────────────────────────

    [Fact]
    public async Task GetErrorInstance_ById_ReturnsSpecificObject()
    {
        var (_, _, project) = await SeedFullStack();
        var eg = new ErrorGroup { ProjectId = project.Id, Event = "NullRef", Type = "error", State = HoldFast.Domain.Enums.ErrorGroupState.Open };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var eo1 = new ErrorObject { ErrorGroupId = eg.Id, Event = "instance-1", ProjectId = project.Id };
        var eo2 = new ErrorObject { ErrorGroupId = eg.Id, Event = "instance-2", ProjectId = project.Id };
        _db.ErrorObjects.AddRange(eo1, eo2);
        await _db.SaveChangesAsync();

        var result = await _query.GetErrorInstance(
            eg.Id, eo1.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(eo1.Id, result!.Id);
    }

    [Fact]
    public async Task GetErrorInstance_NoId_ReturnsAnInstance()
    {
        var (_, _, project) = await SeedFullStack();
        var eg = new ErrorGroup { ProjectId = project.Id, Event = "TypeError", Type = "error", State = HoldFast.Domain.Enums.ErrorGroupState.Open };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var eo1 = new ErrorObject { ErrorGroupId = eg.Id, Event = "old", ProjectId = project.Id };
        var eo2 = new ErrorObject { ErrorGroupId = eg.Id, Event = "new", ProjectId = project.Id };
        _db.ErrorObjects.AddRange(eo1, eo2);
        await _db.SaveChangesAsync();

        var result = await _query.GetErrorInstance(
            eg.Id, null, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(eg.Id, result!.ErrorGroupId);
        // Should return one of the two — exact ordering depends on DB
        Assert.Contains(result.Id, new[] { eo1.Id, eo2.Id });
    }

    [Fact]
    public async Task GetErrorInstance_NoAccess_Throws()
    {
        var (_, _, project) = await SeedFullStack();
        var eg = new ErrorGroup { ProjectId = project.Id, Event = "err", Type = "error", State = HoldFast.Domain.Enums.ErrorGroupState.Open };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var outsider = new Admin { Uid = "outsider", Email = "outsider@test.com" };
        _db.Admins.Add(outsider);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _query.GetErrorInstance(eg.Id, null, MakePrincipal("outsider"), _authz, _db, CancellationToken.None));
    }

    // ── GetClientIntegration ───────────────────────────────────────

    [Fact]
    public async Task GetClientIntegration_NoSessions_ReturnsFalse()
    {
        var (_, _, project) = await SeedFullStack();

        var result = await _query.GetClientIntegration(
            project.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.False(result.Integrated);
    }

    [Fact]
    public async Task GetClientIntegration_WithSessions_ReturnsTrue()
    {
        var (_, _, project) = await SeedFullStack();
        _db.Sessions.Add(new Session { SecureId = "s1", ProjectId = project.Id });
        await _db.SaveChangesAsync();

        var result = await _query.GetClientIntegration(
            project.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(result.Integrated);
    }

    // ── GetServerIntegration ───────────────────────────────────────

    [Fact]
    public async Task GetServerIntegration_NoErrors_ReturnsFalse()
    {
        var (_, _, project) = await SeedFullStack();

        var result = await _query.GetServerIntegration(
            project.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.False(result.Integrated);
    }

    [Fact]
    public async Task GetServerIntegration_WithErrors_ReturnsTrue()
    {
        var (_, _, project) = await SeedFullStack();
        _db.ErrorGroups.Add(new ErrorGroup { ProjectId = project.Id, Event = "err", Type = "error", State = HoldFast.Domain.Enums.ErrorGroupState.Open });
        await _db.SaveChangesAsync();

        var result = await _query.GetServerIntegration(
            project.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(result.Integrated);
    }

    // ── GetLogsIntegration / GetTracesIntegration ──────────────────

    [Fact]
    public async Task GetLogsIntegration_DelegatesToClickHouse()
    {
        var (_, _, project) = await SeedFullStack();

        var result = await _query.GetLogsIntegration(
            project.Id, MakePrincipal("admin-uid"), _authz, _clickHouse, CancellationToken.None);

        Assert.False(result.Integrated); // Stub returns empty
        Assert.Equal("ReadLogsAsync", _clickHouse.LastCalledMethod);
    }

    [Fact]
    public async Task GetTracesIntegration_DelegatesToClickHouse()
    {
        var (_, _, project) = await SeedFullStack();

        var result = await _query.GetTracesIntegration(
            project.Id, MakePrincipal("admin-uid"), _authz, _clickHouse, CancellationToken.None);

        Assert.False(result.Integrated); // Stub returns empty
        Assert.Equal("ReadTracesAsync", _clickHouse.LastCalledMethod);
    }

    // ── GetAlert ───────────────────────────────────────────────────

    [Fact]
    public async Task GetAlert_WithAccess_ReturnsAlert()
    {
        var (_, _, project) = await SeedFullStack();
        var alert = new Alert { ProjectId = project.Id, Name = "Test Alert", ProductType = "ERROR" };
        _db.Alerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _query.GetAlert(
            alert.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Test Alert", result!.Name);
    }

    [Fact]
    public async Task GetAlert_NonExistent_ReturnsNull()
    {
        await SeedFullStack();

        var result = await _query.GetAlert(
            99999, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Null(result);
    }

    // ── GetMetricMonitors ──────────────────────────────────────────

    [Fact]
    public async Task GetMetricMonitors_ReturnsMonitors()
    {
        var (_, _, project) = await SeedFullStack();
        _db.MetricMonitors.Add(new MetricMonitor
        {
            ProjectId = project.Id, Name = "CPU Alert", MetricToMonitor = "cpu_usage",
            Aggregator = "AVG", Threshold = 90.0,
        });
        await _db.SaveChangesAsync();

        var result = await _query.GetMetricMonitors(
            project.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("CPU Alert", result[0].Name);
    }

    [Fact]
    public async Task GetMetricMonitors_NoAccess_Throws()
    {
        var (_, _, project) = await SeedFullStack();
        var outsider = new Admin { Uid = "outsider5", Email = "outsider5@test.com" };
        _db.Admins.Add(outsider);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _query.GetMetricMonitors(project.Id, MakePrincipal("outsider5"), _authz, _db, CancellationToken.None));
    }

    // ── GetAdminHasCreatedComment ──────────────────────────────────

    [Fact]
    public async Task GetAdminHasCreatedComment_NoComments_ReturnsFalse()
    {
        await SeedFullStack();

        var result = await _query.GetAdminHasCreatedComment(
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task GetAdminHasCreatedComment_WithComments_ReturnsTrue()
    {
        var (admin, _, project) = await SeedFullStack();
        var session = new Session { SecureId = "s1", ProjectId = project.Id };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        _db.SessionComments.Add(new SessionComment
        {
            SessionId = session.Id, AdminId = admin.Id, ProjectId = project.Id,
            Text = "test comment",
        });
        await _db.SaveChangesAsync();

        var result = await _query.GetAdminHasCreatedComment(
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(result);
    }

    // ── GetProjectHasViewedASession ────────────────────────────────

    [Fact]
    public async Task GetProjectHasViewedASession_NoViewed_ReturnsFalse()
    {
        var (_, _, project) = await SeedFullStack();
        _db.Sessions.Add(new Session { SecureId = "s1", ProjectId = project.Id, ViewedByAdmins = 0 });
        await _db.SaveChangesAsync();

        var result = await _query.GetProjectHasViewedASession(
            project.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task GetProjectHasViewedASession_WithViewed_ReturnsTrue()
    {
        var (_, _, project) = await SeedFullStack();
        _db.Sessions.Add(new Session { SecureId = "s1", ProjectId = project.Id, ViewedByAdmins = 3 });
        await _db.SaveChangesAsync();

        var result = await _query.GetProjectHasViewedASession(
            project.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(result);
    }

    // ── GetErrorCommentsForAdmin ───────────────────────────────────

    [Fact]
    public async Task GetErrorCommentsForAdmin_ReturnsOwnComments()
    {
        var (admin, _, project) = await SeedFullStack();
        var eg = new ErrorGroup { ProjectId = project.Id, Event = "err", Type = "error", State = HoldFast.Domain.Enums.ErrorGroupState.Open };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        _db.ErrorComments.Add(new ErrorComment
        {
            ErrorGroupId = eg.Id, AdminId = admin.Id, Text = "my comment",
        });
        await _db.SaveChangesAsync();

        var result = await _query.GetErrorCommentsForAdmin(
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("my comment", result[0].Text);
    }

    // ── GetSessionCommentsForAdmin ─────────────────────────────────

    [Fact]
    public async Task GetSessionCommentsForAdmin_ReturnsOwnComments()
    {
        var (admin, _, project) = await SeedFullStack();
        var session = new Session { SecureId = "s1", ProjectId = project.Id };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        _db.SessionComments.Add(new SessionComment
        {
            SessionId = session.Id, AdminId = admin.Id, ProjectId = project.Id,
            Text = "session note",
        });
        await _db.SaveChangesAsync();

        var result = await _query.GetSessionCommentsForAdmin(
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("session note", result[0].Text);
    }

    // ── Stub ───────────────────────────────────────────────────────

    private class StubClickHouseService : IClickHouseService
    {
        public string? LastCalledMethod { get; private set; }
        public int LastProjectId { get; private set; }

        public Task<LogConnection> ReadLogsAsync(int projectId, QueryInput query, ClickHousePagination pagination, CancellationToken ct)
        { LastCalledMethod = nameof(ReadLogsAsync); LastProjectId = projectId; return Task.FromResult(new LogConnection()); }
        public Task<List<HistogramBucket>> ReadLogsHistogramAsync(int projectId, QueryInput query, CancellationToken ct)
        { LastCalledMethod = nameof(ReadLogsHistogramAsync); return Task.FromResult(new List<HistogramBucket>()); }
        public Task<List<string>> GetLogKeysAsync(int projectId, QueryInput query, CancellationToken ct)
        { LastCalledMethod = nameof(GetLogKeysAsync); return Task.FromResult(new List<string>()); }
        public Task<List<string>> GetLogKeyValuesAsync(int projectId, string key, QueryInput query, CancellationToken ct)
        { LastCalledMethod = nameof(GetLogKeyValuesAsync); return Task.FromResult(new List<string>()); }
        public Task<TraceConnection> ReadTracesAsync(int projectId, QueryInput query, ClickHousePagination pagination, bool omitBody, CancellationToken ct)
        { LastCalledMethod = nameof(ReadTracesAsync); LastProjectId = projectId; return Task.FromResult(new TraceConnection()); }
        public Task<List<HistogramBucket>> ReadTracesHistogramAsync(int projectId, QueryInput query, CancellationToken ct)
        { LastCalledMethod = nameof(ReadTracesHistogramAsync); return Task.FromResult(new List<HistogramBucket>()); }
        public Task<List<string>> GetTraceKeysAsync(int projectId, QueryInput query, CancellationToken ct)
        { LastCalledMethod = nameof(GetTraceKeysAsync); return Task.FromResult(new List<string>()); }
        public Task<List<string>> GetTraceKeyValuesAsync(int projectId, string key, QueryInput query, CancellationToken ct)
        { LastCalledMethod = nameof(GetTraceKeyValuesAsync); return Task.FromResult(new List<string>()); }
        public Task<List<HistogramBucket>> ReadSessionsHistogramAsync(int projectId, QueryInput query, CancellationToken ct)
        { LastCalledMethod = nameof(ReadSessionsHistogramAsync); return Task.FromResult(new List<HistogramBucket>()); }
        public Task<(List<int> Ids, long Total)> QuerySessionIdsAsync(int projectId, QueryInput query, int count, int page, string? sortField, bool sortDesc, CancellationToken ct)
        { LastCalledMethod = nameof(QuerySessionIdsAsync); return Task.FromResult((new List<int>(), 0L)); }
        public Task<(List<int> Ids, long Total)> QueryErrorGroupIdsAsync(int projectId, QueryInput query, int count, int page, CancellationToken ct)
        { LastCalledMethod = nameof(QueryErrorGroupIdsAsync); return Task.FromResult((new List<int>(), 0L)); }
        public Task<List<HistogramBucket>> ReadErrorObjectsHistogramAsync(int projectId, QueryInput query, CancellationToken ct)
        { LastCalledMethod = nameof(ReadErrorObjectsHistogramAsync); return Task.FromResult(new List<HistogramBucket>()); }
        public Task<MetricsBuckets> ReadMetricsAsync(int projectId, QueryInput query, string bucketBy, List<string>? groupBy, string aggregator, string? column, CancellationToken ct)
        { LastCalledMethod = nameof(ReadMetricsAsync); return Task.FromResult(new MetricsBuckets()); }
        public Task<List<QueryKey>> GetSessionsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, CancellationToken ct)
        { LastCalledMethod = nameof(GetSessionsKeysAsync); return Task.FromResult(new List<QueryKey>()); }
        public Task<List<string>> GetSessionsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, CancellationToken ct)
        { LastCalledMethod = nameof(GetSessionsKeyValuesAsync); return Task.FromResult(new List<string>()); }
        public Task<List<QueryKey>> GetErrorsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, CancellationToken ct)
        { LastCalledMethod = nameof(GetErrorsKeysAsync); return Task.FromResult(new List<QueryKey>()); }
        public Task<List<string>> GetErrorsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, CancellationToken ct)
        { LastCalledMethod = nameof(GetErrorsKeyValuesAsync); return Task.FromResult(new List<string>()); }
        public Task<List<QueryKey>> GetEventsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, string? eventName, CancellationToken ct)
        { LastCalledMethod = nameof(GetEventsKeysAsync); return Task.FromResult(new List<QueryKey>()); }
        public Task<List<string>> GetEventsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, string? eventName, CancellationToken ct)
        { LastCalledMethod = nameof(GetEventsKeyValuesAsync); return Task.FromResult(new List<string>()); }
        public Task WriteMetricAsync(int projectId, string metricName, double metricValue, string? category, DateTime timestamp, Dictionary<string, string>? tags, string? sessionSecureId, CancellationToken ct)
        { LastCalledMethod = nameof(WriteMetricAsync); return Task.CompletedTask; }
        public Task WriteLogsAsync(IEnumerable<LogRowInput> logs, CancellationToken ct)
        { LastCalledMethod = nameof(WriteLogsAsync); return Task.CompletedTask; }
        public Task WriteTracesAsync(IEnumerable<TraceRowInput> traces, CancellationToken ct)
        { LastCalledMethod = nameof(WriteTracesAsync); return Task.CompletedTask; }
        public Task WriteSessionsAsync(IEnumerable<SessionRowInput> sessions, CancellationToken ct)
        { LastCalledMethod = nameof(WriteSessionsAsync); return Task.CompletedTask; }
        public Task WriteErrorGroupsAsync(IEnumerable<ErrorGroupRowInput> errorGroups, CancellationToken ct)
        { LastCalledMethod = nameof(WriteErrorGroupsAsync); return Task.CompletedTask; }
        public Task WriteErrorObjectsAsync(IEnumerable<ErrorObjectRowInput> errorObjects, CancellationToken ct)
        { LastCalledMethod = nameof(WriteErrorObjectsAsync); return Task.CompletedTask; }

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
