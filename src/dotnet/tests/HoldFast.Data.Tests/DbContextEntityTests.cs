using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.Data.Tests;

// ── DbSet Accessibility Tests ───────────────────────────────────────

public class DbSetAccessibilityTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public void AllCoreSets_Accessible()
    {
        using var db = _factory.Create();
        Assert.NotNull(db.Organizations);
        Assert.NotNull(db.Workspaces);
        Assert.NotNull(db.Admins);
        Assert.NotNull(db.WorkspaceAdmins);
        Assert.NotNull(db.WorkspaceInviteLinks);
        Assert.NotNull(db.WorkspaceAccessRequests);
        Assert.NotNull(db.Projects);
        Assert.NotNull(db.SetupEvents);
        Assert.NotNull(db.ProjectFilterSettings);
        Assert.NotNull(db.ProjectClientSamplingSettings);
        Assert.NotNull(db.AllWorkspaceSettings);
    }

    [Fact]
    public void AllSessionSets_Accessible()
    {
        using var db = _factory.Create();
        Assert.NotNull(db.Sessions);
        Assert.NotNull(db.SessionIntervals);
        Assert.NotNull(db.SessionExports);
        Assert.NotNull(db.SessionInsights);
        Assert.NotNull(db.SessionAdminsViews);
        Assert.NotNull(db.EventChunks);
        Assert.NotNull(db.RageClickEvents);
    }

    [Fact]
    public void AllErrorSets_Accessible()
    {
        using var db = _factory.Create();
        Assert.NotNull(db.ErrorGroups);
        Assert.NotNull(db.ErrorObjects);
        Assert.NotNull(db.ErrorFingerprints);
        Assert.NotNull(db.ErrorGroupEmbeddings);
        Assert.NotNull(db.ErrorTags);
        Assert.NotNull(db.ErrorComments);
        Assert.NotNull(db.ErrorGroupActivityLogs);
        Assert.NotNull(db.ErrorGroupAdminsViews);
        Assert.NotNull(db.ExternalAttachments);
    }

    [Fact]
    public void AllCommentSets_Accessible()
    {
        using var db = _factory.Create();
        Assert.NotNull(db.SessionComments);
        Assert.NotNull(db.SessionCommentTags);
        Assert.NotNull(db.CommentReplies);
        Assert.NotNull(db.CommentFollowers);
        Assert.NotNull(db.CommentSlackThreads);
    }

    [Fact]
    public void AllAlertSets_Accessible()
    {
        using var db = _factory.Create();
        Assert.NotNull(db.Alerts);
        Assert.NotNull(db.AlertDestinations);
        Assert.NotNull(db.ErrorAlerts);
        Assert.NotNull(db.ErrorAlertEvents);
        Assert.NotNull(db.SessionAlerts);
        Assert.NotNull(db.SessionAlertEvents);
        Assert.NotNull(db.LogAlerts);
        Assert.NotNull(db.LogAlertEvents);
        Assert.NotNull(db.MetricMonitors);
    }

    [Fact]
    public void AllDashboardSets_Accessible()
    {
        using var db = _factory.Create();
        Assert.NotNull(db.Dashboards);
        Assert.NotNull(db.DashboardMetrics);
        Assert.NotNull(db.DashboardMetricFilters);
        Assert.NotNull(db.Graphs);
        Assert.NotNull(db.Visualizations);
    }

    [Fact]
    public void AllIntegrationSets_Accessible()
    {
        using var db = _factory.Create();
        Assert.NotNull(db.IntegrationProjectMappings);
        Assert.NotNull(db.IntegrationWorkspaceMappings);
        Assert.NotNull(db.VercelIntegrationConfigs);
        Assert.NotNull(db.ResthookSubscriptions);
        Assert.NotNull(db.OAuthClientStores);
        Assert.NotNull(db.OAuthOperations);
        Assert.NotNull(db.SSOClients);
    }

    [Fact]
    public void AllMiscSets_Accessible()
    {
        using var db = _factory.Create();
        Assert.NotNull(db.Fields);
        Assert.NotNull(db.EnhancedUserDetails);
        Assert.NotNull(db.RegistrationData);
        Assert.NotNull(db.SavedSegments);
        Assert.NotNull(db.SavedAssets);
        Assert.NotNull(db.ProjectAssetTransforms);
        Assert.NotNull(db.DailySessionCounts);
        Assert.NotNull(db.DailyErrorCounts);
        Assert.NotNull(db.EmailSignups);
        Assert.NotNull(db.EmailOptOuts);
        Assert.NotNull(db.Services);
        Assert.NotNull(db.DeleteSessionsTasks);
        Assert.NotNull(db.UserJourneySteps);
        Assert.NotNull(db.SystemConfigurations);
        Assert.NotNull(db.LogAdminsViews);
    }

    public void Dispose() => _factory.Dispose();
}

