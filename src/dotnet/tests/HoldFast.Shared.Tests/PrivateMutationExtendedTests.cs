using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.Shared.Tests;

/// <summary>
/// Tests for extended PrivateMutation resolvers: error group viewed, session public,
/// error tags, metric monitors, admin management, error comments, project membership.
/// </summary>
public class PrivateMutationExtendedTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateMutation _mutation;

    public PrivateMutationExtendedTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
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

    // ── MarkErrorGroupAsViewed ─────────────────────────────────────

    [Fact]
    public async Task MarkErrorGroupAsViewed_Success()
    {
        var (admin, _, project) = await SeedFullStack();
        var eg = new ErrorGroup { ProjectId = project.Id, Event = "err", Type = "error" };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var result = await _mutation.MarkErrorGroupAsViewed(
            eg.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        var view = await _db.ErrorGroupAdminsViews
            .FirstOrDefaultAsync(v => v.ErrorGroupId == eg.Id && v.AdminId == admin.Id);
        Assert.NotNull(view);
    }

    [Fact]
    public async Task MarkErrorGroupAsViewed_Idempotent()
    {
        var (_, _, project) = await SeedFullStack();
        var eg = new ErrorGroup { ProjectId = project.Id, Event = "err", Type = "error" };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        await _mutation.MarkErrorGroupAsViewed(eg.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);
        await _mutation.MarkErrorGroupAsViewed(eg.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        var views = await _db.ErrorGroupAdminsViews.Where(v => v.ErrorGroupId == eg.Id).CountAsync();
        Assert.Equal(1, views);
    }

    [Fact]
    public async Task MarkErrorGroupAsViewed_NoAccess_Throws()
    {
        var (_, _, project) = await SeedFullStack();
        var eg = new ErrorGroup { ProjectId = project.Id, Event = "err", Type = "error" };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var outsider = new Admin { Uid = "outsider", Email = "outsider@test.com" };
        _db.Admins.Add(outsider);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.MarkErrorGroupAsViewed(eg.Id, MakePrincipal("outsider"), _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task MarkErrorGroupAsViewed_NonExistent_Throws()
    {
        await SeedFullStack();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.MarkErrorGroupAsViewed(99999, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None));
    }

    // ── CreateErrorTag ─────────────────────────────────────────────

    [Fact]
    public async Task CreateErrorTag_Success()
    {
        var (_, _, project) = await SeedFullStack();
        var eg = new ErrorGroup { ProjectId = project.Id, Event = "err", Type = "error" };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var tag = await _mutation.CreateErrorTag(
            eg.Id, "Known Issue", "Tracked in Jira",
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Equal("Known Issue", tag.Title);
        Assert.Equal("Tracked in Jira", tag.Description);
        Assert.Equal(eg.Id, tag.ErrorGroupId);
    }

    [Fact]
    public async Task CreateErrorTag_NullDescription()
    {
        var (_, _, project) = await SeedFullStack();
        var eg = new ErrorGroup { ProjectId = project.Id, Event = "err", Type = "error" };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var tag = await _mutation.CreateErrorTag(
            eg.Id, "Bug", null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Null(tag.Description);
    }

    // ── CreateErrorComment / DeleteErrorComment ─────────────────────

    [Fact]
    public async Task CreateErrorComment_Success()
    {
        var (admin, _, project) = await SeedFullStack();
        var eg = new ErrorGroup { ProjectId = project.Id, Event = "err", Type = "error" };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var comment = await _mutation.CreateErrorComment(
            eg.Id, "This looks related to the migration",
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Equal(eg.Id, comment.ErrorGroupId);
        Assert.Equal(admin.Id, comment.AdminId);
        Assert.Equal("This looks related to the migration", comment.Text);
    }

    [Fact]
    public async Task DeleteErrorComment_Success()
    {
        var (admin, _, project) = await SeedFullStack();
        var eg = new ErrorGroup { ProjectId = project.Id, Event = "err", Type = "error" };
        _db.ErrorGroups.Add(eg);
        await _db.SaveChangesAsync();

        var comment = new ErrorComment { ErrorGroupId = eg.Id, AdminId = admin.Id, Text = "delete me" };
        _db.ErrorComments.Add(comment);
        await _db.SaveChangesAsync();

        var result = await _mutation.DeleteErrorComment(
            comment.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        Assert.Null(await _db.ErrorComments.FindAsync(comment.Id));
    }

    [Fact]
    public async Task DeleteErrorComment_NonExistent_Throws()
    {
        await SeedFullStack();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteErrorComment(99999, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None));
    }

    // ── Metric Monitor CRUD ────────────────────────────────────────

    [Fact]
    public async Task CreateMetricMonitor_Success()
    {
        var (_, _, project) = await SeedFullStack();

        var monitor = await _mutation.CreateMetricMonitor(
            project.Id, "CPU Monitor", "cpu_usage", "AVG", 90.0,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Equal("CPU Monitor", monitor.Name);
        Assert.Equal("cpu_usage", monitor.MetricToMonitor);
        Assert.Equal("AVG", monitor.Aggregator);
        Assert.Equal(90.0, monitor.Threshold);
    }

    [Fact]
    public async Task UpdateMetricMonitor_Success()
    {
        var (_, _, project) = await SeedFullStack();
        var monitor = new MetricMonitor
        {
            ProjectId = project.Id, Name = "Old Name", MetricToMonitor = "mem",
        };
        _db.MetricMonitors.Add(monitor);
        await _db.SaveChangesAsync();

        var updated = await _mutation.UpdateMetricMonitor(
            monitor.Id, "New Name", "P95", 95.0, true,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Equal("New Name", updated.Name);
        Assert.Equal("P95", updated.Aggregator);
        Assert.Equal(95.0, updated.Threshold);
        Assert.True(updated.Disabled);
    }

    [Fact]
    public async Task UpdateMetricMonitor_PartialUpdate()
    {
        var (_, _, project) = await SeedFullStack();
        var monitor = new MetricMonitor
        {
            ProjectId = project.Id, Name = "Keep This", MetricToMonitor = "cpu",
            Aggregator = "AVG", Threshold = 80.0,
        };
        _db.MetricMonitors.Add(monitor);
        await _db.SaveChangesAsync();

        var updated = await _mutation.UpdateMetricMonitor(
            monitor.Id, null, null, null, true,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Equal("Keep This", updated.Name);
        Assert.Equal("AVG", updated.Aggregator);
        Assert.True(updated.Disabled);
    }

    [Fact]
    public async Task DeleteMetricMonitor_Success()
    {
        var (_, _, project) = await SeedFullStack();
        var monitor = new MetricMonitor { ProjectId = project.Id, Name = "Del", MetricToMonitor = "x" };
        _db.MetricMonitors.Add(monitor);
        await _db.SaveChangesAsync();

        var result = await _mutation.DeleteMetricMonitor(
            monitor.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        Assert.Null(await _db.MetricMonitors.FindAsync(monitor.Id));
    }

    [Fact]
    public async Task DeleteMetricMonitor_NonExistent_Throws()
    {
        await SeedFullStack();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteMetricMonitor(99999, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task MetricMonitor_NoAccess_Throws()
    {
        var (_, _, project) = await SeedFullStack();
        var outsider = new Admin { Uid = "outsider", Email = "outsider@test.com" };
        _db.Admins.Add(outsider);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateMetricMonitor(
                project.Id, "X", "y", null, null,
                MakePrincipal("outsider"), _authz, _db, CancellationToken.None));
    }

    // ── ChangeAdminRole ────────────────────────────────────────────

    [Fact]
    public async Task ChangeAdminRole_Success()
    {
        var (admin, workspace, _) = await SeedFullStack();
        var member = new Admin { Uid = "member", Email = "member@test.com" };
        _db.Admins.Add(member);
        await _db.SaveChangesAsync();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = member.Id, WorkspaceId = workspace.Id, Role = "MEMBER",
        });
        await _db.SaveChangesAsync();

        var result = await _mutation.ChangeAdminRole(
            workspace.Id, member.Id, "ADMIN",
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        var wa = await _db.WorkspaceAdmins
            .FirstAsync(wa => wa.AdminId == member.Id && wa.WorkspaceId == workspace.Id);
        Assert.Equal("ADMIN", wa.Role);
    }

    [Fact]
    public async Task ChangeAdminRole_MemberCantPromote()
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

        // Member trying to promote themselves — should fail
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.ChangeAdminRole(
                workspace.Id, member.Id, "ADMIN",
                MakePrincipal("member"), _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task ChangeAdminRole_NonMember_Throws()
    {
        var (_, workspace, _) = await SeedFullStack();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.ChangeAdminRole(
                workspace.Id, 99999, "ADMIN",
                MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None));
    }

    // ── DeleteAdminFromWorkspace ────────────────────────────────────

    [Fact]
    public async Task DeleteAdminFromWorkspace_Success()
    {
        var (_, workspace, _) = await SeedFullStack();
        var member = new Admin { Uid = "removee", Email = "removee@test.com" };
        _db.Admins.Add(member);
        await _db.SaveChangesAsync();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = member.Id, WorkspaceId = workspace.Id, Role = "MEMBER",
        });
        await _db.SaveChangesAsync();

        var result = await _mutation.DeleteAdminFromWorkspace(
            workspace.Id, member.Id,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        var wa = await _db.WorkspaceAdmins
            .FirstOrDefaultAsync(wa => wa.AdminId == member.Id && wa.WorkspaceId == workspace.Id);
        Assert.Null(wa);
    }

    // ── ChangeProjectMembership ────────────────────────────────────

    [Fact]
    public async Task ChangeProjectMembership_Success()
    {
        var (_, workspace, project) = await SeedFullStack();
        var member = new Admin { Uid = "limited", Email = "limited@test.com" };
        _db.Admins.Add(member);
        await _db.SaveChangesAsync();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = member.Id, WorkspaceId = workspace.Id, Role = "MEMBER",
        });
        await _db.SaveChangesAsync();

        var result = await _mutation.ChangeProjectMembership(
            workspace.Id, member.Id, [project.Id],
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        var wa = await _db.WorkspaceAdmins
            .FirstAsync(wa => wa.AdminId == member.Id && wa.WorkspaceId == workspace.Id);
        Assert.Equal([project.Id], wa.ProjectIds);
    }

    [Fact]
    public async Task ChangeProjectMembership_NullProjectIds_GrantsFullAccess()
    {
        var (_, workspace, _) = await SeedFullStack();
        var member = new Admin { Uid = "full", Email = "full@test.com" };
        _db.Admins.Add(member);
        await _db.SaveChangesAsync();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = member.Id, WorkspaceId = workspace.Id, Role = "MEMBER",
            ProjectIds = [1, 2],
        });
        await _db.SaveChangesAsync();

        await _mutation.ChangeProjectMembership(
            workspace.Id, member.Id, null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        var wa = await _db.WorkspaceAdmins
            .FirstAsync(wa => wa.AdminId == member.Id && wa.WorkspaceId == workspace.Id);
        Assert.Null(wa.ProjectIds);
    }

    // ── UpdateAllowedEmailOrigins ──────────────────────────────────

    [Fact]
    public async Task UpdateAllowedEmailOrigins_Success()
    {
        var (_, workspace, _) = await SeedFullStack();

        var result = await _mutation.UpdateAllowedEmailOrigins(
            workspace.Id, ["example.com", "test.org"],
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        var ws = await _db.Workspaces.FindAsync(workspace.Id);
        Assert.Equal("example.com,test.org", ws!.AllowedAutoJoinEmailOrigins);
    }

    [Fact]
    public async Task UpdateAllowedEmailOrigins_NullClears()
    {
        var (_, workspace, _) = await SeedFullStack();
        workspace.AllowedAutoJoinEmailOrigins = "old.com";
        await _db.SaveChangesAsync();

        await _mutation.UpdateAllowedEmailOrigins(
            workspace.Id, null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        var ws = await _db.Workspaces.FindAsync(workspace.Id);
        Assert.Null(ws!.AllowedAutoJoinEmailOrigins);
    }

    [Fact]
    public async Task UpdateAllowedEmailOrigins_MemberCantUpdate()
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

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.UpdateAllowedEmailOrigins(
                workspace.Id, ["evil.com"],
                MakePrincipal("member"), _authz, _db, CancellationToken.None));
    }

    // ── AddAdminToWorkspace ────────────────────────────────────────

    [Fact]
    public async Task AddAdminToWorkspace_Success()
    {
        var (_, workspace, _) = await SeedFullStack();
        var newAdmin = new Admin { Uid = "new-admin", Email = "new@test.com" };
        _db.Admins.Add(newAdmin);
        await _db.SaveChangesAsync();

        var result = await _mutation.AddAdminToWorkspace(
            workspace.Id, newAdmin.Id, "MEMBER",
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        var wa = await _db.WorkspaceAdmins
            .FirstOrDefaultAsync(wa => wa.AdminId == newAdmin.Id && wa.WorkspaceId == workspace.Id);
        Assert.NotNull(wa);
        Assert.Equal("MEMBER", wa!.Role);
    }
}
