using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.Data.Tests;

// ── Session Relationships ───────────────────────────────────────────

public class SessionRelationshipTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    private async Task<(Project project, Session session)> SeedSessionData(HoldFastDbContext db)
    {
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();
        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var session = new Session { SecureId = "rel-sess", ProjectId = project.Id };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        return (project, session);
    }

    [Fact]
    public async Task Session_HasEventChunks()
    {
        using var db = _factory.Create();
        var (_, session) = await SeedSessionData(db);

        db.EventChunks.AddRange(
            new EventChunk { SessionId = session.Id, ChunkIndex = 0, Timestamp = 100 },
            new EventChunk { SessionId = session.Id, ChunkIndex = 1, Timestamp = 200 },
            new EventChunk { SessionId = session.Id, ChunkIndex = 2, Timestamp = 300 });
        await db.SaveChangesAsync();

        var chunks = await db.EventChunks
            .Where(c => c.SessionId == session.Id)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync();
        Assert.Equal(3, chunks.Count);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal(2, chunks[2].ChunkIndex);
    }

    [Fact]
    public async Task Session_HasSessionIntervals()
    {
        using var db = _factory.Create();
        var (_, session) = await SeedSessionData(db);

        db.SessionIntervals.AddRange(
            new SessionInterval { SessionId = session.Id, StartTime = 0, EndTime = 5000, Duration = 5000, Active = true },
            new SessionInterval { SessionId = session.Id, StartTime = 5000, EndTime = 8000, Duration = 3000, Active = false });
        await db.SaveChangesAsync();

        var intervals = await db.SessionIntervals
            .Where(i => i.SessionId == session.Id)
            .ToListAsync();
        Assert.Equal(2, intervals.Count);
    }

    [Fact]
    public async Task Session_HasRageClickEvents()
    {
        using var db = _factory.Create();
        var (project, session) = await SeedSessionData(db);

        db.RageClickEvents.AddRange(
            new RageClickEvent { ProjectId = project.Id, SessionId = session.Id, TotalClicks = 10, Selector = ".btn", StartTimestamp = 100, EndTimestamp = 200 },
            new RageClickEvent { ProjectId = project.Id, SessionId = session.Id, TotalClicks = 8, Selector = "#submit", StartTimestamp = 300, EndTimestamp = 400 });
        await db.SaveChangesAsync();

        var clicks = await db.RageClickEvents
            .Where(r => r.SessionId == session.Id)
            .ToListAsync();
        Assert.Equal(2, clicks.Count);
        Assert.All(clicks, c => Assert.True(c.TotalClicks > 0));
    }

    [Fact]
    public async Task Session_HasSessionExports()
    {
        using var db = _factory.Create();
        var (_, session) = await SeedSessionData(db);

        db.SessionExports.Add(new SessionExport
        {
            SessionId = session.Id,
            Type = "mp4",
            Url = "https://storage.example.com/export.mp4",
        });
        await db.SaveChangesAsync();

        var exports = await db.SessionExports
            .Where(e => e.SessionId == session.Id)
            .ToListAsync();
        Assert.Single(exports);
        Assert.Equal("mp4", exports[0].Type);
    }

    [Fact]
    public async Task Session_HasSessionInsights()
    {
        using var db = _factory.Create();
        var (_, session) = await SeedSessionData(db);

        db.SessionInsights.Add(new SessionInsight
        {
            SessionId = session.Id,
            Insight = "User experienced slow page load",
        });
        await db.SaveChangesAsync();

        var insights = await db.SessionInsights
            .Where(i => i.SessionId == session.Id)
            .ToListAsync();
        Assert.Single(insights);
    }

    [Fact]
    public async Task Session_HasAdminsViews()
    {
        using var db = _factory.Create();
        var (_, session) = await SeedSessionData(db);

        var admin = new Admin { Uid = "viewer" };
        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        db.SessionAdminsViews.Add(new SessionAdminsView { SessionId = session.Id, AdminId = admin.Id });
        await db.SaveChangesAsync();

        var views = await db.SessionAdminsViews
            .Include(v => v.Admin)
            .Where(v => v.SessionId == session.Id)
            .ToListAsync();
        Assert.Single(views);
        Assert.Equal("viewer", views[0].Admin.Uid);
    }

    [Fact]
    public async Task EventChunk_NavigatesToSession()
    {
        using var db = _factory.Create();
        var (_, session) = await SeedSessionData(db);

        db.EventChunks.Add(new EventChunk { SessionId = session.Id, ChunkIndex = 0, Timestamp = 100 });
        await db.SaveChangesAsync();

        var chunk = await db.EventChunks.Include(c => c.Session).FirstAsync();
        Assert.Equal("rel-sess", chunk.Session.SecureId);
    }

    [Fact]
    public async Task SessionInterval_NavigatesToSession()
    {
        using var db = _factory.Create();
        var (_, session) = await SeedSessionData(db);

        db.SessionIntervals.Add(new SessionInterval
        {
            SessionId = session.Id, StartTime = 0, EndTime = 1000, Duration = 1000, Active = true
        });
        await db.SaveChangesAsync();

        var interval = await db.SessionIntervals.Include(i => i.Session).FirstAsync();
        Assert.Equal("rel-sess", interval.Session.SecureId);
    }

    public void Dispose() => _factory.Dispose();
}