// ── CRUD All Major Entities ─────────────────────────────────────────

public class CrudAllEntitiesTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    private async Task<(Workspace ws, Project project, Admin admin)> SeedBaseData(HoldFastDbContext db)
    {
        var ws = new Workspace { Name = "CRUD Test WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        var project = new Project { Name = "CRUD App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        var admin = new Admin { Uid = "crud-admin", Name = "Tester", Email = "test@test.com" };
        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        return (ws, project, admin);
    }

    [Fact]
    public async Task Crud_Session()
    {
        using var db = _factory.Create();
        var (_, project, _) = await SeedBaseData(db);

        // Create
        var session = new Session { SecureId = "crud-sess", ProjectId = project.Id, Environment = "test" };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        Assert.True(session.Id > 0);

        // Read
        var read = await db.Sessions.FindAsync(session.Id);
        Assert.NotNull(read);
        Assert.Equal("crud-sess", read!.SecureId);

        // Update
        read.Environment = "production";
        await db.SaveChangesAsync();
        var updated = await db.Sessions.FindAsync(session.Id);
        Assert.Equal("production", updated!.Environment);

        // Delete
        db.Sessions.Remove(updated);
        await db.SaveChangesAsync();
        Assert.Null(await db.Sessions.FindAsync(session.Id));
    }

    [Fact]
    public async Task Crud_ErrorGroup()
    {
        using var db = _factory.Create();
        var (_, project, _) = await SeedBaseData(db);

        var eg = new ErrorGroup { ProjectId = project.Id, Event = "NPE", Type = "NullRef", SecureId = "eg-1" };
        db.ErrorGroups.Add(eg);
        await db.SaveChangesAsync();
        Assert.True(eg.Id > 0);

        var read = await db.ErrorGroups.FindAsync(eg.Id);
        Assert.Equal("NPE", read!.Event);

        read.State = ErrorGroupState.Resolved;
        await db.SaveChangesAsync();
        Assert.Equal(ErrorGroupState.Resolved, (await db.ErrorGroups.FindAsync(eg.Id))!.State);

        db.ErrorGroups.Remove(read);
        await db.SaveChangesAsync();
        Assert.Null(await db.ErrorGroups.FindAsync(eg.Id));
    }

    [Fact]
    public async Task Crud_ErrorObject()
    {
        using var db = _factory.Create();
        var (_, project, _) = await SeedBaseData(db);

        var eg = new ErrorGroup { ProjectId = project.Id, Event = "E", Type = "T" };
        db.ErrorGroups.Add(eg);
        await db.SaveChangesAsync();

        var eo = new ErrorObject { ProjectId = project.Id, ErrorGroupId = eg.Id, Event = "err-1" };
        db.ErrorObjects.Add(eo);
        await db.SaveChangesAsync();
        Assert.True(eo.Id > 0);

        db.ErrorObjects.Remove(eo);
        await db.SaveChangesAsync();
        Assert.Empty(await db.ErrorObjects.ToListAsync());
    }

    [Fact]
    public async Task Crud_Field()
    {
        using var db = _factory.Create();
        var (_, project, _) = await SeedBaseData(db);

        var field = new Field { ProjectId = project.Id, Type = "session", Name = "os_name", Value = "Windows" };
        db.Fields.Add(field);
        await db.SaveChangesAsync();
        Assert.True(field.Id > 0);

        var read = await db.Fields.FindAsync(field.Id);
        Assert.Equal("os_name", read!.Name);
        Assert.Equal("Windows", read.Value);

        db.Fields.Remove(read);
        await db.SaveChangesAsync();
        Assert.Empty(await db.Fields.ToListAsync());
    }

    [Fact]
    public async Task Crud_EventChunk()
    {
        using var db = _factory.Create();
        var (_, project, _) = await SeedBaseData(db);

        var session = new Session { SecureId = "ec-sess", ProjectId = project.Id };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        var chunk = new EventChunk { SessionId = session.Id, ChunkIndex = 0, Timestamp = 1234567890 };
        db.EventChunks.Add(chunk);
        await db.SaveChangesAsync();
        Assert.True(chunk.Id > 0);

        var read = await db.EventChunks.FindAsync(chunk.Id);
        Assert.Equal(0, read!.ChunkIndex);
        Assert.Equal(1234567890, read.Timestamp);
    }

    [Fact]
    public async Task Crud_SessionInterval()
    {
        using var db = _factory.Create();
        var (_, project, _) = await SeedBaseData(db);

        var session = new Session { SecureId = "si-sess", ProjectId = project.Id };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        var interval = new SessionInterval
        {
            SessionId = session.Id, StartTime = 0, EndTime = 5000, Duration = 5000, Active = true
        };
        db.SessionIntervals.Add(interval);
        await db.SaveChangesAsync();
        Assert.True(interval.Id > 0);

        var read = await db.SessionIntervals.FindAsync(interval.Id);
        Assert.True(read!.Active);
        Assert.Equal(5000, read.Duration);
    }

    [Fact]
    public async Task Crud_RageClickEvent()
    {
        using var db = _factory.Create();
        var (_, project, _) = await SeedBaseData(db);

        var session = new Session { SecureId = "rc-sess", ProjectId = project.Id };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        var rce = new RageClickEvent
        {
            ProjectId = project.Id,
            SessionId = session.Id,
            TotalClicks = 15,
            Selector = "button.submit",
            StartTimestamp = 100,
            EndTimestamp = 200,
        };
        db.RageClickEvents.Add(rce);
        await db.SaveChangesAsync();
        Assert.True(rce.Id > 0);
    }

    [Fact]
    public async Task Crud_ErrorFingerprint()
    {
        using var db = _factory.Create();
        var (_, project, _) = await SeedBaseData(db);

        var eg = new ErrorGroup { ProjectId = project.Id, Event = "E", Type = "T" };
        db.ErrorGroups.Add(eg);
        await db.SaveChangesAsync();

        var fp = new ErrorFingerprint { ProjectId = project.Id, ErrorGroupId = eg.Id, Type = "stacktrace", Value = "hash123" };
        db.ErrorFingerprints.Add(fp);
        await db.SaveChangesAsync();
        Assert.True(fp.Id > 0);

        var read = await db.ErrorFingerprints.FindAsync(fp.Id);
        Assert.Equal("stacktrace", read!.Type);
        Assert.Equal("hash123", read.Value);
    }

    [Fact]
    public async Task Crud_ErrorTag()
    {
        using var db = _factory.Create();
        var (_, project, _) = await SeedBaseData(db);

        var eg = new ErrorGroup { ProjectId = project.Id, Event = "E", Type = "T" };
        db.ErrorGroups.Add(eg);
        await db.SaveChangesAsync();

        var tag = new ErrorTag { ErrorGroupId = eg.Id, Title = "Performance", Description = "Slow query" };
        db.ErrorTags.Add(tag);
        await db.SaveChangesAsync();
        Assert.True(tag.Id > 0);
    }

    [Fact]
    public async Task Crud_ErrorComment()
    {
        using var db = _factory.Create();
        var (_, project, admin) = await SeedBaseData(db);

        var eg = new ErrorGroup { ProjectId = project.Id, Event = "E", Type = "T" };
        db.ErrorGroups.Add(eg);
        await db.SaveChangesAsync();

        var comment = new ErrorComment { ErrorGroupId = eg.Id, AdminId = admin.Id, Text = "Investigating" };
        db.ErrorComments.Add(comment);
        await db.SaveChangesAsync();
        Assert.True(comment.Id > 0);

        var read = await db.ErrorComments.FindAsync(comment.Id);
        Assert.Equal("Investigating", read!.Text);
    }

    [Fact]
    public async Task Crud_Dashboard()
    {
        using var db = _factory.Create();
        var (_, project, _) = await SeedBaseData(db);

        var dash = new Dashboard { ProjectId = project.Id, Name = "Perf" };
        db.Dashboards.Add(dash);
        await db.SaveChangesAsync();
        Assert.True(dash.Id > 0);

        var read = await db.Dashboards.FindAsync(dash.Id);
        Assert.Equal("Perf", read!.Name);
    }

    [Fact]
    public async Task Crud_DashboardMetric()
    {
        using var db = _factory.Create();
        var (_, project, _) = await SeedBaseData(db);

        var dash = new Dashboard { ProjectId = project.Id, Name = "D" };
        db.Dashboards.Add(dash);
        await db.SaveChangesAsync();

        var metric = new DashboardMetric { DashboardId = dash.Id, Name = "FCP", Description = "First Contentful Paint" };
        db.DashboardMetrics.Add(metric);
        await db.SaveChangesAsync();
        Assert.True(metric.Id > 0);
    }

    [Fact]
    public async Task Crud_Alert_WithDestination()
    {
        using var db = _factory.Create();
        var (_, project, _) = await SeedBaseData(db);

        var alert = new Alert { ProjectId = project.Id, Name = "Error spike", ProductType = "errors" };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();

        var dest = new AlertDestination { AlertId = alert.Id, DestinationType = "email", TypeId = "user@test.com" };
        db.AlertDestinations.Add(dest);
        await db.SaveChangesAsync();

        var loaded = await db.Alerts.Include(a => a.Destinations).FirstAsync(a => a.Id == alert.Id);
        Assert.Single(loaded.Destinations);
    }

    [Fact]
    public async Task Crud_Service()
    {
        using var db = _factory.Create();
        var (_, project, _) = await SeedBaseData(db);

        var svc = new Service { ProjectId = project.Id, Name = "api-server", Status = "healthy" };
        db.Services.Add(svc);
        await db.SaveChangesAsync();
        Assert.True(svc.Id > 0);
    }

    [Fact]
    public async Task Crud_SavedSegment()
    {
        using var db = _factory.Create();
        var (_, project, _) = await SeedBaseData(db);

        var seg = new SavedSegment { ProjectId = project.Id, Name = "Errors > 100", Params = "{}", EntityType = "error" };
        db.SavedSegments.Add(seg);
        await db.SaveChangesAsync();
        Assert.True(seg.Id > 0);
    }

    [Fact]
    public async Task Crud_WorkspaceAdmin()
    {
        using var db = _factory.Create();
        var (ws, _, admin) = await SeedBaseData(db);

        var wa = new WorkspaceAdmin { AdminId = admin.Id, WorkspaceId = ws.Id, Role = "ADMIN" };
        db.WorkspaceAdmins.Add(wa);
        await db.SaveChangesAsync();

        var read = await db.WorkspaceAdmins.FindAsync(admin.Id, ws.Id);
        Assert.NotNull(read);
        Assert.Equal("ADMIN", read!.Role);

        // Update role
        read.Role = "MEMBER";
        await db.SaveChangesAsync();
        var updated = await db.WorkspaceAdmins.FindAsync(admin.Id, ws.Id);
        Assert.Equal("MEMBER", updated!.Role);

        // Delete
        db.WorkspaceAdmins.Remove(updated);
        await db.SaveChangesAsync();
        Assert.Null(await db.WorkspaceAdmins.FindAsync(admin.Id, ws.Id));
    }

    [Fact]
    public async Task Crud_Organization()
    {
        using var db = _factory.Create();

        var org = new Organization { Name = "Test Org" };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        Assert.True(org.Id > 0);

        var read = await db.Organizations.FindAsync(org.Id);
        Assert.Equal("Test Org", read!.Name);
    }

    [Fact]
    public async Task Crud_SSOClient()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        var sso = new SSOClient { WorkspaceId = ws.Id, Domain = "example.com", ProviderUrl = "https://idp.example.com" };
        db.SSOClients.Add(sso);
        await db.SaveChangesAsync();
        Assert.True(sso.Id > 0);
    }

    [Fact]
    public async Task Crud_IntegrationWorkspaceMapping()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        var mapping = new IntegrationWorkspaceMapping
        {
            IntegrationType = "slack",
            WorkspaceId = ws.Id,
            AccessToken = "xoxb-token",
        };
        db.IntegrationWorkspaceMappings.Add(mapping);
        await db.SaveChangesAsync();
        Assert.True(mapping.Id > 0);
    }

    [Fact]
    public async Task Crud_EmailSignup()
    {
        using var db = _factory.Create();

        var signup = new EmailSignup { Email = "user@example.com", AdsenseAction = "signup" };
        db.EmailSignups.Add(signup);
        await db.SaveChangesAsync();
        Assert.True(signup.Id > 0);
    }

    [Fact]
    public async Task Crud_SystemConfiguration()
    {
        using var db = _factory.Create();

        var config = new SystemConfiguration { Active = true, MainWorkerCount = 4 };
        db.SystemConfigurations.Add(config);
        await db.SaveChangesAsync();

        var read = await db.SystemConfigurations.FindAsync(config.Id);
        Assert.True(read!.Active);
        Assert.Equal(4, read.MainWorkerCount);
    }

    [Fact]
    public async Task Crud_Graph()
    {
        using var db = _factory.Create();
        var (_, project, _) = await SeedBaseData(db);

        var graph = new Graph { ProjectId = project.Id, Title = "Error Rate" };
        db.Graphs.Add(graph);
        await db.SaveChangesAsync();
        Assert.True(graph.Id > 0);
    }

    [Fact]
    public async Task Crud_Visualization()
    {
        using var db = _factory.Create();
        var (_, project, _) = await SeedBaseData(db);

        var viz = new Visualization { ProjectId = project.Id, Name = "Overview" };
        db.Visualizations.Add(viz);
        await db.SaveChangesAsync();
        Assert.True(viz.Id > 0);
    }

    public void Dispose() => _factory.Dispose();
}

