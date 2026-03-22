using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoldFast.Shared.Tests;

/// <summary>
/// Tests for analytics queries (daily counts, live users, unprocessed sessions),
/// comment replies, comment muting, email opt-outs, error keys, and misc new queries/mutations.
/// </summary>
public class AnalyticsAndCommentTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly PrivateMutation _mutation;
    private readonly PrivateQuery _query;
    private readonly AuthorizationService _authz;
    private readonly ClaimsPrincipal _principal;
    private readonly Admin _admin;
    private readonly Project _project;

    private static ClaimsPrincipal MakePrincipal(string uid) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, uid),
            new Claim(HoldFastClaimTypes.Email, $"{uid}@test.com"),
        }, "Test"));

    public AnalyticsAndCommentTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _admin = new Admin { Uid = "analytics-uid", Name = "Analytics Admin", Email = "analytics@test.com" };
        _db.Admins.Add(_admin);
        var workspace = new Workspace { Name = "WS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(workspace);
        _db.SaveChanges();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin { AdminId = _admin.Id, WorkspaceId = workspace.Id, Role = "ADMIN" });
        _project = new Project { Name = "Proj", WorkspaceId = workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _authz = new AuthorizationService(_db);
        _principal = MakePrincipal("analytics-uid");
        _mutation = new PrivateMutation();
        _query = new PrivateQuery();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ── Unprocessed Sessions Count ───────────────────────────────────

    [Fact]
    public async Task GetUnprocessedSessionsCount_CountsRecent()
    {
        // Add recent unprocessed session
        _db.Sessions.Add(new Session
        {
            ProjectId = _project.Id,
            Processed = false,
            Excluded = false,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        });
        // Old session — should not count
        _db.Sessions.Add(new Session
        {
            ProjectId = _project.Id,
            Processed = false,
            Excluded = false,
            CreatedAt = DateTime.UtcNow.AddHours(-5)
        });
        // Processed session — should not count
        _db.Sessions.Add(new Session
        {
            ProjectId = _project.Id,
            Processed = true,
            Excluded = false,
            CreatedAt = DateTime.UtcNow.AddMinutes(-15)
        });
        await _db.SaveChangesAsync();

        var count = await _query.GetUnprocessedSessionsCount(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetUnprocessedSessionsCount_Empty_ReturnsZero()
    {
        var count = await _query.GetUnprocessedSessionsCount(
            _project.Id, _principal, _authz, _db, CancellationToken.None);
        Assert.Equal(0, count);
    }

    // ── Live Users Count ─────────────────────────────────────────────

    [Fact]
    public async Task GetLiveUsersCount_DistinctIdentifiers()
    {
        _db.Sessions.AddRange(
            new Session { ProjectId = _project.Id, Identifier = "user1", Processed = false, Excluded = false, CreatedAt = DateTime.UtcNow.AddMinutes(-10) },
            new Session { ProjectId = _project.Id, Identifier = "user1", Processed = false, Excluded = false, CreatedAt = DateTime.UtcNow.AddMinutes(-5) },
            new Session { ProjectId = _project.Id, Identifier = "user2", Processed = false, Excluded = false, CreatedAt = DateTime.UtcNow.AddMinutes(-2) });
        await _db.SaveChangesAsync();

        var count = await _query.GetLiveUsersCount(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, count);
    }

    // ── Daily Session Counts ─────────────────────────────────────────

    [Fact]
    public async Task GetDailySessionsCount_FiltersByDateRange()
    {
        _db.DailySessionCounts.AddRange(
            new DailySessionCount { ProjectId = _project.Id, Date = new DateTime(2026, 1, 1), Count = 10 },
            new DailySessionCount { ProjectId = _project.Id, Date = new DateTime(2026, 1, 2), Count = 20 },
            new DailySessionCount { ProjectId = _project.Id, Date = new DateTime(2026, 1, 3), Count = 30 },
            new DailySessionCount { ProjectId = _project.Id, Date = new DateTime(2026, 1, 10), Count = 5 });
        await _db.SaveChangesAsync();

        var results = await _query.GetDailySessionsCount(
            _project.Id, new DateTime(2026, 1, 1), new DateTime(2026, 1, 3),
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.Equal(10, results[0].Count);
        Assert.Equal(30, results[2].Count);
    }

    [Fact]
    public async Task GetDailySessionsCount_Empty_ReturnsEmptyList()
    {
        var results = await _query.GetDailySessionsCount(
            _project.Id, DateTime.UtcNow, DateTime.UtcNow,
            _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(results);
    }

    // ── Daily Error Counts ───────────────────────────────────────────

    [Fact]
    public async Task GetDailyErrorsCount_FiltersByDateRange()
    {
        _db.DailyErrorCounts.AddRange(
            new DailyErrorCount { ProjectId = _project.Id, Date = new DateTime(2026, 2, 1), Count = 5 },
            new DailyErrorCount { ProjectId = _project.Id, Date = new DateTime(2026, 2, 2), Count = 15 });
        await _db.SaveChangesAsync();

        var results = await _query.GetDailyErrorsCount(
            _project.Id, new DateTime(2026, 2, 1), new DateTime(2026, 2, 2),
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(2, results.Count);
    }

    // ── Workspace Admins By Project ──────────────────────────────────

    [Fact]
    public async Task GetWorkspaceAdminsByProjectId_ReturnsAdmins()
    {
        var results = await _query.GetWorkspaceAdminsByProjectId(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(_admin.Id, results[0].Admin.Id);
    }

    // ── Comment Replies ──────────────────────────────────────────────

    [Fact]
    public async Task ReplyToErrorComment_CreatesReply()
    {
        var errorGroup = new ErrorGroup
        {
            ProjectId = _project.Id,
            Event = "test error",
            State = ErrorGroupState.Open,
            Type = "RuntimeError"
        };
        _db.ErrorGroups.Add(errorGroup);
        await _db.SaveChangesAsync();

        var comment = new ErrorComment { ErrorGroupId = errorGroup.Id, AdminId = _admin.Id, Text = "Original" };
        _db.ErrorComments.Add(comment);
        await _db.SaveChangesAsync();

        var reply = await _mutation.ReplyToErrorComment(
            comment.Id, "My reply", _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("My reply", reply.Text);
        Assert.Equal(_admin.Id, reply.AdminId);
        Assert.Equal(comment.Id, reply.ErrorCommentId);

        var dbReply = await _db.CommentReplies.FirstAsync();
        Assert.Equal("My reply", dbReply.Text);
    }

    [Fact]
    public async Task ReplyToErrorComment_NotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.ReplyToErrorComment(9999, "text", _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task ReplyToErrorComment_MultipleReplies()
    {
        var errorGroup = new ErrorGroup
        {
            ProjectId = _project.Id,
            Event = "error",
            State = ErrorGroupState.Open,
            Type = "Error"
        };
        _db.ErrorGroups.Add(errorGroup);
        await _db.SaveChangesAsync();

        var comment = new ErrorComment { ErrorGroupId = errorGroup.Id, AdminId = _admin.Id, Text = "Base" };
        _db.ErrorComments.Add(comment);
        await _db.SaveChangesAsync();

        await _mutation.ReplyToErrorComment(comment.Id, "Reply 1", _principal, _authz, _db, CancellationToken.None);
        await _mutation.ReplyToErrorComment(comment.Id, "Reply 2", _principal, _authz, _db, CancellationToken.None);
        await _mutation.ReplyToErrorComment(comment.Id, "Reply 3", _principal, _authz, _db, CancellationToken.None);

        var replies = await _db.CommentReplies.Where(r => r.ErrorCommentId == comment.Id).ToListAsync();
        Assert.Equal(3, replies.Count);
    }

    // ── Comment Muting ───────────────────────────────────────────────

    [Fact]
    public async Task MuteErrorCommentThread_CreatesFollower()
    {
        var errorGroup = new ErrorGroup
        {
            ProjectId = _project.Id, Event = "e", State = ErrorGroupState.Open, Type = "T"
        };
        _db.ErrorGroups.Add(errorGroup);
        await _db.SaveChangesAsync();

        var comment = new ErrorComment { ErrorGroupId = errorGroup.Id, AdminId = _admin.Id, Text = "C" };
        _db.ErrorComments.Add(comment);
        await _db.SaveChangesAsync();

        var result = await _mutation.MuteErrorCommentThread(
            comment.Id, true, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result);

        var follower = await _db.CommentFollowers.FirstAsync();
        Assert.True(follower.HasMuted);
        Assert.Equal(_admin.Id, follower.AdminId);
    }

    [Fact]
    public async Task MuteErrorCommentThread_UnmuteExisting()
    {
        var errorGroup = new ErrorGroup
        {
            ProjectId = _project.Id, Event = "e", State = ErrorGroupState.Open, Type = "T"
        };
        _db.ErrorGroups.Add(errorGroup);
        await _db.SaveChangesAsync();

        var comment = new ErrorComment { ErrorGroupId = errorGroup.Id, AdminId = _admin.Id, Text = "C" };
        _db.ErrorComments.Add(comment);
        await _db.SaveChangesAsync();

        // Mute first
        await _mutation.MuteErrorCommentThread(comment.Id, true, _principal, _authz, _db, CancellationToken.None);
        // Unmute
        await _mutation.MuteErrorCommentThread(comment.Id, false, _principal, _authz, _db, CancellationToken.None);

        var follower = await _db.CommentFollowers.FirstAsync();
        Assert.False(follower.HasMuted);
        // Should be exactly one follower record, not two
        Assert.Equal(1, await _db.CommentFollowers.CountAsync());
    }

    [Fact]
    public async Task MuteSessionCommentThread_CreatesFollower()
    {
        var session = new Session { ProjectId = _project.Id };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var comment = new SessionComment
        {
            ProjectId = _project.Id, SessionId = session.Id, AdminId = _admin.Id, Text = "SC"
        };
        _db.SessionComments.Add(comment);
        await _db.SaveChangesAsync();

        var result = await _mutation.MuteSessionCommentThread(
            comment.Id, true, _principal, _authz, _db, CancellationToken.None);
        Assert.True(result);

        var follower = await _db.CommentFollowers.FirstAsync();
        Assert.True(follower.HasMuted);
        Assert.Equal(comment.Id, follower.SessionCommentId);
    }

    // ── Email Opt-Out ────────────────────────────────────────────────

    [Fact]
    public async Task UpdateEmailOptOut_OptOut_CreatesRecord()
    {
        await _mutation.UpdateEmailOptOut("alerts", true, _principal, _authz, _db, CancellationToken.None);

        var optOuts = await _db.EmailOptOuts.Where(e => e.AdminId == _admin.Id).ToListAsync();
        Assert.Single(optOuts);
        Assert.Equal("alerts", optOuts[0].Category);
    }

    [Fact]
    public async Task UpdateEmailOptOut_OptIn_RemovesRecord()
    {
        _db.EmailOptOuts.Add(new EmailOptOut { AdminId = _admin.Id, Category = "alerts" });
        await _db.SaveChangesAsync();

        await _mutation.UpdateEmailOptOut("alerts", false, _principal, _authz, _db, CancellationToken.None);

        Assert.Empty(await _db.EmailOptOuts.Where(e => e.AdminId == _admin.Id).ToListAsync());
    }

    [Fact]
    public async Task UpdateEmailOptOut_DoubleOptOut_NoDuplicate()
    {
        await _mutation.UpdateEmailOptOut("alerts", true, _principal, _authz, _db, CancellationToken.None);
        await _mutation.UpdateEmailOptOut("alerts", true, _principal, _authz, _db, CancellationToken.None);

        var count = await _db.EmailOptOuts.CountAsync(e => e.AdminId == _admin.Id && e.Category == "alerts");
        // Second opt-out is a no-op since already opted out
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetEmailOptOuts_ReturnsAdminOptOuts()
    {
        _db.EmailOptOuts.AddRange(
            new EmailOptOut { AdminId = _admin.Id, Category = "alerts" },
            new EmailOptOut { AdminId = _admin.Id, Category = "digest" });
        await _db.SaveChangesAsync();

        var results = await _query.GetEmailOptOuts(_principal, _authz, _db, CancellationToken.None);
        Assert.Equal(2, results.Count);
    }

    // ── Workspaces Count ─────────────────────────────────────────────

    [Fact]
    public async Task GetWorkspacesCount_ReturnsCount()
    {
        var count = await _query.GetWorkspacesCount(_principal, _authz, _db, CancellationToken.None);
        Assert.Equal(1, count);
    }

    // ── Error Keys (Static) ─────────────────────────────────────────

    [Fact]
    public async Task GetErrorsKeys_ReturnsReservedKeys()
    {
        var keys = await _query.GetErrorsKeys(
            _project.Id, null, _principal, _authz, CancellationToken.None);

        Assert.True(keys.Count >= 10);
        Assert.Contains(keys, k => k.Name == "browser");
        Assert.Contains(keys, k => k.Name == "environment");
        Assert.Contains(keys, k => k.Name == "service_name");
    }

    [Fact]
    public async Task GetErrorsKeys_FiltersWithQuery()
    {
        var keys = await _query.GetErrorsKeys(
            _project.Id, "service", _principal, _authz, CancellationToken.None);

        Assert.All(keys, k => Assert.Contains("service", k.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetErrorsKeys_NoMatch_ReturnsEmpty()
    {
        var keys = await _query.GetErrorsKeys(
            _project.Id, "zzz_nonexistent_zzz", _principal, _authz, CancellationToken.None);

        Assert.Empty(keys);
    }

    // ── Update Error Tags (no-op) ────────────────────────────────────

    [Fact]
    public async Task UpdateErrorTags_ReturnsTrue()
    {
        var result = await _mutation.UpdateErrorTags(_principal, _authz, _db, CancellationToken.None);
        Assert.True(result);
    }

    // ── Edit Project Platforms ────────────────────────────────────────

    [Fact]
    public async Task EditProjectPlatforms_UpdatesPlatforms()
    {
        await _mutation.EditProjectPlatforms(
            _project.Id, "web,mobile,backend", _principal, _authz, _db, CancellationToken.None);

        var project = await _db.Projects.FindAsync(_project.Id);
        Assert.Equal(3, project!.Platforms.Count);
        Assert.Contains("web", project.Platforms);
        Assert.Contains("mobile", project.Platforms);
        Assert.Contains("backend", project.Platforms);
    }

    [Fact]
    public async Task EditProjectPlatforms_EmptyString_ClearsPlatforms()
    {
        _project.Platforms = ["web", "mobile"];
        await _db.SaveChangesAsync();

        await _mutation.EditProjectPlatforms(
            _project.Id, "", _principal, _authz, _db, CancellationToken.None);

        var project = await _db.Projects.FindAsync(_project.Id);
        Assert.Empty(project!.Platforms);
    }

    [Fact]
    public async Task EditProjectPlatforms_NotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.EditProjectPlatforms(9999, "web", _principal, _authz, _db, CancellationToken.None));
    }
}