// ── ErrorGroup Relationships ────────────────────────────────────────

public class ErrorGroupRelationshipTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    private async Task<(Project project, ErrorGroup errorGroup)> SeedErrorData(HoldFastDbContext db)
    {
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();
        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var eg = new ErrorGroup { ProjectId = project.Id, Event = "TestError", Type = "TestType", SecureId = "eg-rel" };
        db.ErrorGroups.Add(eg);
        await db.SaveChangesAsync();
        return (project, eg);
    }

    [Fact]
    public async Task ErrorGroup_HasErrorObjects()
    {
        using var db = _factory.Create();
        var (project, eg) = await SeedErrorData(db);

        db.ErrorObjects.AddRange(
            new ErrorObject { ProjectId = project.Id, ErrorGroupId = eg.Id, Event = "Instance 1" },
            new ErrorObject { ProjectId = project.Id, ErrorGroupId = eg.Id, Event = "Instance 2" },
            new ErrorObject { ProjectId = project.Id, ErrorGroupId = eg.Id, Event = "Instance 3" });
        await db.SaveChangesAsync();

        var group = await db.ErrorGroups
            .Include(g => g.ErrorObjects)
            .FirstAsync(g => g.Id == eg.Id);
        Assert.Equal(3, group.ErrorObjects.Count);
    }

    [Fact]
    public async Task ErrorGroup_HasErrorFingerprints()
    {
        using var db = _factory.Create();
        var (project, eg) = await SeedErrorData(db);

        db.ErrorFingerprints.AddRange(
            new ErrorFingerprint { ProjectId = project.Id, ErrorGroupId = eg.Id, Type = "stacktrace", Value = "hash1" },
            new ErrorFingerprint { ProjectId = project.Id, ErrorGroupId = eg.Id, Type = "message", Value = "hash2" });
        await db.SaveChangesAsync();

        var group = await db.ErrorGroups
            .Include(g => g.Fingerprints)
            .FirstAsync(g => g.Id == eg.Id);
        Assert.Equal(2, group.Fingerprints.Count);
    }

    [Fact]
    public async Task ErrorGroup_HasEmbeddings()
    {
        using var db = _factory.Create();
        var (project, eg) = await SeedErrorData(db);

        db.ErrorGroupEmbeddings.Add(new ErrorGroupEmbeddings
        {
            ProjectId = project.Id,
            ErrorGroupId = eg.Id,
            GTELargeEmbedding = "[0.1, 0.2, 0.3]",
        });
        await db.SaveChangesAsync();

        var embeddings = await db.ErrorGroupEmbeddings
            .Where(e => e.ErrorGroupId == eg.Id)
            .ToListAsync();
        Assert.Single(embeddings);
    }

    [Fact]
    public async Task ErrorGroup_HasTags()
    {
        using var db = _factory.Create();
        var (_, eg) = await SeedErrorData(db);

        db.ErrorTags.AddRange(
            new ErrorTag { ErrorGroupId = eg.Id, Title = "Performance" },
            new ErrorTag { ErrorGroupId = eg.Id, Title = "Network" });
        await db.SaveChangesAsync();

        var tags = await db.ErrorTags
            .Where(t => t.ErrorGroupId == eg.Id)
            .ToListAsync();
        Assert.Equal(2, tags.Count);
    }

    [Fact]
    public async Task ErrorGroup_HasActivityLogs()
    {
        using var db = _factory.Create();
        var (_, eg) = await SeedErrorData(db);

        db.ErrorGroupActivityLogs.AddRange(
            new ErrorGroupActivityLog { ErrorGroupId = eg.Id, Action = "Created" },
            new ErrorGroupActivityLog { ErrorGroupId = eg.Id, Action = "Resolved" },
            new ErrorGroupActivityLog { ErrorGroupId = eg.Id, Action = "Reopened" });
        await db.SaveChangesAsync();

        var logs = await db.ErrorGroupActivityLogs
            .Where(l => l.ErrorGroupId == eg.Id)
            .OrderBy(l => l.Id)
            .ToListAsync();
        Assert.Equal(3, logs.Count);
        Assert.Equal("Created", logs[0].Action);
        Assert.Equal("Reopened", logs[2].Action);
    }

    [Fact]
    public async Task ErrorGroup_HasAdminsViews()
    {
        using var db = _factory.Create();
        var (_, eg) = await SeedErrorData(db);

        var admin = new Admin { Uid = "eg-viewer" };
        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        db.ErrorGroupAdminsViews.Add(new ErrorGroupAdminsView { ErrorGroupId = eg.Id, AdminId = admin.Id });
        await db.SaveChangesAsync();

        var views = await db.ErrorGroupAdminsViews
            .Where(v => v.ErrorGroupId == eg.Id)
            .ToListAsync();
        Assert.Single(views);
    }

    [Fact]
    public async Task ErrorObject_OptionalSession_Null()
    {
        using var db = _factory.Create();
        var (project, eg) = await SeedErrorData(db);

        db.ErrorObjects.Add(new ErrorObject
        {
            ProjectId = project.Id,
            ErrorGroupId = eg.Id,
            Event = "Backend error",
            SessionId = null,
        });
        await db.SaveChangesAsync();

        var eo = await db.ErrorObjects.Include(e => e.Session).FirstAsync();
        Assert.Null(eo.Session);
        Assert.Null(eo.SessionId);
    }

    [Fact]
    public async Task ErrorObject_WithSession()
    {
        using var db = _factory.Create();
        var (project, eg) = await SeedErrorData(db);

        var session = new Session { SecureId = "eo-sess", ProjectId = project.Id };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        db.ErrorObjects.Add(new ErrorObject
        {
            ProjectId = project.Id,
            ErrorGroupId = eg.Id,
            Event = "Frontend error",
            SessionId = session.Id,
        });
        await db.SaveChangesAsync();

        var eo = await db.ErrorObjects.Include(e => e.Session).FirstAsync();
        Assert.NotNull(eo.Session);
        Assert.Equal("eo-sess", eo.Session!.SecureId);
    }

    public void Dispose() => _factory.Dispose();
}

