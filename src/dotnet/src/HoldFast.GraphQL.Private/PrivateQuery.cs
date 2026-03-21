using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
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

    // ── Logs (ClickHouse) ──────────────────────────────────────────────

    /// <summary>
    /// Query logs from ClickHouse with cursor-based pagination.
    /// </summary>
    public async Task<LogConnection> GetLogs(
        int projectId,
        string query,
        DateTime dateRangeStart,
        DateTime dateRangeEnd,
        string? after,
        string? before,
        string? at,
        string direction,
        int limit,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await clickHouse.ReadLogsAsync(projectId,
            new QueryInput { Query = query, DateRangeStart = dateRangeStart, DateRangeEnd = dateRangeEnd },
            new ClickHousePagination { After = after, Before = before, At = at, Direction = direction, Limit = limit },
            ct);
    }

    /// <summary>
    /// Get log histogram buckets for chart display.
    /// </summary>
    public async Task<List<HistogramBucket>> GetLogsHistogram(
        int projectId,
        string query,
        DateTime dateRangeStart,
        DateTime dateRangeEnd,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await clickHouse.ReadLogsHistogramAsync(projectId,
            new QueryInput { Query = query, DateRangeStart = dateRangeStart, DateRangeEnd = dateRangeEnd },
            ct);
    }

    /// <summary>
    /// Get available log attribute keys for filter UI.
    /// </summary>
    public async Task<List<string>> GetLogKeys(
        int projectId,
        DateTime dateRangeStart,
        DateTime dateRangeEnd,
        string? query,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await clickHouse.GetLogKeysAsync(projectId,
            new QueryInput { Query = query ?? "", DateRangeStart = dateRangeStart, DateRangeEnd = dateRangeEnd },
            ct);
    }

    /// <summary>
    /// Get values for a specific log attribute key.
    /// </summary>
    public async Task<List<string>> GetLogKeyValues(
        int projectId,
        string key,
        DateTime dateRangeStart,
        DateTime dateRangeEnd,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await clickHouse.GetLogKeyValuesAsync(projectId, key,
            new QueryInput { DateRangeStart = dateRangeStart, DateRangeEnd = dateRangeEnd },
            ct);
    }

    // ── Traces (ClickHouse) ─────────────────────────────────────────

    /// <summary>
    /// Query traces from ClickHouse with cursor-based pagination.
    /// </summary>
    public async Task<TraceConnection> GetTraces(
        int projectId,
        string query,
        DateTime dateRangeStart,
        DateTime dateRangeEnd,
        string? after,
        string? before,
        string? at,
        string direction,
        int limit,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await clickHouse.ReadTracesAsync(projectId,
            new QueryInput { Query = query, DateRangeStart = dateRangeStart, DateRangeEnd = dateRangeEnd },
            new ClickHousePagination { After = after, Before = before, At = at, Direction = direction, Limit = limit },
            ct: ct);
    }

    /// <summary>
    /// Get trace histogram buckets for chart display.
    /// </summary>
    public async Task<List<HistogramBucket>> GetTracesHistogram(
        int projectId,
        string query,
        DateTime dateRangeStart,
        DateTime dateRangeEnd,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await clickHouse.ReadTracesHistogramAsync(projectId,
            new QueryInput { Query = query, DateRangeStart = dateRangeStart, DateRangeEnd = dateRangeEnd },
            ct);
    }

    /// <summary>
    /// Get available trace attribute keys for filter UI.
    /// </summary>
    public async Task<List<string>> GetTraceKeys(
        int projectId,
        DateTime dateRangeStart,
        DateTime dateRangeEnd,
        string? query,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await clickHouse.GetTraceKeysAsync(projectId,
            new QueryInput { Query = query ?? "", DateRangeStart = dateRangeStart, DateRangeEnd = dateRangeEnd },
            ct);
    }

    /// <summary>
    /// Get values for a specific trace attribute key.
    /// </summary>
    public async Task<List<string>> GetTraceKeyValues(
        int projectId,
        string key,
        DateTime dateRangeStart,
        DateTime dateRangeEnd,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await clickHouse.GetTraceKeyValuesAsync(projectId, key,
            new QueryInput { DateRangeStart = dateRangeStart, DateRangeEnd = dateRangeEnd },
            ct);
    }

    // ── Metrics (ClickHouse) ────────────────────────────────────────

    /// <summary>
    /// Query metrics from ClickHouse with time bucketing and aggregation.
    /// </summary>
    public async Task<MetricsBuckets> GetMetrics(
        int projectId,
        string query,
        DateTime dateRangeStart,
        DateTime dateRangeEnd,
        string bucketBy,
        List<string>? groupBy,
        string aggregator,
        string? column,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await clickHouse.ReadMetricsAsync(projectId,
            new QueryInput { Query = query, DateRangeStart = dateRangeStart, DateRangeEnd = dateRangeEnd },
            bucketBy, groupBy, aggregator, column, ct);
    }

    // ── Error Group Search (ClickHouse) ─────────────────────────────

    /// <summary>
    /// Search error group IDs via ClickHouse (for the error groups list page).
    /// Returns IDs and total count for pagination.
    /// </summary>
    public async Task<ErrorGroupSearchResult> SearchErrorGroups(
        int projectId,
        string query,
        DateTime dateRangeStart,
        DateTime dateRangeEnd,
        int count,
        int page,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var (ids, total) = await clickHouse.QueryErrorGroupIdsAsync(projectId,
            new QueryInput { Query = query, DateRangeStart = dateRangeStart, DateRangeEnd = dateRangeEnd },
            count, page, ct);

        return new ErrorGroupSearchResult { ErrorGroupIds = ids, TotalCount = total };
    }

    /// <summary>
    /// Get error objects histogram for charts.
    /// </summary>
    public async Task<List<HistogramBucket>> GetErrorObjectsHistogram(
        int projectId,
        string query,
        DateTime dateRangeStart,
        DateTime dateRangeEnd,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await clickHouse.ReadErrorObjectsHistogramAsync(projectId,
            new QueryInput { Query = query, DateRangeStart = dateRangeStart, DateRangeEnd = dateRangeEnd },
            ct);
    }

    // ── Session Search (ClickHouse) ─────────────────────────────────

    /// <summary>
    /// Search session IDs via ClickHouse (for the sessions list page).
    /// </summary>
    public async Task<SessionSearchResult> SearchSessions(
        int projectId,
        string query,
        DateTime dateRangeStart,
        DateTime dateRangeEnd,
        int count,
        int page,
        string? sortField,
        bool sortDesc,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var (ids, total) = await clickHouse.QuerySessionIdsAsync(projectId,
            new QueryInput { Query = query, DateRangeStart = dateRangeStart, DateRangeEnd = dateRangeEnd },
            count, page, sortField, sortDesc, ct);

        return new SessionSearchResult { SessionIds = ids, TotalCount = total };
    }

    // ── Admin Role ──────────────────────────────────────────────────

    /// <summary>
    /// Get the admin's role within a workspace. Returns null if not a member.
    /// </summary>
    public async Task<string?> GetAdminRole(
        int workspaceId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);
        var result = await authz.GetAdminRoleAsync(admin.Id, workspaceId, ct);
        return result?.Role;
    }

    /// <summary>
    /// Get the admin's role for a workspace via project ID.
    /// </summary>
    public async Task<string?> GetAdminRoleByProject(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);
        var project = await db.Projects.FindAsync([projectId], ct);
        if (project == null) return null;
        var result = await authz.GetAdminRoleAsync(admin.Id, project.WorkspaceId, ct);
        return result?.Role;
    }

    // ── Workspace Pending Invites ───────────────────────────────────

    /// <summary>
    /// Get pending (non-expired) invites for the current admin.
    /// </summary>
    public async Task<List<WorkspaceInviteLink>> GetWorkspacePendingInvites(
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);
        var now = DateTime.UtcNow;

        return await db.WorkspaceInviteLinks
            .Where(l => l.InviteeEmail == admin.Email
                        && (l.ExpirationDate == null || l.ExpirationDate > now))
            .Include(l => l.Workspace)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get workspaces the user can join (has pending invites).
    /// </summary>
    public async Task<List<Workspace>> GetJoinableWorkspaces(
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);
        var now = DateTime.UtcNow;

        return await db.WorkspaceInviteLinks
            .Where(l => l.InviteeEmail == admin.Email
                        && (l.ExpirationDate == null || l.ExpirationDate > now))
            .Select(l => l.Workspace)
            .Distinct()
            .ToListAsync(ct);
    }

    // ── Session Detail ──────────────────────────────────────────────

    /// <summary>
    /// Get session events (raw recording data) from storage.
    /// </summary>
    public async Task<List<EventChunk>> GetEvents(
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
    /// Check if a session is still being processed.
    /// </summary>
    public async Task<bool> IsSessionPending(
        int sessionId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var session = await db.Sessions.FindAsync([sessionId], ct);
        if (session == null) return false;

        await AuthHelper.RequireProjectAccess(claimsPrincipal, session.ProjectId, authz, ct);

        return session.Processed != true;
    }

    // ── Error Detail ────────────────────────────────────────────────

    /// <summary>
    /// Get a single error instance with full stack trace and metadata.
    /// </summary>
    public async Task<ErrorObject?> GetErrorInstance(
        int errorGroupId,
        int? errorObjectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var eg = await db.ErrorGroups.FindAsync([errorGroupId], ct);
        if (eg != null)
            await AuthHelper.RequireProjectAccess(claimsPrincipal, eg.ProjectId, authz, ct);

        if (errorObjectId.HasValue)
        {
            return await db.ErrorObjects
                .Include(e => e.ErrorGroup)
                .FirstOrDefaultAsync(e => e.Id == errorObjectId.Value, ct);
        }

        // Default: return latest error object
        return await db.ErrorObjects
            .Include(e => e.ErrorGroup)
            .Where(e => e.ErrorGroupId == errorGroupId)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Get the error object linked to a log entry.
    /// </summary>
    public async Task<ErrorObject?> GetErrorObjectForLog(
        int logCursor,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var eo = await db.ErrorObjects
            .Include(e => e.ErrorGroup)
            .FirstOrDefaultAsync(e => e.Id == logCursor, ct);

        if (eo != null)
            await AuthHelper.RequireProjectAccess(claimsPrincipal, eo.ErrorGroup.ProjectId, authz, ct);

        return eo;
    }

    // ── Error Comments ──────────────────────────────────────────────

    /// <summary>
    /// List all error comments visible to the current admin.
    /// </summary>
    public async Task<List<ErrorComment>> GetErrorCommentsForAdmin(
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        return await db.ErrorComments
            .Where(c => c.AdminId == admin.Id)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// List all error comments for a project.
    /// </summary>
    public async Task<List<ErrorComment>> GetErrorCommentsForProject(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        // Error comments are on error groups which belong to projects
        return await db.ErrorComments
            .Include(c => c.ErrorGroup)
            .Where(c => c.ErrorGroup.ProjectId == projectId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);
    }

    // ── Session Comments (Admin-scoped) ─────────────────────────────

    /// <summary>
    /// List all session comments created by the current admin.
    /// </summary>
    public async Task<List<SessionComment>> GetSessionCommentsForAdmin(
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        return await db.SessionComments
            .Include(c => c.Tags)
            .Where(c => c.AdminId == admin.Id)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);
    }

    // ── Session Comment Tags ────────────────────────────────────────

    /// <summary>
    /// Get all distinct comment tags for a project.
    /// </summary>
    public async Task<List<SessionCommentTag>> GetSessionCommentTagsForProject(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await db.SessionCommentTags
            .Where(t => t.SessionComment.ProjectId == projectId)
            .GroupBy(t => t.Name)
            .Select(g => g.First())
            .ToListAsync(ct);
    }

    // ── Rage Clicks (Project) ───────────────────────────────────────

    /// <summary>
    /// Get rage click events for an entire project.
    /// </summary>
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public async Task<IQueryable<RageClickEvent>> GetRageClicksForProject(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return db.RageClickEvents
            .Include(r => r.Session)
            .Where(r => r.Session.ProjectId == projectId);
    }

    // ── Integration Status ──────────────────────────────────────────

    /// <summary>
    /// Check if a project has any sessions (client integration working).
    /// </summary>
    public async Task<bool> GetClientIntegration(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        return await db.Sessions.AnyAsync(s => s.ProjectId == projectId, ct);
    }

    /// <summary>
    /// Check if a project has any error groups (server integration working).
    /// </summary>
    public async Task<bool> GetServerIntegration(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        return await db.ErrorGroups.AnyAsync(eg => eg.ProjectId == projectId, ct);
    }

    /// <summary>
    /// Check if a project has received any logs.
    /// </summary>
    public async Task<bool> GetLogsIntegration(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var result = await clickHouse.ReadLogsAsync(projectId,
            new QueryInput
            {
                DateRangeStart = DateTime.UtcNow.AddDays(-30),
                DateRangeEnd = DateTime.UtcNow,
            },
            new ClickHousePagination { Limit = 1 }, ct);
        return result.Edges.Count > 0;
    }

    /// <summary>
    /// Check if a project has received any traces.
    /// </summary>
    public async Task<bool> GetTracesIntegration(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var result = await clickHouse.ReadTracesAsync(projectId,
            new QueryInput
            {
                DateRangeStart = DateTime.UtcNow.AddDays(-30),
                DateRangeEnd = DateTime.UtcNow,
            },
            new ClickHousePagination { Limit = 1 }, ct: ct);
        return result.Edges.Count > 0;
    }

    // ── Alert Detail ────────────────────────────────────────────────

    /// <summary>
    /// Get a single alert by ID.
    /// </summary>
    public async Task<Alert?> GetAlert(
        int id,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var alert = await db.Alerts
            .Include(a => a.Destinations)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (alert != null)
            await AuthHelper.RequireProjectAccess(claimsPrincipal, alert.ProjectId, authz, ct);

        return alert;
    }

    // ── Metric Monitors ─────────────────────────────────────────────

    /// <summary>
    /// Get metric monitors for a project.
    /// </summary>
    public async Task<List<MetricMonitor>> GetMetricMonitors(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await db.MetricMonitors
            .Where(m => m.ProjectId == projectId)
            .ToListAsync(ct);
    }

    // ── Event Chunk URL ─────────────────────────────────────────────

    /// <summary>
    /// Get a pre-signed download URL for a specific event chunk.
    /// </summary>
    public async Task<string?> GetEventChunkUrl(
        int sessionId,
        int chunkIndex,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        [Service] Storage.IStorageService storage,
        CancellationToken ct)
    {
        var session = await db.Sessions.FindAsync([sessionId], ct);
        if (session == null) return null;
        await AuthHelper.RequireProjectAccess(claimsPrincipal, session.ProjectId, authz, ct);

        var chunk = await db.EventChunks
            .FirstOrDefaultAsync(c => c.SessionId == sessionId && c.ChunkIndex == chunkIndex, ct);
        if (chunk == null) return null;

        return await storage.GetDownloadUrlAsync(
            "sessions", $"{session.ProjectId}/{sessionId}/{chunkIndex}",
            TimeSpan.FromMinutes(5), ct);
    }

    // ── Admin Check Flags ───────────────────────────────────────────

    /// <summary>
    /// Check if the current admin has created any session comments.
    /// </summary>
    public async Task<bool> GetAdminHasCreatedComment(
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);
        return await db.SessionComments.AnyAsync(c => c.AdminId == admin.Id, ct);
    }

    /// <summary>
    /// Check if any sessions have been viewed in a project.
    /// </summary>
    public async Task<bool> GetProjectHasViewedASession(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        return await db.Sessions.AnyAsync(s => s.ProjectId == projectId && s.ViewedByAdmins != null && s.ViewedByAdmins > 0, ct);
    }

    // ── Legacy Alert Queries ──────────────────────────────────────────

    /// <summary>
    /// Get error alerts for a project.
    /// </summary>
    public async Task<List<ErrorAlert>> GetErrorAlerts(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        return await db.ErrorAlerts.Where(a => a.ProjectId == projectId).OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
    }

    /// <summary>
    /// Get session alerts for a project, optionally filtered by type.
    /// </summary>
    public async Task<List<SessionAlert>> GetSessionAlerts(
        int projectId,
        string? type,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        var query = db.SessionAlerts.Where(a => a.ProjectId == projectId);
        if (!string.IsNullOrEmpty(type))
            query = query.Where(a => a.Type == type);
        return await query.OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
    }

    /// <summary>
    /// Get log alerts for a project.
    /// </summary>
    public async Task<List<LogAlert>> GetLogAlerts(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        return await db.LogAlerts.Where(a => a.ProjectId == projectId).OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
    }

    /// <summary>
    /// Get a single log alert by id.
    /// </summary>
    public async Task<LogAlert?> GetLogAlert(
        int id,
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        return await db.LogAlerts.FirstOrDefaultAsync(a => a.Id == id && a.ProjectId == projectId, ct);
    }

    // ── Visualization & Graph Queries ────────────────────────────────

    /// <summary>
    /// Get all visualizations for a project.
    /// </summary>
    public async Task<List<Visualization>> GetVisualizations(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        return await db.Visualizations
            .Where(v => v.ProjectId == projectId)
            .Include(v => v.Graphs)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get a single visualization by id.
    /// </summary>
    public async Task<Visualization?> GetVisualization(
        int id,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var viz = await db.Visualizations
            .Include(v => v.Graphs)
            .FirstOrDefaultAsync(v => v.Id == id, ct);
        if (viz == null) return null;

        await AuthHelper.RequireProjectAccess(claimsPrincipal, viz.ProjectId, authz, ct);
        return viz;
    }

    /// <summary>
    /// Get a single graph by id.
    /// </summary>
    public async Task<Graph?> GetGraph(
        int id,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var graph = await db.Graphs.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (graph == null) return null;

        await AuthHelper.RequireProjectAccess(claimsPrincipal, graph.ProjectId, authz, ct);
        return graph;
    }

    // ── Analytics Queries ────────────────────────────────────────────

    /// <summary>
    /// Count unprocessed sessions (last 4h10m window, matching Go lookback).
    /// </summary>
    public async Task<long> GetUnprocessedSessionsCount(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        var cutoff = DateTime.UtcNow.AddHours(-4).AddMinutes(-10);
        return await db.Sessions
            .Where(s => s.ProjectId == projectId
                && s.Processed != true
                && s.Excluded != true
                && s.CreatedAt > cutoff)
            .LongCountAsync(ct);
    }

    /// <summary>
    /// Count distinct live users (unprocessed sessions in last 4h10m).
    /// </summary>
    public async Task<long> GetLiveUsersCount(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        var cutoff = DateTime.UtcNow.AddHours(-4).AddMinutes(-10);
        return await db.Sessions
            .Where(s => s.ProjectId == projectId
                && s.Processed != true
                && s.Excluded != true
                && s.CreatedAt > cutoff)
            .Select(s => string.IsNullOrEmpty(s.Identifier) ? s.Fingerprint.ToString() : s.Identifier)
            .Distinct()
            .LongCountAsync(ct);
    }

    /// <summary>
    /// Daily session counts for a date range (from materialized view).
    /// </summary>
    public async Task<List<DailySessionCount>> GetDailySessionsCount(
        int projectId,
        DateTime startDate,
        DateTime endDate,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        var start = startDate.Date;
        var end = endDate.Date;
        return await db.DailySessionCounts
            .Where(d => d.ProjectId == projectId && d.Date >= start && d.Date <= end)
            .OrderBy(d => d.Date)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Daily error counts for a date range (from materialized view).
    /// </summary>
    public async Task<List<DailyErrorCount>> GetDailyErrorsCount(
        int projectId,
        DateTime startDate,
        DateTime endDate,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        var start = startDate.Date;
        var end = endDate.Date;
        return await db.DailyErrorCounts
            .Where(d => d.ProjectId == projectId && d.Date >= start && d.Date <= end)
            .OrderBy(d => d.Date)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Workspace admins filtered by project-level access.
    /// </summary>
    public async Task<List<Admin>> GetWorkspaceAdminsByProjectId(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var project = await db.Projects.FindAsync([projectId], ct)
            ?? throw new GraphQLException("Project not found");

        // Get all workspace admins via join table
        var adminIds = await db.WorkspaceAdmins
            .Where(wa => wa.WorkspaceId == project.WorkspaceId)
            .Select(wa => wa.AdminId)
            .ToListAsync(ct);

        return await db.Admins.Where(a => adminIds.Contains(a.Id)).ToListAsync(ct);
    }

    // ── ClickHouse Key Queries (Sessions/Errors/Events) ─────────────

    /// <summary>
    /// Get searchable keys for sessions.
    /// </summary>
    public async Task<List<QueryKey>> GetSessionsKeys(
        int projectId,
        DateTime startDate,
        DateTime endDate,
        string? query,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        return await clickHouse.GetSessionsKeysAsync(projectId, startDate, endDate, query, ct);
    }

    /// <summary>
    /// Get key values for sessions.
    /// </summary>
    public async Task<List<string>> GetSessionsKeyValues(
        int projectId,
        string keyName,
        DateTime startDate,
        DateTime endDate,
        string? query,
        int? count,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        return await clickHouse.GetSessionsKeyValuesAsync(projectId, keyName, startDate, endDate, query, count, ct);
    }

    /// <summary>
    /// Get searchable keys for errors (reserved key set, matching Go).
    /// </summary>
    public Task<List<QueryKey>> GetErrorsKeys(
        int projectId,
        string? query,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        CancellationToken ct)
    {
        // Go returns a static list of reserved error keys, filtered by query
        var reservedKeys = new[] {
            "browser", "environment", "event", "os_name", "service_name",
            "service_version", "secure_session_id", "status", "tag", "type",
            "visited_url"
        };

        var keys = reservedKeys
            .Where(k => string.IsNullOrEmpty(query) || k.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(k => new QueryKey { Name = k, Type = "String" })
            .ToList();

        return Task.FromResult(keys);
    }

    /// <summary>
    /// Get key values for errors (delegates to ClickHouse).
    /// </summary>
    public async Task<List<string>> GetErrorsKeyValues(
        int projectId,
        string keyName,
        DateTime startDate,
        DateTime endDate,
        string? query,
        int? count,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        return await clickHouse.GetErrorsKeyValuesAsync(projectId, keyName, startDate, endDate, query, count, ct);
    }

    /// <summary>
    /// Get searchable keys for events (delegates to ClickHouse).
    /// </summary>
    public async Task<List<QueryKey>> GetEventsKeys(
        int projectId,
        DateTime startDate,
        DateTime endDate,
        string? query,
        string? eventName,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        return await clickHouse.GetEventsKeysAsync(projectId, startDate, endDate, query, eventName, ct);
    }

    /// <summary>
    /// Get key values for events (delegates to ClickHouse).
    /// </summary>
    public async Task<List<string>> GetEventsKeyValues(
        int projectId,
        string keyName,
        DateTime startDate,
        DateTime endDate,
        string? query,
        int? count,
        string? eventName,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        return await clickHouse.GetEventsKeyValuesAsync(projectId, keyName, startDate, endDate, query, count, eventName, ct);
    }

    /// <summary>
    /// Get workspaces count for the current admin.
    /// </summary>
    public async Task<long> GetWorkspacesCount(
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);
        return await db.WorkspaceAdmins
            .Where(wa => wa.AdminId == admin.Id)
            .Select(wa => wa.WorkspaceId)
            .Distinct()
            .LongCountAsync(ct);
    }

    /// <summary>
    /// Get email opt-outs for the current admin.
    /// </summary>
    public async Task<List<EmailOptOut>> GetEmailOptOuts(
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);
        return await db.EmailOptOuts.Where(e => e.AdminId == admin.Id).ToListAsync(ct);
    }

    // ── Dashboard Definitions ────────────────────────────────────────

    /// <summary>
    /// Get dashboard definitions with metrics and filters for a project.
    /// </summary>
    public async Task<List<Dashboard>> GetDashboardDefinitions(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        return await db.Dashboards
            .Where(d => d.ProjectId == projectId)
            .Include(d => d.Metrics)
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync(ct);
    }

    // ── Metric Tags/Values ───────────────────────────────────────────

    /// <summary>
    /// Get metric tag keys (delegates to trace keys with 30-day lookback).
    /// </summary>
    public async Task<List<string>> GetMetricTags(
        int projectId,
        string metricName,
        string? query,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        var keys = await clickHouse.GetTraceKeysAsync(
            projectId,
            new QueryInput
            {
                Query = query ?? "",
                DateRangeStart = DateTime.UtcNow.AddDays(-30),
                DateRangeEnd = DateTime.UtcNow
            },
            ct);
        return keys;
    }

    /// <summary>
    /// Get metric tag values for a given tag key.
    /// </summary>
    public async Task<List<string>> GetMetricTagValues(
        int projectId,
        string metricName,
        string tagName,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        return await clickHouse.GetTraceKeyValuesAsync(
            projectId,
            tagName,
            new QueryInput
            {
                Query = "",
                DateRangeStart = DateTime.UtcNow.AddDays(-30),
                DateRangeEnd = DateTime.UtcNow
            },
            ct);
    }

    // ── Session Histogram ────────────────────────────────────────────

    /// <summary>
    /// Get sessions histogram (delegates to ClickHouse).
    /// </summary>
    public async Task<List<HistogramBucket>> GetSessionsHistogram(
        int projectId,
        string query,
        DateTime startDate,
        DateTime endDate,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        return await clickHouse.ReadSessionsHistogramAsync(
            projectId,
            new QueryInput { Query = query, DateRangeStart = startDate, DateRangeEnd = endDate },
            ct);
    }

    /// <summary>
    /// Get errors histogram (delegates to ClickHouse).
    /// </summary>
    public async Task<List<HistogramBucket>> GetErrorsHistogram(
        int projectId,
        string query,
        DateTime startDate,
        DateTime endDate,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        return await clickHouse.ReadErrorObjectsHistogramAsync(
            projectId,
            new QueryInput { Query = query, DateRangeStart = startDate, DateRangeEnd = endDate },
            ct);
    }

    // ── Error Issue & Enhanced Details ────────────────────────────────

    /// <summary>
    /// Get enhanced user details by email lookup.
    /// </summary>
    public async Task<EnhancedUserDetails?> GetEnhancedUserDetails(
        string email,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);
        return await db.Set<EnhancedUserDetails>()
            .FirstOrDefaultAsync(e => e.Email != null && e.Email.ToLower() == email.ToLower(), ct);
    }

    /// <summary>
    /// Get a single trace by ID (for trace detail view).
    /// </summary>
    public async Task<TraceConnection> GetTrace(
        int projectId,
        string traceId,
        string? sessionSecureId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        return await clickHouse.ReadTracesAsync(
            projectId,
            new QueryInput { Query = $"trace_id={traceId}", DateRangeStart = DateTime.UtcNow.AddDays(-30), DateRangeEnd = DateTime.UtcNow },
            new ClickHousePagination { Limit = 1000 },
            omitBody: false,
            ct: ct);
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
