using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Shared.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.Shared.Tests.Auth;

public class AuthorizationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;

    public AuthorizationServiceTests()
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

    private async Task<(Admin admin, Workspace workspace)> SeedAdminAndWorkspace(
        string role = "ADMIN", List<int>? projectIds = null)
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
            ProjectIds = projectIds,
        });
        await _db.SaveChangesAsync();

        return (admin, workspace);
    }

    private async Task<Project> SeedProject(int workspaceId, string name = "Test Project")
    {
        var project = new Project { WorkspaceId = workspaceId, Name = name };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        return project;
    }

    // ── GetCurrentAdminAsync ────────────────────────────────────────

    [Fact]
    public async Task GetCurrentAdmin_ExistingUid_ReturnsAdmin()
    {
        var admin = new Admin { Uid = "existing-uid", Email = "a@b.com" };
        _db.Admins.Add(admin);
        await _db.SaveChangesAsync();

        var result = await _authz.GetCurrentAdminAsync("existing-uid");
        Assert.Equal(admin.Id, result.Id);
    }

    [Fact]
    public async Task GetCurrentAdmin_NewUid_AutoCreatesAdmin()
    {
        var result = await _authz.GetCurrentAdminAsync("brand-new-uid");
        Assert.True(result.Id > 0);
        Assert.Equal("brand-new-uid", result.Uid);

        var fromDb = await _db.Admins.FirstOrDefaultAsync(a => a.Uid == "brand-new-uid");
        Assert.NotNull(fromDb);
    }

    [Fact]
    public async Task GetCurrentAdmin_EmptyUid_ThrowsAuthenticationError()
    {
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _authz.GetCurrentAdminAsync(""));
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task GetCurrentAdmin_NullUid_ThrowsAuthenticationError()
    {
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _authz.GetCurrentAdminAsync(null!));
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task GetCurrentAdmin_CalledTwiceWithSameUid_ReturnsSameAdmin()
    {
        var a1 = await _authz.GetCurrentAdminAsync("reused-uid");
        var a2 = await _authz.GetCurrentAdminAsync("reused-uid");
        Assert.Equal(a1.Id, a2.Id);
    }

    [Fact]
    public async Task GetCurrentAdmin_UnicodeUid_Works()
    {
        var result = await _authz.GetCurrentAdminAsync("ユーザー123");
        Assert.Equal("ユーザー123", result.Uid);
    }

    // ── IsAdminInWorkspaceAsync ─────────────────────────────────────

    [Fact]
    public async Task IsAdminInWorkspace_Member_ReturnsWorkspace()
    {
        var (admin, workspace) = await SeedAdminAndWorkspace("MEMBER");
        var result = await _authz.IsAdminInWorkspaceAsync(admin.Id, workspace.Id);
        Assert.Equal(workspace.Id, result.Id);
    }

    [Fact]
    public async Task IsAdminInWorkspace_Admin_ReturnsWorkspace()
    {
        var (admin, workspace) = await SeedAdminAndWorkspace("ADMIN");
        var result = await _authz.IsAdminInWorkspaceAsync(admin.Id, workspace.Id);
        Assert.Equal(workspace.Id, result.Id);
    }

    [Fact]
    public async Task IsAdminInWorkspace_NotMember_ThrowsAuthorizationError()
    {
        var (admin, workspace) = await SeedAdminAndWorkspace();
        var otherAdmin = new Admin { Uid = "other" };
        _db.Admins.Add(otherAdmin);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _authz.IsAdminInWorkspaceAsync(otherAdmin.Id, workspace.Id));
        Assert.Contains("403", ex.Message);
    }

    [Fact]
    public async Task IsAdminInWorkspace_NonExistentWorkspace_ThrowsAuthorizationError()
    {
        var admin = new Admin { Uid = "test" };
        _db.Admins.Add(admin);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _authz.IsAdminInWorkspaceAsync(admin.Id, 99999));
    }

    [Fact]
    public async Task IsAdminInWorkspace_NonExistentAdmin_ThrowsAuthorizationError()
    {
        var workspace = new Workspace { Name = "ws" };
        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _authz.IsAdminInWorkspaceAsync(99999, workspace.Id));
    }

    // ── IsAdminInWorkspaceFullAccessAsync ────────────────────────────

    [Fact]
    public async Task IsAdminInWorkspaceFullAccess_AdminRole_Succeeds()
    {
        var (admin, workspace) = await SeedAdminAndWorkspace("ADMIN");
        var result = await _authz.IsAdminInWorkspaceFullAccessAsync(admin.Id, workspace.Id);
        Assert.Equal(workspace.Id, result.Id);
    }

    [Fact]
    public async Task IsAdminInWorkspaceFullAccess_MemberWithNullProjectIds_Succeeds()
    {
        var (admin, workspace) = await SeedAdminAndWorkspace("MEMBER", null);
        var result = await _authz.IsAdminInWorkspaceFullAccessAsync(admin.Id, workspace.Id);
        Assert.Equal(workspace.Id, result.Id);
    }

    [Fact]
    public async Task IsAdminInWorkspaceFullAccess_MemberWithProjectIds_Denied()
    {
        var (admin, workspace) = await SeedAdminAndWorkspace("MEMBER", [1, 2, 3]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _authz.IsAdminInWorkspaceFullAccessAsync(admin.Id, workspace.Id));
    }

    [Fact]
    public async Task IsAdminInWorkspaceFullAccess_AdminWithProjectIds_Succeeds()
    {
        // ADMIN role overrides project ID restriction
        var (admin, workspace) = await SeedAdminAndWorkspace("ADMIN", [1]);
        var result = await _authz.IsAdminInWorkspaceFullAccessAsync(admin.Id, workspace.Id);
        Assert.Equal(workspace.Id, result.Id);
    }

    // ── IsAdminInProjectAsync ───────────────────────────────────────

    [Fact]
    public async Task IsAdminInProject_FullAccessMember_Succeeds()
    {
        var (admin, workspace) = await SeedAdminAndWorkspace("MEMBER", null);
        var project = await SeedProject(workspace.Id);

        var result = await _authz.IsAdminInProjectAsync(admin.Id, project.Id);
        Assert.Equal(project.Id, result.Id);
    }

    [Fact]
    public async Task IsAdminInProject_LimitedMember_AllowedProject_Succeeds()
    {
        var (admin, workspace) = await SeedAdminAndWorkspace("MEMBER");
        var project = await SeedProject(workspace.Id);

        // Update membership to restrict to this project
        var wa = await _db.WorkspaceAdmins.FirstAsync(
            x => x.AdminId == admin.Id && x.WorkspaceId == workspace.Id);
        wa.ProjectIds = [project.Id];
        await _db.SaveChangesAsync();

        var result = await _authz.IsAdminInProjectAsync(admin.Id, project.Id);
        Assert.Equal(project.Id, result.Id);
    }

    [Fact]
    public async Task IsAdminInProject_LimitedMember_DifferentProject_Denied()
    {
        var (admin, workspace) = await SeedAdminAndWorkspace("MEMBER");
        var project1 = await SeedProject(workspace.Id, "Project 1");
        var project2 = await SeedProject(workspace.Id, "Project 2");

        var wa = await _db.WorkspaceAdmins.FirstAsync(
            x => x.AdminId == admin.Id && x.WorkspaceId == workspace.Id);
        wa.ProjectIds = [project1.Id]; // only allowed project1
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _authz.IsAdminInProjectAsync(admin.Id, project2.Id));
    }

    [Fact]
    public async Task IsAdminInProject_NotInWorkspace_Denied()
    {
        var (_, workspace) = await SeedAdminAndWorkspace();
        var project = await SeedProject(workspace.Id);

        var outsider = new Admin { Uid = "outsider" };
        _db.Admins.Add(outsider);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _authz.IsAdminInProjectAsync(outsider.Id, project.Id));
    }

    [Fact]
    public async Task IsAdminInProject_NonExistentProject_Denied()
    {
        var (admin, _) = await SeedAdminAndWorkspace();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _authz.IsAdminInProjectAsync(admin.Id, 99999));
    }

    [Fact]
    public async Task IsAdminInProject_AdminRole_AlwaysSucceeds()
    {
        var (admin, workspace) = await SeedAdminAndWorkspace("ADMIN");
        var project = await SeedProject(workspace.Id);
        var result = await _authz.IsAdminInProjectAsync(admin.Id, project.Id);
        Assert.Equal(project.Id, result.Id);
    }

    // ── GetAdminRoleAsync ───────────────────────────────────────────

    [Fact]
    public async Task GetAdminRole_Admin_ReturnsAdminRole()
    {
        var (admin, workspace) = await SeedAdminAndWorkspace("ADMIN");
        var result = await _authz.GetAdminRoleAsync(admin.Id, workspace.Id);

        Assert.NotNull(result);
        Assert.Equal("ADMIN", result.Value.Role);
        Assert.Null(result.Value.ProjectIds);
    }

    [Fact]
    public async Task GetAdminRole_MemberWithProjects_ReturnsMemberAndProjectIds()
    {
        var (admin, workspace) = await SeedAdminAndWorkspace("MEMBER", [10, 20, 30]);
        var result = await _authz.GetAdminRoleAsync(admin.Id, workspace.Id);

        Assert.NotNull(result);
        Assert.Equal("MEMBER", result.Value.Role);
        Assert.Equal([10, 20, 30], result.Value.ProjectIds);
    }

    [Fact]
    public async Task GetAdminRole_NotMember_ReturnsNull()
    {
        var workspace = new Workspace { Name = "ws" };
        _db.Workspaces.Add(workspace);
        var admin = new Admin { Uid = "lonely" };
        _db.Admins.Add(admin);
        await _db.SaveChangesAsync();

        var result = await _authz.GetAdminRoleAsync(admin.Id, workspace.Id);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAdminRole_NullRole_DefaultsToMember()
    {
        var admin = new Admin { Uid = "u" };
        _db.Admins.Add(admin);
        var workspace = new Workspace { Name = "ws" };
        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = admin.Id,
            WorkspaceId = workspace.Id,
            Role = null, // null role
        });
        await _db.SaveChangesAsync();

        var result = await _authz.GetAdminRoleAsync(admin.Id, workspace.Id);
        Assert.Equal("MEMBER", result!.Value.Role);
    }

    // ── ValidateAdminRoleAsync ──────────────────────────────────────

    [Fact]
    public async Task ValidateAdminRole_Admin_Succeeds()
    {
        var (admin, workspace) = await SeedAdminAndWorkspace("ADMIN");
        await _authz.ValidateAdminRoleAsync(admin.Id, workspace.Id);
        // No exception = success
    }

    [Fact]
    public async Task ValidateAdminRole_Member_ThrowsAuthorizationError()
    {
        var (admin, workspace) = await SeedAdminAndWorkspace("MEMBER");
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _authz.ValidateAdminRoleAsync(admin.Id, workspace.Id));
        Assert.Contains("403", ex.Message);
    }

    [Fact]
    public async Task ValidateAdminRole_NotInWorkspace_ThrowsAuthorizationError()
    {
        var (_, workspace) = await SeedAdminAndWorkspace();
        var outsider = new Admin { Uid = "outsider" };
        _db.Admins.Add(outsider);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _authz.ValidateAdminRoleAsync(outsider.Id, workspace.Id));
    }

    // ── Multi-workspace scenarios ───────────────────────────────────

    [Fact]
    public async Task MultiWorkspace_AdminInOne_MemberInAnother()
    {
        var admin = new Admin { Uid = "multi" };
        _db.Admins.Add(admin);
        var ws1 = new Workspace { Name = "WS1" };
        var ws2 = new Workspace { Name = "WS2" };
        _db.Workspaces.AddRange(ws1, ws2);
        await _db.SaveChangesAsync();

        _db.WorkspaceAdmins.AddRange(
            new WorkspaceAdmin { AdminId = admin.Id, WorkspaceId = ws1.Id, Role = "ADMIN" },
            new WorkspaceAdmin { AdminId = admin.Id, WorkspaceId = ws2.Id, Role = "MEMBER" });
        await _db.SaveChangesAsync();

        // Admin in ws1
        await _authz.ValidateAdminRoleAsync(admin.Id, ws1.Id);

        // Member in ws2 — validate fails
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _authz.ValidateAdminRoleAsync(admin.Id, ws2.Id));

        // But basic membership check passes for both
        Assert.NotNull(await _authz.IsAdminInWorkspaceAsync(admin.Id, ws1.Id));
        Assert.NotNull(await _authz.IsAdminInWorkspaceAsync(admin.Id, ws2.Id));
    }

    [Fact]
    public async Task MultiProject_LimitedToSubset()
    {
        var admin = new Admin { Uid = "limited" };
        _db.Admins.Add(admin);
        var workspace = new Workspace { Name = "WS" };
        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync();

        var p1 = await SeedProject(workspace.Id, "P1");
        var p2 = await SeedProject(workspace.Id, "P2");
        var p3 = await SeedProject(workspace.Id, "P3");

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = admin.Id,
            WorkspaceId = workspace.Id,
            Role = "MEMBER",
            ProjectIds = [p1.Id, p2.Id], // limited to p1 and p2
        });
        await _db.SaveChangesAsync();

        Assert.NotNull(await _authz.IsAdminInProjectAsync(admin.Id, p1.Id));
        Assert.NotNull(await _authz.IsAdminInProjectAsync(admin.Id, p2.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _authz.IsAdminInProjectAsync(admin.Id, p3.Id));
    }
}
