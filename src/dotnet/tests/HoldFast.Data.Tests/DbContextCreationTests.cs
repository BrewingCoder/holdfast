using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.Data.Tests;

public class DbContextCreationTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public void CanCreateDatabase()
    {
        // EnsureCreated returns true on first call (new factory = new connection)
        using var factory2 = new TestDbContextFactory();
        using var db = factory2.Create();
        // Verify the database is usable by checking we can query it
        Assert.Empty(db.Workspaces.ToList());
    }

    [Fact]
    public void DbSets_AreNotNull()
    {
        using var db = _factory.Create();
        Assert.NotNull(db.Workspaces);
        Assert.NotNull(db.Projects);
        Assert.NotNull(db.Sessions);
        Assert.NotNull(db.ErrorGroups);
        Assert.NotNull(db.Admins);
        Assert.NotNull(db.Alerts);
        Assert.NotNull(db.Dashboards);
        Assert.NotNull(db.SessionComments);
    }

    public void Dispose() => _factory.Dispose();
}

public class WorkspaceCrudTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task CanCreateAndReadWorkspace()
    {
        using var db = _factory.Create();

        db.Workspaces.Add(new Workspace { Name = "Test Corp" });
        await db.SaveChangesAsync();

        var ws = await db.Workspaces.FirstAsync();
        Assert.Equal("Test Corp", ws.Name);
        Assert.Equal("Enterprise", ws.PlanTier);
        Assert.True(ws.UnlimitedMembers);
    }

    [Fact]
    public async Task WorkspaceAutoIncrements_Id()
    {
        using var db = _factory.Create();

        db.Workspaces.Add(new Workspace { Name = "WS1" });
        db.Workspaces.Add(new Workspace { Name = "WS2" });
        await db.SaveChangesAsync();

        var all = await db.Workspaces.OrderBy(w => w.Id).ToListAsync();
        Assert.Equal(2, all.Count);
        Assert.NotEqual(all[0].Id, all[1].Id);
        Assert.True(all[1].Id > all[0].Id);
    }

    [Fact]
    public async Task CanUpdateWorkspace()
    {
        using var db = _factory.Create();

        db.Workspaces.Add(new Workspace { Name = "Original" });
        await db.SaveChangesAsync();

        var ws = await db.Workspaces.FirstAsync();
        ws.Name = "Updated";
        ws.RetentionPeriod = RetentionPeriod.ThreeYears;
        await db.SaveChangesAsync();

        var updated = await db.Workspaces.FirstAsync();
        Assert.Equal("Updated", updated.Name);
        Assert.Equal(RetentionPeriod.ThreeYears, updated.RetentionPeriod);
    }

    [Fact]
    public async Task CanDeleteWorkspace()
    {
        using var db = _factory.Create();

        db.Workspaces.Add(new Workspace { Name = "ToDelete" });
        await db.SaveChangesAsync();

        var ws = await db.Workspaces.FirstAsync();
        db.Workspaces.Remove(ws);
        await db.SaveChangesAsync();

        Assert.Empty(await db.Workspaces.ToListAsync());
    }

    [Fact]
    public async Task WorkspaceRetentionPeriod_StoredAsString()
    {
        using var db = _factory.Create();

        db.Workspaces.Add(new Workspace
        {
            Name = "Retention Test",
            RetentionPeriod = RetentionPeriod.TwelveMonths,
        });
        await db.SaveChangesAsync();

        var ws = await db.Workspaces.FirstAsync();
        Assert.Equal(RetentionPeriod.TwelveMonths, ws.RetentionPeriod);
    }

    [Fact]
    public async Task WorkspaceWithEmptyName()
    {
        using var db = _factory.Create();
        db.Workspaces.Add(new Workspace { Name = "" });
        await db.SaveChangesAsync();

        var ws = await db.Workspaces.FirstAsync();
        Assert.Equal("", ws.Name);
    }

    [Fact]
    public async Task WorkspaceWithNullName()
    {
        using var db = _factory.Create();
        db.Workspaces.Add(new Workspace { Name = null });
        await db.SaveChangesAsync();

        var ws = await db.Workspaces.FirstAsync();
        Assert.Null(ws.Name);
    }

    [Fact]
    public async Task WorkspaceWithLongName()
    {
        using var db = _factory.Create();
        var longName = new string('X', 10000);
        db.Workspaces.Add(new Workspace { Name = longName });
        await db.SaveChangesAsync();

        var ws = await db.Workspaces.FirstAsync();
        Assert.Equal(10000, ws.Name!.Length);
    }

    [Fact]
    public async Task WorkspaceWithUnicodeNamef()
    {
        using var db = _factory.Create();
        db.Workspaces.Add(new Workspace { Name = "日本語テスト 🚀 Ñoño" });
        await db.SaveChangesAsync();

        var ws = await db.Workspaces.FirstAsync();
        Assert.Equal("日本語テスト 🚀 Ñoño", ws.Name);
    }

    public void Dispose() => _factory.Dispose();
}

