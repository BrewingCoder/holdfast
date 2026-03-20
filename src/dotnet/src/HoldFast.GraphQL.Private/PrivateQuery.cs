using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Shared.Auth;
using HotChocolate;
using HotChocolate.Data;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using HoldFast.Storage;

namespace HoldFast.GraphQL.Private;

/// <summary>
/// Private GraphQL queries — dashboard API.
/// All queries require authentication; most require workspace or project authorization.
/// </summary>
public class PrivateQuery
{
    // ── Workspace ─────────────────────────────────────────────────────

    [UseProjection]
    [UseFiltering]
    public async Task<Workspace?> GetWorkspace(
        int id,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, id, authz, ct);

        return await db.Workspaces
            .Include(w => w.Projects)
            .FirstOrDefaultAsync(w => w.Id == id, ct);
    }

    /// <summary>
    /// Get the workspace that contains a given project.
    /// </summary>
    public async Task<Workspace?> GetWorkspaceForProject(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => p.Workspace)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Get the workspace for an invite link. No workspace auth required (invite is the auth).
    /// </summary>
    public async Task<Workspace?> GetWorkspaceForInviteLink(
        string secret,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        // Only require authentication, not workspace membership
        await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        var link = await db.WorkspaceInviteLinks
            .Include(l => l.Workspace)
            .FirstOrDefaultAsync(l => l.Secret == secret, ct);
        return link?.Workspace;
    }

    /// <summary>
    /// List all workspaces the authenticated admin belongs to.
    /// </summary>
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public async Task<IQueryable<Workspace>> GetWorkspaces(
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        return db.WorkspaceAdmins
            .Where(wa => wa.AdminId == admin.Id)
            .Select(wa => wa.Workspace);
    }

    /// <summary>
    /// Get workspace invite links.
    /// </summary>
    public async Task<List<WorkspaceInviteLink>> GetWorkspaceInviteLinks(
        int workspaceId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

        return await db.WorkspaceInviteLinks
            .Where(l => l.WorkspaceId == workspaceId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// List workspace admins with their roles.
    /// </summary>
    public async Task<List<WorkspaceAdmin>> GetWorkspaceAdmins(
        int workspaceId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

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
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, id, authz, ct);

        return await db.Projects
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public async Task<IQueryable<Project>> GetProjects(
        int workspaceId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

        return db.Projects.Where(p => p.WorkspaceId == workspaceId);
    }

    /// <summary>
    /// Get project settings (filter/sampling configuration).
    /// </summary>
    public async Task<ProjectFilterSettings?> GetProjectSettings(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await db.ProjectFilterSettings
            .FirstOrDefaultAsync(s => s.ProjectId == projectId, ct);
    }

    // ── Error Groups ──────────────────────────────────────────────────

    [UseProjection]
    public async Task<ErrorGroup?> GetErrorGroup(
        int id,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var errorGroup = await db.ErrorGroups
            .FirstOrDefaultAsync(eg => eg.Id == id, ct);

        if (errorGroup != null)
            await AuthHelper.RequireProjectAccess(claimsPrincipal, errorGroup.ProjectId, authz, ct);

        return errorGroup;
    }

    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public async Task<IQueryable<ErrorGroup>> GetErrorGroups(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return db.ErrorGroups.Where(eg => eg.ProjectId == projectId);
    }

    /// <summary>
    /// Get a specific error object by ID.
    /// </summary>
    public async Task<ErrorObject?> GetErrorObject(
        int id,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var eo = await db.ErrorObjects
            .Include(e => e.ErrorGroup)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        if (eo != null)
            await AuthHelper.RequireProjectAccess(claimsPrincipal, eo.ErrorGroup.ProjectId, authz, ct);

        return eo;
    }

    /// <summary>
    /// List error objects for an error group.
    /// </summary>
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public async Task<IQueryable<ErrorObject>> GetErrorObjects(
        int errorGroupId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var eg = await db.ErrorGroups.FindAsync([errorGroupId], ct);
        if (eg != null)
            await AuthHelper.RequireProjectAccess(claimsPrincipal, eg.ProjectId, authz, ct);

        return db.ErrorObjects.Where(eo => eo.ErrorGroupId == errorGroupId);
    }

    /// <summary>
    /// Get error group tags.
    /// </summary>
    public async Task<List<ErrorTag>> GetErrorGroupTags(
        int errorGroupId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var eg = await db.ErrorGroups.FindAsync([errorGroupId], ct);
        if (eg != null)
            await AuthHelper.RequireProjectAccess(claimsPrincipal, eg.ProjectId, authz, ct);

        return await db.ErrorTags
            .Where(t => t.ErrorGroupId == errorGroupId)
            .ToListAsync(ct);
    }

    // ── Sessions ──────────────────────────────────────────────────────

    [UseProjection]
    public async Task<Session?> GetSession(
        string secureId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var session = await db.Sessions
            .FirstOrDefaultAsync(s => s.SecureId == secureId, ct);

        if (session != null)
            await AuthHelper.RequireProjectAccess(claimsPrincipal, session.ProjectId, authz, ct);

        return session;
    }

    /// <summary>
    /// Get session intervals for timeline display.
    /// </summary>
    public async Task<List<SessionInterval>> GetSessionIntervals(
        int sessionId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var session = await db.Sessions.FindAsync([sessionId], ct);
        if (session != null)
            await AuthHelper.RequireProjectAccess(claimsPrincipal, session.ProjectId, authz, ct);

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
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var session = await db.Sessions.FindAsync([sessionId], ct);
        if (session != null)
            await AuthHelper.RequireProjectAccess(claimsPrincipal, session.ProjectId, authz, ct);

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
    public async Task<IQueryable<RageClickEvent>> GetRageClicks(
        int sessionId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var session = await db.Sessions.FindAsync([sessionId], ct);
        if (session != null)
            await AuthHelper.RequireProjectAccess(claimsPrincipal, session.ProjectId, authz, ct);

        return db.RageClickEvents.Where(r => r.SessionId == sessionId);
    }

    /// <summary>
    /// Get session exports.
    /// </summary>
    public async Task<List<SessionExport>> GetSessionExports(
        int sessionId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var session = await db.Sessions.FindAsync([sessionId], ct);
        if (session != null)
            await AuthHelper.RequireProjectAccess(claimsPrincipal, session.ProjectId, authz, ct);

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
    public async Task<IQueryable<SessionComment>> GetSessionComments(
        int sessionId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var session = await db.Sessions.FindAsync([sessionId], ct);
        if (session != null)
            await AuthHelper.RequireProjectAccess(claimsPrincipal, session.ProjectId, authz, ct);

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
    public async Task<IQueryable<SessionComment>> GetSessionCommentsForProject(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return db.SessionComments
            .Include(c => c.Tags)
            .Where(c => c.ProjectId == projectId);
    }

    /// <summary>
    /// List error comments for an error group.
    /// </summary>
    [UseProjection]
    [UseSorting]
    public async Task<IQueryable<ErrorComment>> GetErrorComments(
        int errorGroupId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var eg = await db.ErrorGroups.FindAsync([errorGroupId], ct);
        if (eg != null)
            await AuthHelper.RequireProjectAccess(claimsPrincipal, eg.ProjectId, authz, ct);

        return db.ErrorComments.Where(c => c.ErrorGroupId == errorGroupId);
    }

    // ── Workspace Settings ────────────────────────────────────────────

    public async Task<AllWorkspaceSettings?> GetWorkspaceSettings(
        int workspaceId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

        return await db.AllWorkspaceSettings
            .FirstOrDefaultAsync(s => s.WorkspaceId == workspaceId, ct);
    }

    // ── Alerts ────────────────────────────────────────────────────────

    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public async Task<IQueryable<Alert>> GetAlerts(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return db.Alerts
            .Include(a => a.Destinations)
            .Where(a => a.ProjectId == projectId);
    }

    // ── Dashboards ────────────────────────────────────────────────────

    [UseProjection]
    [UseFiltering]
    public async Task<IQueryable<Dashboard>> GetDashboards(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return db.Dashboards
            .Include(d => d.Metrics)
            .Where(d => d.ProjectId == projectId);
    }

    // ── Admin ─────────────────────────────────────────────────────────

    /// <summary>
    /// Get the currently authenticated admin.
    /// </summary>
    public async Task<Admin?> GetAdmin(
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        CancellationToken ct)
    {
        return await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);
    }

    // ── Services ──────────────────────────────────────────────────────

    /// <summary>
    /// List services for a project.
    /// </summary>
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public async Task<IQueryable<Domain.Entities.Service>> GetServices(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

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
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

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
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

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
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await db.IntegrationProjectMappings
            .Where(m => m.ProjectId == projectId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get integration workspace mappings.
    /// </summary>
    public async Task<List<IntegrationWorkspaceMapping>> GetIntegrationWorkspaceMappings(
        int workspaceId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

        return await db.IntegrationWorkspaceMappings
            .Where(m => m.WorkspaceId == workspaceId)
            .ToListAsync(ct);
    }

    // ── Source Maps ─────────────────────────────────────────────────

    /// <summary>
    /// List source map files for a project version.
    /// </summary>
    public async Task<List<string>> GetSourcemapFiles(
        int projectId,
        string? version,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] Storage.IStorageService storage,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        // Source maps stored under: sourcemaps/{projectId}/{version}/
        var prefix = version != null
            ? $"sourcemaps/{projectId}/{version}"
            : $"sourcemaps/{projectId}";

        // Check if the path exists
        var exists = await storage.ExistsAsync("sourcemaps", $"{projectId}", ct);
        if (!exists) return [];

        // Return the download URL for the prefix (listing is storage-implementation specific)
        var url = await storage.GetDownloadUrlAsync("sourcemaps", $"{projectId}", TimeSpan.FromMinutes(5), ct);
        return [url];
    }

    /// <summary>
    /// List source map versions for a project.
    /// </summary>
    public async Task<List<string>> GetSourcemapVersions(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        // Versions are tracked via the project's SDK version history
        // For now return the current version if set
        var project = await db.Projects.FindAsync([projectId], ct);
        if (project?.BackendSetup != true) return [];

        return ["latest"];
    }

    // ── System ────────────────────────────────────────────────────────

    /// <summary>
    /// Get system configuration (self-hosted settings). No auth required.
    /// </summary>
    public async Task<SystemConfiguration?> GetSystemConfiguration(
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.SystemConfigurations.FirstOrDefaultAsync(ct);
    }
}
