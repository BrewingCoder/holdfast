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
/// Tests for session management and admin profile mutations.
/// </summary>
public class PrivateMutationSessionMgmtTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateMutation _mutation;
    private readonly ClaimsPrincipal _principal;
    private readonly Admin _admin;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public PrivateMutationSessionMgmtTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();
        _mutation = new PrivateMutation();

        _admin = new Admin { Uid = "admin-1", Email = "admin@test.com", Name = "Test Admin" };
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

    // ── MarkSessionAsViewed ───────────────────────────────────────────

    [Fact]
    public async Task MarkSessionAsViewed_Success()
    {
        var session = new Session
        {
            ProjectId = _project.Id, SecureId = "sess-1",
            Fingerprint = "123", City = "", State = "", Country = "",
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var result = await _mutation.MarkSessionAsViewed(
            "sess-1", _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var view = await _db.SessionAdminsViews
            .FirstOrDefaultAsync(v => v.SessionId == session.Id && v.AdminId == _admin.Id);
        Assert.NotNull(view);
    }

    [Fact]
    public async Task MarkSessionAsViewed_Idempotent()
    {
        var session = new Session
        {
            ProjectId = _project.Id, SecureId = "sess-idem",
            Fingerprint = "123", City = "", State = "", Country = "",
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        await _mutation.MarkSessionAsViewed("sess-idem", _principal, _authz, _db, CancellationToken.None);
        await _mutation.MarkSessionAsViewed("sess-idem", _principal, _authz, _db, CancellationToken.None);

        var views = await _db.SessionAdminsViews
            .Where(v => v.SessionId == session.Id && v.AdminId == _admin.Id)
            .CountAsync();
        Assert.Equal(1, views); // Only one view record
    }

    [Fact]
    public async Task MarkSessionAsViewed_NonexistentSession_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.MarkSessionAsViewed("nonexistent", _principal, _authz, _db, CancellationToken.None));
    }

    // ── UpdateSessionIsPublic ─────────────────────────────────────────

    [Fact]
    public async Task UpdateSessionIsPublic_SetTrue()
    {
        var session = new Session
        {
            ProjectId = _project.Id, SecureId = "sess-pub",
            Fingerprint = "123", City = "", State = "", Country = "",
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var updated = await _mutation.UpdateSessionIsPublic(
            "sess-pub", true, _principal, _authz, _db, CancellationToken.None);

        Assert.True(updated.Starred);
    }

    [Fact]
    public async Task UpdateSessionIsPublic_SetFalse()
    {
        var session = new Session
        {
            ProjectId = _project.Id, SecureId = "sess-priv",
            Fingerprint = "123", City = "", State = "", Country = "",
            Starred = true,
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var updated = await _mutation.UpdateSessionIsPublic(
            "sess-priv", false, _principal, _authz, _db, CancellationToken.None);

        Assert.False(updated.Starred);
    }

    [Fact]
    public async Task UpdateSessionIsPublic_Nonexistent_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateSessionIsPublic("nope", true, _principal, _authz, _db, CancellationToken.None));
    }

    // ── DeleteSessions ────────────────────────────────────────────────

    [Fact]
    public async Task DeleteSessions_CreatesTask()
    {
        var result = await _mutation.DeleteSessions(
            _project.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var task = await _db.DeleteSessionsTasks.FirstOrDefaultAsync(t => t.ProjectId == _project.Id);
        Assert.NotNull(task);
    }

    [Fact]
    public async Task DeleteSessions_NonexistentProject_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteSessions(99999, _principal, _authz, _db, CancellationToken.None));
    }

    // ── UpdateAdminAboutYouDetails ────────────────────────────────────

    [Fact]
    public async Task UpdateAdminAboutYouDetails_UpdatesName()
    {
        var updated = await _mutation.UpdateAdminAboutYouDetails(
            "Updated Name", null, null, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("Updated Name", updated.Name);
    }

    [Fact]
    public async Task UpdateAdminAboutYouDetails_UpdatesAllFields()
    {
        var updated = await _mutation.UpdateAdminAboutYouDetails(
            "New Name", "word-of-mouth", "Developer",
            _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("New Name", updated.Name);
    }

    [Fact]
    public async Task UpdateAdminAboutYouDetails_NullsLeaveUnchanged()
    {
        var updated = await _mutation.UpdateAdminAboutYouDetails(
            null, null, null, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal("Test Admin", updated.Name); // Original name preserved
    }

    // ── ChangeProjectMembership ───────────────────────────────────────

    [Fact]
    public async Task ChangeProjectMembership_AddProject()
    {
        // Create second admin with MEMBER role
        var admin2 = new Admin { Uid = "admin-2", Email = "admin2@test.com" };
        _db.Admins.Add(admin2);
        await _db.SaveChangesAsync();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = admin2.Id, WorkspaceId = _workspace.Id,
            Role = "MEMBER", ProjectIds = null,
        });
        await _db.SaveChangesAsync();

        var result = await _mutation.ChangeProjectMembership(
            _workspace.Id, admin2.Id, [_project.Id], _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var wa = await _db.WorkspaceAdmins
            .FirstAsync(x => x.AdminId == admin2.Id && x.WorkspaceId == _workspace.Id);
        Assert.Contains(_project.Id, wa.ProjectIds!);
    }

    // ── UpdateAllowedEmailOrigins ─────────────────────────────────────

    [Fact]
    public async Task UpdateAllowedEmailOrigins_SetsValues()
    {
        var result = await _mutation.UpdateAllowedEmailOrigins(
            _workspace.Id, ["@company.com", "@partner.com"], _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        Assert.Contains("@company.com", ws!.AllowedAutoJoinEmailOrigins!);
    }

    [Fact]
    public async Task UpdateAllowedEmailOrigins_EmptyList()
    {
        var result = await _mutation.UpdateAllowedEmailOrigins(
            _workspace.Id, [], _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        Assert.Equal("", ws!.AllowedAutoJoinEmailOrigins);
    }

    // ── SaveBillingPlan (retention periods for self-hosted) ─────────

    [Fact]
    public async Task SaveBillingPlan_SetsRetentionPeriods()
    {
        var result = await _mutation.SaveBillingPlan(
            _workspace.Id,
            RetentionPeriod.ThirtyDays,
            RetentionPeriod.ThreeMonths,
            RetentionPeriod.SixMonths,
            RetentionPeriod.TwelveMonths,
            RetentionPeriod.TwoYears,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var ws = await _db.Workspaces.FindAsync(_workspace.Id);
        Assert.Equal(RetentionPeriod.ThirtyDays, ws!.RetentionPeriod);
        Assert.Equal(RetentionPeriod.ThreeMonths, ws.ErrorsRetentionPeriod);
        Assert.Equal(RetentionPeriod.SixMonths, ws.LogsRetentionPeriod);
    }

    [Fact]
    public async Task SaveBillingPlan_NonexistentWorkspace_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.SaveBillingPlan(
                99999,
                RetentionPeriod.ThirtyDays,
                RetentionPeriod.ThirtyDays,
                RetentionPeriod.ThirtyDays,
                RetentionPeriod.ThirtyDays,
                RetentionPeriod.ThirtyDays,
                _principal, _authz, _db, CancellationToken.None));
    }
}
