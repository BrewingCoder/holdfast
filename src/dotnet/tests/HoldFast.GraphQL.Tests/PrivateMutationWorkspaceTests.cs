using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.GraphQL.Private;
using HoldFast.Shared.Auth;
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Tests for PrivateMutation workspace/project CRUD operations:
/// CreateWorkspace, EditWorkspace, EditWorkspaceSettings, CreateProject,
/// EditProject, EditProjectSettings, DeleteProject, SaveBillingPlan,
/// ChangeProjectMembership, UpdateAllowedEmailOrigins.
/// </summary>
public class PrivateMutationWorkspaceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly AuthorizationService _authz;
    private readonly PrivateMutation _mutation;
    private readonly Admin _admin;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public PrivateMutationWorkspaceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _admin = new Admin { Uid = "admin-uid", Email = "admin@test.com", Name = "Admin" };
        _db.Admins.Add(_admin);
        _db.SaveChanges();

        _workspace = new Workspace { Name = "TestWS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = _admin.Id,
            WorkspaceId = _workspace.Id,
            Role = "ADMIN",
        });
        _db.SaveChanges();

        _project = new Project { Name = "TestProj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _db.AllWorkspaceSettings.Add(new AllWorkspaceSettings { WorkspaceId = _workspace.Id });
        _db.ProjectFilterSettings.Add(new ProjectFilterSettings { ProjectId = _project.Id });
        _db.SaveChanges();

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
            new Claim(HoldFastClaimTypes.Email, "test@example.com"),
        }, "Test"));

    private static ClaimsPrincipal AnonymousPrincipal => new(new ClaimsIdentity());

    // ── CreateWorkspace ─────────────────────────────────────────────

    [Fact]
    public async Task CreateWorkspace_SetsEnterpriseTier()
    {
        var result = await _mutation.CreateWorkspace(
            "New WS", MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Equal("New WS", result.Name);
        Assert.Equal("Enterprise", result.PlanTier);
        Assert.True(result.UnlimitedMembers);
    }

    [Fact]
    public async Task CreateWorkspace_CreatesDefaultSettings()
    {
        var result = await _mutation.CreateWorkspace(
            "WS-Settings", MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        var settings = await _db.AllWorkspaceSettings
            .FirstOrDefaultAsync(s => s.WorkspaceId == result.Id);
        Assert.NotNull(settings);
        Assert.True(settings!.AIApplication);
        Assert.True(settings.EnableSSO);
    }

    [Fact]
    public async Task CreateWorkspace_LinksAdmin()
    {
        var result = await _mutation.CreateWorkspace(
            "WS-Admin", MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        // The admin collection should contain the creator
        var ws = await _db.Workspaces.Include(w => w.Admins)
            .FirstAsync(w => w.Id == result.Id);
        Assert.Contains(ws.Admins, a => a.Id == _admin.Id);
    }

    [Fact]
    public async Task CreateWorkspace_Unauthenticated_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateWorkspace(
                "WS", AnonymousPrincipal, _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task CreateWorkspace_EmptyName_Succeeds()
    {
        // The mutation does not validate name — empty is allowed
        var result = await _mutation.CreateWorkspace(
            "", MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);
        Assert.Equal("", result.Name);
    }

    [Fact]
    public async Task CreateWorkspace_MultipleTimes_CreatesDistinct()
    {
        var ws1 = await _mutation.CreateWorkspace(
            "WS-A", MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);
        var ws2 = await _mutation.CreateWorkspace(
            "WS-B", MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.NotEqual(ws1.Id, ws2.Id);
    }

    // ── EditWorkspace ───────────────────────────────────────────────

    [Fact]
    public async Task EditWorkspace_UpdatesName()
    {
        var result = await _mutation.EditWorkspace(
            _workspace.Id, "Renamed WS",
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Equal("Renamed WS", result.Name);
    }

    [Fact]
    public async Task EditWorkspace_NullName_NoChange()
    {
        var result = await _mutation.EditWorkspace(
            _workspace.Id, null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Equal("TestWS", result.Name);
    }

    [Fact]
    public async Task EditWorkspace_NonexistentWorkspace_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.EditWorkspace(
                99999, "Name",
                MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task EditWorkspace_MemberRole_Throws()
    {
        var member = new Admin { Uid = "member-uid", Email = "member@test.com" };
        _db.Admins.Add(member);
        _db.SaveChanges();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = member.Id, WorkspaceId = _workspace.Id, Role = "MEMBER",
        });
        _db.SaveChanges();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.EditWorkspace(
                _workspace.Id, "Renamed",
                MakePrincipal("member-uid"), _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task EditWorkspace_Unauthenticated_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.EditWorkspace(
                _workspace.Id, "Renamed",
                AnonymousPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── EditWorkspaceSettings ───────────────────────────────────────

    [Fact]
    public async Task EditWorkspaceSettings_TogglesAIApplication()
    {
        var result = await _mutation.EditWorkspaceSettings(
            _workspace.Id,
            aiApplication: false, aiInsights: null, enableSSO: null,
            enableSessionExport: null, enableNetworkTraces: null, enableDataDeletion: null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.False(result.AIApplication);
    }

    [Fact]
    public async Task EditWorkspaceSettings_TogglesMultipleFlags()
    {
        var result = await _mutation.EditWorkspaceSettings(
            _workspace.Id,
            aiApplication: false, aiInsights: false, enableSSO: false,
            enableSessionExport: false, enableNetworkTraces: false, enableDataDeletion: false,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.False(result.AIApplication);
        Assert.False(result.AIInsights);
        Assert.False(result.EnableSSO);
        Assert.False(result.EnableSessionExport);
        Assert.False(result.EnableNetworkTraces);
        Assert.False(result.EnableDataDeletion);
    }

    [Fact]
    public async Task EditWorkspaceSettings_NullFlags_NoChange()
    {
        var result = await _mutation.EditWorkspaceSettings(
            _workspace.Id,
            aiApplication: null, aiInsights: null, enableSSO: null,
            enableSessionExport: null, enableNetworkTraces: null, enableDataDeletion: null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        // All should remain at defaults (true)
        Assert.True(result.AIApplication);
        Assert.True(result.EnableSSO);
    }

    [Fact]
    public async Task EditWorkspaceSettings_NonexistentSettings_Throws()
    {
        // Create workspace without settings
        var ws2 = new Workspace { Name = "No Settings" };
        _db.Workspaces.Add(ws2);
        _db.SaveChanges();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = _admin.Id, WorkspaceId = ws2.Id, Role = "ADMIN",
        });
        _db.SaveChanges();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.EditWorkspaceSettings(
                ws2.Id,
                aiApplication: true, aiInsights: null, enableSSO: null,
                enableSessionExport: null, enableNetworkTraces: null, enableDataDeletion: null,
                MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None));
    }

    // ── CreateProject ───────────────────────────────────────────────

    [Fact]
    public async Task CreateProject_CreatesWithDefaults()
    {
        var result = await _mutation.CreateProject(
            _workspace.Id, "New Project",
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Equal("New Project", result.Name);
        Assert.Equal(_workspace.Id, result.WorkspaceId);
    }

    [Fact]
    public async Task CreateProject_CreatesFilterSettings()
    {
        var result = await _mutation.CreateProject(
            _workspace.Id, "Filter Test",
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        var settings = await _db.ProjectFilterSettings
            .FirstOrDefaultAsync(s => s.ProjectId == result.Id);
        Assert.NotNull(settings);
        Assert.Equal(1.0, settings!.SessionSamplingRate);
    }

    [Fact]
    public async Task CreateProject_NonexistentWorkspace_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateProject(
                99999, "Project",
                MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task CreateProject_Unauthenticated_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.CreateProject(
                _workspace.Id, "Project",
                AnonymousPrincipal, _authz, _db, CancellationToken.None));
    }

    // ── EditProject ─────────────────────────────────────────────────

    [Fact]
    public async Task EditProject_UpdatesName()
    {
        var result = await _mutation.EditProject(
            _project.Id, "Updated Name", null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Equal("Updated Name", result.Name);
    }

    [Fact]
    public async Task EditProject_UpdatesBillingEmail()
    {
        var result = await _mutation.EditProject(
            _project.Id, null, "billing@company.com",
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Equal("billing@company.com", result.BillingEmail);
    }

    [Fact]
    public async Task EditProject_BothNameAndEmail()
    {
        var result = await _mutation.EditProject(
            _project.Id, "Both Updated", "both@test.com",
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Equal("Both Updated", result.Name);
        Assert.Equal("both@test.com", result.BillingEmail);
    }

    [Fact]
    public async Task EditProject_NullBoth_NoChange()
    {
        var result = await _mutation.EditProject(
            _project.Id, null, null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Equal("TestProj", result.Name);
    }

    [Fact]
    public async Task EditProject_NonexistentProject_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.EditProject(
                99999, "Name", null,
                MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None));
    }

    // ── EditProjectSettings ─────────────────────────────────────────

    [Fact]
    public async Task EditProjectSettings_UpdatesSamplingRates()
    {
        var result = await _mutation.EditProjectSettings(
            _project.Id,
            excludedUsers: null, errorFilters: null,
            rageClickWindowSeconds: null, rageClickRadiusPixels: null, rageClickCount: null,
            filterChromeExtension: null, filterSessionsWithoutError: null,
            autoResolveStaleErrorsDayInterval: null,
            sessionSamplingRate: 0.5, errorSamplingRate: 0.75,
            logSamplingRate: 0.25, traceSamplingRate: 0.1,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.Equal(0.5, result.SessionSamplingRate);
        Assert.Equal(0.75, result.ErrorSamplingRate);
        Assert.Equal(0.25, result.LogSamplingRate);
        Assert.Equal(0.1, result.TraceSamplingRate);
    }

    [Fact]
    public async Task EditProjectSettings_UpdatesRageClickParams()
    {
        await _mutation.EditProjectSettings(
            _project.Id,
            excludedUsers: null, errorFilters: null,
            rageClickWindowSeconds: 10, rageClickRadiusPixels: 15, rageClickCount: 3,
            filterChromeExtension: true, filterSessionsWithoutError: null,
            autoResolveStaleErrorsDayInterval: null,
            sessionSamplingRate: null, errorSamplingRate: null,
            logSamplingRate: null, traceSamplingRate: null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        await _db.Entry(_project).ReloadAsync();
        Assert.Equal(10, _project.RageClickWindowSeconds);
        Assert.Equal(15, _project.RageClickRadiusPixels);
        Assert.Equal(3, _project.RageClickCount);
        Assert.True(_project.FilterChromeExtension);
    }

    [Fact]
    public async Task EditProjectSettings_CreatesFilterSettingsIfMissing()
    {
        // Create project without filter settings
        var proj2 = new Project { Name = "NoFilter", WorkspaceId = _workspace.Id };
        _db.Projects.Add(proj2);
        _db.SaveChanges();

        var result = await _mutation.EditProjectSettings(
            proj2.Id,
            excludedUsers: null, errorFilters: null,
            rageClickWindowSeconds: null, rageClickRadiusPixels: null, rageClickCount: null,
            filterChromeExtension: null, filterSessionsWithoutError: true,
            autoResolveStaleErrorsDayInterval: 14,
            sessionSamplingRate: null, errorSamplingRate: null,
            logSamplingRate: null, traceSamplingRate: null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(result.FilterSessionsWithoutError);
        Assert.Equal(14, result.AutoResolveStaleErrorsDayInterval);
    }

    // ── DeleteProject ───────────────────────────────────────────────

    [Fact]
    public async Task DeleteProject_RemovesProject()
    {
        var proj = new Project { Name = "ToDelete", WorkspaceId = _workspace.Id };
        _db.Projects.Add(proj);
        _db.SaveChanges();

        var result = await _mutation.DeleteProject(
            proj.Id, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        Assert.Null(await _db.Projects.FindAsync(proj.Id));
    }

    [Fact]
    public async Task DeleteProject_NonexistentProject_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteProject(
                99999, MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteProject_MemberRole_Throws()
    {
        var member = new Admin { Uid = "del-member", Email = "delmem@test.com" };
        _db.Admins.Add(member);
        _db.SaveChanges();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = member.Id, WorkspaceId = _workspace.Id, Role = "MEMBER",
        });
        _db.SaveChanges();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.DeleteProject(
                _project.Id, MakePrincipal("del-member"), _authz, _db, CancellationToken.None));
    }

    // ── SaveBillingPlan (Retention) ─────────────────────────────────

    [Fact]
    public async Task SaveBillingPlan_UpdatesRetention()
    {
        var result = await _mutation.SaveBillingPlan(
            _workspace.Id,
            RetentionPeriod.TwelveMonths,
            RetentionPeriod.ThreeMonths,
            RetentionPeriod.SevenDays,
            RetentionPeriod.ThirtyDays,
            RetentionPeriod.TwoYears,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        await _db.Entry(_workspace).ReloadAsync();
        Assert.Equal(RetentionPeriod.TwelveMonths, _workspace.RetentionPeriod);
        Assert.Equal(RetentionPeriod.ThreeMonths, _workspace.ErrorsRetentionPeriod);
        Assert.Equal(RetentionPeriod.SevenDays, _workspace.LogsRetentionPeriod);
        Assert.Equal(RetentionPeriod.ThirtyDays, _workspace.TracesRetentionPeriod);
        Assert.Equal(RetentionPeriod.TwoYears, _workspace.MetricsRetentionPeriod);
    }

    [Fact]
    public async Task SaveBillingPlan_NonexistentWorkspace_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.SaveBillingPlan(
                99999,
                RetentionPeriod.ThirtyDays, RetentionPeriod.ThirtyDays,
                RetentionPeriod.ThirtyDays, RetentionPeriod.ThirtyDays,
                RetentionPeriod.ThirtyDays,
                MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None));
    }

    // ── ChangeProjectMembership ─────────────────────────────────────

    [Fact]
    public async Task ChangeProjectMembership_SetsProjectIds()
    {
        var member = new Admin { Uid = "cpm-member", Email = "cpm@test.com" };
        _db.Admins.Add(member);
        _db.SaveChanges();
        _db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = member.Id, WorkspaceId = _workspace.Id, Role = "MEMBER",
        });
        _db.SaveChanges();

        var result = await _mutation.ChangeProjectMembership(
            _workspace.Id, member.Id, [_project.Id],
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        var wa = await _db.WorkspaceAdmins
            .FirstAsync(x => x.AdminId == member.Id && x.WorkspaceId == _workspace.Id);
        Assert.NotNull(wa.ProjectIds);
        Assert.Contains(_project.Id, wa.ProjectIds!);
    }

    [Fact]
    public async Task ChangeProjectMembership_AdminNotInWorkspace_Throws()
    {
        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.ChangeProjectMembership(
                _workspace.Id, 99999, [_project.Id],
                MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None));
    }

    // ── UpdateAllowedEmailOrigins ───────────────────────────────────

    [Fact]
    public async Task UpdateAllowedEmailOrigins_SetsOrigins()
    {
        var result = await _mutation.UpdateAllowedEmailOrigins(
            _workspace.Id, ["company.com", "org.com"],
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        Assert.True(result);
        await _db.Entry(_workspace).ReloadAsync();
        Assert.Equal("company.com,org.com", _workspace.AllowedAutoJoinEmailOrigins);
    }

    [Fact]
    public async Task UpdateAllowedEmailOrigins_NullList_ClearsOrigins()
    {
        _workspace.AllowedAutoJoinEmailOrigins = "old.com";
        _db.SaveChanges();

        await _mutation.UpdateAllowedEmailOrigins(
            _workspace.Id, null,
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        await _db.Entry(_workspace).ReloadAsync();
        Assert.Null(_workspace.AllowedAutoJoinEmailOrigins);
    }

    [Fact]
    public async Task UpdateAllowedEmailOrigins_EmptyList_SetsEmpty()
    {
        await _mutation.UpdateAllowedEmailOrigins(
            _workspace.Id, [],
            MakePrincipal("admin-uid"), _authz, _db, CancellationToken.None);

        await _db.Entry(_workspace).ReloadAsync();
        Assert.Equal("", _workspace.AllowedAutoJoinEmailOrigins);
    }
}