// ── Project-Workspace Relationship ──────────────────────────────────

public class ProjectWorkspaceRelationshipTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task Project_BelongsToWorkspace_ViaNavigation()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "Parent WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        db.Projects.Add(new Project { Name = "Child App", WorkspaceId = ws.Id });
        await db.SaveChangesAsync();

        var project = await db.Projects.Include(p => p.Workspace).FirstAsync();
        Assert.Equal("Parent WS", project.Workspace.Name);
    }

    [Fact]
    public async Task Workspace_HasMultipleProjects()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "Multi WS" };
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
    public async Task Project_HasSetupEvents()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();
        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.SetupEvents.AddRange(
            new SetupEvent { ProjectId = project.Id, Type = "backend" },
            new SetupEvent { ProjectId = project.Id, Type = "frontend" });
        await db.SaveChangesAsync();

        var events = await db.SetupEvents.Where(e => e.ProjectId == project.Id).ToListAsync();
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task Project_HasFilterSettings()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();
        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.ProjectFilterSettings.Add(new ProjectFilterSettings { ProjectId = project.Id, SessionSamplingRate = 0.5 });
        await db.SaveChangesAsync();

        var settings = await db.ProjectFilterSettings.FirstAsync(s => s.ProjectId == project.Id);
        Assert.Equal(0.5, settings.SessionSamplingRate);
    }

    [Fact]
    public async Task Project_HasClientSamplingSettings()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();
        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.ProjectClientSamplingSettings.Add(new ProjectClientSamplingSettings
        {
            ProjectId = project.Id,
            SpanSamplingConfigs = "{\"default\": 1.0}",
        });
        await db.SaveChangesAsync();

        var settings = await db.ProjectClientSamplingSettings.FirstAsync(s => s.ProjectId == project.Id);
        Assert.Contains("default", settings.SpanSamplingConfigs);
    }

    public void Dispose() => _factory.Dispose();
}

