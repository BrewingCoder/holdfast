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
/// Tests for workspace invite and admin management mutations:
/// CreateWorkspaceInviteLink, AcceptWorkspaceInvite, DeleteWorkspaceInviteLink,
/// SendAdminWorkspaceInvite, JoinWorkspace, AddAdminToWorkspace,
/// ChangeAdminRole, DeleteAdminFromWorkspace.
/// </summary>
public class PrivateMutationWorkspaceInviteTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateMutation _mutation;
    private readonly ClaimsPrincipal _principal;
    private readonly Admin _admin;
    private readonly Workspace _workspace;

    public PrivateMutationWorkspaceInviteTests()
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

    // ── CreateWorkspaceInviteLink ───────────────────────────────────

    [Fact]
    public async Task CreateWorkspaceInviteLink_Success()
    {
        var invite = await _mutation.CreateWorkspaceInviteLink(
            _workspace.Id, "invitee@test.com", "MEMBER", null, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(invite);
        Assert.Equal("invitee@test.com", invite.InviteeEmail);
        Assert.Equal("MEMBER", invite.InviteeRole);
        Assert.NotNull(invite.Secret);
        Assert.NotNull(invite.ExpirationDate);
        // Default 7-day expiry
        Assert.True(invite.ExpirationDate!.Value > DateTime.UtcNow.AddDays(6));
    }

    [Fact]
    public async Task CreateWorkspaceInviteLink_CustomExpiration()
    {
        var invite = await _mutation.CreateWorkspaceInviteLink(
            _workspace.Id, "invitee@test.com", "ADMIN", null, 30,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(invite.ExpirationDate!.Value > DateTime.UtcNow.AddDays(29));
    }

    [Fact]
    public async Task CreateWorkspaceInviteLink_WithProjectIds()
    {
        var invite = await _mutation.CreateWorkspaceInviteLink(
            _workspace.Id, "invitee@test.com", "MEMBER", [1, 2, 3], null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(invite.ProjectIds);
        Assert.Equal(3, invite.ProjectIds!.Count);
    }

    [Fact]
    public async Task CreateWorkspaceInviteLink_DuplicateInvite_Throws()
    {
        await _mutation.CreateWorkspaceInviteLink(
            _workspace.Id, "dup@test.com", "MEMBER", null, null,
            _principal, _authz, _db, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateWorkspaceInviteLink(
                _workspace.Id, "dup@test.com", "MEMBER", null, null,
                _principal, _authz, _db, CancellationToken.None));

        Assert.Contains("active invite already exists", ex.Message);
    }

    [Fact]
    public async Task CreateWorkspaceInviteLink_AlreadyMember_Throws()
    {
        // admin@test.com is already a member
        var ex = await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateWorkspaceInviteLink(
                _workspace.Id, "admin@test.com", "MEMBER", null, null,
                _principal, _authz, _db, CancellationToken.None));

        Assert.Contains("already a member", ex.Message);
    }

    [Fact]
    public async Task CreateWorkspaceInviteLink_SecretIsUnique()
    {
        var inv1 = await _mutation.CreateWorkspaceInviteLink(
            _workspace.Id, "a@test.com", "MEMBER", null, null,
            _principal, _authz, _db, CancellationToken.None);
        var inv2 = await _mutation.CreateWorkspaceInviteLink(
            _workspace.Id, "b@test.com", "MEMBER", null, null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.NotEqual(inv1.Secret, inv2.Secret);
    }

    // ── AcceptWorkspaceInvite ───────────────────────────────────────

    [Fact]
    public async Task AcceptWorkspaceInvite_Success()
    {
        // Create a second admin who will accept
        var admin2 = new Admin { Uid = "admin-2", Email = "admin2@test.com" };
        _db.Admins.Add(admin2);
        await _db.SaveChangesAsync();

        var principal2 = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, "admin-2"),
            new Claim(HoldFastClaimTypes.AdminId, admin2.Id.ToString()),
        }, "Test"));

        // Create invite
        var invite = await _mutation.CreateWorkspaceInviteLink(
            _workspace.Id, "admin2@test.com", "MEMBER", null, null,
            _principal, _authz, _db, CancellationToken.None);

        // Accept
        var result = await _mutation.AcceptWorkspaceInvite(
            invite.Secret!, principal2, _authz, _db, CancellationToken.None);

        Assert.True(result);

        // Verify membership
        var wa = await _db.WorkspaceAdmins
            .FirstOrDefaultAsync(x => x.AdminId == admin2.Id && x.WorkspaceId == _workspace.Id);
        Assert.NotNull(wa);
        Assert.Equal("MEMBER", wa!.Role);

        // Verify invite is removed
        Assert.Null(await _db.WorkspaceInviteLinks.FindAsync(invite.Id));
    }

    [Fact]
    public async Task AcceptWorkspaceInvite_WithRole()
    {
        var admin2 = new Admin { Uid = "admin-2", Email = "admin2@test.com" };
        _db.Admins.Add(admin2);
        await _db.SaveChangesAsync();

        var principal2 = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, "admin-2"),
            new Claim(HoldFastClaimTypes.AdminId, admin2.Id.ToString()),
        }, "Test"));

        var invite = await _mutation.CreateWorkspaceInviteLink(
            _workspace.Id, "admin2@test.com", "ADMIN", null, null,
            _principal, _authz, _db, CancellationToken.None);

        await _mutation.AcceptWorkspaceInvite(
            invite.Secret!, principal2, _authz, _db, CancellationToken.None);

        var wa = await _db.WorkspaceAdmins
            .FirstAsync(x => x.AdminId == admin2.Id && x.WorkspaceId == _workspace.Id);
        Assert.Equal("ADMIN", wa.Role);
    }

    [Fact]
    public async Task AcceptWorkspaceInvite_InvalidSecret_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.AcceptWorkspaceInvite(
                "invalid-secret", _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task AcceptWorkspaceInvite_ExpiredInvite_Throws()
    {
        // Create invite that's already expired
        var invite = new WorkspaceInviteLink
        {
            WorkspaceId = _workspace.Id,
            InviteeEmail = "expired@test.com",
            InviteeRole = "MEMBER",
            Secret = "expired-secret",
            ExpirationDate = DateTime.UtcNow.AddDays(-1),
        };
        _db.WorkspaceInviteLinks.Add(invite);
        await _db.SaveChangesAsync();

        var admin2 = new Admin { Uid = "admin-2", Email = "expired@test.com" };
        _db.Admins.Add(admin2);
        await _db.SaveChangesAsync();

        var principal2 = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, "admin-2"),
            new Claim(HoldFastClaimTypes.AdminId, admin2.Id.ToString()),
        }, "Test"));

        var ex = await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.AcceptWorkspaceInvite(
                "expired-secret", principal2, _authz, _db, CancellationToken.None));

        Assert.Contains("expired", ex.Message);
    }

    [Fact]
    public async Task AcceptWorkspaceInvite_AlreadyMember_Throws()
    {
        // admin-1 is already a member, create an invite and try to accept
        var invite = new WorkspaceInviteLink
        {
            WorkspaceId = _workspace.Id,
            InviteeEmail = "admin@test.com",
            InviteeRole = "MEMBER",
            Secret = "already-member-secret",
            ExpirationDate = DateTime.UtcNow.AddDays(7),
        };
        _db.WorkspaceInviteLinks.Add(invite);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.AcceptWorkspaceInvite(
                "already-member-secret", _principal, _authz, _db, CancellationToken.None));

        Assert.Contains("Already a member", ex.Message);
    }

    [Fact]
    public async Task AcceptWorkspaceInvite_NullWorkspaceId_Throws()
    {
        var invite = new WorkspaceInviteLink
        {
            WorkspaceId = null,
            InviteeEmail = "test@test.com",
            Secret = "null-ws-secret",
            ExpirationDate = DateTime.UtcNow.AddDays(7),
        };
        _db.WorkspaceInviteLinks.Add(invite);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.AcceptWorkspaceInvite(
                "null-ws-secret", _principal, _authz, _db, CancellationToken.None));

        Assert.Contains("no workspace", ex.Message);
    }

    [Fact]
    public async Task AcceptWorkspaceInvite_CopiesProjectIds()
    {
        var admin2 = new Admin { Uid = "admin-2", Email = "admin2@test.com" };
        _db.Admins.Add(admin2);
        await _db.SaveChangesAsync();

        var principal2 = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, "admin-2"),
            new Claim(HoldFastClaimTypes.AdminId, admin2.Id.ToString()),
        }, "Test"));

        var invite = await _mutation.CreateWorkspaceInviteLink(
            _workspace.Id, "admin2@test.com", "MEMBER", [10, 20], null,
            _principal, _authz, _db, CancellationToken.None);

        await _mutation.AcceptWorkspaceInvite(
            invite.Secret!, principal2, _authz, _db, CancellationToken.None);

        var wa = await _db.WorkspaceAdmins
            .FirstAsync(x => x.AdminId == admin2.Id && x.WorkspaceId == _workspace.Id);
        Assert.NotNull(wa.ProjectIds);
        Assert.Contains(10, wa.ProjectIds!);
        Assert.Contains(20, wa.ProjectIds!);
    }

    // ── DeleteWorkspaceInviteLink ───────────────────────────────────

    [Fact]
    public async Task DeleteWorkspaceInviteLink_Success()
    {
        var invite = await _mutation.CreateWorkspaceInviteLink(
            _workspace.Id, "del@test.com", "MEMBER", null, null,
            _principal, _authz, _db, CancellationToken.None);

        var result = await _mutation.DeleteWorkspaceInviteLink(
            invite.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        Assert.Null(await _db.WorkspaceInviteLinks.FindAsync(invite.Id));
    }

    [Fact]
    public async Task DeleteWorkspaceInviteLink_Nonexistent_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteWorkspaceInviteLink(
                99999, _principal, _authz, _db, CancellationToken.None));
    }

    // ── SendAdminWorkspaceInvite ────────────────────────────────────

    [Fact]
    public async Task SendAdminWorkspaceInvite_Success()
    {
        var secret = await _mutation.SendAdminWorkspaceInvite(
            _workspace.Id, "new@test.com", "MEMBER", null,
            _principal, _authz, _db, CancellationToken.None);

        Assert.NotNull(secret);
        Assert.True(secret.Length > 0);

        var invite = await _db.WorkspaceInviteLinks
            .FirstOrDefaultAsync(i => i.Secret == secret);
        Assert.NotNull(invite);
        Assert.Equal("new@test.com", invite!.InviteeEmail);
        Assert.Equal("MEMBER", invite.InviteeRole);
    }

    [Fact]
    public async Task SendAdminWorkspaceInvite_InvalidRole_Throws()
    {
        var ex = await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.SendAdminWorkspaceInvite(
                _workspace.Id, "new@test.com", "INVALID_ROLE", null,
                _principal, _authz, _db, CancellationToken.None));

        Assert.Contains("Invalid role", ex.Message);
    }

    [Fact]
    public async Task SendAdminWorkspaceInvite_DuplicateEmail_Throws()
    {
        await _mutation.SendAdminWorkspaceInvite(
            _workspace.Id, "dup@test.com", "MEMBER", null,
            _principal, _authz, _db, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.SendAdminWorkspaceInvite(
                _workspace.Id, "dup@test.com", "ADMIN", null,
                _principal, _authz, _db, CancellationToken.None));

        Assert.Contains("already been invited", ex.Message);
    }

    [Fact]
    public async Task SendAdminWorkspaceInvite_CaseInsensitiveDuplicateEmail_Throws()
    {
        await _mutation.SendAdminWorkspaceInvite(
            _workspace.Id, "Test@Example.Com", "MEMBER", null,
            _principal, _authz, _db, CancellationToken.None);

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.SendAdminWorkspaceInvite(
                _workspace.Id, "test@example.com", "MEMBER", null,
                _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task SendAdminWorkspaceInvite_ExistingMember_Throws()
    {
        // admin@test.com is already a workspace member
        var ex = await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.SendAdminWorkspaceInvite(
                _workspace.Id, "admin@test.com", "MEMBER", null,
                _principal, _authz, _db, CancellationToken.None));

        Assert.Contains("already a member", ex.Message);
    }

    [Fact]
    public async Task SendAdminWorkspaceInvite_AdminRole_Success()
    {
        var secret = await _mutation.SendAdminWorkspaceInvite(
            _workspace.Id, "adminrole@test.com", "ADMIN", null,
            _principal, _authz, _db, CancellationToken.None);

        var invite = await _db.WorkspaceInviteLinks
            .FirstAsync(i => i.Secret == secret);
        Assert.Equal("ADMIN", invite.InviteeRole);
    }

    [Fact]
    public async Task SendAdminWorkspaceInvite_WithProjectIds()
    {
        var secret = await _mutation.SendAdminWorkspaceInvite(
            _workspace.Id, "proj@test.com", "MEMBER", [5, 10],
            _principal, _authz, _db, CancellationToken.None);

        var invite = await _db.WorkspaceInviteLinks
            .FirstAsync(i => i.Secret == secret);
        Assert.NotNull(invite.ProjectIds);
        Assert.Equal(2, invite.ProjectIds!.Count);
    }

    // ── JoinWorkspace ───────────────────────────────────────────────

    [Fact]
    public async Task JoinWorkspace_Success()
    {
        // Create a second workspace and admin not in it
        var ws2 = new Workspace { Name = "WS2" };
        _db.Workspaces.Add(ws2);
        await _db.SaveChangesAsync();

        var result = await _mutation.JoinWorkspace(
            ws2.Id, _principal, _authz, _db, CancellationToken.None);

        Assert.Equal(ws2.Id, result);
        var wa = await _db.WorkspaceAdmins
            .FirstOrDefaultAsync(x => x.AdminId == _admin.Id && x.WorkspaceId == ws2.Id);
        Assert.NotNull(wa);
        Assert.Equal("MEMBER", wa!.Role);
    }

    [Fact]
    public async Task JoinWorkspace_AlreadyMember_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.JoinWorkspace(
                _workspace.Id, _principal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task JoinWorkspace_NonexistentWorkspace_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.JoinWorkspace(
                99999, _principal, _authz, _db, CancellationToken.None));
    }

    // ── AddAdminToWorkspace ─────────────────────────────────────────

    [Fact]
    public async Task AddAdminToWorkspace_Success()
    {
        var admin2 = new Admin { Uid = "admin-2", Email = "admin2@test.com" };
        _db.Admins.Add(admin2);
        await _db.SaveChangesAsync();

        var result = await _mutation.AddAdminToWorkspace(
            _workspace.Id, admin2.Id, "MEMBER",
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var wa = await _db.WorkspaceAdmins
            .FirstOrDefaultAsync(x => x.AdminId == admin2.Id && x.WorkspaceId == _workspace.Id);
        Assert.NotNull(wa);
        Assert.Equal("MEMBER", wa!.Role);
    }

    // ── ChangeAdminRole ─────────────────────────────────────────────

    [Fact]
    public async Task ChangeAdminRole_Success()
    {
        var admin2 = new Admin { Uid = "admin-2", Email = "admin2@test.com" };
        _db.Admins.Add(admin2);
        await _db.SaveChangesAsync();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = admin2.Id, WorkspaceId = _workspace.Id, Role = "MEMBER" });
        await _db.SaveChangesAsync();

        var result = await _mutation.ChangeAdminRole(
            _workspace.Id, admin2.Id, "ADMIN",
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var wa = await _db.WorkspaceAdmins
            .FirstAsync(x => x.AdminId == admin2.Id && x.WorkspaceId == _workspace.Id);
        Assert.Equal("ADMIN", wa.Role);
    }

    [Fact]
    public async Task ChangeAdminRole_NonexistentAdmin_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.ChangeAdminRole(
                _workspace.Id, 99999, "ADMIN",
                _principal, _authz, _db, CancellationToken.None));
    }

    // ── DeleteAdminFromWorkspace ────────────────────────────────────

    [Fact]
    public async Task DeleteAdminFromWorkspace_Success()
    {
        var admin2 = new Admin { Uid = "admin-2", Email = "admin2@test.com" };
        _db.Admins.Add(admin2);
        await _db.SaveChangesAsync();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
            { AdminId = admin2.Id, WorkspaceId = _workspace.Id, Role = "MEMBER" });
        await _db.SaveChangesAsync();

        var result = await _mutation.DeleteAdminFromWorkspace(
            _workspace.Id, admin2.Id,
            _principal, _authz, _db, CancellationToken.None);

        Assert.True(result);
        var wa = await _db.WorkspaceAdmins
            .FirstOrDefaultAsync(x => x.AdminId == admin2.Id && x.WorkspaceId == _workspace.Id);
        Assert.Null(wa);
    }

    [Fact]
    public async Task DeleteAdminFromWorkspace_NonexistentAdmin_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteAdminFromWorkspace(
                _workspace.Id, 99999,
                _principal, _authz, _db, CancellationToken.None));
    }
}
