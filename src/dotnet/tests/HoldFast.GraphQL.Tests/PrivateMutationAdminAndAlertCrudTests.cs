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
/// Tests for admin management mutations (AddAdminToWorkspace, ChangeAdminRole,
/// DeleteAdminFromWorkspace, MarkSessionAsViewed) and legacy alert CRUD
/// (ErrorAlert, SessionAlert, LogAlert delete/disable/update).
/// </summary>
public class PrivateMutationAdminAndAlertCrudTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateMutation _mutation;

    private readonly Admin _admin;
    private readonly ClaimsPrincipal _adminPrincipal;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public PrivateMutationAdminAndAlertCrudTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();
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
        _adminPrincipal = MakePrincipal(_admin);
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

    // ── AddAdminToWorkspace ──────────────────────────────────────────

    [Fact]
    public async Task AddAdminToWorkspace_CreatesWorkspaceAdmin()
    {
        var newAdmin = new Admin { Uid = "new-uid", Email = "new@test.com" };
        _db.Admins.Add(newAdmin);
        await _db.SaveChangesAsync();

        var result = await _mutation.AddAdminToWorkspace(
            _workspace.Id, newAdmin.Id, "MEMBER",
            _adminPrincipal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var wa = await _db.WorkspaceAdmins
            .FirstOrDefaultAsync(wa => wa.AdminId == newAdmin.Id && wa.WorkspaceId == _workspace.Id);
        Assert.NotNull(wa);
        Assert.Equal("MEMBER", wa!.Role);
    }

    [Fact]
    public async Task AddAdminToWorkspace_MemberCannotAdd_Throws()
    {
        var member = new Admin { Uid = "member-uid", Email = "member@test.com" };
        _db.Admins.Add(member);
        await _db.SaveChangesAsync();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = member.Id, WorkspaceId = _workspace.Id, Role = "MEMBER" });
        await _db.SaveChangesAsync();

        var target = new Admin { Uid = "target-uid", Email = "target@test.com" };
        _db.Admins.Add(target);
        await _db.SaveChangesAsync();

        var memberPrincipal = MakePrincipal(member);

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.AddAdminToWorkspace(
                _workspace.Id, target.Id, "MEMBER",
                memberPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── ChangeAdminRole ──────────────────────────────────────────────

    [Fact]
    public async Task ChangeAdminRole_UpdatesRole()
    {
        var member = new Admin { Uid = "m-uid", Email = "m@test.com" };
        _db.Admins.Add(member);
        await _db.SaveChangesAsync();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = member.Id, WorkspaceId = _workspace.Id, Role = "MEMBER" });
        await _db.SaveChangesAsync();

        var result = await _mutation.ChangeAdminRole(
            _workspace.Id, member.Id, "ADMIN",
            _adminPrincipal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var wa = await _db.WorkspaceAdmins
            .FirstAsync(wa => wa.AdminId == member.Id && wa.WorkspaceId == _workspace.Id);
        Assert.Equal("ADMIN", wa.Role);
    }

    [Fact]
    public async Task ChangeAdminRole_NonExistentAdmin_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.ChangeAdminRole(
                _workspace.Id, 99999, "ADMIN",
                _adminPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── DeleteAdminFromWorkspace ─────────────────────────────────────

    [Fact]
    public async Task DeleteAdminFromWorkspace_RemovesMembership()
    {
        var member = new Admin { Uid = "del-uid", Email = "del@test.com" };
        _db.Admins.Add(member);
        await _db.SaveChangesAsync();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = member.Id, WorkspaceId = _workspace.Id, Role = "MEMBER" });
        await _db.SaveChangesAsync();

        var result = await _mutation.DeleteAdminFromWorkspace(
            _workspace.Id, member.Id,
            _adminPrincipal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var exists = await _db.WorkspaceAdmins
            .AnyAsync(wa => wa.AdminId == member.Id && wa.WorkspaceId == _workspace.Id);
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteAdminFromWorkspace_NonExistent_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteAdminFromWorkspace(
                _workspace.Id, 99999,
                _adminPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── MarkSessionAsViewed ──────────────────────────────────────────

    [Fact]
    public async Task MarkSessionAsViewed_CreatesViewRecord()
    {
        var session = new Session { SecureId = "sess-view", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var result = await _mutation.MarkSessionAsViewed(
            "sess-view", _adminPrincipal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var view = await _db.SessionAdminsViews
            .FirstOrDefaultAsync(v => v.SessionId == session.Id && v.AdminId == _admin.Id);
        Assert.NotNull(view);
    }

    [Fact]
    public async Task MarkSessionAsViewed_Idempotent_DoesNotDuplicate()
    {
        var session = new Session { SecureId = "sess-view2", ProjectId = _project.Id, CreatedAt = DateTime.UtcNow };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        await _mutation.MarkSessionAsViewed("sess-view2", _adminPrincipal, _authz, _db, CancellationToken.None);
        await _mutation.MarkSessionAsViewed("sess-view2", _adminPrincipal, _authz, _db, CancellationToken.None);

        var count = await _db.SessionAdminsViews
            .CountAsync(v => v.SessionId == session.Id && v.AdminId == _admin.Id);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task MarkSessionAsViewed_NotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.MarkSessionAsViewed(
                "nonexistent", _adminPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── DeleteErrorAlert ─────────────────────────────────────────────

    [Fact]
    public async Task DeleteErrorAlert_RemovesAlert()
    {
        var alert = new ErrorAlert { ProjectId = _project.Id, Name = "ToDelete", CountThreshold = 5 };
        _db.ErrorAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _mutation.DeleteErrorAlert(
            _project.Id, alert.Id,
            _adminPrincipal, _authz, _db, CancellationToken.None);

        Assert.Equal("ToDelete", result.Name);
        Assert.False(await _db.ErrorAlerts.AnyAsync(a => a.Id == alert.Id));
    }

    [Fact]
    public async Task DeleteErrorAlert_NotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteErrorAlert(
                _project.Id, 99999,
                _adminPrincipal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteErrorAlert_WrongProject_Throws()
    {
        var otherProj = new Project { Name = "Other", WorkspaceId = _workspace.Id };
        _db.Projects.Add(otherProj);
        await _db.SaveChangesAsync();

        var alert = new ErrorAlert { ProjectId = otherProj.Id, Name = "Other", CountThreshold = 1 };
        _db.ErrorAlerts.Add(alert);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteErrorAlert(
                _project.Id, alert.Id,
                _adminPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── UpdateErrorAlertIsDisabled ───────────────────────────────────

    [Fact]
    public async Task UpdateErrorAlertIsDisabled_TogglesDisabled()
    {
        var alert = new ErrorAlert { ProjectId = _project.Id, Name = "Toggle", CountThreshold = 5, Disabled = false };
        _db.ErrorAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateErrorAlertIsDisabled(
            alert.Id, _project.Id, true,
            _adminPrincipal, _authz, _db, CancellationToken.None);

        Assert.True(result.Disabled);

        result = await _mutation.UpdateErrorAlertIsDisabled(
            alert.Id, _project.Id, false,
            _adminPrincipal, _authz, _db, CancellationToken.None);

        Assert.False(result.Disabled);
    }

    [Fact]
    public async Task UpdateErrorAlertIsDisabled_NotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateErrorAlertIsDisabled(
                99999, _project.Id, true,
                _adminPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── UpdateErrorAlert ─────────────────────────────────────────────

    [Fact]
    public async Task UpdateErrorAlert_UpdatesAllFields()
    {
        var alert = new ErrorAlert { ProjectId = _project.Id, Name = "Original", CountThreshold = 1, ThresholdWindow = 60, Frequency = 300 };
        _db.ErrorAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateErrorAlert(
            _project.Id, alert.Id,
            "Renamed", 10, 120, "service:api", true, 600,
            _adminPrincipal, _authz, _db, CancellationToken.None);

        Assert.Equal("Renamed", result.Name);
        Assert.Equal(10, result.CountThreshold);
        Assert.Equal(120, result.ThresholdWindow);
        Assert.Equal("service:api", result.Query);
        Assert.True(result.Disabled);
        Assert.Equal(600, result.Frequency);
        Assert.Equal(_admin.Id, result.LastAdminToEditId);
    }

    [Fact]
    public async Task UpdateErrorAlert_PartialUpdate_OnlyChangesProvided()
    {
        var alert = new ErrorAlert { ProjectId = _project.Id, Name = "Keep", CountThreshold = 5, ThresholdWindow = 60 };
        _db.ErrorAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateErrorAlert(
            _project.Id, alert.Id,
            null, 20, null, null, null, null,
            _adminPrincipal, _authz, _db, CancellationToken.None);

        Assert.Equal("Keep", result.Name); // unchanged
        Assert.Equal(20, result.CountThreshold); // changed
        Assert.Equal(60, result.ThresholdWindow); // unchanged
    }

    // ── DeleteSessionAlert ───────────────────────────────────────────

    [Fact]
    public async Task DeleteSessionAlert_RemovesAlert()
    {
        var alert = new SessionAlert { ProjectId = _project.Id, Name = "SA-Del", CountThreshold = 3, Type = "NEW_USER" };
        _db.SessionAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _mutation.DeleteSessionAlert(
            _project.Id, alert.Id,
            _adminPrincipal, _authz, _db, CancellationToken.None);

        Assert.Equal("SA-Del", result.Name);
        Assert.False(await _db.SessionAlerts.AnyAsync(a => a.Id == alert.Id));
    }

    [Fact]
    public async Task DeleteSessionAlert_NotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteSessionAlert(
                _project.Id, 99999,
                _adminPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── UpdateSessionAlertIsDisabled ─────────────────────────────────

    [Fact]
    public async Task UpdateSessionAlertIsDisabled_TogglesDisabled()
    {
        var alert = new SessionAlert { ProjectId = _project.Id, Name = "SA-Toggle", CountThreshold = 3, Type = "RAGE_CLICK", Disabled = false };
        _db.SessionAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateSessionAlertIsDisabled(
            alert.Id, _project.Id, true,
            _adminPrincipal, _authz, _db, CancellationToken.None);

        Assert.True(result.Disabled);
    }

    // ── UpdateSessionAlert ───────────────────────────────────────────

    [Fact]
    public async Task UpdateSessionAlert_UpdatesFields()
    {
        var alert = new SessionAlert { ProjectId = _project.Id, Name = "Orig", CountThreshold = 1, Type = "NEW_USER" };
        _db.SessionAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateSessionAlert(
            alert.Id, _project.Id,
            "Updated", 10, 120, "env:prod", true,
            _adminPrincipal, _authz, _db, CancellationToken.None);

        Assert.Equal("Updated", result.Name);
        Assert.Equal(10, result.CountThreshold);
        Assert.Equal(120, result.ThresholdWindow);
        Assert.Equal("env:prod", result.Query);
        Assert.True(result.Disabled);
        Assert.Equal(_admin.Id, result.LastAdminToEditId);
    }

    [Fact]
    public async Task UpdateSessionAlert_NotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateSessionAlert(
                99999, _project.Id,
                "X", null, null, null, null,
                _adminPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── DeleteLogAlert ───────────────────────────────────────────────

    [Fact]
    public async Task DeleteLogAlert_RemovesAlert()
    {
        var alert = new LogAlert { ProjectId = _project.Id, Name = "LA-Del", CountThreshold = 100 };
        _db.LogAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _mutation.DeleteLogAlert(
            _project.Id, alert.Id,
            _adminPrincipal, _authz, _db, CancellationToken.None);

        Assert.Equal("LA-Del", result.Name);
        Assert.False(await _db.LogAlerts.AnyAsync(a => a.Id == alert.Id));
    }

    [Fact]
    public async Task DeleteLogAlert_NotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteLogAlert(
                _project.Id, 99999,
                _adminPrincipal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteLogAlert_WrongProject_Throws()
    {
        var otherProj = new Project { Name = "Other", WorkspaceId = _workspace.Id };
        _db.Projects.Add(otherProj);
        await _db.SaveChangesAsync();

        var alert = new LogAlert { ProjectId = otherProj.Id, Name = "Other", CountThreshold = 50 };
        _db.LogAlerts.Add(alert);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteLogAlert(
                _project.Id, alert.Id,
                _adminPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── UpdateLogAlertIsDisabled ─────────────────────────────────────

    [Fact]
    public async Task UpdateLogAlertIsDisabled_TogglesDisabled()
    {
        var alert = new LogAlert { ProjectId = _project.Id, Name = "LA-Toggle", CountThreshold = 50, Disabled = false };
        _db.LogAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateLogAlertIsDisabled(
            alert.Id, _project.Id, true,
            _adminPrincipal, _authz, _db, CancellationToken.None);

        Assert.True(result.Disabled);

        result = await _mutation.UpdateLogAlertIsDisabled(
            alert.Id, _project.Id, false,
            _adminPrincipal, _authz, _db, CancellationToken.None);

        Assert.False(result.Disabled);
    }

    // ── UpdateLogAlert ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateLogAlert_UpdatesAllFields()
    {
        var alert = new LogAlert { ProjectId = _project.Id, Name = "LA-Orig", CountThreshold = 10 };
        _db.LogAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateLogAlert(
            alert.Id, _project.Id,
            "LA-Updated", 50, 300, 1, "level:error", true,
            _adminPrincipal, _authz, _db, CancellationToken.None);

        Assert.Equal("LA-Updated", result.Name);
        Assert.Equal(50, result.CountThreshold);
        Assert.Equal(300, result.ThresholdWindow);
        Assert.Equal(1, result.BelowThreshold);
        Assert.Equal("level:error", result.Query);
        Assert.True(result.Disabled);
        Assert.Equal(_admin.Id, result.LastAdminToEditId);
    }

    [Fact]
    public async Task UpdateLogAlert_PartialUpdate()
    {
        var alert = new LogAlert { ProjectId = _project.Id, Name = "Keep", CountThreshold = 10, ThresholdWindow = 60 };
        _db.LogAlerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _mutation.UpdateLogAlert(
            alert.Id, _project.Id,
            null, null, null, null, "new-query", null,
            _adminPrincipal, _authz, _db, CancellationToken.None);

        Assert.Equal("Keep", result.Name);
        Assert.Equal(10, result.CountThreshold);
        Assert.Equal("new-query", result.Query);
    }

    [Fact]
    public async Task UpdateLogAlert_NotFound_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateLogAlert(
                99999, _project.Id,
                "X", null, null, null, null, null,
                _adminPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── UpdateErrorTags (no-op placeholder) ──────────────────────────

    [Fact]
    public async Task UpdateErrorTags_ReturnsTrue()
    {
        var result = await _mutation.UpdateErrorTags(
            _adminPrincipal, _authz, _db, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task UpdateErrorTags_Anonymous_Throws()
    {
        var anon = new ClaimsPrincipal(new ClaimsIdentity());

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateErrorTags(anon, _authz, _db, CancellationToken.None));
    }
}