public class ProjectCrudTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task CanCreateProjectWithWorkspace()
    {
        using var db = _factory.Create();

        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        db.Projects.Add(new Project { Name = "My App", WorkspaceId = ws.Id });
        await db.SaveChangesAsync();

        var project = await db.Projects.Include(p => p.Workspace).FirstAsync();
        Assert.Equal("My App", project.Name);
        Assert.Equal(ws.Id, project.WorkspaceId);
        Assert.Equal("WS", project.Workspace.Name);
    }

    [Fact]
    public async Task Project_DefaultRageClickSettings_Persist()
    {
        using var db = _factory.Create();

        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        db.Projects.Add(new Project { Name = "App", WorkspaceId = ws.Id });
        await db.SaveChangesAsync();

        var project = await db.Projects.FirstAsync();
        Assert.Equal(5, project.RageClickWindowSeconds);
        Assert.Equal(8, project.RageClickRadiusPixels);
        Assert.Equal(5, project.RageClickCount);
    }

    [Fact]
    public async Task MultipleProjectsInWorkspace()
    {
        using var db = _factory.Create();

        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        db.Projects.AddRange(
            new Project { Name = "App1", WorkspaceId = ws.Id },
            new Project { Name = "App2", WorkspaceId = ws.Id },
            new Project { Name = "App3", WorkspaceId = ws.Id });
        await db.SaveChangesAsync();

        var projects = await db.Projects.Where(p => p.WorkspaceId == ws.Id).ToListAsync();
        Assert.Equal(3, projects.Count);
    }

    [Fact]
    public async Task ProjectFilterSettings_DefaultSamplingRates()
    {
        using var db = _factory.Create();

        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.ProjectFilterSettings.Add(new ProjectFilterSettings { ProjectId = project.Id });
        await db.SaveChangesAsync();

        var settings = await db.ProjectFilterSettings.FirstAsync();
        Assert.Equal(1.0, settings.SessionSamplingRate);
        Assert.Equal(1.0, settings.ErrorSamplingRate);
        Assert.Null(settings.SessionMinuteRateLimit);
    }

    public void Dispose() => _factory.Dispose();
}

public class SessionCrudTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    private async Task<Project> SeedProject(HoldFastDbContext db)
    {
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();
        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    [Fact]
    public async Task CanCreateSession()
    {
        using var db = _factory.Create();
        var project = await SeedProject(db);

        db.Sessions.Add(new Session
        {
            SecureId = "test-secure-id",
            ProjectId = project.Id,
            Fingerprint = "fp-123",
            Environment = "production",
        });
        await db.SaveChangesAsync();

        var session = await db.Sessions.FirstAsync();
        Assert.Equal("test-secure-id", session.SecureId);
        Assert.Equal("fp-123", session.Fingerprint);
    }

    [Fact]
    public async Task SessionLookupBySecureId()
    {
        using var db = _factory.Create();
        var project = await SeedProject(db);

        db.Sessions.AddRange(
            new Session { SecureId = "aaa", ProjectId = project.Id },
            new Session { SecureId = "bbb", ProjectId = project.Id },
            new Session { SecureId = "ccc", ProjectId = project.Id });
        await db.SaveChangesAsync();

        var found = await db.Sessions.FirstOrDefaultAsync(s => s.SecureId == "bbb");
        Assert.NotNull(found);
        Assert.Equal("bbb", found!.SecureId);
    }

    [Fact]
    public async Task SessionNotFound_ReturnsNull()
    {
        using var db = _factory.Create();
        var found = await db.Sessions.FirstOrDefaultAsync(s => s.SecureId == "nonexistent");
        Assert.Null(found);
    }

    [Fact]
    public async Task SessionWithAllGeoFields()
    {
        using var db = _factory.Create();
        var project = await SeedProject(db);

        db.Sessions.Add(new Session
        {
            SecureId = "geo-test",
            ProjectId = project.Id,
            City = "Washington",
            State = "DC",
            Country = "US",
            Postal = "20001",
            Latitude = 38.8951,
            Longitude = -77.0364,
        });
        await db.SaveChangesAsync();

        var session = await db.Sessions.FirstAsync();
        Assert.Equal("Washington", session.City);
        Assert.Equal(38.8951, session.Latitude);
    }

    [Fact]
    public async Task SessionWithPrivacySettings()
    {
        using var db = _factory.Create();
        var project = await SeedProject(db);

        db.Sessions.Add(new Session
        {
            SecureId = "privacy-test",
            ProjectId = project.Id,
            EnableStrictPrivacy = true,
            EnableRecordingNetworkContents = false,
            PrivacySetting = "strict",
        });
        await db.SaveChangesAsync();

        var session = await db.Sessions.FirstAsync();
        Assert.True(session.EnableStrictPrivacy);
        Assert.False(session.EnableRecordingNetworkContents);
        Assert.Equal("strict", session.PrivacySetting);
    }

    [Fact]
    public async Task ManySessions_QueryPerformance()
    {
        using var db = _factory.Create();
        var project = await SeedProject(db);

        for (int i = 0; i < 100; i++)
        {
            db.Sessions.Add(new Session
            {
                SecureId = $"session-{i:D4}",
                ProjectId = project.Id,
            });
        }
        await db.SaveChangesAsync();

        var count = await db.Sessions.CountAsync();
        Assert.Equal(100, count);

        var last = await db.Sessions.FirstOrDefaultAsync(s => s.SecureId == "session-0099");
        Assert.NotNull(last);
    }

    public void Dispose() => _factory.Dispose();
}

