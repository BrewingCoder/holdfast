using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Tests for session/error detail queries, comment-scoped queries,
/// and comment reply/mute mutations.
/// </summary>
public class PrivateQuerySessionAndCommentTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateQuery _query;
    private readonly PrivateMutation _mutation;

    private readonly Admin _admin;
    private readonly ClaimsPrincipal _principal;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public PrivateQuerySessionAndCommentTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();
        _query = new PrivateQuery();
        _mutation = new PrivateMutation();

        _admin = new Admin { Uid = "admin-uid", Email = "admin@test.com" };
        _db.Admins.Add(_admin);
        _workspace = new Workspace { Name = "TestWS" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = _admin.Id, WorkspaceId = _workspace.Id, Role = "ADMIN" });
        _project = new Project { Name = "TestProj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _authz = new AuthorizationService(_db);
        _principal = MakePrincipal(_admin);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static ClaimsPrincipal MakePrincipal(Admin admin) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, admin.Uid!),
            new Claim(HoldFastClaimTypes.AdminId, admin.Id.ToString()),
        }, "Test"));

    // ── GetEvents (EventChunks) ──────────────────────────────────────

    [Fact]
    public async Task GetEvents_ReturnsChunksOrderedByIndex()
    {
        var session = new Session { SecureId = "sess-ev", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        _db.EventChunks.AddRange(
            new EventChunk { SessionId = session.Id, ChunkIndex = 2, Timestamp = 300 },
            new EventChunk { SessionId = session.Id, ChunkIndex = 0, Timestamp = 100 },
            new EventChunk { SessionId = session.Id, ChunkIndex = 1, Timestamp = 200 });
        await _db.SaveChangesAsync();

        var chunks = await _query.GetEvents(
            session.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(3, chunks.Count);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal(1, chunks[1].ChunkIndex);
        Assert.Equal(2, chunks[2].ChunkIndex);
    }

    [Fact]
    public async Task GetEvents_NoChunks_ReturnsEmpty()
    {
        var session = new Session { SecureId = "sess-no-ev", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var chunks = await _query.GetEvents(
            session.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(chunks);
    }

    // ── IsSessionPending ─────────────────────────────────────────────

    [Fact]
    public async Task IsSessionPending_Unprocessed_ReturnsTrue()
    {
        var session = new Session { SecureId = "sess-pend", ProjectId = _project.Id, Processed = false, CreatedAt = DateTime.UtcNow };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var pending = await _query.IsSessionPending(
            session.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.True(pending);
    }

    [Fact]
    public async Task IsSessionPending_Processed_ReturnsFalse()
    {
        var session = new Session { SecureId = "sess-proc", ProjectId = _project.Id, Processed = true, CreatedAt = DateTime.UtcNow };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var pending = await _query.IsSessionPending(
            session.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.False(pending);
    }

    [Fact]
    public async Task IsSessionPending_NonExistent_ReturnsFalse()
    {
        var pending = await _query.IsSessionPending(
            99999, _principal, _authz, _db, CancellationToken.None);

        Assert.False(pending);
    }

    // ── GetErrorInstance ─────────────────────────────────────────────

    [Fact]
    public async Task GetErrorInstance_ByObjectId_ReturnsSpecific()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "err", SecureId = "eg-inst", State = ErrorGroupState.Open };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var eo1 = new ErrorObject { ErrorGroupId = eg.Id, ProjectId = _project.Id, Event = "err" };
        var eo2 = new ErrorObject { ErrorGroupId = eg.Id, ProjectId = _project.Id, Event = "err" };
        _db.ErrorObjects.AddRange(eo1, eo2);
        await _db.SaveChangesAsync();

        var result = await _query.GetErrorInstance(
            eg.Id, eo1.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(eo1.Id, result!.Id);
    }

    [Fact]
    public async Task GetErrorInstance_NoObjectId_ReturnsLatest()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "err", SecureId = "eg-latest", State = ErrorGroupState.Open };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var eo1 = new ErrorObject { ErrorGroupId = eg.Id, ProjectId = _project.Id, Event = "err", CreatedAt = DateTime.UtcNow.AddHours(-1) };
        var eo2 = new ErrorObject { ErrorGroupId = eg.Id, ProjectId = _project.Id, Event = "err", CreatedAt = DateTime.UtcNow };
        _db.ErrorObjects.AddRange(eo1, eo2);
        await _db.SaveChangesAsync();

        var result = await _query.GetErrorInstance(
            eg.Id, null, _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(eo2.Id, result!.Id);
    }

    // ── GetErrorCommentsForAdmin ─────────────────────────────────────

    [Fact]
    public async Task GetErrorCommentsForAdmin_ReturnsOnlyAdminComments()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "err", SecureId = "eg-eca", State = ErrorGroupState.Open };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var other = new Admin { Uid = "other", Email = "other@test.com" };
        _db.Admins.Add(other);
        await _db.SaveChangesAsync();

        _db.ErrorComments.AddRange(
            new ErrorComment { ErrorGroupId = eg.Id, AdminId = _admin.Id, Text = "Mine" },
            new ErrorComment { ErrorGroupId = eg.Id, AdminId = other.Id, Text = "Not mine" });
        await _db.SaveChangesAsync();

        var list = await _query.GetErrorCommentsForAdmin(
            _principal, _authz, _db, CancellationToken.None);

        Assert.Single(list);
        Assert.Equal("Mine", list[0].Text);
    }

    // ── GetErrorCommentsForProject ───────────────────────────────────

    [Fact]
    public async Task GetErrorCommentsForProject_ReturnsProjectComments()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "err", SecureId = "eg-ecp", State = ErrorGroupState.Open };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        _db.ErrorComments.AddRange(
            new ErrorComment { ErrorGroupId = eg.Id, AdminId = _admin.Id, Text = "C1" },
            new ErrorComment { ErrorGroupId = eg.Id, AdminId = _admin.Id, Text = "C2" });
        await _db.SaveChangesAsync();

        var list = await _query.GetErrorCommentsForProject(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, list.Count);
    }

    // ── GetSessionCommentsForAdmin ───────────────────────────────────

    [Fact]
    public async Task GetSessionCommentsForAdmin_ReturnsOnlyAdminComments()
    {
        var session = new Session { SecureId = "sess-sca", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var other = new Admin { Uid = "other-sca", Email = "other-sca@test.com" };
        _db.Admins.Add(other);
        await _db.SaveChangesAsync();

        _db.SessionComments.AddRange(
            new SessionComment { SessionId = session.Id, ProjectId = _project.Id, AdminId = _admin.Id, Text = "Mine" },
            new SessionComment { SessionId = session.Id, ProjectId = _project.Id, AdminId = other.Id, Text = "Not mine" });
        await _db.SaveChangesAsync();

        var list = await _query.GetSessionCommentsForAdmin(
            _principal, _authz, _db, CancellationToken.None);

        Assert.Single(list);
        Assert.Equal("Mine", list[0].Text);
    }

    // ── GetJoinableWorkspaces ────────────────────────────────────────

    [Fact]
    public async Task GetJoinableWorkspaces_ReturnsWorkspacesWithPendingInvites()
    {
        var ws2 = new Workspace { Name = "InvitedWS" };
        _db.Workspaces.Add(ws2);
        await _db.SaveChangesAsync();

        _db.WorkspaceInviteLinks.Add(new WorkspaceInviteLink
            { WorkspaceId = ws2.Id, Secret = "inv1", InviteeEmail = _admin.Email });
        await _db.SaveChangesAsync();

        var list = await _query.GetJoinableWorkspaces(
            _principal, _authz, _db, CancellationToken.None);

        Assert.Single(list);
        Assert.Equal("InvitedWS", list[0].Name);
    }

    [Fact]
    public async Task GetJoinableWorkspaces_ExcludesExpiredInvites()
    {
        var ws2 = new Workspace { Name = "ExpiredWS" };
        _db.Workspaces.Add(ws2);
        await _db.SaveChangesAsync();

        _db.WorkspaceInviteLinks.Add(new WorkspaceInviteLink
        {
            WorkspaceId = ws2.Id, Secret = "expired",
            InviteeEmail = _admin.Email,
            ExpirationDate = DateTime.UtcNow.AddDays(-1), // expired
        });
        await _db.SaveChangesAsync();

        var list = await _query.GetJoinableWorkspaces(
            _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(list);
    }

    [Fact]
    public async Task GetJoinableWorkspaces_NoInvites_ReturnsEmpty()
    {
        var list = await _query.GetJoinableWorkspaces(
            _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(list);
    }

    // ── GetWorkspaceAdminsByProjectId ────────────────────────────────

    [Fact]
    public async Task GetWorkspaceAdminsByProjectId_ReturnsAdmins()
    {
        var list = await _query.GetWorkspaceAdminsByProjectId(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Single(list);
        Assert.Equal(_admin.Id, list[0].Id);
    }

    // ── ReplyToErrorComment ──────────────────────────────────────────

    [Fact]
    public async Task ReplyToErrorComment_CreatesReply()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "err", SecureId = "eg-reply", State = ErrorGroupState.Open };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var comment = new ErrorComment { ErrorGroupId = eg.Id, AdminId = _admin.Id, Text = "Original" };
        _db.ErrorComments.Add(comment);
        await _db.SaveChangesAsync();

        var reply = await _mutation.ReplyToErrorComment(
            comment.Id, "This is a reply",
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("This is a reply", reply.Text);
        Assert.Equal(_admin.Id, reply.AdminId);
        Assert.Equal(comment.Id, reply.ErrorCommentId);

        var persisted = await _db.CommentReplies.FirstAsync(r => r.Id == reply.Id);
        Assert.Equal("This is a reply", persisted.Text);
    }

    [Fact]
    public async Task ReplyToErrorComment_NotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.ReplyToErrorComment(
                99999, "Reply",
                _principal, _authz, _db, CancellationToken.None));
    }

    // ── MuteErrorCommentThread ───────────────────────────────────────

    [Fact]
    public async Task MuteErrorCommentThread_CreatesMuteRecord()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "err", SecureId = "eg-mute", State = ErrorGroupState.Open };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var comment = new ErrorComment { ErrorGroupId = eg.Id, AdminId = _admin.Id, Text = "Muteable" };
        _db.ErrorComments.Add(comment);
        await _db.SaveChangesAsync();

        var result = await _mutation.MuteErrorCommentThread(
            comment.Id, true,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var follower = await _db.CommentFollowers
            .FirstOrDefaultAsync(f => f.ErrorCommentId == comment.Id && f.AdminId == _admin.Id);
        Assert.NotNull(follower);
        Assert.True(follower!.HasMuted);
    }

    [Fact]
    public async Task MuteErrorCommentThread_Toggle_UpdatesExisting()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "err", SecureId = "eg-mute2", State = ErrorGroupState.Open };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var comment = new ErrorComment { ErrorGroupId = eg.Id, AdminId = _admin.Id, Text = "Toggle" };
        _db.ErrorComments.Add(comment);
        await _db.SaveChangesAsync();

        // Mute
        await _mutation.MuteErrorCommentThread(comment.Id, true, _principal, _authz, _db, CancellationToken.None);
        // Unmute
        await _mutation.MuteErrorCommentThread(comment.Id, false, _principal, _authz, _db, CancellationToken.None);

        var follower = await _db.CommentFollowers
            .FirstAsync(f => f.ErrorCommentId == comment.Id && f.AdminId == _admin.Id);
        Assert.False(follower.HasMuted);

        // Should not create duplicate
        var count = await _db.CommentFollowers.CountAsync(f => f.ErrorCommentId == comment.Id && f.AdminId == _admin.Id);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task MuteErrorCommentThread_NullHasMuted_DefaultsToTrue()
    {
        var eg = new ErrorGroup { ProjectId = _project.Id, Event = "err", SecureId = "eg-mute3", State = ErrorGroupState.Open };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var comment = new ErrorComment { ErrorGroupId = eg.Id, AdminId = _admin.Id, Text = "Default" };
        _db.ErrorComments.Add(comment);
        await _db.SaveChangesAsync();

        await _mutation.MuteErrorCommentThread(comment.Id, null, _principal, _authz, _db, CancellationToken.None);

        var follower = await _db.CommentFollowers
            .FirstAsync(f => f.ErrorCommentId == comment.Id && f.AdminId == _admin.Id);
        Assert.True(follower.HasMuted);
    }

    // ── MuteSessionCommentThread ─────────────────────────────────────

    [Fact]
    public async Task MuteSessionCommentThread_CreatesMuteRecord()
    {
        var session = new Session { SecureId = "sess-mute", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var comment = new SessionComment { SessionId = session.Id, ProjectId = _project.Id, AdminId = _admin.Id, Text = "Muteable" };
        _db.SessionComments.Add(comment);
        await _db.SaveChangesAsync();

        var result = await _mutation.MuteSessionCommentThread(
            comment.Id, true,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var follower = await _db.CommentFollowers
            .FirstOrDefaultAsync(f => f.SessionCommentId == comment.Id && f.AdminId == _admin.Id);
        Assert.NotNull(follower);
        Assert.True(follower!.HasMuted);
    }

    [Fact]
    public async Task MuteSessionCommentThread_Toggle_UpdatesExisting()
    {
        var session = new Session { SecureId = "sess-mute2", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var comment = new SessionComment { SessionId = session.Id, ProjectId = _project.Id, AdminId = _admin.Id, Text = "Toggle" };
        _db.SessionComments.Add(comment);
        await _db.SaveChangesAsync();

        await _mutation.MuteSessionCommentThread(comment.Id, true, _principal, _authz, _db, CancellationToken.None);
        await _mutation.MuteSessionCommentThread(comment.Id, false, _principal, _authz, _db, CancellationToken.None);

        var follower = await _db.CommentFollowers
            .FirstAsync(f => f.SessionCommentId == comment.Id && f.AdminId == _admin.Id);
        Assert.False(follower.HasMuted);
    }

    // ── GetWorkspaceForProject ───────────────────────────────────────

    [Fact]
    public async Task GetWorkspaceForProject_ReturnsWorkspace()
    {
        var ws = await _query.GetWorkspaceForProject(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(ws);
        Assert.Equal(_workspace.Id, ws!.Id);
    }

    [Fact]
    public async Task GetWorkspaceForProject_NotFound_Throws()
    {
        // RequireProjectAccess throws for nonexistent project
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _query.GetWorkspaceForProject(
                99999, _principal, _authz, _db, CancellationToken.None));
    }

    // ── GetSession ───────────────────────────────────────────────────

    [Fact]
    public async Task GetSession_ReturnsSession()
    {
        var session = new Session { SecureId = "sess-get", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var result = await _query.GetSession(
            "sess-get", _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("sess-get", result!.SecureId);
    }

    [Fact]
    public async Task GetSession_NotFound_ReturnsNull()
    {
        var result = await _query.GetSession(
            "nonexistent", _principal, _authz, _db, CancellationToken.None);

        Assert.Null(result);
    }

    // ── GetSessionIntervals ──────────────────────────────────────────

    [Fact]
    public async Task GetSessionIntervals_ReturnsIntervalsForSession()
    {
        var session = new Session { SecureId = "sess-int", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        _db.SessionIntervals.AddRange(
            new SessionInterval { SessionId = session.Id, StartTime = 0, EndTime = 1000, Duration = 1000, Active = true },
            new SessionInterval { SessionId = session.Id, StartTime = 1000, EndTime = 2000, Duration = 1000, Active = false });
        await _db.SaveChangesAsync();

        var intervals = await _query.GetSessionIntervals(
            session.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, intervals.Count);
    }

    // ── GetAdminRoleByProject ────────────────────────────────────────

    [Fact]
    public async Task GetAdminRoleByProject_ReturnsRole()
    {
        var role = await _query.GetAdminRoleByProject(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(role);
        Assert.Equal("ADMIN", role!.Role);
    }

    [Fact]
    public async Task GetAdminRoleByProject_ProjectNotFound_ReturnsNull()
    {
        var role = await _query.GetAdminRoleByProject(
            99999, _principal, _authz, _db, CancellationToken.None);

        Assert.Null(role);
    }
}