// ── WorkspaceAdmin Relationship ─────────────────────────────────────

public class WorkspaceAdminRelationshipTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task WorkspaceAdmin_Links_Workspace_And_Admin()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        var admin = new Admin { Uid = "wa-uid", Name = "Admin User" };
        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        db.WorkspaceAdmins.Add(new WorkspaceAdmin { AdminId = admin.Id, WorkspaceId = ws.Id, Role = "ADMIN" });
        await db.SaveChangesAsync();

        var wa = await db.WorkspaceAdmins
            .Include(x => x.Admin)
            .Include(x => x.Workspace)
            .FirstAsync();
        Assert.Equal("Admin User", wa.Admin.Name);
        Assert.Equal("WS", wa.Workspace.Name);
    }

    [Fact]
    public async Task MultipleAdmins_InOneWorkspace()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        var admin1 = new Admin { Uid = "a1" };
        var admin2 = new Admin { Uid = "a2" };
        var admin3 = new Admin { Uid = "a3" };
        db.Admins.AddRange(admin1, admin2, admin3);
        await db.SaveChangesAsync();

        db.WorkspaceAdmins.AddRange(
            new WorkspaceAdmin { AdminId = admin1.Id, WorkspaceId = ws.Id, Role = "ADMIN" },
            new WorkspaceAdmin { AdminId = admin2.Id, WorkspaceId = ws.Id, Role = "MEMBER" },
            new WorkspaceAdmin { AdminId = admin3.Id, WorkspaceId = ws.Id, Role = "MEMBER" });
        await db.SaveChangesAsync();

        var admins = await db.WorkspaceAdmins
            .Where(wa => wa.WorkspaceId == ws.Id)
            .ToListAsync();
        Assert.Equal(3, admins.Count);
        Assert.Single(admins, a => a.Role == "ADMIN");
        Assert.Equal(2, admins.Count(a => a.Role == "MEMBER"));
    }

    [Fact]
    public async Task OneAdmin_MultipleWorkspaces()
    {
        using var db = _factory.Create();
        var ws1 = new Workspace { Name = "WS1" };
        var ws2 = new Workspace { Name = "WS2" };
        db.Workspaces.AddRange(ws1, ws2);
        var admin = new Admin { Uid = "multi-ws" };
        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        db.WorkspaceAdmins.AddRange(
            new WorkspaceAdmin { AdminId = admin.Id, WorkspaceId = ws1.Id },
            new WorkspaceAdmin { AdminId = admin.Id, WorkspaceId = ws2.Id });
        await db.SaveChangesAsync();

        var memberships = await db.WorkspaceAdmins
            .Where(wa => wa.AdminId == admin.Id)
            .ToListAsync();
        Assert.Equal(2, memberships.Count);
    }

    [Fact]
    public async Task WorkspaceAdmin_CompositeKey_Prevents_Duplicate()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        var admin = new Admin { Uid = "dup" };
        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        db.WorkspaceAdmins.Add(new WorkspaceAdmin { AdminId = admin.Id, WorkspaceId = ws.Id });
        await db.SaveChangesAsync();

        // EF Core's change tracker detects duplicate composite keys at Add() time
        Assert.Throws<InvalidOperationException>(() =>
            db.WorkspaceAdmins.Add(new WorkspaceAdmin { AdminId = admin.Id, WorkspaceId = ws.Id }));
    }

    public void Dispose() => _factory.Dispose();
}

// ── Comment Relationships ───────────────────────────────────────────

