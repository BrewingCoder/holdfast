using HoldFast.Data;
using HoldFast.Domain.Entities;
using HotChocolate;
using HotChocolate.Data;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.GraphQL.Private;

/// <summary>
/// Private GraphQL queries — dashboard API.
/// Hot Chocolate code-first: no codegen, no generated files.
/// </summary>
public class PrivateQuery
{
    // ── Workspace ─────────────────────────────────────────────────────

    [UseProjection]
    [UseFiltering]
    public async Task<Workspace?> GetWorkspace(
        int id,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.Workspaces
            .Include(w => w.Projects)
            .FirstOrDefaultAsync(w => w.Id == id, ct);
    }

    /// <summary>
    /// Get the workspace that contains a given project.
    /// </summary>
    public async Task<Workspace?> GetWorkspaceForProject(
        int projectId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => p.Workspace)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Get the workspace for an invite link.
    /// </summary>
    public async Task<Workspace?> GetWorkspaceForInviteLink(
        string secret,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var link = await db.WorkspaceInviteLinks
            .Include(l => l.Workspace)
            .FirstOrDefaultAsync(l => l.Secret == secret, ct);
        return link?.Workspace;
    }

    /// <summary>
    /// List all workspaces an admin belongs to.
    /// </summary>
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<Workspace> GetWorkspaces(
        int adminId,
        [Service] HoldFastDbContext db)
    {
        return db.WorkspaceAdmins
            .Where(wa => wa.AdminId == adminId)
            .Select(wa => wa.Workspace);
    }

