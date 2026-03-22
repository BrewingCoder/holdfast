using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.Shared.Tests.Auth;

public class InviteFlowTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateMutation _mutation;

    public InviteFlowTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();
        _authz = new AuthorizationService(_db);
        _mutation = new PrivateMutation();
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
            new Claim(HoldFastClaimTypes.Email, $"{uid}@example.com"),
        }, "Test"));

    private async Task<(Admin admin, Workspace workspace)> SeedAdminAndWorkspace(string uid = "admin-uid")
    {
        var admin = new Admin { Uid = uid, Email = $"{uid}@example.com" };
        _db.Admins.Add(admin);
        var workspace = new Workspace { Name = "Test Workspace" };
        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = admin.Id,
            WorkspaceId = workspace.Id,
            Role = "ADMIN",
        });
        await _db.SaveChangesAsync();

        return (admin, workspace);
    }

    // ── CreateWorkspaceInviteLink ───────────────────────────────────

    [Fact]
    public async Task CreateInvite_Success_ReturnsInviteWithSecret()
    {
        var (admin, workspace) = await SeedAdminAndWorkspace();

        var invite = await _mutation.CreateWorkspaceInviteLink(
            workspace.Id, "invitee@example.com", "MEMBER", null, null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.NotNull(invite);
        Assert.NotNull(invite.Secret);
        Assert.Equal(32, invite.Secret.Length); // Guid.ToString("N") = 32 chars
        Assert.Equal("invitee@example.com", invite.InviteeEmail);
        Assert.Equal("MEMBER", invite.InviteeRole);
        Assert.Equal(workspace.Id, invite.WorkspaceId);
    }

    [Fact]
    public async Task CreateInvite_DefaultExpiry_IsSevenDays()
    {
        var (_, workspace) = await SeedAdminAndWorkspace();

        var invite = await _mutation.CreateWorkspaceInviteLink(
            workspace.Id, "new@test.com", "MEMBER", null, null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.NotNull(invite.ExpirationDate);
        var expectedExpiry = DateTime.UtcNow.AddDays(7);
        Assert.InRange(invite.ExpirationDate!.Value, expectedExpiry.AddMinutes(-1), expectedExpiry.AddMinutes(1));
    }

    [Fact]
    public async Task CreateInvite_CustomExpiry()
    {
        var (_, workspace) = await SeedAdminAndWorkspace();

        var invite = await _mutation.CreateWorkspaceInviteLink(
            workspace.Id, "new@test.com", "ADMIN", null, 30,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.NotNull(invite.ExpirationDate);
        var expectedExpiry = DateTime.UtcNow.AddDays(30);
        Assert.InRange(invite.ExpirationDate!.Value, expectedExpiry.AddMinutes(-1), expectedExpiry.AddMinutes(1));
    }

    [Fact]
    public async Task CreateInvite_WithProjectIds()
    {
        var (_, workspace) = await SeedAdminAndWorkspace();

        var invite = await _mutation.CreateWorkspaceInviteLink(
            workspace.Id, "limited@test.com", "MEMBER", [10, 20], null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Equal([10, 20], invite.ProjectIds);
    }

    [Fact]
    public async Task CreateInvite_DuplicateEmail_ThrowsGraphQLException()
    {
        var (_, workspace) = await SeedAdminAndWorkspace();

        await _mutation.CreateWorkspaceInviteLink(
            workspace.Id, "dup@test.com", "MEMBER", null, null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => _mutation.CreateWorkspaceInviteLink(
                workspace.Id, "dup@test.com", "MEMBER", null, null,
                MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None));
        Assert.Contains("active invite already exists", ex.Message);
    }

    [Fact]
    public async Task CreateInvite_AlreadyMember_ThrowsGraphQLException()
    {
        var (admin, workspace) = await SeedAdminAndWorkspace();

        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => _mutation.CreateWorkspaceInviteLink(
                workspace.Id, admin.Email!, "MEMBER", null, null,
                MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None));
        Assert.Contains("already a member", ex.Message);
    }

    [Fact]
    public async Task CreateInvite_MemberRole_ThrowsGraphQLException()
    {
        var admin = new Admin { Uid = "member-uid", Email = "member@test.com" };
        _db.Admins.Add(admin);
        var workspace = new Workspace { Name = "WS" };
        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = admin.Id, WorkspaceId = workspace.Id, Role = "MEMBER",
        });
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => _mutation.CreateWorkspaceInviteLink(
                workspace.Id, "invite@test.com", "MEMBER", null, null,
                MakePrincipal("member-uid"), _authz, _db, CancellationToken.None));
        Assert.Contains("Admin role required", ex.Message);
    }

    // ── AcceptWorkspaceInvite ───────────────────────────────────────

    [Fact]
    public async Task AcceptInvite_Success_AddsToWorkspace()
    {
        var (_, workspace) = await SeedAdminAndWorkspace();

        var invite = await _mutation.CreateWorkspaceInviteLink(
            workspace.Id, "new-member@test.com", "MEMBER", null, null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        // Create the invitee
        var invitee = new Admin { Uid = "invitee-uid", Email = "new-member@test.com" };
        _db.Admins.Add(invitee);
        await _db.SaveChangesAsync();

        var result = await _mutation.AcceptWorkspaceInvite(
            invite.Secret!, MakePrincipal("invitee-uid"), _authz, _db, CancellationToken.None);

        Assert.True(result);

        // Verify membership
        var membership = await _db.WorkspaceAdmins
            .FirstOrDefaultAsync(wa => wa.AdminId == invitee.Id && wa.WorkspaceId == workspace.Id);
        Assert.NotNull(membership);
        Assert.Equal("MEMBER", membership.Role);
    }

    [Fact]
    public async Task AcceptInvite_InheritsRoleFromInvite()
    {
        var (_, workspace) = await SeedAdminAndWorkspace();

        var invite = await _mutation.CreateWorkspaceInviteLink(
            workspace.Id, "admin-invitee@test.com", "ADMIN", null, null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        var invitee = new Admin { Uid = "admin-invitee", Email = "admin-invitee@test.com" };
        _db.Admins.Add(invitee);
        await _db.SaveChangesAsync();

        await _mutation.AcceptWorkspaceInvite(
            invite.Secret!, MakePrincipal("admin-invitee"), _authz, _db, CancellationToken.None);

        var membership = await _db.WorkspaceAdmins
            .FirstOrDefaultAsync(wa => wa.AdminId == invitee.Id && wa.WorkspaceId == workspace.Id);
        Assert.Equal("ADMIN", membership!.Role);
    }

    [Fact]
    public async Task AcceptInvite_InheritsProjectIds()
    {
        var (_, workspace) = await SeedAdminAndWorkspace();

        var invite = await _mutation.CreateWorkspaceInviteLink(
            workspace.Id, "limited@test.com", "MEMBER", [5, 10], null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        var invitee = new Admin { Uid = "limited-uid", Email = "limited@test.com" };
        _db.Admins.Add(invitee);
        await _db.SaveChangesAsync();

        await _mutation.AcceptWorkspaceInvite(
            invite.Secret!, MakePrincipal("limited-uid"), _authz, _db, CancellationToken.None);

        var membership = await _db.WorkspaceAdmins
            .FirstOrDefaultAsync(wa => wa.AdminId == invitee.Id && wa.WorkspaceId == workspace.Id);
        Assert.Equal([5, 10], membership!.ProjectIds);
    }

    [Fact]
    public async Task AcceptInvite_RemovesInviteAfterAccepting()
    {
        var (_, workspace) = await SeedAdminAndWorkspace();

        var invite = await _mutation.CreateWorkspaceInviteLink(
            workspace.Id, "consume@test.com", "MEMBER", null, null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        var invitee = new Admin { Uid = "consume-uid" };
        _db.Admins.Add(invitee);
        await _db.SaveChangesAsync();

        await _mutation.AcceptWorkspaceInvite(
            invite.Secret!, MakePrincipal("consume-uid"), _authz, _db, CancellationToken.None);

        var remaining = await _db.WorkspaceInviteLinks
            .FirstOrDefaultAsync(l => l.Secret == invite.Secret);
        Assert.Null(remaining);
    }

    [Fact]
    public async Task AcceptInvite_ExpiredInvite_ThrowsGraphQLException()
    {
        var (_, workspace) = await SeedAdminAndWorkspace();

        // Create an already-expired invite directly
        var invite = new WorkspaceInviteLink
        {
            WorkspaceId = workspace.Id,
            InviteeEmail = "expired@test.com",
            InviteeRole = "MEMBER",
            Secret = Guid.NewGuid().ToString("N"),
            ExpirationDate = DateTime.UtcNow.AddDays(-1),
        };
        _db.WorkspaceInviteLinks.Add(invite);
        await _db.SaveChangesAsync();

        var invitee = new Admin { Uid = "expired-uid" };
        _db.Admins.Add(invitee);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => _mutation.AcceptWorkspaceInvite(
                invite.Secret!, MakePrincipal("expired-uid"), _authz, _db, CancellationToken.None));
        Assert.Contains("expired", ex.Message);
    }

    [Fact]
    public async Task AcceptInvite_InvalidSecret_ThrowsGraphQLException()
    {
        var invitee = new Admin { Uid = "bad-secret-uid" };
        _db.Admins.Add(invitee);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => _mutation.AcceptWorkspaceInvite(
                "nonexistent-secret", MakePrincipal("bad-secret-uid"), _authz, _db, CancellationToken.None));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task AcceptInvite_AlreadyMember_ThrowsGraphQLException()
    {
        var (admin, workspace) = await SeedAdminAndWorkspace();

        var invite = new WorkspaceInviteLink
        {
            WorkspaceId = workspace.Id,
            InviteeEmail = admin.Email,
            Secret = Guid.NewGuid().ToString("N"),
            ExpirationDate = DateTime.UtcNow.AddDays(7),
        };
        _db.WorkspaceInviteLinks.Add(invite);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => _mutation.AcceptWorkspaceInvite(
                invite.Secret!, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None));
        Assert.Contains("Already a member", ex.Message);
    }

    [Fact]
    public async Task AcceptInvite_Unauthenticated_ThrowsGraphQLException()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => _mutation.AcceptWorkspaceInvite(
                "any-secret", anonymous, _authz, _db, CancellationToken.None));
        Assert.Contains("Not authenticated", ex.Message);
    }

    // ── DeleteWorkspaceInviteLink ───────────────────────────────────

    [Fact]
    public async Task DeleteInvite_Success_RemovesInvite()
    {
        var (_, workspace) = await SeedAdminAndWorkspace();

        var invite = await _mutation.CreateWorkspaceInviteLink(
            workspace.Id, "delete-me@test.com", "MEMBER", null, null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        var result = await _mutation.DeleteWorkspaceInviteLink(
            invite.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        Assert.Null(await _db.WorkspaceInviteLinks.FindAsync(invite.Id));
    }

    [Fact]
    public async Task DeleteInvite_NonExistent_ThrowsGraphQLException()
    {
        await SeedAdminAndWorkspace();

        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => _mutation.DeleteWorkspaceInviteLink(
                99999, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None));
        Assert.Contains("not found", ex.Message);
    }

    // ── Full invite flow E2E ────────────────────────────────────────

    [Fact]
    public async Task FullInviteFlow_CreateAcceptVerify()
    {
        var (admin, workspace) = await SeedAdminAndWorkspace();

        // Step 1: Create invite
        var invite = await _mutation.CreateWorkspaceInviteLink(
            workspace.Id, "e2e@test.com", "MEMBER", null, 14,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.NotNull(invite.Secret);
        Assert.Equal("e2e@test.com", invite.InviteeEmail);

        // Step 2: New user accepts
        var newUser = new Admin { Uid = "e2e-uid", Email = "e2e@test.com" };
        _db.Admins.Add(newUser);
        await _db.SaveChangesAsync();

        await _mutation.AcceptWorkspaceInvite(
            invite.Secret!, MakePrincipal("e2e-uid"), _authz, _db, CancellationToken.None);

        // Step 3: Verify user is now a member
        var membership = await _db.WorkspaceAdmins
            .FirstOrDefaultAsync(wa => wa.AdminId == newUser.Id && wa.WorkspaceId == workspace.Id);
        Assert.NotNull(membership);
        Assert.Equal("MEMBER", membership.Role);

        // Step 4: Invite is consumed
        Assert.Null(await _db.WorkspaceInviteLinks.FirstOrDefaultAsync(l => l.Secret == invite.Secret));

        // Step 5: User can now access workspace
        var result = await _authz.IsAdminInWorkspaceAsync(newUser.Id, workspace.Id);
        Assert.Equal(workspace.Id, result.Id);
    }
}