public class CommentRelationshipTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    private async Task<(Project project, Session session, Admin admin)> SeedCommentData(HoldFastDbContext db)
    {
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();
        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        var admin = new Admin { Uid = "commenter" };
        db.Admins.Add(admin);
        await db.SaveChangesAsync();
        var session = new Session { SecureId = "comment-sess", ProjectId = project.Id };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        return (project, session, admin);
    }

    [Fact]
    public async Task SessionComment_HasTags()
    {
        using var db = _factory.Create();
        var (project, session, admin) = await SeedCommentData(db);

        var comment = new SessionComment
        {
            ProjectId = project.Id,
            SessionId = session.Id,
            AdminId = admin.Id,
            Text = "Tagged comment",
        };
        db.SessionComments.Add(comment);
        await db.SaveChangesAsync();

        db.SessionCommentTags.AddRange(
            new SessionCommentTag { SessionCommentId = comment.Id, Name = "bug" },
            new SessionCommentTag { SessionCommentId = comment.Id, Name = "performance" });
        await db.SaveChangesAsync();

        var loaded = await db.SessionComments
            .Include(c => c.Tags)
            .FirstAsync(c => c.Id == comment.Id);
        Assert.Equal(2, loaded.Tags.Count);
    }

    [Fact]
    public async Task SessionComment_HasReplies()
    {
        using var db = _factory.Create();
        var (project, session, admin) = await SeedCommentData(db);

        var comment = new SessionComment
        {
            ProjectId = project.Id,
            SessionId = session.Id,
            AdminId = admin.Id,
            Text = "Original comment",
        };
        db.SessionComments.Add(comment);
        await db.SaveChangesAsync();

        db.CommentReplies.AddRange(
            new CommentReply { SessionCommentId = comment.Id, AdminId = admin.Id, Text = "Reply 1" },
            new CommentReply { SessionCommentId = comment.Id, AdminId = admin.Id, Text = "Reply 2" });
        await db.SaveChangesAsync();

        var loaded = await db.SessionComments
            .Include(c => c.Replies)
            .FirstAsync(c => c.Id == comment.Id);
        Assert.Equal(2, loaded.Replies.Count);
    }

    [Fact]
    public async Task SessionComment_HasFollowers()
    {
        using var db = _factory.Create();
        var (project, session, admin) = await SeedCommentData(db);

        var admin2 = new Admin { Uid = "follower" };
        db.Admins.Add(admin2);
        await db.SaveChangesAsync();

        var comment = new SessionComment
        {
            ProjectId = project.Id,
            SessionId = session.Id,
            AdminId = admin.Id,
            Text = "Followed comment",
        };
        db.SessionComments.Add(comment);
        await db.SaveChangesAsync();

        db.CommentFollowers.AddRange(
            new CommentFollower { SessionCommentId = comment.Id, AdminId = admin.Id, HasMuted = false },
            new CommentFollower { SessionCommentId = comment.Id, AdminId = admin2.Id, HasMuted = true });
        await db.SaveChangesAsync();

        var loaded = await db.SessionComments
            .Include(c => c.Followers)
            .FirstAsync(c => c.Id == comment.Id);
        Assert.Equal(2, loaded.Followers.Count);
    }

    [Fact]
    public async Task ErrorComment_BelongsTo_ErrorGroup()
    {
        using var db = _factory.Create();
        var (project, _, admin) = await SeedCommentData(db);

        var eg = new ErrorGroup { ProjectId = project.Id, Event = "E", Type = "T" };
        db.ErrorGroups.Add(eg);
        await db.SaveChangesAsync();

        db.ErrorComments.AddRange(
            new ErrorComment { ErrorGroupId = eg.Id, AdminId = admin.Id, Text = "Comment 1" },
            new ErrorComment { ErrorGroupId = eg.Id, AdminId = admin.Id, Text = "Comment 2" });
        await db.SaveChangesAsync();

        var comments = await db.ErrorComments
            .Where(c => c.ErrorGroupId == eg.Id)
            .Include(c => c.ErrorGroup)
            .Include(c => c.Admin)
            .ToListAsync();
        Assert.Equal(2, comments.Count);
        Assert.All(comments, c => Assert.Equal(eg.Id, c.ErrorGroup.Id));
        Assert.All(comments, c => Assert.Equal(admin.Id, c.Admin.Id));
    }

    [Fact]
    public async Task CommentReply_CanBelongToErrorComment()
    {
        using var db = _factory.Create();
        var (project, _, admin) = await SeedCommentData(db);

        var eg = new ErrorGroup { ProjectId = project.Id, Event = "E", Type = "T" };
        db.ErrorGroups.Add(eg);
        await db.SaveChangesAsync();

        var errorComment = new ErrorComment { ErrorGroupId = eg.Id, AdminId = admin.Id, Text = "Error comment" };
        db.ErrorComments.Add(errorComment);
        await db.SaveChangesAsync();

        var reply = new CommentReply { ErrorCommentId = errorComment.Id, AdminId = admin.Id, Text = "Reply to error" };
        db.CommentReplies.Add(reply);
        await db.SaveChangesAsync();

        var read = await db.CommentReplies.FindAsync(reply.Id);
        Assert.NotNull(read);
        Assert.Equal(errorComment.Id, read!.ErrorCommentId);
        Assert.Null(read.SessionCommentId);
    }

    [Fact]
    public async Task CommentSlackThread_ForSessionComment()
    {
        using var db = _factory.Create();
        var (project, session, admin) = await SeedCommentData(db);

        var comment = new SessionComment
        {
            ProjectId = project.Id,
            SessionId = session.Id,
            AdminId = admin.Id,
            Text = "Slack thread comment",
        };
        db.SessionComments.Add(comment);
        await db.SaveChangesAsync();

        db.CommentSlackThreads.Add(new CommentSlackThread
        {
            SessionCommentId = comment.Id,
            SlackChannelId = "C12345",
            ThreadTs = "1234567890.123456",
        });
        await db.SaveChangesAsync();

        var thread = await db.CommentSlackThreads
            .FirstAsync(t => t.SessionCommentId == comment.Id);
        Assert.Equal("C12345", thread.SlackChannelId);
    }

    [Fact]
    public async Task CommentSlackThread_ForErrorComment()
    {
        using var db = _factory.Create();
        var (project, _, admin) = await SeedCommentData(db);

        var eg = new ErrorGroup { ProjectId = project.Id, Event = "E", Type = "T" };
        db.ErrorGroups.Add(eg);
        await db.SaveChangesAsync();

        var errorComment = new ErrorComment { ErrorGroupId = eg.Id, AdminId = admin.Id, Text = "Error" };
        db.ErrorComments.Add(errorComment);
        await db.SaveChangesAsync();

        db.CommentSlackThreads.Add(new CommentSlackThread
        {
            ErrorCommentId = errorComment.Id,
            SlackChannelId = "C99999",
            ThreadTs = "9999999.999",
        });
        await db.SaveChangesAsync();

        var thread = await db.CommentSlackThreads
            .FirstAsync(t => t.ErrorCommentId == errorComment.Id);
        Assert.Null(thread.SessionCommentId);
        Assert.Equal("C99999", thread.SlackChannelId);
    }

    public void Dispose() => _factory.Dispose();
}