// ── Foreign Key Relationship Tests ──────────────────────────────────

public class ForeignKeyTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task Session_BelongsTo_Project()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.Sessions.Add(new Session { SecureId = "s1", ProjectId = project.Id });
        await db.SaveChangesAsync();

        var session = await db.Sessions.Include(s => s.Project).FirstAsync();
        Assert.Equal(project.Id, session.ProjectId);
        Assert.Equal("App", session.Project.Name);
    }

    [Fact]
    public async Task ErrorGroup_BelongsTo_Project()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.ErrorGroups.Add(new ErrorGroup { ProjectId = project.Id, Event = "E", Type = "T" });
        await db.SaveChangesAsync();

        var eg = await db.ErrorGroups.Include(e => e.Project).FirstAsync();
        Assert.Equal(project.Id, eg.ProjectId);
        Assert.Equal("App", eg.Project.Name);
    }

    [Fact]
    public async Task ErrorObject_BelongsTo_ErrorGroup()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var eg = new ErrorGroup { ProjectId = project.Id, Event = "E", Type = "T" };
        db.ErrorGroups.Add(eg);
        await db.SaveChangesAsync();

        db.ErrorObjects.Add(new ErrorObject { ProjectId = project.Id, ErrorGroupId = eg.Id, Event = "err" });
        await db.SaveChangesAsync();

        var eo = await db.ErrorObjects.Include(e => e.ErrorGroup).FirstAsync();
        Assert.Equal(eg.Id, eo.ErrorGroupId);
        Assert.Equal("E", eo.ErrorGroup.Event);
    }

    [Fact]
    public async Task Project_BelongsTo_Workspace()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "My WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        db.Projects.Add(new Project { Name = "App", WorkspaceId = ws.Id });
        await db.SaveChangesAsync();

        var project = await db.Projects.Include(p => p.Workspace).FirstAsync();
        Assert.Equal(ws.Id, project.WorkspaceId);
        Assert.Equal("My WS", project.Workspace.Name);
    }

    [Fact]
    public async Task WorkspaceAdmin_Links_Workspace_And_Admin()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        var admin = new Admin { Uid = "uid-fk", Name = "FK Admin" };
        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        db.WorkspaceAdmins.Add(new WorkspaceAdmin { AdminId = admin.Id, WorkspaceId = ws.Id, Role = "ADMIN" });
        await db.SaveChangesAsync();

        var wa = await db.WorkspaceAdmins
            .Include(x => x.Admin)
            .Include(x => x.Workspace)
            .FirstAsync();
        Assert.Equal("FK Admin", wa.Admin.Name);
        Assert.Equal("WS", wa.Workspace.Name);
    }

    [Fact]
    public async Task AlertDestination_BelongsTo_Alert()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var alert = new Alert { ProjectId = project.Id, Name = "Alert", ProductType = "errors" };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();

        db.AlertDestinations.Add(new AlertDestination { AlertId = alert.Id, DestinationType = "slack" });
        await db.SaveChangesAsync();

        var dest = await db.AlertDestinations.Include(d => d.Alert).FirstAsync();
        Assert.Equal(alert.Id, dest.AlertId);
        Assert.Equal("Alert", dest.Alert.Name);
    }

    [Fact]
    public async Task ErrorComment_BelongsTo_ErrorGroup_And_Admin()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        var admin = new Admin { Uid = "ec-admin" };
        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var eg = new ErrorGroup { ProjectId = project.Id, Event = "E", Type = "T" };
        db.ErrorGroups.Add(eg);
        await db.SaveChangesAsync();

        db.ErrorComments.Add(new ErrorComment { ErrorGroupId = eg.Id, AdminId = admin.Id, Text = "Note" });
        await db.SaveChangesAsync();

        var comment = await db.ErrorComments
            .Include(c => c.ErrorGroup)
            .Include(c => c.Admin)
            .FirstAsync();
        Assert.Equal(eg.Id, comment.ErrorGroupId);
        Assert.Equal(admin.Id, comment.AdminId);
    }

    [Fact]
    public async Task DashboardMetric_BelongsTo_Dashboard()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();
        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var dash = new Dashboard { ProjectId = project.Id, Name = "Dash" };
        db.Dashboards.Add(dash);
        await db.SaveChangesAsync();

        db.DashboardMetrics.Add(new DashboardMetric { DashboardId = dash.Id, Name = "M", Description = "D" });
        await db.SaveChangesAsync();

        var metric = await db.DashboardMetrics.Include(m => m.Dashboard).FirstAsync();
        Assert.Equal(dash.Id, metric.DashboardId);
    }

    [Fact]
    public async Task SessionComment_BelongsTo_Session_And_Admin()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        var admin = new Admin { Uid = "sc-admin" };
        db.Admins.Add(admin);
        await db.SaveChangesAsync();
        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var session = new Session { SecureId = "sc-sess", ProjectId = project.Id };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        db.SessionComments.Add(new SessionComment
        {
            ProjectId = project.Id,
            SessionId = session.Id,
            AdminId = admin.Id,
            Text = "Test comment",
        });
        await db.SaveChangesAsync();

        var comment = await db.SessionComments
            .Include(c => c.Session)
            .Include(c => c.Admin)
            .FirstAsync();
        Assert.Equal(session.Id, comment.SessionId);
        Assert.Equal(admin.Id, comment.AdminId);
    }

    public void Dispose() => _factory.Dispose();
}