    /// <summary>
    /// Get workspace invite links.
    /// </summary>
    public async Task<List<WorkspaceInviteLink>> GetWorkspaceInviteLinks(
        int workspaceId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.WorkspaceInviteLinks
            .Where(l => l.WorkspaceId == workspaceId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// List workspace admins with their roles.
    /// </summary>
    public async Task<List<WorkspaceAdmin>> GetWorkspaceAdmins(
        int workspaceId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.WorkspaceAdmins
            .Include(wa => wa.Admin)
            .Where(wa => wa.WorkspaceId == workspaceId)
            .OrderBy(wa => wa.Admin.CreatedAt)
            .ToListAsync(ct);
    }

    // ── Project ───────────────────────────────────────────────────────

    [UseProjection]
    public async Task<Project?> GetProject(
        int id,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.Projects
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<Project> GetProjects(
        int workspaceId,
        [Service] HoldFastDbContext db)
    {
        return db.Projects.Where(p => p.WorkspaceId == workspaceId);
    }

    /// <summary>
    /// Get project settings (filter/sampling configuration).
    /// </summary>
    public async Task<ProjectFilterSettings?> GetProjectSettings(
        int projectId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.ProjectFilterSettings
            .FirstOrDefaultAsync(s => s.ProjectId == projectId, ct);
    }

    // ── Error Groups ──────────────────────────────────────────────────

    [UseProjection]
    public async Task<ErrorGroup?> GetErrorGroup(
        int id,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.ErrorGroups
            .FirstOrDefaultAsync(eg => eg.Id == id, ct);
    }

    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<ErrorGroup> GetErrorGroups(
        int projectId,
        [Service] HoldFastDbContext db)
    {
        return db.ErrorGroups.Where(eg => eg.ProjectId == projectId);
    }

    /// <summary>
    /// Get a specific error object by ID.
    /// </summary>
    public async Task<ErrorObject?> GetErrorObject(
        int id,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.ErrorObjects
            .FirstOrDefaultAsync(eo => eo.Id == id, ct);
    }

    /// <summary>
    /// List error objects for an error group.
    /// </summary>
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<ErrorObject> GetErrorObjects(
        int errorGroupId,
        [Service] HoldFastDbContext db)
    {
        return db.ErrorObjects.Where(eo => eo.ErrorGroupId == errorGroupId);
    }

    /// <summary>
    /// Get error group tags.
    /// </summary>
    public async Task<List<ErrorTag>> GetErrorGroupTags(
        int errorGroupId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.ErrorTags
            .Where(t => t.ErrorGroupId == errorGroupId)
            .ToListAsync(ct);
    }

    // ── Sessions ──────────────────────────────────────────────────────

    [UseProjection]
    public async Task<Session?> GetSession(
        string secureId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.Sessions
            .FirstOrDefaultAsync(s => s.SecureId == secureId, ct);
    }

    /// <summary>
    /// Get session intervals for timeline display.
    /// </summary>
    public async Task<List<SessionInterval>> GetSessionIntervals(
        int sessionId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.SessionIntervals
            .Where(i => i.SessionId == sessionId)
            .OrderBy(i => i.StartTime)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get event chunk URLs for session replay.
    /// </summary>
    public async Task<List<EventChunk>> GetEventChunks(
        int sessionId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.EventChunks
            .Where(c => c.SessionId == sessionId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get rage click events for a session.
    /// </summary>
    [UseProjection]
    [UseFiltering]
    public IQueryable<RageClickEvent> GetRageClicks(
        int sessionId,
        [Service] HoldFastDbContext db)
    {
        return db.RageClickEvents.Where(r => r.SessionId == sessionId);
    }

    /// <summary>
    /// Get session exports.
    /// </summary>
    public async Task<List<SessionExport>> GetSessionExports(
        int sessionId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.SessionExports
            .Where(e => e.SessionId == sessionId)
            .ToListAsync(ct);
    }

    // ── Comments ──────────────────────────────────────────────────────

    /// <summary>
    /// List session comments for a session.
    /// </summary>
    [UseProjection]
    [UseSorting]
    public IQueryable<SessionComment> GetSessionComments(
        int sessionId,
        [Service] HoldFastDbContext db)
    {
        return db.SessionComments
            .Include(c => c.Tags)
            .Include(c => c.Replies)
            .Where(c => c.SessionId == sessionId);
    }

    /// <summary>
    /// List session comments for a project.
    /// </summary>
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<SessionComment> GetSessionCommentsForProject(
        int projectId,
        [Service] HoldFastDbContext db)
    {
        return db.SessionComments
            .Include(c => c.Tags)
            .Where(c => c.ProjectId == projectId);
    }

    /// <summary>
    /// List error comments for an error group.
    /// </summary>
    [UseProjection]
    [UseSorting]
    public IQueryable<ErrorComment> GetErrorComments(
        int errorGroupId,
        [Service] HoldFastDbContext db)
    {
        return db.ErrorComments.Where(c => c.ErrorGroupId == errorGroupId);
    }

    // ── Workspace Settings ────────────────────────────────────────────

    public async Task<AllWorkspaceSettings?> GetWorkspaceSettings(
        int workspaceId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.AllWorkspaceSettings
            .FirstOrDefaultAsync(s => s.WorkspaceId == workspaceId, ct);
    }

    // ── Alerts ────────────────────────────────────────────────────────

    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<Alert> GetAlerts(
        int projectId,
        [Service] HoldFastDbContext db)
    {
        return db.Alerts
            .Include(a => a.Destinations)
            .Where(a => a.ProjectId == projectId);
    }

    // ── Dashboards ────────────────────────────────────────────────────

    [UseProjection]
    [UseFiltering]
    public IQueryable<Dashboard> GetDashboards(
        int projectId,
        [Service] HoldFastDbContext db)
    {
        return db.Dashboards
            .Include(d => d.Metrics)
            .Where(d => d.ProjectId == projectId);
    }

    // ── Admin ─────────────────────────────────────────────────────────

    public async Task<Admin?> GetAdmin(
        string uid,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.Admins
            .FirstOrDefaultAsync(a => a.Uid == uid, ct);
    }

    // ── Services ──────────────────────────────────────────────────────

    /// <summary>
    /// List services for a project.
    /// </summary>
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<Domain.Entities.Service> GetServices(
        int projectId,
        [Service] HoldFastDbContext db)
    {
        return db.Services
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.Name);
    }

    /// <summary>
    /// Get a service by name within a project.
    /// </summary>
    public async Task<Domain.Entities.Service?> GetServiceByName(
        int projectId,
        string name,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.Services
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Name == name, ct);
    }

    // ── Saved Segments ────────────────────────────────────────────────

    /// <summary>
    /// List saved segments for a project, optionally filtered by entity type.
    /// </summary>
    public async Task<List<SavedSegment>> GetSavedSegments(
        int projectId,
        string? entityType,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var query = db.SavedSegments.Where(s => s.ProjectId == projectId);
        if (entityType != null)
            query = query.Where(s => s.EntityType == entityType);
        return await query.ToListAsync(ct);
    }

    // ── Integrations ──────────────────────────────────────────────────

    /// <summary>
    /// Get integration project mappings.
    /// </summary>
    public async Task<List<IntegrationProjectMapping>> GetIntegrationProjectMappings(
        int projectId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.IntegrationProjectMappings
            .Where(m => m.ProjectId == projectId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get integration workspace mappings.
    /// </summary>
    public async Task<List<IntegrationWorkspaceMapping>> GetIntegrationWorkspaceMappings(
        int workspaceId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.IntegrationWorkspaceMappings
            .Where(m => m.WorkspaceId == workspaceId)
            .ToListAsync(ct);
    }

    // ── System ────────────────────────────────────────────────────────

    /// <summary>
    /// Get system configuration (self-hosted settings).
    /// </summary>
    public async Task<SystemConfiguration?> GetSystemConfiguration(
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.SystemConfigurations.FirstOrDefaultAsync(ct);
    }
}
