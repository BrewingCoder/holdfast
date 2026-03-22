using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.Shared.Tests.Auth;

public class AuthHelperTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;

    public AuthHelperTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();
        _authz = new AuthorizationService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private static ClaimsPrincipal MakePrincipal(string uid, string email = "test@example.com") =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, uid),
            new Claim(HoldFastClaimTypes.Email, email),
        }, "Test"));

    private static ClaimsPrincipal AnonymousPrincipal =>
        new(new ClaimsIdentity()); // No claims, not authenticated

    private async Task<(Admin admin, Workspace workspace, Project project)> SeedFullStack(string role = "ADMIN")
    {
        var admin = new Admin { Uid = "test-uid", Email = "test@example.com" };
        _db.Admins.Add(admin);
        var workspace = new Workspace { Name = "Test Workspace" };
        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = admin.Id,
            WorkspaceId = workspace.Id,
            Role = role,
        });

        var project = new Project { WorkspaceId = workspace.Id, Name = "Test Project" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        return (admin, workspace, project);
    }

    // ── GetRequiredAdmin ────────────────────────────────────────────

    [Fact]
    public async Task GetRequiredAdmin_ValidUid_ReturnsAdmin()
    {
        var admin = new Admin { Uid = "known-uid" };
        _db.Admins.Add(admin);
        await _db.SaveChangesAsync();

        var result = await AuthHelper.GetRequiredAdmin(
            MakePrincipal("known-uid"), _authz, CancellationToken.None);
        Assert.Equal(admin.Id, result.Id);
    }

    [Fact]
    public async Task GetRequiredAdmin_NewUid_AutoCreates()
    {
        var result = await AuthHelper.GetRequiredAdmin(
            MakePrincipal("new-uid"), _authz, CancellationToken.None);
        Assert.True(result.Id > 0);
        Assert.Equal("new-uid", result.Uid);
    }

    [Fact]
    public async Task GetRequiredAdmin_NullPrincipal_ThrowsGraphQLException()
    {
        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => AuthHelper.GetRequiredAdmin(null, _authz, CancellationToken.None));
        Assert.Contains("Not authenticated", ex.Message);
    }

    [Fact]
    public async Task GetRequiredAdmin_AnonymousPrincipal_ThrowsGraphQLException()
    {
        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => AuthHelper.GetRequiredAdmin(AnonymousPrincipal, _authz, CancellationToken.None));
        Assert.Contains("Not authenticated", ex.Message);
    }

    [Fact]
    public async Task GetRequiredAdmin_EmptyUidClaim_ThrowsGraphQLException()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(HoldFastClaimTypes.Uid, ""),
        }, "Test"));

        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => AuthHelper.GetRequiredAdmin(principal, _authz, CancellationToken.None));
        Assert.Contains("Not authenticated", ex.Message);
    }

    // ── RequireWorkspaceAccess ──────────────────────────────────────

    [Fact]
    public async Task RequireWorkspaceAccess_Member_ReturnsAdmin()
    {
        var (admin, workspace, _) = await SeedFullStack("MEMBER");
        var result = await AuthHelper.RequireWorkspaceAccess(
            MakePrincipal("test-uid"), workspace.Id, _authz, CancellationToken.None);
        Assert.Equal(admin.Id, result.Id);
    }

    [Fact]
    public async Task RequireWorkspaceAccess_NotMember_ThrowsGraphQLException()
    {
        var (_, workspace, _) = await SeedFullStack();
        var outsider = new Admin { Uid = "outsider" };
        _db.Admins.Add(outsider);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => AuthHelper.RequireWorkspaceAccess(
                MakePrincipal("outsider"), workspace.Id, _authz, CancellationToken.None));
        Assert.Contains("Not authorized", ex.Message);
    }

    [Fact]
    public async Task RequireWorkspaceAccess_Unauthenticated_ThrowsGraphQLException()
    {
        var (_, workspace, _) = await SeedFullStack();

        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => AuthHelper.RequireWorkspaceAccess(
                AnonymousPrincipal, workspace.Id, _authz, CancellationToken.None));
        Assert.Contains("Not authenticated", ex.Message);
    }

    // ── RequireWorkspaceAdmin ───────────────────────────────────────

    [Fact]
    public async Task RequireWorkspaceAdmin_AdminRole_ReturnsAdmin()
    {
        var (admin, workspace, _) = await SeedFullStack("ADMIN");
        var result = await AuthHelper.RequireWorkspaceAdmin(
            MakePrincipal("test-uid"), workspace.Id, _authz, CancellationToken.None);
        Assert.Equal(admin.Id, result.Id);
    }

    [Fact]
    public async Task RequireWorkspaceAdmin_MemberRole_ThrowsGraphQLException()
    {
        var (_, workspace, _) = await SeedFullStack("MEMBER");

        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => AuthHelper.RequireWorkspaceAdmin(
                MakePrincipal("test-uid"), workspace.Id, _authz, CancellationToken.None));
        Assert.Contains("Admin role required", ex.Message);
    }

    // ── RequireProjectAccess ────────────────────────────────────────

    [Fact]
    public async Task RequireProjectAccess_FullAccessMember_ReturnsAdmin()
    {
        var (admin, _, project) = await SeedFullStack("MEMBER");
        var result = await AuthHelper.RequireProjectAccess(
            MakePrincipal("test-uid"), project.Id, _authz, CancellationToken.None);
        Assert.Equal(admin.Id, result.Id);
    }

    [Fact]
    public async Task RequireProjectAccess_LimitedMember_WrongProject_ThrowsGraphQLException()
    {
        var (admin, workspace, project1) = await SeedFullStack("MEMBER");
        var project2 = new Project { WorkspaceId = workspace.Id, Name = "P2" };
        _db.Projects.Add(project2);
        await _db.SaveChangesAsync();

        // Restrict admin to project1 only
        var wa = await _db.WorkspaceAdmins.FirstAsync(
            x => x.AdminId == admin.Id && x.WorkspaceId == workspace.Id);
        wa.ProjectIds = [project1.Id];
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => AuthHelper.RequireProjectAccess(
                MakePrincipal("test-uid"), project2.Id, _authz, CancellationToken.None));
        Assert.Contains("Not authorized", ex.Message);
    }

    [Fact]
    public async Task RequireProjectAccess_NonExistentProject_ThrowsGraphQLException()
    {
        await SeedFullStack();

        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => AuthHelper.RequireProjectAccess(
                MakePrincipal("test-uid"), 99999, _authz, CancellationToken.None));
        Assert.Contains("Not authorized", ex.Message);
    }

    [Fact]
    public async Task RequireProjectAccess_NotInWorkspace_ThrowsGraphQLException()
    {
        var (_, _, project) = await SeedFullStack();
        var outsider = new Admin { Uid = "outsider" };
        _db.Admins.Add(outsider);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => AuthHelper.RequireProjectAccess(
                MakePrincipal("outsider"), project.Id, _authz, CancellationToken.None));
        Assert.Contains("Not authorized", ex.Message);
    }

    // ── Error code verification ─────────────────────────────────────

    [Fact]
    public async Task GetRequiredAdmin_ErrorCode_IsAuthNotAuthenticated()
    {
        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => AuthHelper.GetRequiredAdmin(null, _authz, CancellationToken.None));
        Assert.Contains("AUTH_NOT_AUTHENTICATED", ex.Errors.First().Code);
    }

    [Fact]
    public async Task RequireWorkspaceAccess_ErrorCode_IsAuthNotAuthorized()
    {
        var (_, workspace, _) = await SeedFullStack();
        var outsider = new Admin { Uid = "outsider" };
        _db.Admins.Add(outsider);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => AuthHelper.RequireWorkspaceAccess(
                MakePrincipal("outsider"), workspace.Id, _authz, CancellationToken.None));
        Assert.Contains("AUTH_NOT_AUTHORIZED", ex.Errors.First().Code);
    }

    [Fact]
    public async Task RequireWorkspaceAdmin_ErrorCode_IsAuthAdminRequired()
    {
        var (_, workspace, _) = await SeedFullStack("MEMBER");

        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => AuthHelper.RequireWorkspaceAdmin(
                MakePrincipal("test-uid"), workspace.Id, _authz, CancellationToken.None));
        Assert.Contains("AUTH_ADMIN_REQUIRED", ex.Errors.First().Code);
    }

    [Fact]
    public async Task RequireProjectAccess_ErrorCode_IsAuthNotAuthorized()
    {
        var (_, _, project) = await SeedFullStack();
        var outsider = new Admin { Uid = "outsider" };
        _db.Admins.Add(outsider);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<GraphQLException>(
            () => AuthHelper.RequireProjectAccess(
                MakePrincipal("outsider"), project.Id, _authz, CancellationToken.None));
        Assert.Contains("AUTH_NOT_AUTHORIZED", ex.Errors.First().Code);
    }
}