// ── Unique Constraint Tests ─────────────────────────────────────────

public class UniqueConstraintTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task AllWorkspaceSettings_UniqueWorkspaceId()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        db.AllWorkspaceSettings.Add(new AllWorkspaceSettings { WorkspaceId = ws.Id });
        await db.SaveChangesAsync();

        db.AllWorkspaceSettings.Add(new AllWorkspaceSettings { WorkspaceId = ws.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SetupEvent_UniqueProjectIdAndType()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();
        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.SetupEvents.Add(new SetupEvent { ProjectId = project.Id, Type = "backend" });
        await db.SaveChangesAsync();

        db.SetupEvents.Add(new SetupEvent { ProjectId = project.Id, Type = "backend" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SetupEvent_DifferentTypes_OK()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();
        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.SetupEvents.Add(new SetupEvent { ProjectId = project.Id, Type = "backend" });
        db.SetupEvents.Add(new SetupEvent { ProjectId = project.Id, Type = "frontend" });
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.SetupEvents.CountAsync());
    }

    [Fact]
    public async Task EnhancedUserDetails_UniqueEmail()
    {
        using var db = _factory.Create();

        db.EnhancedUserDetails.Add(new EnhancedUserDetails { Email = "unique@test.com" });
        await db.SaveChangesAsync();

        db.EnhancedUserDetails.Add(new EnhancedUserDetails { Email = "unique@test.com" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task WorkspaceAccessRequest_UniqueAdminId()
    {
        using var db = _factory.Create();

        db.WorkspaceAccessRequests.Add(new WorkspaceAccessRequest { AdminId = 1, LastRequestedWorkspace = 1 });
        await db.SaveChangesAsync();

        db.WorkspaceAccessRequests.Add(new WorkspaceAccessRequest { AdminId = 1, LastRequestedWorkspace = 2 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    public void Dispose() => _factory.Dispose();
}

// ── Model Creation / Index Tests ────────────────────────────────────

public class ModelCreationTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public void EnsureCreated_DoesNotThrow()
    {
        using var db = _factory.Create();
        Assert.NotNull(db.Model);
    }

    [Fact]
    public void Model_HasExpectedEntityCount()
    {
        using var db = _factory.Create();
        var entityTypes = db.Model.GetEntityTypes().ToList();
        // We have many entity types; just check we have a reasonable number
        Assert.True(entityTypes.Count >= 40, $"Expected >= 40 entity types, got {entityTypes.Count}");
    }

    [Fact]
    public void WorkspaceAdmin_HasCompositeKey()
    {
        using var db = _factory.Create();
        var entityType = db.Model.FindEntityType(typeof(WorkspaceAdmin));
        Assert.NotNull(entityType);
        var pk = entityType!.FindPrimaryKey();
        Assert.NotNull(pk);
        Assert.Equal(2, pk!.Properties.Count);
    }

    [Fact]
    public void RetentionPeriod_IsMapped()
    {
        using var db = _factory.Create();
        var entityType = db.Model.FindEntityType(typeof(Workspace));
        var prop = entityType!.FindProperty(nameof(Workspace.RetentionPeriod));
        Assert.NotNull(prop);
    }

    [Fact]
    public void Project_VerboseId_IsIgnored()
    {
        using var db = _factory.Create();
        var entityType = db.Model.FindEntityType(typeof(Project));
        var prop = entityType!.FindProperty("VerboseId");
        Assert.Null(prop); // Ignored, not mapped
    }

    public void Dispose() => _factory.Dispose();
}

// ── Auto-Increment Tests ────────────────────────────────────────────

public class AutoIncrementTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task Workspace_AutoIncrementIds()
    {
        using var db = _factory.Create();
        db.Workspaces.Add(new Workspace { Name = "A" });
        db.Workspaces.Add(new Workspace { Name = "B" });
        db.Workspaces.Add(new Workspace { Name = "C" });
        await db.SaveChangesAsync();

        var all = await db.Workspaces.OrderBy(w => w.Id).ToListAsync();
        Assert.Equal(3, all.Count);
        Assert.True(all[0].Id < all[1].Id);
        Assert.True(all[1].Id < all[2].Id);
    }

    [Fact]
    public async Task Session_AutoIncrementIds()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();
        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.Sessions.Add(new Session { SecureId = "s1", ProjectId = project.Id });
        db.Sessions.Add(new Session { SecureId = "s2", ProjectId = project.Id });
        await db.SaveChangesAsync();

        var all = await db.Sessions.OrderBy(s => s.Id).ToListAsync();
        Assert.True(all[1].Id > all[0].Id);
    }

    public void Dispose() => _factory.Dispose();
}

// ── List/Array Property Tests (JSON conversion in SQLite) ───────────

public class ListPropertyTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task Project_ExcludedUsers_PersistsAsJson()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        var project = new Project
        {
            Name = "App",
            WorkspaceId = ws.Id,
            ExcludedUsers = ["bot@test.com", "admin@test.com"],
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var read = await db.Projects.FindAsync(project.Id);
        Assert.Equal(2, read!.ExcludedUsers.Count);
        Assert.Contains("bot@test.com", read.ExcludedUsers);
        Assert.Contains("admin@test.com", read.ExcludedUsers);
    }

    [Fact]
    public async Task Project_Platforms_PersistsAsJson()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        var project = new Project
        {
            Name = "App",
            WorkspaceId = ws.Id,
            Platforms = ["web", "ios", "android"],
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var read = await db.Projects.FindAsync(project.Id);
        Assert.Equal(3, read!.Platforms.Count);
    }

    [Fact]
    public async Task Project_EmptyLists_Persist()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var read = await db.Projects.FindAsync(project.Id);
        Assert.NotNull(read!.ExcludedUsers);
        Assert.Empty(read.ExcludedUsers);
    }

    [Fact]
    public async Task WorkspaceAdmin_ProjectIds_PersistsAsJson()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        var admin = new Admin { Uid = "uid-list" };
        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        var wa = new WorkspaceAdmin { AdminId = admin.Id, WorkspaceId = ws.Id, ProjectIds = [10, 20, 30] };
        db.WorkspaceAdmins.Add(wa);
        await db.SaveChangesAsync();

        var read = await db.WorkspaceAdmins.FindAsync(admin.Id, ws.Id);
        Assert.Equal(3, read!.ProjectIds!.Count);
        Assert.Contains(20, read.ProjectIds);
    }

    [Fact]
    public async Task DashboardMetric_Groups_PersistsAsJson()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();
        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var dash = new Dashboard { ProjectId = project.Id, Name = "D" };
        db.Dashboards.Add(dash);
        await db.SaveChangesAsync();

        var metric = new DashboardMetric
        {
            DashboardId = dash.Id,
            Name = "M",
            Description = "D",
            Groups = ["frontend", "backend"],
        };
        db.DashboardMetrics.Add(metric);
        await db.SaveChangesAsync();

        var read = await db.DashboardMetrics.FindAsync(metric.Id);
        Assert.Equal(2, read!.Groups.Count);
    }

    public void Dispose() => _factory.Dispose();
}