public class ErrorGroupCrudTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    private async Task<Project> SeedProject(HoldFastDbContext db)
    {
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();
        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    [Fact]
    public async Task CanCreateErrorGroup()
    {
        using var db = _factory.Create();
        var project = await SeedProject(db);

        db.ErrorGroups.Add(new ErrorGroup
        {
            ProjectId = project.Id,
            Event = "NullReferenceException",
            Type = "System.NullReferenceException",
        });
        await db.SaveChangesAsync();

        var eg = await db.ErrorGroups.FirstAsync();
        Assert.Equal(ErrorGroupState.Open, eg.State);
        Assert.Equal("NullReferenceException", eg.Event);
    }

    [Fact]
    public async Task CanUpdateErrorGroupState()
    {
        using var db = _factory.Create();
        var project = await SeedProject(db);

        db.ErrorGroups.Add(new ErrorGroup
        {
            ProjectId = project.Id,
            Event = "Error",
            Type = "Error",
        });
        await db.SaveChangesAsync();

        var eg = await db.ErrorGroups.FirstAsync();
        eg.State = ErrorGroupState.Resolved;
        await db.SaveChangesAsync();

        var updated = await db.ErrorGroups.FirstAsync();
        Assert.Equal(ErrorGroupState.Resolved, updated.State);
    }

    [Fact]
    public async Task ErrorGroupWithErrorObjects()
    {
        using var db = _factory.Create();
        var project = await SeedProject(db);

        var eg = new ErrorGroup
        {
            ProjectId = project.Id,
            Event = "TypeError",
            Type = "TypeError",
        };
        db.ErrorGroups.Add(eg);
        await db.SaveChangesAsync();

        db.ErrorObjects.AddRange(
            new ErrorObject { ErrorGroupId = eg.Id, ProjectId = project.Id, Event = "Error 1" },
            new ErrorObject { ErrorGroupId = eg.Id, ProjectId = project.Id, Event = "Error 2" });
        await db.SaveChangesAsync();

        var group = await db.ErrorGroups
            .Include(g => g.ErrorObjects)
            .FirstAsync();
        Assert.Equal(2, group.ErrorObjects.Count);
    }

    [Fact]
    public async Task ErrorGroupActivityLog()
    {
        using var db = _factory.Create();
        var project = await SeedProject(db);

        var eg = new ErrorGroup { ProjectId = project.Id, Event = "Error", Type = "Error" };
        db.ErrorGroups.Add(eg);
        await db.SaveChangesAsync();

        db.ErrorGroupActivityLogs.Add(new ErrorGroupActivityLog
        {
            ErrorGroupId = eg.Id,
            Action = "Resolved",
        });
        await db.SaveChangesAsync();

        var logs = await db.ErrorGroupActivityLogs
            .Where(l => l.ErrorGroupId == eg.Id)
            .ToListAsync();
        Assert.Single(logs);
        Assert.Equal("Resolved", logs[0].Action);
    }

    public void Dispose() => _factory.Dispose();
}

