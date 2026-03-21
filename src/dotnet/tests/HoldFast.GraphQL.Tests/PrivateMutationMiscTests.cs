using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Tests for miscellaneous mutations: MuteErrorCommentThread, MuteSessionCommentThread,
/// UpdateEmailOptOut, ExportSession, EditProjectPlatforms.
/// </summary>
public class PrivateMutationMiscTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateMutation _mutation;
    private readonly ClaimsPrincipal _principal;
    private readonly Admin _admin;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public PrivateMutationMiscTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();
        _mutation = new PrivateMutation();

        _admin = new Admin { Uid = "admin-1", Email = "admin@test.com" };
        _db.Admins.Add(_admin);
        _workspace = new Workspace { Name = "WS" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = _admin.Id, WorkspaceId = _workspace.Id, Role = "ADMIN" });
        _project = new Project { Name = "Proj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _authz = new AuthorizationService(_db);
        _principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, "admin-1"),
            new Claim(HoldFastClaimTypes.AdminId, _admin.Id.ToString()),
        }, "Test"));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ── MuteErrorCommentThread ──────────────────────────────────────

    [Fact]
    public async Task MuteErrorCommentThread_CreatesFollower()
    {
        var errorGroup = new ErrorGroup { ProjectId = _project.Id, Event = "err", Type = "TypeError" };
        _db.ErrorGroups.Add(errorGroup);
        await _db.SaveChangesAsync();

        var comment = new ErrorComment { ErrorGroupId = errorGroup.Id, AdminId = _admin.Id, Text = "test" };
        _db.ErrorComments.Add(comment);
        await _db.SaveChangesAsync();

        var result = await _mutation.MuteErrorCommentThread(
            comment.Id, true, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var follower = await _db.CommentFollowers
            .FirstOrDefaultAsync(f => f.ErrorCommentId == comment.Id && f.AdminId == _admin.Id);
        Assert.NotNull(follower);
        Assert.True(follower!.HasMuted);
    }

    [Fact]
    public async Task MuteErrorCommentThread_UpdateExisting()
    {
        var errorGroup = new ErrorGroup { ProjectId = _project.Id, Event = "err", Type = "TypeError" };
        _db.ErrorGroups.Add(errorGroup);
        await _db.SaveChangesAsync();

        var comment = new ErrorComment { ErrorGroupId = errorGroup.Id, AdminId = _admin.Id, Text = "test" };
        _db.ErrorComments.Add(comment);
        await _db.SaveChangesAsync();

        // First mute
        await _mutation.MuteErrorCommentThread(
            comment.Id, true, _principal, _authz, _db, CancellationToken.None);

        // Now unmute
        await _mutation.MuteErrorCommentThread(
            comment.Id, false, _principal, _authz, _db, CancellationToken.None);

        var follower = await _db.CommentFollowers
            .FirstAsync(f => f.ErrorCommentId == comment.Id && f.AdminId == _admin.Id);
        Assert.False(follower.HasMuted);
    }

    [Fact]
    public async Task MuteErrorCommentThread_NullHasMuted_DefaultsTrue()
    {
        var errorGroup = new ErrorGroup { ProjectId = _project.Id, Event = "err", Type = "TypeError" };
        _db.ErrorGroups.Add(errorGroup);
        await _db.SaveChangesAsync();

        var comment = new ErrorComment { ErrorGroupId = errorGroup.Id, AdminId = _admin.Id, Text = "test" };
        _db.ErrorComments.Add(comment);
        await _db.SaveChangesAsync();

        await _mutation.MuteErrorCommentThread(
            comment.Id, null, _principal, _authz, _db, CancellationToken.None);

        var follower = await _db.CommentFollowers
            .FirstAsync(f => f.ErrorCommentId == comment.Id && f.AdminId == _admin.Id);
        Assert.True(follower.HasMuted);
    }

    [Fact]
    public async Task MuteErrorCommentThread_OnlyOneFollowerPerAdmin()
    {
        var errorGroup = new ErrorGroup { ProjectId = _project.Id, Event = "err", Type = "TypeError" };
        _db.ErrorGroups.Add(errorGroup);
        await _db.SaveChangesAsync();

        var comment = new ErrorComment { ErrorGroupId = errorGroup.Id, AdminId = _admin.Id, Text = "test" };
        _db.ErrorComments.Add(comment);
        await _db.SaveChangesAsync();

        // Mute then unmute — should not create duplicate records
        await _mutation.MuteErrorCommentThread(comment.Id, true, _principal, _authz, _db, CancellationToken.None);
        await _mutation.MuteErrorCommentThread(comment.Id, false, _principal, _authz, _db, CancellationToken.None);
        await _mutation.MuteErrorCommentThread(comment.Id, true, _principal, _authz, _db, CancellationToken.None);

        var count = await _db.CommentFollowers
            .CountAsync(f => f.ErrorCommentId == comment.Id && f.AdminId == _admin.Id);
        Assert.Equal(1, count);
    }

    // ── MuteSessionCommentThread ────────────────────────────────────

    [Fact]
    public async Task MuteSessionCommentThread_CreatesFollower()
    {
        var session = new Session
        {
            ProjectId = _project.Id, SecureId = "sess-mute",
            Fingerprint = "123", City = "", State = "", Country = "",
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var comment = new SessionComment { SessionId = session.Id, AdminId = _admin.Id, Text = "test" };
        _db.SessionComments.Add(comment);
        await _db.SaveChangesAsync();

        var result = await _mutation.MuteSessionCommentThread(
            comment.Id, true, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var follower = await _db.CommentFollowers
            .FirstOrDefaultAsync(f => f.SessionCommentId == comment.Id && f.AdminId == _admin.Id);
        Assert.NotNull(follower);
        Assert.True(follower!.HasMuted);
    }

    [Fact]
    public async Task MuteSessionCommentThread_UpdateExisting()
    {
        var session = new Session
        {
            ProjectId = _project.Id, SecureId = "sess-mute2",
            Fingerprint = "123", City = "", State = "", Country = "",
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var comment = new SessionComment { SessionId = session.Id, AdminId = _admin.Id, Text = "test" };
        _db.SessionComments.Add(comment);
        await _db.SaveChangesAsync();

        await _mutation.MuteSessionCommentThread(comment.Id, true, _principal, _authz, _db, CancellationToken.None);
        await _mutation.MuteSessionCommentThread(comment.Id, false, _principal, _authz, _db, CancellationToken.None);

        var follower = await _db.CommentFollowers
            .FirstAsync(f => f.SessionCommentId == comment.Id && f.AdminId == _admin.Id);
        Assert.False(follower.HasMuted);
    }

    [Fact]
    public async Task MuteSessionCommentThread_NullHasMuted_DefaultsTrue()
    {
        var session = new Session
        {
            ProjectId = _project.Id, SecureId = "sess-mute3",
            Fingerprint = "123", City = "", State = "", Country = "",
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var comment = new SessionComment { SessionId = session.Id, AdminId = _admin.Id, Text = "test" };
        _db.SessionComments.Add(comment);
        await _db.SaveChangesAsync();

        await _mutation.MuteSessionCommentThread(comment.Id, null, _principal, _authz, _db, CancellationToken.None);

        var follower = await _db.CommentFollowers
            .FirstAsync(f => f.SessionCommentId == comment.Id && f.AdminId == _admin.Id);
        Assert.True(follower.HasMuted);
    }

    // ── UpdateEmailOptOut ───────────────────────────────────────────

    [Fact]
    public async Task UpdateEmailOptOut_OptOut()
    {
        var result = await _mutation.UpdateEmailOptOut(
            "digest", true, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var optOut = await _db.EmailOptOuts
            .FirstOrDefaultAsync(e => e.AdminId == _admin.Id && e.Category == "digest");
        Assert.NotNull(optOut);
    }

    [Fact]
    public async Task UpdateEmailOptOut_OptBackIn()
    {
        // First opt out
        await _mutation.UpdateEmailOptOut(
            "digest", true, _principal, _authz, _db, CancellationToken.None);

        // Then opt back in
        await _mutation.UpdateEmailOptOut(
            "digest", false, _principal, _authz, _db, CancellationToken.None);

        var optOut = await _db.EmailOptOuts
            .FirstOrDefaultAsync(e => e.AdminId == _admin.Id && e.Category == "digest");
        Assert.Null(optOut);
    }

    [Fact]
    public async Task UpdateEmailOptOut_OptInWhenNotOptedOut_NoOp()
    {
        // Opt in without prior opt-out
        var result = await _mutation.UpdateEmailOptOut(
            "digest", false, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var count = await _db.EmailOptOuts.CountAsync(e => e.AdminId == _admin.Id);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task UpdateEmailOptOut_DuplicateOptOut_NoOp()
    {
        await _mutation.UpdateEmailOptOut("alerts", true, _principal, _authz, _db, CancellationToken.None);
        await _mutation.UpdateEmailOptOut("alerts", true, _principal, _authz, _db, CancellationToken.None);

        var count = await _db.EmailOptOuts.CountAsync(e => e.AdminId == _admin.Id && e.Category == "alerts");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task UpdateEmailOptOut_DifferentCategories()
    {
        await _mutation.UpdateEmailOptOut("digest", true, _principal, _authz, _db, CancellationToken.None);
        await _mutation.UpdateEmailOptOut("alerts", true, _principal, _authz, _db, CancellationToken.None);
        await _mutation.UpdateEmailOptOut("marketing", true, _principal, _authz, _db, CancellationToken.None);

        var count = await _db.EmailOptOuts.CountAsync(e => e.AdminId == _admin.Id);
        Assert.Equal(3, count);
    }

    // ── ExportSession ───────────────────────────────────────────────

    [Fact]
    public async Task ExportSession_CreatesExport()
    {
        var session = new Session
        {
            ProjectId = _project.Id, SecureId = "sess-export",
            Fingerprint = "123", City = "", State = "", Country = "",
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var result = await _mutation.ExportSession(
            "sess-export", _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var export = await _db.SessionExports
            .FirstOrDefaultAsync(e => e.SessionId == session.Id);
        Assert.NotNull(export);
        Assert.Equal("mp4", export!.Type);
    }

    [Fact]
    public async Task ExportSession_ReExport_ResetsUrlAndError()
    {
        var session = new Session
        {
            ProjectId = _project.Id, SecureId = "sess-reexport",
            Fingerprint = "123", City = "", State = "", Country = "",
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        // Create initial export with URL
        _db.SessionExports.Add(new SessionExport
        {
            SessionId = session.Id, Type = "mp4",
            Url = "https://old.url/video.mp4", Error = "some error",
        });
        await _db.SaveChangesAsync();

        // Re-export should reset
        await _mutation.ExportSession(
            "sess-reexport", _principal, _authz, _db, CancellationToken.None);

        var export = await _db.SessionExports
            .FirstAsync(e => e.SessionId == session.Id && e.Type == "mp4");
        Assert.Null(export.Url);
        Assert.Null(export.Error);
    }

    [Fact]
    public async Task ExportSession_NonexistentSession_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.ExportSession(
                "nonexistent", _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task ExportSession_Idempotent()
    {
        var session = new Session
        {
            ProjectId = _project.Id, SecureId = "sess-idem",
            Fingerprint = "123", City = "", State = "", Country = "",
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        await _mutation.ExportSession("sess-idem", _principal, _authz, _db, CancellationToken.None);
        await _mutation.ExportSession("sess-idem", _principal, _authz, _db, CancellationToken.None);

        var count = await _db.SessionExports
            .CountAsync(e => e.SessionId == session.Id && e.Type == "mp4");
        Assert.Equal(1, count);
    }

    // ── EditProjectPlatforms ────────────────────────────────────────

    [Fact]
    public async Task EditProjectPlatforms_SetsPlatforms()
    {
        var result = await _mutation.EditProjectPlatforms(
            _project.Id, "javascript,python,go",
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var project = await _db.Projects.FindAsync(_project.Id);
        Assert.NotNull(project!.Platforms);
        Assert.Equal(3, project.Platforms!.Count);
        Assert.Contains("javascript", project.Platforms);
        Assert.Contains("python", project.Platforms);
        Assert.Contains("go", project.Platforms);
    }

    [Fact]
    public async Task EditProjectPlatforms_EmptyString_ClearsPlatforms()
    {
        var result = await _mutation.EditProjectPlatforms(
            _project.Id, "",
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var project = await _db.Projects.FindAsync(_project.Id);
        Assert.NotNull(project!.Platforms);
        Assert.Empty(project.Platforms!);
    }

    [Fact]
    public async Task EditProjectPlatforms_TrimsWhitespace()
    {
        await _mutation.EditProjectPlatforms(
            _project.Id, " javascript , python , go ",
            _principal, _authz, _db, CancellationToken.None);

        var project = await _db.Projects.FindAsync(_project.Id);
        Assert.Contains("javascript", project!.Platforms!);
        Assert.Contains("python", project.Platforms!);
        Assert.Contains("go", project.Platforms!);
    }

    [Fact]
    public async Task EditProjectPlatforms_NonexistentProject_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.EditProjectPlatforms(
                99999, "javascript",
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task EditProjectPlatforms_SinglePlatform()
    {
        await _mutation.EditProjectPlatforms(
            _project.Id, "javascript",
            _principal, _authz, _db, CancellationToken.None);

        var project = await _db.Projects.FindAsync(_project.Id);
        Assert.Single(project!.Platforms!);
        Assert.Equal("javascript", project.Platforms![0]);
    }

    [Fact]
    public async Task EditProjectPlatforms_OverwritesPrevious()
    {
        await _mutation.EditProjectPlatforms(
            _project.Id, "javascript,python",
            _principal, _authz, _db, CancellationToken.None);

        await _mutation.EditProjectPlatforms(
            _project.Id, "go,rust",
            _principal, _authz, _db, CancellationToken.None);

        var project = await _db.Projects.FindAsync(_project.Id);
        Assert.Equal(2, project!.Platforms!.Count);
        Assert.Contains("go", project.Platforms!);
        Assert.Contains("rust", project.Platforms!);
        Assert.DoesNotContain("javascript", project.Platforms!);
    }
}