// ── Dashboard / Visualization Relationships ─────────────────────────

public class DashboardRelationshipTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task Dashboard_HasMultipleMetrics()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();
        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var dash = new Dashboard { ProjectId = project.Id, Name = "Web Vitals" };
        db.Dashboards.Add(dash);
        await db.SaveChangesAsync();

        db.DashboardMetrics.AddRange(
            new DashboardMetric { DashboardId = dash.Id, Name = "LCP", Description = "Largest Contentful Paint" },
            new DashboardMetric { DashboardId = dash.Id, Name = "FID", Description = "First Input Delay" },
            new DashboardMetric { DashboardId = dash.Id, Name = "CLS", Description = "Cumulative Layout Shift" });
        await db.SaveChangesAsync();

        var loaded = await db.Dashboards
            .Include(d => d.Metrics)
            .FirstAsync(d => d.Id == dash.Id);
        Assert.Equal(3, loaded.Metrics.Count);
    }

    [Fact]
    public async Task Visualization_HasGraphs()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();
        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var viz = new Visualization { ProjectId = project.Id, Name = "Error Overview" };
        db.Visualizations.Add(viz);
        await db.SaveChangesAsync();

        db.Graphs.AddRange(
            new Graph { ProjectId = project.Id, Title = "Error Rate", VisualizationId = viz.Id },
            new Graph { ProjectId = project.Id, Title = "Error Count", VisualizationId = viz.Id });
        await db.SaveChangesAsync();

        var loaded = await db.Visualizations
            .Include(v => v.Graphs)
            .FirstAsync(v => v.Id == viz.Id);
        Assert.Equal(2, loaded.Graphs.Count);
    }

    public void Dispose() => _factory.Dispose();
}