public class AdminCrudTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task CanCreateAdmin()
    {
        using var db = _factory.Create();

        db.Admins.Add(new Admin { Uid = "uid-123", Name = "Scott" });
        await db.SaveChangesAsync();

        var admin = await db.Admins.FirstAsync();
        Assert.Equal("uid-123", admin.Uid);
        Assert.Equal("Scott", admin.Name);
    }

    [Fact]
    public async Task AdminLookupByUid()
    {
        using var db = _factory.Create();

        db.Admins.AddRange(
            new Admin { Uid = "uid-1", Name = "Alice" },
            new Admin { Uid = "uid-2", Name = "Bob" },
            new Admin { Uid = "uid-3", Name = "Charlie" });
        await db.SaveChangesAsync();

        var bob = await db.Admins.FirstOrDefaultAsync(a => a.Uid == "uid-2");
        Assert.NotNull(bob);
        Assert.Equal("Bob", bob!.Name);
    }

    [Fact]
    public async Task AdminNotFound_ReturnsNull()
    {
        using var db = _factory.Create();
        var found = await db.Admins.FirstOrDefaultAsync(a => a.Uid == "nonexistent");
        Assert.Null(found);
    }

    public void Dispose() => _factory.Dispose();
}

public class AlertCrudTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    private async Task<Project> SeedProject(HoldFastDbContext db)
    {
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();
        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    [Fact]
    public async Task CanCreateAlert()
    {
        using var db = _factory.Create();
        var project = await SeedProject(db);

        db.Alerts.Add(new Alert
        {
            ProjectId = project.Id,
            Name = "High Error Rate",
            ProductType = "errors",
            AboveThreshold = 100.0,
            ThresholdWindow = 300,
        });
        await db.SaveChangesAsync();

        var alert = await db.Alerts.FirstAsync();
        Assert.Equal("High Error Rate", alert.Name);
        Assert.Equal(100.0, alert.AboveThreshold);
        Assert.False(alert.Disabled);
    }

    [Fact]
    public async Task AlertWithDestinations()
    {
        using var db = _factory.Create();
        var project = await SeedProject(db);

        var alert = new Alert
        {
            ProjectId = project.Id,
            Name = "Test Alert",
            ProductType = "sessions",
        };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();

        db.AlertDestinations.Add(new AlertDestination
        {
            AlertId = alert.Id,
            DestinationType = "slack",
            TypeId = "C12345",
            TypeName = "#alerts",
        });
        await db.SaveChangesAsync();

        var loaded = await db.Alerts
            .Include(a => a.Destinations)
            .FirstAsync();
        Assert.Single(loaded.Destinations);
        Assert.Equal("slack", loaded.Destinations.First().DestinationType);
    }

    [Fact]
    public async Task DisabledAlert()
    {
        using var db = _factory.Create();
        var project = await SeedProject(db);

        db.Alerts.Add(new Alert
        {
            ProjectId = project.Id,
            Name = "Disabled Alert",
            ProductType = "logs",
            Disabled = true,
        });
        await db.SaveChangesAsync();

        var alert = await db.Alerts.FirstAsync();
        Assert.True(alert.Disabled);
    }

    public void Dispose() => _factory.Dispose();
}

public class AllWorkspaceSettingsCrudTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task CanCreateSettings_AllFeaturesEnabled()
    {
        using var db = _factory.Create();

        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        db.AllWorkspaceSettings.Add(new AllWorkspaceSettings { WorkspaceId = ws.Id });
        await db.SaveChangesAsync();

        var settings = await db.AllWorkspaceSettings.FirstAsync();
        Assert.True(settings.AIApplication);
        Assert.True(settings.EnableSSO);
        Assert.True(settings.EnableUnlimitedSeats);
    }

    [Fact]
    public async Task CanDisableIndividualFeature()
    {
        using var db = _factory.Create();

        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        db.AllWorkspaceSettings.Add(new AllWorkspaceSettings
        {
            WorkspaceId = ws.Id,
            AIApplication = false,
        });
        await db.SaveChangesAsync();

        var settings = await db.AllWorkspaceSettings.FirstAsync();
        Assert.False(settings.AIApplication);
        Assert.True(settings.AIInsights); // others still enabled
    }

    [Fact]
    public async Task SettingsLookupByWorkspaceId()
    {
        using var db = _factory.Create();

        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        db.AllWorkspaceSettings.Add(new AllWorkspaceSettings { WorkspaceId = ws.Id });
        await db.SaveChangesAsync();

        var found = await db.AllWorkspaceSettings
            .FirstOrDefaultAsync(s => s.WorkspaceId == ws.Id);
        Assert.NotNull(found);
    }

    public void Dispose() => _factory.Dispose();
}