// ── Alert Relationships ─────────────────────────────────────────────

public class AlertRelationshipTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task Alert_HasMultipleDestinations()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();
        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var alert = new Alert { ProjectId = project.Id, Name = "Multi-dest Alert", ProductType = "errors" };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();

        db.AlertDestinations.AddRange(
            new AlertDestination { AlertId = alert.Id, DestinationType = "slack", TypeId = "C1", TypeName = "#alerts" },
            new AlertDestination { AlertId = alert.Id, DestinationType = "email", TypeId = "user@test.com" },
            new AlertDestination { AlertId = alert.Id, DestinationType = "webhook", TypeId = "https://hook.example.com" });
        await db.SaveChangesAsync();

        var loaded = await db.Alerts
            .Include(a => a.Destinations)
            .FirstAsync(a => a.Id == alert.Id);
        Assert.Equal(3, loaded.Destinations.Count);
    }

    public void Dispose() => _factory.Dispose();
}

// ── Integration Relationships ───────────────────────────────────────

public class IntegrationRelationshipTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task IntegrationProjectMapping_BelongsTo_Project()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();
        var project = new Project { Name = "App", WorkspaceId = ws.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.IntegrationProjectMappings.Add(new IntegrationProjectMapping
        {
            IntegrationType = "github",
            ProjectId = project.Id,
            ExternalId = "repo-123",
        });
        await db.SaveChangesAsync();

        var mapping = await db.IntegrationProjectMappings
            .Include(m => m.Project)
            .FirstAsync();
        Assert.Equal("App", mapping.Project.Name);
    }

    [Fact]
    public async Task IntegrationWorkspaceMapping_BelongsTo_Workspace()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        db.IntegrationWorkspaceMappings.Add(new IntegrationWorkspaceMapping
        {
            IntegrationType = "linear",
            WorkspaceId = ws.Id,
            AccessToken = "token-123",
        });
        await db.SaveChangesAsync();

        var mapping = await db.IntegrationWorkspaceMappings
            .Include(m => m.Workspace)
            .FirstAsync();
        Assert.Equal("WS", mapping.Workspace.Name);
    }

    [Fact]
    public async Task SSOClient_BelongsTo_Workspace()
    {
        using var db = _factory.Create();
        var ws = new Workspace { Name = "SSO WS" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        db.SSOClients.Add(new SSOClient
        {
            WorkspaceId = ws.Id,
            Domain = "corp.example.com",
            ProviderUrl = "https://idp.corp.example.com",
        });
        await db.SaveChangesAsync();

        var sso = await db.SSOClients
            .Include(s => s.Workspace)
            .FirstAsync();
        Assert.Equal("SSO WS", sso.Workspace.Name);
    }

    public void Dispose() => _factory.Dispose();
}

// ── ExternalAttachment Relationships ────────────────────────────────

public class ExternalAttachmentRelationshipTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task ExternalAttachment_ForErrorGroup()
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

        db.ExternalAttachments.Add(new ExternalAttachment
        {
            ErrorGroupId = eg.Id,
            IntegrationType = "jira",
            ExternalId = "PROJ-123",
            Title = "Fix null ref",
        });
        await db.SaveChangesAsync();

        var attachment = await db.ExternalAttachments
            .Include(a => a.ErrorGroup)
            .FirstAsync();
        Assert.Equal(eg.Id, attachment.ErrorGroupId);
        Assert.Equal("PROJ-123", attachment.ExternalId);
    }

    [Fact]
    public async Task ExternalAttachment_WithoutErrorGroup()
    {
        using var db = _factory.Create();

        db.ExternalAttachments.Add(new ExternalAttachment
        {
            ErrorGroupId = null,
            SessionCommentId = null,
            IntegrationType = "linear",
            ExternalId = "LIN-456",
        });
        await db.SaveChangesAsync();

        var attachment = await db.ExternalAttachments.FirstAsync();
        Assert.Null(attachment.ErrorGroupId);
        Assert.Null(attachment.SessionCommentId);
    }

    public void Dispose() => _factory.Dispose();
}