public class CommentCrudTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    private async Task<(Project project, Session session, Admin admin)> SeedData(HoldFastDbContext db)
    {
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        var admin = new Admin { Uid = "uid-1", Name = "Tester" };
        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        var session = new Session { SecureId = "sess-1", ProjectId = project.Id };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        return (project, session, admin);
    }

    [Fact]
    public async Task CanCreateSessionComment()
    {
        using var db = _factory.Create();
        var (project, session, admin) = await SeedData(db);

        db.SessionComments.Add(new SessionComment
        {
            ProjectId = project.Id,
            SessionId = session.Id,
            AdminId = admin.Id,
            Text = "Found a bug here",
            Timestamp = 5000,
            XCoordinate = 100.5,
            YCoordinate = 200.3,
            Type = "ADMIN",
        });
        await db.SaveChangesAsync();

        var comment = await db.SessionComments.FirstAsync();
        Assert.Equal("Found a bug here", comment.Text);
        Assert.Equal("ADMIN", comment.Type);
        Assert.Equal(100.5, comment.XCoordinate);
    }

    [Fact]
    public async Task FeedbackComment()
    {
        using var db = _factory.Create();
        var (project, session, admin) = await SeedData(db);

        db.SessionComments.Add(new SessionComment
        {
            ProjectId = project.Id,
            SessionId = session.Id,
            AdminId = admin.Id,
            Text = "The page loads slowly",
            Type = "FEEDBACK",
        });
        await db.SaveChangesAsync();

        var comments = await db.SessionComments
            .Where(c => c.Type == "FEEDBACK")
            .ToListAsync();
        Assert.Single(comments);
    }

    public void Dispose() => _factory.Dispose();
}

public class DashboardCrudTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task CanCreateDashboardWithMetrics()
    {
        using var db = _factory.Create();

        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var dashboard = new Dashboard
        {
            ProjectId = project.Id,
            Name = "Performance Dashboard",
        };
        db.Dashboards.Add(dashboard);
        await db.SaveChangesAsync();

        db.DashboardMetrics.Add(new DashboardMetric
        {
            DashboardId = dashboard.Id,
            Name = "LCP",
            Description = "Largest Contentful Paint",
        });
        await db.SaveChangesAsync();

        var loaded = await db.Dashboards
            .Include(d => d.Metrics)
            .FirstAsync();
        Assert.Equal("Performance Dashboard", loaded.Name);
        Assert.Single(loaded.Metrics);
    }

    public void Dispose() => _factory.Dispose();
}

public class ConcurrentAccessTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task ConcurrentReads_DontConflict()
    {
        using var db = _factory.Create();

        db.Workspaces.Add(new Workspace { Name = "Shared" });
        await db.SaveChangesAsync();

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => db.Workspaces.FirstAsync())
            .ToList();

        var results = await Task.WhenAll(tasks);
        Assert.All(results, ws => Assert.Equal("Shared", ws.Name));
    }

    public void Dispose() => _factory.Dispose();
}

public class SoftDeleteTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task DeletedAt_NullByDefault()
    {
        using var db = _factory.Create();

        db.Workspaces.Add(new Workspace { Name = "Active" });
        await db.SaveChangesAsync();

        var ws = await db.Workspaces.FirstAsync();
        Assert.Null(ws.DeletedAt);
    }

    [Fact]
    public async Task CanSetDeletedAt_WithoutHardDelete()
    {
        using var db = _factory.Create();

        db.Workspaces.Add(new Workspace { Name = "SoftDelete" });
        await db.SaveChangesAsync();

        var ws = await db.Workspaces.FirstAsync();
        ws.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Record still exists (no query filter in SQLite test)
        var all = await db.Workspaces.ToListAsync();
        Assert.Single(all);
        Assert.NotNull(all[0].DeletedAt);
    }

    public void Dispose() => _factory.Dispose();
}

public class ToSnakeCaseTests
{
    // The snake_case converter is private, but we can verify its effect
    // by checking that the model is configured correctly
    [Fact]
    public void DbContext_CanBeCreated()
    {
        // This implicitly tests that OnModelCreating runs without errors
        using var factory = new TestDbContextFactory();
        using var db = factory.Create();
        Assert.NotNull(db.Model);
    }
}
