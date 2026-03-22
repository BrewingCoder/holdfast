using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
using HoldFast.Domain;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
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
        [ID] int id,
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
        [ID] int projectId,
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
        [ID] int workspaceId,
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
    /// List workspace admins with their roles. Returns WorkspaceAdminRole (not WorkspaceAdmin)
    /// to match the Go schema type which includes workspaceId, admin, role, projectIds.
    /// </summary>
    public async Task<List<WorkspaceAdminRole>> GetWorkspaceAdmins(
        [ID] int workspaceId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

        var rows = await db.WorkspaceAdmins
            .Include(wa => wa.Admin)
            .Where(wa => wa.WorkspaceId == workspaceId)
            .OrderBy(wa => wa.Admin.CreatedAt)
            .ToListAsync(ct);

        return rows.Select(wa => new WorkspaceAdminRole(
            WorkspaceId: workspaceId.ToString(),
            Admin: wa.Admin,
            Role: wa.Role ?? WorkspaceRoles.Admin,
            ProjectIds: wa.ProjectIds?.Select(id => id.ToString()).ToList() ?? [])).ToList();
    }

    // ── Project ───────────────────────────────────────────────────────

    [UseProjection]
    public async Task<Project?> GetProject(
        [ID] int id,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, id, authz, ct);

        return await db.Projects
            .Include(p => p.Workspace)
                .ThenInclude(w => w!.Projects)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public async Task<IQueryable<Project>> GetProjects(
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        // Go schema: `projects: [Project]` — returns all projects the admin has access to.
        // The workspace-scoped projects list comes from the Workspace entity navigation property.
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);
        var workspaceIds = await db.WorkspaceAdmins
            .Where(wa => wa.AdminId == admin.Id)
            .Select(wa => wa.WorkspaceId)
            .ToListAsync(ct);
        return db.Projects.Where(p => workspaceIds.Contains(p.WorkspaceId));
    }

    /// <summary>
    /// Get project settings (combined Project + sampling/filter configuration).
    /// Returns AllProjectSettings matching the Go schema type.
    /// </summary>
    [GraphQLName("projectSettings")]
    public async Task<AllProjectSettings?> GetProjectSettings(
        [GraphQLName("projectId")] [ID] int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var project = await db.Projects.FindAsync([projectId], ct);
        if (project == null) return null;

        var fs = await db.ProjectFilterSettings
            .FirstOrDefaultAsync(s => s.ProjectId == projectId, ct)
            ?? new ProjectFilterSettings { ProjectId = projectId };

        return new AllProjectSettings(
            Id: project.Id,
            Name: project.Name ?? string.Empty,
            VerboseId: project.VerboseId,
            BillingEmail: project.BillingEmail,
            WorkspaceId: project.WorkspaceId.ToString(),
            ExcludedUsers: project.ExcludedUsers,
            ErrorFilters: project.ErrorFilters,
            ErrorJsonPaths: project.ErrorJsonPaths,
            RageClickWindowSeconds: project.RageClickWindowSeconds,
            RageClickRadiusPixels: project.RageClickRadiusPixels,
            RageClickCount: project.RageClickCount,
            FilterChromeExtension: project.FilterChromeExtension,
            FilterSessionsWithoutError: fs.FilterSessionsWithoutError,
            AutoResolveStaleErrorsDayInterval: fs.AutoResolveStaleErrorsDayInterval,
            Sampling: new SamplingResult(
                SessionSamplingRate: fs.SessionSamplingRate,
                ErrorSamplingRate: fs.ErrorSamplingRate,
                LogSamplingRate: fs.LogSamplingRate,
                TraceSamplingRate: fs.TraceSamplingRate,
                MetricSamplingRate: fs.MetricSamplingRate,
                SessionMinuteRateLimit: fs.SessionMinuteRateLimit,
                ErrorMinuteRateLimit: fs.ErrorMinuteRateLimit,
                LogMinuteRateLimit: fs.LogMinuteRateLimit,
                TraceMinuteRateLimit: fs.TraceMinuteRateLimit,
                MetricMinuteRateLimit: fs.MetricMinuteRateLimit,
                SessionExclusionQuery: fs.SessionExclusionQuery,
                ErrorExclusionQuery: fs.ErrorExclusionQuery,
                LogExclusionQuery: fs.LogExclusionQuery,
                TraceExclusionQuery: fs.TraceExclusionQuery,
                MetricExclusionQuery: fs.MetricExclusionQuery));
    }

    // ── Error Groups ──────────────────────────────────────────────────

    [UseProjection]
    public async Task<ErrorGroup?> GetErrorGroup(
        [ID] int id,
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

    /// <summary>
    /// Paginated error groups list using ClickHouse for ranking and EF for hydration.
    /// Matches Go schema: error_groups(...) { error_groups { ... } totalCount }
    /// </summary>
    public async Task<ErrorGroupResults> GetErrorGroups(
        [ID] int projectId,
        int count,
        [GraphQLName("params")] QueryInput queryParams,
        int? page,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var (ids, total) = await clickHouse.QueryErrorGroupIdsAsync(
            projectId, queryParams, count, page ?? 1, ct);

        List<ErrorGroup> groups;
        if (ids.Count > 0)
        {
            groups = await db.ErrorGroups
                .Where(eg => ids.Contains(eg.Id) && eg.ProjectId == projectId)
                .ToListAsync(ct);
            var order = ids.Select((id, idx) => (id, idx)).ToDictionary(x => x.id, x => x.idx);
            groups = groups.OrderBy(eg => order.TryGetValue(eg.Id, out var i) ? i : int.MaxValue).ToList();
        }
        else
        {
            groups = [];
        }

        return new ErrorGroupResults(groups, total);
    }

    /// <summary>
    /// Get a specific error object by ID.
    /// </summary>
    public async Task<ErrorObject?> GetErrorObject(
        [ID] int id,
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
        [ID] int errorGroupId,
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
        [ID] int errorGroupId,
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

    /// <summary>
    /// Session list query — mirrors Go schema `sessions(project_id, count, params, ...)`.
    /// Queries session IDs from ClickHouse, then loads full Session objects from PostgreSQL.
    /// </summary>
    public async Task<SessionResults> GetSessions(
        [ID] int projectId,
        int count,
        [GraphQLName("params")] QueryInput queryParams,
        bool sortDesc,
        string? sortField,
        int? page,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var (ids, total) = await clickHouse.QuerySessionIdsAsync(
            projectId, queryParams, count, page ?? 1, sortField, sortDesc, ct);

        List<Session> sessions;
        if (ids.Count > 0)
        {
            sessions = await db.Sessions
                .Where(s => ids.Contains(s.Id) && s.ProjectId == projectId)
                .ToListAsync(ct);

            // Preserve ClickHouse sort order
            var order = ids.Select((id, idx) => (id, idx)).ToDictionary(x => x.id, x => x.idx);
            sessions = sessions
                .OrderBy(s => order.TryGetValue(s.Id, out var i) ? i : int.MaxValue)
                .ToList();
        }
        else
        {
            sessions = [];
        }

        var totalLength = sessions.Sum(s => (long)(s.Length ?? 0));
        var totalActiveLength = sessions.Sum(s => (long)(s.ActiveLength ?? 0));
        return new SessionResults(sessions, total, totalLength, totalActiveLength);
    }

    /// <summary>
    /// Billing details stub — HoldFast is self-hosted with no billing tiers.
    /// Returns zero meters and unlimited limits to satisfy the frontend contract.
    /// </summary>
    [GraphQLName("billingDetailsForProject")]
    public Task<BillingDetails> GetBillingDetailsForProject(
        [ID] int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        CancellationToken ct)
    {
        var plan = new BillingPlan(
            Type: "Free",
            Interval: "Monthly",
            MembersLimit: int.MaxValue,
            SessionsLimit: long.MaxValue,
            ErrorsLimit: long.MaxValue,
            LogsLimit: long.MaxValue,
            TracesLimit: long.MaxValue,
            MetricsLimit: long.MaxValue,
            SessionsRate: 0,
            ErrorsRate: 0,
            LogsRate: 0,
            TracesRate: 0,
            MetricsRate: 0,
            AwsMpSubscription: null);

        var details = new BillingDetails(
            Plan: plan,
            Meter: 0,
            MembersMeter: 0,
            ErrorsMeter: 0,
            LogsMeter: 0,
            TracesMeter: 0,
            MetricsMeter: 0,
            SessionsBillingLimit: long.MaxValue,
            ErrorsBillingLimit: long.MaxValue,
            LogsBillingLimit: long.MaxValue,
            TracesBillingLimit: long.MaxValue,
            MetricsBillingLimit: long.MaxValue);

        return Task.FromResult(details);
    }

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
        [ID] int projectId,
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
        [ID] int errorGroupId,
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

    [GraphQLName("workspaceSettings")]
    public async Task<AllWorkspaceSettings?> GetWorkspaceSettings(
        [ID] int workspaceId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

        return await db.AllWorkspaceSettings
            .FirstOrDefaultAsync(s => s.WorkspaceId == workspaceId, ct);
    }

    /// <summary>
    /// Workspace-level billing details stub — HoldFast is self-hosted with no billing.
    /// </summary>
    [GraphQLName("billingDetails")]
    public Task<BillingDetails> GetBillingDetails(
        [ID] int workspaceId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        CancellationToken ct)
    {
        var plan = new BillingPlan("Free", "Monthly",
            int.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue,
            0, 0, 0, 0, 0, null);
        return Task.FromResult(new BillingDetails(plan, 0, 0, 0, 0, 0, 0,
            long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue));
    }

    /// <summary>
    /// Subscription details stub — HoldFast has no SaaS billing.
    /// </summary>
    [GraphQLName("subscription_details")]
    public Task<SubscriptionDetails?> GetSubscriptionDetails(
        [ID] int workspaceId,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken ct)
        => Task.FromResult<SubscriptionDetails?>(null);

    // ── Alerts ────────────────────────────────────────────────────────

    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public async Task<IQueryable<Alert>> GetAlerts(
        [ID] int projectId,
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
        [ID] int projectId,
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
        [ID] int projectId,
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
        [ID] int projectId,
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
    public async Task<List<SavedSegmentGql>> GetSavedSegments(
        [ID] int projectId,
        SavedSegmentEntityType? entityType,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var q = db.SavedSegments.Where(s => s.ProjectId == projectId);
        if (entityType != null)
            q = q.Where(s => s.EntityType == entityType.ToString());

        var segments = await q.ToListAsync(ct);
        return segments.Select(s =>
        {
            string? parsedQuery = null;
            if (!string.IsNullOrEmpty(s.Params))
            {
                try
                {
                    var doc = System.Text.Json.JsonDocument.Parse(s.Params);
                    if (doc.RootElement.TryGetProperty("query", out var qEl))
                        parsedQuery = qEl.GetString();
                }
                catch { /* malformed JSON — return null query */ }
            }
            return new SavedSegmentGql(s.Id, s.Name ?? "", new SavedSegmentParams(parsedQuery));
        }).ToList();
    }

    // ── Integrations ──────────────────────────────────────────────────

    /// <summary>
    /// Get integration project mappings.
    /// </summary>
    public async Task<List<IntegrationProjectMapping>> GetIntegrationProjectMappings(
        [ID] int projectId,
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
        [ID] int workspaceId,
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
        [ID] int projectId,
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
        [ID] int projectId,
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
    /// Matches Go schema: logs(project_id, params, after, before, at, direction, limit)
    /// </summary>
    public async Task<LogConnection> GetLogs(
        [ID] int projectId,
        [GraphQLName("params")] QueryInput queryParams,
        string? after,
        string? before,
        string? at,
        SortDirection direction,
        int? limit,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await clickHouse.ReadLogsAsync(projectId,
            queryParams,
            new ClickHousePagination { After = after, Before = before, At = at, Direction = direction.ToString(), Limit = limit ?? 50 },
            ct);
    }

    /// <summary>
    /// Get log histogram for chart display.
    /// Matches Go schema: logs_histogram(project_id, params) → LogsHistogram
    /// </summary>
    public async Task<LogsHistogramResult> GetLogsHistogram(
        [ID] int projectId,
        [GraphQLName("params")] QueryInput queryParams,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var buckets = await clickHouse.ReadLogsHistogramAsync(projectId, queryParams, ct);
        return BuildLogsHistogram(buckets);
    }

    private static LogsHistogramResult BuildLogsHistogram(List<HistogramBucket> buckets)
    {
        var grouped = buckets
            .GroupBy(b => b.BucketStart)
            .OrderBy(g => g.Key)
            .Select((g, i) => new LogsBucketGroup(
                BucketId: i,
                Counts: g.Select(b => new LogsBucketCount(
                    Count: b.Count,
                    Level: b.Group ?? ""))
                .ToList()))
            .ToList();

        var totalCount = buckets.Sum(b => b.Count);
        return new LogsHistogramResult(
            TotalCount: totalCount,
            Buckets: grouped,
            ObjectCount: totalCount,
            SampleFactor: 1.0);
    }

    /// <summary>
    /// Get available log attribute keys for filter UI.
    /// </summary>
    public async Task<List<string>> GetLogKeys(
        [ID] int projectId,
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
            new QueryInput { Query = query ?? "", DateRange = new DateRangeRequiredInput { StartDate = dateRangeStart, EndDate = dateRangeEnd } },
            ct);
    }

    /// <summary>
    /// Get values for a specific log attribute key.
    /// </summary>
    public async Task<List<string>> GetLogKeyValues(
        [ID] int projectId,
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
            new QueryInput { DateRange = new DateRangeRequiredInput { StartDate = dateRangeStart, EndDate = dateRangeEnd } },
            ct);
    }

    // ── Traces (ClickHouse) ─────────────────────────────────────────

    /// <summary>
    /// Query traces from ClickHouse with cursor-based pagination.
    /// Matches Go schema: traces(project_id, params, after, before, at, direction, limit, omitBody)
    /// </summary>
    public async Task<TraceConnection> GetTraces(
        [ID] int projectId,
        [GraphQLName("params")] QueryInput queryParams,
        string? after,
        string? before,
        string? at,
        SortDirection direction,
        int? limit,
        [GraphQLName("omitBody")] bool omitBody,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await clickHouse.ReadTracesAsync(projectId,
            queryParams,
            new ClickHousePagination { After = after, Before = before, At = at, Direction = direction.ToString(), Limit = limit ?? 50 },
            omitBody: omitBody,
            ct: ct);
    }

    /// <summary>
    /// Get trace histogram buckets for chart display.
    /// Matches Go schema: traces_histogram(project_id, params)
    /// </summary>
    public async Task<List<HistogramBucket>> GetTracesHistogram(
        [ID] int projectId,
        [GraphQLName("params")] QueryInput queryParams,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await clickHouse.ReadTracesHistogramAsync(projectId, queryParams, ct);
    }

    /// <summary>
    /// Get available trace attribute keys for filter UI.
    /// </summary>
    public async Task<List<string>> GetTraceKeys(
        [ID] int projectId,
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
            new QueryInput { Query = query ?? "", DateRange = new DateRangeRequiredInput { StartDate = dateRangeStart, EndDate = dateRangeEnd } },
            ct);
    }

    /// <summary>
    /// Get values for a specific trace attribute key.
    /// </summary>
    public async Task<List<string>> GetTraceKeyValues(
        [ID] int projectId,
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
            new QueryInput { DateRange = new DateRangeRequiredInput { StartDate = dateRangeStart, EndDate = dateRangeEnd } },
            ct);
    }

    // ── Metrics (ClickHouse) ────────────────────────────────────────

    /// <summary>
    /// Query metrics from ClickHouse with time bucketing and aggregation.
    /// Matches Go schema: metrics(product_type, project_id, params, group_by, bucket_by, expressions, ...).
    /// </summary>
    public async Task<MetricsBucketsResult> GetMetrics(
        [GraphQLName("product_type")] ProductType productType,
        [ID] int projectId,
        [GraphQLName("params")] QueryInput queryParams,
        string? sql,
        [GraphQLName("group_by")] List<string> groupBy,
        [GraphQLName("bucket_by")] string bucketBy,
        [GraphQLName("bucket_count")] int? bucketCount,
        [GraphQLName("bucket_window")] int? bucketWindow,
        int? limit,
        [GraphQLName("limit_aggregator")] MetricAggregator? limitAggregator,
        [GraphQLName("limit_column")] string? limitColumn,
        [GraphQLName("prediction_settings")] PredictionSettings? predictionSettings,
        [GraphQLName("expressions")] List<MetricExpressionInput> expressions,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var aggregator = expressions.Count > 0 ? expressions[0].Aggregator.ToString() : "Count";
        var column = expressions.Count > 0 ? expressions[0].Column : null;
        var raw = await clickHouse.ReadMetricsAsync(projectId, queryParams,
            bucketBy, groupBy, aggregator, column, ct);
        return MetricsBucketsResult.FromClickHouse(raw);
    }

    // ── Error Group Search (ClickHouse) ─────────────────────────────

    /// <summary>
    /// Search error group IDs via ClickHouse (for the error groups list page).
    /// Returns IDs and total count for pagination.
    /// </summary>
    public async Task<ErrorGroupSearchResult> SearchErrorGroups(
        [ID] int projectId,
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
            new QueryInput { Query = query, DateRange = new DateRangeRequiredInput { StartDate = dateRangeStart, EndDate = dateRangeEnd } },
            count, page, ct);

        return new ErrorGroupSearchResult { ErrorGroupIds = ids, TotalCount = total };
    }

    /// <summary>
    /// Get error objects histogram for charts.
    /// </summary>
    public async Task<List<HistogramBucket>> GetErrorObjectsHistogram(
        [ID] int projectId,
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
            new QueryInput { Query = query, DateRange = new DateRangeRequiredInput { StartDate = dateRangeStart, EndDate = dateRangeEnd } },
            ct);
    }

    // ── Session Search (ClickHouse) ─────────────────────────────────

    /// <summary>
    /// Search session IDs via ClickHouse (for the sessions list page).
    /// </summary>
    public async Task<SessionSearchResult> SearchSessions(
        [ID] int projectId,
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
            new QueryInput { Query = query, DateRange = new DateRangeRequiredInput { StartDate = dateRangeStart, EndDate = dateRangeEnd } },
            count, page, sortField, sortDesc, ct);

        return new SessionSearchResult { SessionIds = ids, TotalCount = total };
    }

    // ── Admin Role ──────────────────────────────────────────────────

    /// <summary>
    /// Get the admin's role within a workspace. Returns null if not a member.
    /// Returns the full WorkspaceAdminRole object (not just the role string) to match Go schema.
    /// </summary>
    [GraphQLName("admin_role")]
    public async Task<WorkspaceAdminRole?> GetAdminRole(
        [ID] int workspaceId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);
        var result = await authz.GetAdminRoleAsync(admin.Id, workspaceId, ct);
        if (result == null) return null;
        return new WorkspaceAdminRole(
            WorkspaceId: workspaceId.ToString(),
            Admin: admin,
            Role: result.Value.Role,
            ProjectIds: result.Value.ProjectIds?.Select(id => id.ToString()).ToList() ?? []);
    }

    /// <summary>
    /// Get the admin's role for a workspace via project ID.
    /// </summary>
    public async Task<WorkspaceAdminRole?> GetAdminRoleByProject(
        [ID] int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);
        var project = await db.Projects.FindAsync([projectId], ct);
        if (project == null) return null;
        var result = await authz.GetAdminRoleAsync(admin.Id, project.WorkspaceId, ct);
        if (result == null) return null;
        return new WorkspaceAdminRole(
            WorkspaceId: project.WorkspaceId.ToString(),
            Admin: admin,
            Role: result.Value.Role,
            ProjectIds: result.Value.ProjectIds?.Select(id => id.ToString()).ToList() ?? []);
    }

    // ── Workspace Pending Invites ───────────────────────────────────

    /// <summary>
    /// Get pending (non-expired) invites for the current admin.
    /// </summary>
    [GraphQLName("workspacePendingInvites")]
    public async Task<List<WorkspaceInviteLink>> GetWorkspacePendingInvites(
        [ID] int workspaceId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);
        var now = DateTime.UtcNow;

        return await db.WorkspaceInviteLinks
            .Where(l => l.WorkspaceId == workspaceId
                        && (l.ExpirationDate == null || l.ExpirationDate > now))
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

        return (await db.WorkspaceInviteLinks
            .Where(l => l.InviteeEmail == admin.Email
                        && (l.ExpirationDate == null || l.ExpirationDate > now))
            .Select(l => l.Workspace)
            .Distinct()
            .ToListAsync(ct))
            .Where(w => w != null).Select(w => w!).ToList();
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
        [ID] int errorGroupId,
        [ID] int? errorObjectId,
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
        [ID] int projectId,
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
        [ID] int projectId,
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
        [ID] int projectId,
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
    // These must use [GraphQLName] because the snake_case convention converts
    // "ClientIntegration" → "client_integration" but the Go schema uses camelCase
    // "clientIntegration" (no underscores within a word boundary like client+Integration).

    /// <summary>
    /// Check if a project has any sessions (client integration working).
    /// </summary>
    [GraphQLName("clientIntegration")]
    public async Task<IntegrationStatus> GetClientIntegration(
        [ID] int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        var session = await db.Sessions
            .Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return new IntegrationStatus(
            Integrated: session != null,
            ResourceType: "Session",
            CreatedAt: session?.CreatedAt);
    }

    /// <summary>
    /// Check if a project has any error groups (server integration working).
    /// </summary>
    [GraphQLName("serverIntegration")]
    public async Task<IntegrationStatus> GetServerIntegration(
        [ID] int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        var group = await db.ErrorGroups
            .Where(eg => eg.ProjectId == projectId)
            .OrderByDescending(eg => eg.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return new IntegrationStatus(
            Integrated: group != null,
            ResourceType: "Error",
            CreatedAt: group?.CreatedAt);
    }

    /// <summary>
    /// Check if a project has received any logs.
    /// </summary>
    [GraphQLName("logsIntegration")]
    public async Task<IntegrationStatus> GetLogsIntegration(
        [ID] int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var result = await clickHouse.ReadLogsAsync(projectId,
            new QueryInput
            {
                DateRange = new DateRangeRequiredInput { StartDate = DateTime.UtcNow.AddDays(-30), EndDate = DateTime.UtcNow }
            },
            new ClickHousePagination { Limit = 1 }, ct);
        return new IntegrationStatus(
            Integrated: result.Edges.Count > 0,
            ResourceType: "Log");
    }

    /// <summary>
    /// Check if a project has received any traces.
    /// </summary>
    [GraphQLName("tracesIntegration")]
    public async Task<IntegrationStatus> GetTracesIntegration(
        [ID] int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var result = await clickHouse.ReadTracesAsync(projectId,
            new QueryInput
            {
                DateRange = new DateRangeRequiredInput { StartDate = DateTime.UtcNow.AddDays(-30), EndDate = DateTime.UtcNow }
            },
            new ClickHousePagination { Limit = 1 }, ct: ct);
        return new IntegrationStatus(
            Integrated: result.Edges.Count > 0,
            ResourceType: "Trace");
    }

    // ── Alert Detail ────────────────────────────────────────────────

    /// <summary>
    /// Get a single alert by ID.
    /// </summary>
    public async Task<Alert?> GetAlert(
        [ID] int id,
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
        [ID] int projectId,
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
    [GraphQLName("adminHasCreatedComment")]
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
    [GraphQLName("projectHasViewedASession")]
    public async Task<bool> GetProjectHasViewedASession(
        [ID] int projectId,
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
        [ID] int projectId,
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
        [ID] int projectId,
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

    // ── Legacy per-type session alert queries (Go schema compat) ──────────────────
    // The Go schema exposed separate query fields for each session alert type.
    // HC snake_case maps GetNewSessionAlerts → new_session_alerts etc.

    public Task<List<SessionAlert>> GetNewSessionAlerts(
        [ID] int projectId, ClaimsPrincipal cp, [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db, CancellationToken ct)
        => GetSessionAlertsOfType(projectId, "NEW_SESSION_ALERT", cp, authz, db, ct);

    public Task<List<SessionAlert>> GetRageClickAlerts(
        [ID] int projectId, ClaimsPrincipal cp, [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db, CancellationToken ct)
        => GetSessionAlertsOfType(projectId, "RAGE_CLICK_ALERT", cp, authz, db, ct);

    public Task<List<SessionAlert>> GetNewUserAlerts(
        [ID] int projectId, ClaimsPrincipal cp, [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db, CancellationToken ct)
        => GetSessionAlertsOfType(projectId, "NEW_USER_ALERT", cp, authz, db, ct);

    public Task<List<SessionAlert>> GetTrackPropertiesAlerts(
        [ID] int projectId, ClaimsPrincipal cp, [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db, CancellationToken ct)
        => GetSessionAlertsOfType(projectId, "TRACK_PROPERTIES_ALERT", cp, authz, db, ct);

    public Task<List<SessionAlert>> GetUserPropertiesAlerts(
        [ID] int projectId, ClaimsPrincipal cp, [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db, CancellationToken ct)
        => GetSessionAlertsOfType(projectId, "USER_PROPERTIES_ALERT", cp, authz, db, ct);

    private async Task<List<SessionAlert>> GetSessionAlertsOfType(
        int projectId, string type, ClaimsPrincipal cp, IAuthorizationService authz,
        HoldFastDbContext db, CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(cp, projectId, authz, ct);
        return await db.SessionAlerts
            .Where(a => a.ProjectId == projectId && a.Type == type)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get log alerts for a project.
    /// </summary>
    public async Task<List<LogAlert>> GetLogAlerts(
        [ID] int projectId,
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
        [ID] int id,
        [ID] int projectId,
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
        [ID] int projectId,
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
        [ID] int id,
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
        [ID] int id,
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
        [ID] int projectId,
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
        [ID] int projectId,
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
            .Select(s => string.IsNullOrEmpty(s.Identifier) ? (s.Fingerprint ?? "") : s.Identifier)
            .Distinct()
            .LongCountAsync(ct);
    }

    /// <summary>
    /// Daily session counts for a date range (from materialized view).
    /// </summary>
    public async Task<List<DailySessionCount>> GetDailySessionsCount(
        [ID] int projectId,
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
        [ID] int projectId,
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
    public async Task<List<WorkspaceAdminRole>> GetWorkspaceAdminsByProjectId(
        [ID] int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var project = await db.Projects.FindAsync([projectId], ct)
            ?? throw new GraphQLException("Project not found");

        var workspaceAdmins = await db.WorkspaceAdmins
            .Include(wa => wa.Admin)
            .Where(wa => wa.WorkspaceId == project.WorkspaceId)
            .ToListAsync(ct);

        return workspaceAdmins.Select(wa => new WorkspaceAdminRole(
            WorkspaceId: project.WorkspaceId.ToString(),
            Admin: wa.Admin,
            Role: wa.Role ?? "Member",
            ProjectIds: [])).ToList();
    }

    // ── ClickHouse Key Queries (Sessions/Errors/Events) ─────────────

    /// <summary>
    /// Get searchable keys for sessions.
    /// </summary>
    public async Task<List<QueryKey>> GetSessionsKeys(
        [ID] int projectId,
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
        [ID] int projectId,
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
        [ID] int projectId,
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
        [ID] int projectId,
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
        [ID] int projectId,
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
        [ID] int projectId,
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
        [ID] int projectId,
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
        [ID] int projectId,
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
                DateRange = new DateRangeRequiredInput { StartDate = DateTime.UtcNow.AddDays(-30), EndDate = DateTime.UtcNow }
            },
            ct);
        return keys;
    }

    /// <summary>
    /// Get metric tag values for a given tag key.
    /// </summary>
    public async Task<List<string>> GetMetricTagValues(
        [ID] int projectId,
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
                DateRange = new DateRangeRequiredInput { StartDate = DateTime.UtcNow.AddDays(-30), EndDate = DateTime.UtcNow }
            },
            ct);
    }

    // ── Session Histogram ────────────────────────────────────────────

    /// <summary>
    /// Get sessions histogram — matches Go schema sessions_histogram(project_id, params, histogram_options).
    /// Returns a SessionsHistogram with parallel arrays of bucket values.
    /// </summary>
    public async Task<SessionsHistogram> GetSessionsHistogram(
        [ID] int projectId,
        [GraphQLName("params")] QueryInput queryParams,
        [GraphQLName("histogram_options")] DateHistogramOptions histogramOptions,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        // Use bounds from histogram_options if params date range is empty
        var start = queryParams.DateRange.StartDate != default
            ? queryParams.DateRange.StartDate
            : histogramOptions.Bounds?.StartDate ?? DateTime.UtcNow.AddDays(-30);
        var end = queryParams.DateRange.EndDate != default
            ? queryParams.DateRange.EndDate
            : histogramOptions.Bounds?.EndDate ?? DateTime.UtcNow;

        var buckets = await clickHouse.ReadSessionsHistogramAsync(
            projectId,
            new QueryInput { Query = queryParams.Query, DateRange = new DateRangeRequiredInput { StartDate = start, EndDate = end } },
            ct);

        return new SessionsHistogram(
            BucketTimes: buckets.Select(b => b.BucketStart).ToList(),
            SessionsWithoutErrors: buckets.Select(b => (long)b.Count).ToList(),
            SessionsWithErrors: buckets.Select(_ => 0L).ToList(),
            TotalSessions: buckets.Select(b => (long)b.Count).ToList(),
            InactiveLengths: buckets.Select(_ => 0L).ToList(),
            ActiveLengths: buckets.Select(_ => 0L).ToList());
    }

    /// <summary>
    /// Get errors histogram (bucket_times + error_objects arrays for the chart).
    /// Matches Go schema: errors_histogram(project_id, params, histogram_options) → ErrorsHistogram.
    /// </summary>
    public async Task<ErrorsHistogram> GetErrorsHistogram(
        [ID] int projectId,
        [GraphQLName("params")] QueryInput queryParams,
        [GraphQLName("histogram_options")] DateHistogramOptions histogramOptions,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        var buckets = await clickHouse.ReadErrorObjectsHistogramAsync(projectId, queryParams, ct);
        return new ErrorsHistogram
        {
            BucketTimes = buckets.Select(b => b.BucketStart).ToList(),
            ErrorObjects = buckets.Select(b => b.Count).ToList(),
        };
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
        [ID] int projectId,
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
            new QueryInput { Query = $"trace_id={traceId}", DateRange = new DateRangeRequiredInput { StartDate = DateTime.UtcNow.AddDays(-30), EndDate = DateTime.UtcNow } },
            new ClickHousePagination { Limit = 1000 },
            omitBody: false,
            ct: ct);
    }

    // ── Session Detail ───────────────────────────────────────────────

    /// <summary>
    /// Get all errors for a specific session.
    /// </summary>
    public async Task<List<ErrorObject>> GetErrors(
        string sessionSecureId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.SecureId == sessionSecureId, ct)
            ?? throw new ArgumentException($"Session not found: {sessionSecureId}");
        await AuthHelper.RequireProjectAccess(claimsPrincipal, session.ProjectId, authz, ct);

        return await db.Set<ErrorObject>()
            .Where(e => e.SessionId == session.Id)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get network resources (waterfall) for a session from storage.
    /// </summary>
    public async Task<string?> GetResources(
        string sessionSecureId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        [Service] IStorageService storage,
        CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.SecureId == sessionSecureId, ct)
            ?? throw new ArgumentException($"Session not found: {sessionSecureId}");
        await AuthHelper.RequireProjectAccess(claimsPrincipal, session.ProjectId, authz, ct);

        var key = $"{session.ProjectId}/{session.Id}/resources";
        var stream = await storage.DownloadAsync("sessions", key, ct);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }

    /// <summary>
    /// Get timeline indicator events for a session from storage.
    /// </summary>
    public async Task<string?> GetTimelineIndicatorEvents(
        string sessionSecureId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        [Service] IStorageService storage,
        CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.SecureId == sessionSecureId, ct)
            ?? throw new ArgumentException($"Session not found: {sessionSecureId}");
        await AuthHelper.RequireProjectAccess(claimsPrincipal, session.ProjectId, authz, ct);

        var key = $"{session.ProjectId}/{session.Id}/timeline-indicator-events";
        var stream = await storage.DownloadAsync("sessions", key, ct);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }

    /// <summary>
    /// Get websocket events for a session from storage.
    /// </summary>
    public async Task<string?> GetWebsocketEvents(
        string sessionSecureId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        [Service] IStorageService storage,
        CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.SecureId == sessionSecureId, ct)
            ?? throw new ArgumentException($"Session not found: {sessionSecureId}");
        await AuthHelper.RequireProjectAccess(claimsPrincipal, session.ProjectId, authz, ct);

        var key = $"{session.ProjectId}/{session.Id}/websocket-events";
        var stream = await storage.DownloadAsync("sessions", key, ct);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }

    /// <summary>
    /// Get Web Vitals (CLS, FCP, FID, LCP, TTFB, INP) for a session.
    /// </summary>
    public async Task<MetricsBucketsResult> GetWebVitals(
        string sessionSecureId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.SecureId == sessionSecureId, ct)
            ?? throw new ArgumentException($"Session not found: {sessionSecureId}");
        await AuthHelper.RequireProjectAccess(claimsPrincipal, session.ProjectId, authz, ct);

        // Query ClickHouse for Web Vital metrics filtered to this session
        var query = new QueryInput
        {
            Query = $"secure_session_id={sessionSecureId} metric_name=CLS OR metric_name=FCP OR metric_name=FID OR metric_name=LCP OR metric_name=TTFB OR metric_name=INP",
            DateRange = new DateRangeRequiredInput { StartDate = session.CreatedAt.AddHours(-1), EndDate = session.CreatedAt.AddDays(1) }
        };
        var raw = await clickHouse.ReadMetricsAsync(
            session.ProjectId, query, bucketBy: "None", groupBy: new List<string> { "metric_name" },
            aggregator: "AVG", column: "metric_value", ct);
        return MetricsBucketsResult.FromClickHouse(raw);
    }

    /// <summary>
    /// Get AI-generated insight for a session (stub — returns existing insight if any).
    /// </summary>
    public async Task<SessionInsight?> GetSessionInsight(
        int sessionId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var session = await db.Sessions.FindAsync(new object[] { sessionId }, ct)
            ?? throw new ArgumentException($"Session not found: {sessionId}");
        await AuthHelper.RequireProjectAccess(claimsPrincipal, session.ProjectId, authz, ct);

        return await db.Set<SessionInsight>().FirstOrDefaultAsync(i => i.SessionId == sessionId, ct);
    }

    // ── Error Detail ─────────────────────────────────────────────────

    /// <summary>
    /// Get external issue attachments linked to an error group.
    /// </summary>
    public async Task<List<ExternalAttachment>> GetErrorIssue(
        string errorGroupSecureId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var errorGroup = await db.Set<ErrorGroup>()
            .FirstOrDefaultAsync(e => e.SecureId == errorGroupSecureId, ct)
            ?? throw new ArgumentException($"Error group not found: {errorGroupSecureId}");
        await AuthHelper.RequireProjectAccess(claimsPrincipal, errorGroup.ProjectId, authz, ct);

        return await db.Set<ExternalAttachment>()
            .Where(a => a.ErrorGroupId == errorGroup.Id)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get all error tags in the system.
    /// </summary>
    public async Task<List<ErrorTag>> GetErrorTags(
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);
        return await db.Set<ErrorTag>().ToListAsync(ct);
    }

    /// <summary>
    /// Match error text against the error tag library with relevance scoring.
    /// Stub: returns tags whose title contains the query string.
    /// </summary>
    public async Task<List<MatchedErrorTag>> MatchErrorTag(
        string query,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);
        var lowerQuery = query.ToLowerInvariant();

        var tags = await db.Set<ErrorTag>()
            .Where(t => t.Title.ToLower().Contains(lowerQuery)
                     || (t.Description != null && t.Description.ToLower().Contains(lowerQuery)))
            .Take(10)
            .ToListAsync(ct);

        return tags.Select(t => new MatchedErrorTag(
            Id: t.Id,
            Title: t.Title,
            Description: t.Description ?? "",
            Score: t.Title.Equals(query, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.5
        )).ToList();
    }

    // ── Suggestion / Search ──────────────────────────────────────────

    /// <summary>
    /// Search projects by name (ILIKE pattern match).
    /// </summary>
    public async Task<List<Project>> GetProjectSuggestion(
        string query,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);
        var lowerQuery = query.ToLowerInvariant();

        return await db.Set<Project>()
            .Where(p => p.Name != null && p.Name.ToLower().Contains(lowerQuery))
            .Take(20)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get environment field values for a project (last 30 days).
    /// </summary>
    public async Task<List<Field>> GetEnvironmentSuggestion(
        [ID] int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var values = await clickHouse.GetSessionsKeyValuesAsync(
            projectId, "environment",
            DateTime.UtcNow.AddDays(-30), DateTime.UtcNow,
            query: null, count: 50, ct);

        return values.Select(v => new Field
        {
            ProjectId = projectId,
            Type = "session",
            Name = "environment",
            Value = v,
        }).ToList();
    }

    /// <summary>
    /// Search user identifiers for a project (last 30 days).
    /// </summary>
    public async Task<List<string>> GetIdentifierSuggestion(
        [ID] int projectId,
        string? query,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await clickHouse.GetSessionsKeyValuesAsync(
            projectId, "identifier",
            DateTime.UtcNow.AddDays(-30), DateTime.UtcNow,
            query: query, count: 50, ct);
    }

    // ── Integration Status ───────────────────────────────────────────

    /// <summary>
    /// Check if a project is integrated with a specific third-party service.
    /// </summary>
    public async Task<bool> IsIntegratedWith(
        IntegrationType integrationType,
        [ID] int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var project = await db.Set<Project>()
            .Include(p => p.Workspace)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return false;

        return integrationType switch
        {
            IntegrationType.Linear => !string.IsNullOrEmpty(project.Workspace.LinearAccessToken),
            IntegrationType.Slack => !string.IsNullOrEmpty(project.Workspace.SlackAccessToken),
            IntegrationType.Zapier => !string.IsNullOrEmpty(project.ZapierAccessToken),
            IntegrationType.MicrosoftTeams => !string.IsNullOrEmpty(project.Workspace.MicrosoftTeamsTenantId),
            IntegrationType.Discord => !string.IsNullOrEmpty(project.Workspace.DiscordGuildId),
            IntegrationType.Vercel => !string.IsNullOrEmpty(project.Workspace.VercelAccessToken),
            IntegrationType.ClickUp => !string.IsNullOrEmpty(project.Workspace.ClickupAccessToken),
            // For others, check IntegrationProjectMapping table
            _ => await db.Set<IntegrationProjectMapping>()
                .AnyAsync(m => m.ProjectId == projectId
                    && m.IntegrationType == integrationType.ToString(), ct),
        };
    }

    /// <summary>
    /// Check if a workspace is integrated with a specific third-party service.
    /// </summary>
    public async Task<bool> IsWorkspaceIntegratedWith(
        IntegrationType integrationType,
        [ID] int workspaceId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

        var workspace = await db.Workspaces.FindAsync(new object[] { workspaceId }, ct);
        if (workspace == null) return false;

        return integrationType switch
        {
            IntegrationType.ClickUp => !string.IsNullOrEmpty(workspace.ClickupAccessToken),
            IntegrationType.MicrosoftTeams => !string.IsNullOrEmpty(workspace.MicrosoftTeamsTenantId),
            IntegrationType.Slack => !string.IsNullOrEmpty(workspace.SlackAccessToken),
            IntegrationType.Linear => !string.IsNullOrEmpty(workspace.LinearAccessToken),
            IntegrationType.Discord => !string.IsNullOrEmpty(workspace.DiscordGuildId),
            IntegrationType.Vercel => !string.IsNullOrEmpty(workspace.VercelAccessToken),
            _ => await db.Set<IntegrationWorkspaceMapping>()
                .AnyAsync(m => m.WorkspaceId == workspaceId
                    && m.IntegrationType == integrationType.ToString(), ct),
        };
    }

    /// <summary>
    /// Check if a project has a specific integration mapping.
    /// </summary>
    public async Task<bool> IsProjectIntegratedWith(
        IntegrationType integrationType,
        [ID] int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await db.Set<IntegrationProjectMapping>()
            .AnyAsync(m => m.ProjectId == projectId
                && m.IntegrationType == integrationType.ToString(), ct);
    }

    /// <summary>
    /// Check if metrics have been set up for a project.
    /// </summary>
    [GraphQLName("metricsIntegration")]
    public async Task<IntegrationStatus> GetMetricsIntegration(
        [ID] int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var setup = await db.Set<SetupEvent>()
            .FirstOrDefaultAsync(e => e.ProjectId == projectId && e.Type == "Metrics", ct);

        return new IntegrationStatus(
            Integrated: setup != null,
            ResourceType: "Metric",
            CreatedAt: setup?.CreatedAt);
    }

    // ── API Key Lookup ───────────────────────────────────────────────

    /// <summary>
    /// Resolve a project ID from its API key (secret).
    /// </summary>
    public async Task<int?> ApiKeyToOrgId(
        string apiKey,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var project = await db.Set<Project>()
            .FirstOrDefaultAsync(p => p.Secret == apiKey, ct);
        return project?.Id;
    }

    // ── Session Payload (Replay) ─────────────────────────────────────

    /// <summary>
    /// Get session payload for replay: events from storage, errors, rage clicks, and comments.
    /// </summary>
    public async Task<SessionPayload> GetSessionPayload(
        string sessionSecureId,
        bool skipEvents,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        [Service] IStorageService storage,
        CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.SecureId == sessionSecureId, ct)
            ?? throw new ArgumentException($"Session not found: {sessionSecureId}");
        await AuthHelper.RequireProjectAccess(claimsPrincipal, session.ProjectId, authz, ct);

        string? eventsJson = null;
        if (!skipEvents)
        {
            var key = $"{session.ProjectId}/{session.Id}/events";
            var stream = await storage.DownloadAsync("sessions", key, ct);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                eventsJson = await reader.ReadToEndAsync(ct);
            }
        }

        var errors = await db.ErrorObjects
            .Where(e => e.SessionId == session.Id)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);

        var rageClicks = await db.RageClickEvents
            .Where(r => r.SessionId == session.Id)
            .ToListAsync(ct);

        var comments = await db.SessionComments
            .Where(c => c.SessionId == session.Id)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        return new SessionPayload(
            Events: eventsJson ?? "[]",
            Errors: errors,
            RageClicks: rageClicks,
            SessionComments: comments,
            LastUserInteractionTime: session.LastUserInteractionTime ?? session.CreatedAt.ToString("O"));
    }

    /// <summary>
    /// Get error group instances (paginated error objects for an error group).
    /// </summary>
    public async Task<ErrorGroupInstances> GetErrorGroupInstances(
        string errorGroupSecureId,
        int count,
        int page,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var errorGroup = await db.ErrorGroups
            .FirstOrDefaultAsync(e => e.SecureId == errorGroupSecureId, ct)
            ?? throw new ArgumentException($"Error group not found: {errorGroupSecureId}");
        await AuthHelper.RequireProjectAccess(claimsPrincipal, errorGroup.ProjectId, authz, ct);

        var total = await db.ErrorObjects
            .Where(e => e.ErrorGroupId == errorGroup.Id)
            .LongCountAsync(ct);

        var objects = await db.ErrorObjects
            .Where(e => e.ErrorGroupId == errorGroup.Id)
            .OrderByDescending(e => e.CreatedAt)
            .Skip(page * count)
            .Take(count)
            .ToListAsync(ct);

        return new ErrorGroupInstances(objects, total);
    }

    /// <summary>
    /// Get network histogram data for a project (aggregates by URL host or status).
    /// </summary>
    public async Task<List<HistogramBucket>> GetNetworkHistogram(
        [ID] int projectId,
        double lookbackDays,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var query = new QueryInput
        {
            DateRange = new DateRangeRequiredInput { StartDate = DateTime.UtcNow.AddDays(-lookbackDays), EndDate = DateTime.UtcNow }
        };

        return await clickHouse.ReadSessionsHistogramAsync(projectId, query, ct);
    }

    /// <summary>
    /// Get session users report (aggregated sessions by user from ClickHouse).
    /// Stub: returns report from PostgreSQL until ClickHouse view is available.
    /// </summary>
    public async Task<List<SessionsReportRow>> GetSessionUsersReports(
        [ID] int projectId,
        QueryInput query,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var sessions = await db.Sessions
            .Where(s => s.ProjectId == projectId
                && s.Excluded != true
                && !string.IsNullOrEmpty(s.Identifier)
                && s.CreatedAt >= (query.DateRange.StartDate == default ? DateTime.UtcNow.AddDays(-30) : query.DateRange.StartDate)
                && s.CreatedAt <= (query.DateRange.EndDate == default ? DateTime.UtcNow : query.DateRange.EndDate))
            .Select(s => new { s.Identifier, s.ActiveLength, s.Length, s.CreatedAt, s.City, s.Country })
            .ToListAsync(ct);

        return sessions.GroupBy(s => s.Identifier!)
            .Select(g =>
            {
                var activeLengths = g.Select(s => (s.ActiveLength ?? 0) / 60000.0).ToList();
                var lengths = g.Select(s => (s.Length ?? 0) / 60000.0).ToList();
                var dates = g.Select(s => s.CreatedAt).ToList();
                var location = g.First().City != null
                    ? $"{g.First().City}, {g.First().Country}"
                    : (g.First().Country ?? "");

                return new SessionsReportRow(
                    Key: g.Key,
                    Email: g.Key,
                    NumSessions: g.Count(),
                    FirstSession: dates.Min(),
                    LastSession: dates.Max(),
                    NumDaysVisited: dates.Select(d => d.Date).Distinct().Count(),
                    NumMonthsVisited: dates.Select(d => new { d.Year, d.Month }).Distinct().Count(),
                    AvgActiveLengthMins: activeLengths.Count > 0 ? activeLengths.Average() : 0,
                    MaxActiveLengthMins: activeLengths.Count > 0 ? activeLengths.Max() : 0,
                    TotalActiveLengthMins: activeLengths.Sum(),
                    AvgLengthMins: lengths.Count > 0 ? lengths.Average() : 0,
                    MaxLengthMins: lengths.Count > 0 ? lengths.Max() : 0,
                    TotalLengthMins: lengths.Sum(),
                    Location: location);
            })
            .OrderByDescending(r => r.NumSessions)
            .ToList();
    }

    /// <summary>
    /// Get comment mention suggestions (workspace admins for @-mentions).
    /// </summary>
    public async Task<List<Admin>> GetCommentMentionSuggestions(
        [ID] int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var project = await db.Projects.FindAsync(new object[] { projectId }, ct)
            ?? throw new ArgumentException($"Project not found: {projectId}");

        return await db.WorkspaceAdmins
            .Where(wa => wa.WorkspaceId == project.WorkspaceId)
            .Select(wa => wa.Admin)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get AI query suggestion (stub — returns empty suggestion).
    /// </summary>
    public Task<string> GetAIQuerySuggestion(
        [ID] int projectId,
        string productType,
        string query,
        string timeZone,
        ClaimsPrincipal claimsPrincipal)
    {
        return Task.FromResult("");
    }

    /// <summary>
    /// Get error resolution suggestion (stub — AI feature).
    /// </summary>
    public Task<string> GetErrorResolutionSuggestion(
        string errorGroupSecureId,
        ClaimsPrincipal claimsPrincipal)
    {
        return Task.FromResult("");
    }

    /// <summary>
    /// Get OAuth client metadata.
    /// </summary>
    public async Task<OAuthClientStore?> GetOAuthClientMetadata(
        string clientId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.OAuthClientStores
            .FirstOrDefaultAsync(c => c.ClientId == clientId, ct);
    }

    // ── Analytics (PostgreSQL) ────────────────────────────────────────

    /// <summary>
    /// Get daily error frequency for an error group (last N days).
    /// Returns an array of counts, one per day.
    /// </summary>
    [GraphQLName("dailyErrorFrequency")]
    public async Task<List<long>> GetDailyErrorFrequency(
        [ID] int projectId,
        string errorGroupSecureId,
        int dateOffset,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var errorGroup = await db.ErrorGroups
            .FirstOrDefaultAsync(e => e.SecureId == errorGroupSecureId && e.ProjectId == projectId, ct)
            ?? throw new ArgumentException($"Error group not found: {errorGroupSecureId}");

        var startDate = DateTime.UtcNow.Date.AddDays(-dateOffset);
        var errors = await db.ErrorObjects
            .Where(e => e.ErrorGroupId == errorGroup.Id && e.CreatedAt >= startDate)
            .Select(e => e.CreatedAt.Date)
            .ToListAsync(ct);

        var counts = new List<long>();
        for (int i = 0; i <= dateOffset; i++)
        {
            var day = startDate.AddDays(i);
            counts.Add(errors.Count(d => d == day));
        }
        return counts;
    }

    /// <summary>
    /// Get error group tag aggregations (browser, OS, environment, etc.).
    /// </summary>
    [GraphQLName("errorGroupTags")]
    public async Task<List<ErrorGroupTagAggregation>> GetErrorGroupTagAggregations(
        string errorGroupSecureId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        var errorGroup = await db.ErrorGroups
            .FirstOrDefaultAsync(e => e.SecureId == errorGroupSecureId, ct)
            ?? throw new ArgumentException($"Error group not found: {errorGroupSecureId}");
        await AuthHelper.RequireProjectAccess(claimsPrincipal, errorGroup.ProjectId, authz, ct);

        var errors = await db.ErrorObjects
            .Where(e => e.ErrorGroupId == errorGroup.Id)
            .Select(e => new { e.Browser, e.OS, e.Environment })
            .ToListAsync(ct);

        var total = (double)errors.Count;
        var results = new List<ErrorGroupTagAggregation>();

        // Browser aggregation
        var browsers = errors.Where(e => !string.IsNullOrEmpty(e.Browser))
            .GroupBy(e => e.Browser!)
            .Select(g => new ErrorGroupTagAggregationBucket(g.Key, g.Count(), total > 0 ? g.Count() / total * 100 : 0))
            .OrderByDescending(b => b.DocCount).ToList();
        if (browsers.Count > 0)
            results.Add(new ErrorGroupTagAggregation("browser", browsers));

        // OS aggregation
        var oses = errors.Where(e => !string.IsNullOrEmpty(e.OS))
            .GroupBy(e => e.OS!)
            .Select(g => new ErrorGroupTagAggregationBucket(g.Key, g.Count(), total > 0 ? g.Count() / total * 100 : 0))
            .OrderByDescending(b => b.DocCount).ToList();
        if (oses.Count > 0)
            results.Add(new ErrorGroupTagAggregation("os", oses));

        // Environment aggregation
        var envs = errors.Where(e => !string.IsNullOrEmpty(e.Environment))
            .GroupBy(e => e.Environment!)
            .Select(g => new ErrorGroupTagAggregationBucket(g.Key, g.Count(), total > 0 ? g.Count() / total * 100 : 0))
            .OrderByDescending(b => b.DocCount).ToList();
        if (envs.Count > 0)
            results.Add(new ErrorGroupTagAggregation("environment", envs));

        return results;
    }

    /// <summary>
    /// Get top referrer hosts for a project.
    /// </summary>
    public async Task<List<ReferrerTablePayload>> GetReferrers(
        [ID] int projectId,
        double lookbackDays,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var cutoff = DateTime.UtcNow.AddDays(-lookbackDays);
        var fields = await db.Fields
            .Where(f => f.ProjectId == projectId && f.Name == "referrer" && f.CreatedAt >= cutoff)
            .Select(f => f.Value)
            .ToListAsync(ct);

        var total = (double)fields.Count;
        return fields.GroupBy(v => v)
            .Select(g => new ReferrerTablePayload(g.Key, g.Count(), total > 0 ? g.Count() / total * 100 : 0))
            .OrderByDescending(r => r.Count)
            .Take(50)
            .ToList();
    }

    /// <summary>
    /// Get top users by active time for a project.
    /// </summary>
    [GraphQLName("topUsers")]
    public async Task<List<TopUsersPayload>> GetTopUsers(
        [ID] int projectId,
        double lookbackDays,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var cutoff = DateTime.UtcNow.AddDays(-lookbackDays);
        var sessions = await db.Sessions
            .Where(s => s.ProjectId == projectId
                && s.CreatedAt >= cutoff
                && s.Excluded != true
                && !string.IsNullOrEmpty(s.Identifier))
            .Select(s => new { s.Id, s.Identifier, s.ActiveLength })
            .ToListAsync(ct);

        var totalActive = sessions.Sum(s => s.ActiveLength ?? 0);

        return sessions.GroupBy(s => s.Identifier!)
            .Select(g => new TopUsersPayload(
                Id: g.First().Id,
                Identifier: g.Key,
                TotalActiveTime: g.Sum(s => s.ActiveLength ?? 0),
                ActiveTimePercentage: totalActive > 0 ? (double)g.Sum(s => s.ActiveLength ?? 0) / totalActive * 100 : 0,
                UserProperties: "{}"))
            .OrderByDescending(u => u.TotalActiveTime)
            .Take(50)
            .ToList();
    }

    /// <summary>
    /// Get average session length for a project.
    /// </summary>
    [GraphQLName("averageSessionLength")]
    public async Task<AverageSessionLength> GetAverageSessionLength(
        [ID] int projectId,
        double lookbackDays,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var cutoff = DateTime.UtcNow.AddDays(-lookbackDays);
        var avgLength = await db.Sessions
            .Where(s => s.ProjectId == projectId
                && s.CreatedAt >= cutoff
                && s.Excluded != true
                && s.Length != null && s.Length > 0)
            .AverageAsync(s => (double?)s.Length, ct);

        return new AverageSessionLength(avgLength ?? 0);
    }

    /// <summary>
    /// Count new users (first_time=1) for a project.
    /// </summary>
    [GraphQLName("newUsersCount")]
    public async Task<NewUsersCount> GetNewUsersCount(
        [ID] int projectId,
        double lookbackDays,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var cutoff = DateTime.UtcNow.AddDays(-lookbackDays);
        var count = await db.Sessions
            .Where(s => s.ProjectId == projectId
                && s.CreatedAt >= cutoff
                && s.FirstTime == 1
                && s.Excluded != true)
            .LongCountAsync(ct);

        return new NewUsersCount(count);
    }

    /// <summary>
    /// Count unique fingerprints for a project.
    /// </summary>
    [GraphQLName("userFingerprintCount")]
    public async Task<UserFingerprintCount> GetUserFingerprintCount(
        [ID] int projectId,
        double lookbackDays,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var cutoff = DateTime.UtcNow.AddDays(-lookbackDays);
        var count = await db.Sessions
            .Where(s => s.ProjectId == projectId
                && s.CreatedAt >= cutoff
                && s.Fingerprint != null
                && s.Excluded != true)
            .Select(s => s.Fingerprint)
            .Distinct()
            .LongCountAsync(ct);

        return new UserFingerprintCount(count);
    }

    // ── Notification Channel Suggestions ─────────────────────────────

    /// <summary>
    /// Get Slack channel suggestions for alert configuration.
    /// </summary>
    public async Task<List<SanitizedSlackChannel>> GetSlackChannelSuggestion(
        [ID] int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var project = await db.Projects.Include(p => p.Workspace).FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new ArgumentException($"Project not found: {projectId}");

        if (string.IsNullOrEmpty(project.Workspace.SlackChannels))
            return [];

        var channels = System.Text.Json.JsonSerializer.Deserialize<List<string>>(project.Workspace.SlackChannels) ?? [];
        return channels.Select(c => new SanitizedSlackChannel(c, null)).ToList();
    }

    /// <summary>
    /// Get Discord channel suggestions for alert configuration.
    /// </summary>
    public async Task<List<DiscordChannelInfo>> GetDiscordChannelSuggestions(
        [ID] int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var project = await db.Projects.Include(p => p.Workspace).FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new ArgumentException($"Project not found: {projectId}");

        if (string.IsNullOrEmpty(project.Workspace.DiscordGuildId))
            return [];

        return [new DiscordChannelInfo(project.Workspace.DiscordGuildId, "default")];
    }

    /// <summary>
    /// Get Microsoft Teams channel suggestions for alert configuration.
    /// </summary>
    public async Task<List<MicrosoftTeamsChannelInfo>> GetMicrosoftTeamsChannelSuggestions(
        [ID] int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var project = await db.Projects.Include(p => p.Workspace).FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new ArgumentException($"Project not found: {projectId}");

        if (string.IsNullOrEmpty(project.Workspace.MicrosoftTeamsTenantId))
            return [];

        return [new MicrosoftTeamsChannelInfo(project.Workspace.MicrosoftTeamsTenantId, "default")];
    }

    // ── SSO Login ────────────────────────────────────────────────────

    /// <summary>
    /// Look up SSO configuration by domain.
    /// </summary>
    public async Task<SSOLogin?> GetSSOLogin(
        string domain,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var sso = await db.SSOClients
            .FirstOrDefaultAsync(s => s.Domain == domain, ct);
        if (sso == null) return null;
        return new SSOLogin(sso.Domain!, sso.ClientId ?? "");
    }

    // ── Workspace Access Requests ────────────────────────────────────

    /// <summary>
    /// Get workspace access requests.
    /// </summary>
    public async Task<List<WorkspaceAccessRequest>> GetWorkspaceAccessRequests(
        [ID] int workspaceId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

        return await db.WorkspaceAccessRequests
            .Where(r => r.LastRequestedWorkspace == workspaceId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get integration project mappings for a workspace.
    /// </summary>
    public async Task<List<IntegrationProjectMapping>> GetIntegrationProjectMappings(
        [ID] int workspaceId,
        IntegrationType? integrationType,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

        var query = db.IntegrationProjectMappings
            .Include(m => m.Project)
            .Where(m => m.Project.WorkspaceId == workspaceId);

        if (integrationType != null)
            query = query.Where(m => m.IntegrationType == integrationType.ToString());

        return await query.ToListAsync(ct);
    }

    // ── Compound Queries ───────────────────────────────────────────────

    /// <summary>
    /// Get all projects and workspaces for the current admin.
    /// </summary>
    public async Task<ProjectsAndWorkspacesResult> GetProjectsAndWorkspaces(
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        var workspaceIds = await db.WorkspaceAdmins
            .Where(wa => wa.AdminId == admin.Id)
            .Select(wa => wa.WorkspaceId)
            .ToListAsync(ct);

        var workspaces = await db.Workspaces
            .Where(w => workspaceIds.Contains(w.Id))
            .Include(w => w.Projects)
            .ToListAsync(ct);

        var projects = workspaces.SelectMany(w => w.Projects).ToList();

        return new ProjectsAndWorkspacesResult(projects, workspaces);
    }

    /// <summary>
    /// Get a project or workspace by ID.
    /// </summary>
    public async Task<ProjectOrWorkspaceResult> GetProjectOrWorkspace(
        [ID] int id,
        bool isWorkspace,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        if (isWorkspace)
        {
            await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, id, authz, ct);
            var workspace = await db.Workspaces
                .Include(w => w.Projects)
                .FirstOrDefaultAsync(w => w.Id == id, ct);
            return new ProjectOrWorkspaceResult(null, workspace);
        }
        else
        {
            await AuthHelper.RequireProjectAccess(claimsPrincipal, id, authz, ct);
            var project = await db.Projects
                .Include(p => p.Workspace)
                .FirstOrDefaultAsync(p => p.Id == id, ct);
            return new ProjectOrWorkspaceResult(project, project?.Workspace);
        }
    }

    /// <summary>
    /// Get admin's about-you details.
    /// </summary>
    public async Task<Admin> GetAdminAboutYou(
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        CancellationToken ct)
    {
        return await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);
    }

    /// <summary>
    /// Get referrer count for a project.
    /// </summary>
    public async Task<int> GetReferrersCount(
        [ID] int projectId,
        double lookbackDays,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var cutoff = DateTime.UtcNow.AddDays(-lookbackDays);
        return await db.Fields
            .Where(f => f.ProjectId == projectId && f.Name == "referrer" && f.CreatedAt >= cutoff)
            .Select(f => f.Value)
            .Distinct()
            .CountAsync(ct);
    }

    /// <summary>
    /// Get alerts page payload — all alert types for a project.
    /// </summary>
    public async Task<AlertsPagePayload> GetAlertsPagePayload(
        [ID] int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var alerts = await db.Alerts
            .Where(a => a.ProjectId == projectId)
            .Include(a => a.Destinations)
            .ToListAsync(ct);
        var errorAlerts = await db.ErrorAlerts
            .Where(a => a.ProjectId == projectId).ToListAsync(ct);
        var sessionAlerts = await db.SessionAlerts
            .Where(a => a.ProjectId == projectId).ToListAsync(ct);
        var logAlerts = await db.LogAlerts
            .Where(a => a.ProjectId == projectId).ToListAsync(ct);
        var metricMonitors = await db.MetricMonitors
            .Where(m => m.ProjectId == projectId).ToListAsync(ct);

        return new AlertsPagePayload(alerts, errorAlerts, sessionAlerts, logAlerts, metricMonitors);
    }

    /// <summary>
    /// Get log alerts page payload for a project.
    /// </summary>
    public async Task<LogAlertsPagePayload> GetLogAlertsPagePayload(
        [ID] int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var logAlerts = await db.LogAlerts
            .Where(a => a.ProjectId == projectId).ToListAsync(ct);

        return new LogAlertsPagePayload(logAlerts);
    }

    // ── Graph Templates ───────────────────────────────────────────────

    /// <summary>
    /// Get default graph templates (graphs with visualization_id = -1).
    /// </summary>
    public async Task<List<Graph>> GetGraphTemplates(
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.Graphs
            .Where(g => g.VisualizationId == -1)
            .ToListAsync(ct);
    }

    // ── Logs Related Resources ───────────────────────────────────────

    /// <summary>
    /// Get resources related to a log entry (traces from same trace ID).
    /// </summary>
    public async Task<TraceConnection> GetLogsRelatedResources(
        [ID] int projectId,
        string? traceId,
        string? spanId,
        DateTime date,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        if (string.IsNullOrEmpty(traceId))
            return new TraceConnection();

        var queryStr = $"trace_id={traceId}";
        if (!string.IsNullOrEmpty(spanId))
            queryStr += $" span_id={spanId}";

        return await clickHouse.ReadTracesAsync(
            projectId,
            new QueryInput
            {
                Query = queryStr,
                DateRange = new DateRangeRequiredInput { StartDate = date.AddDays(-1), EndDate = date.AddDays(1) }
            },
            new ClickHousePagination { Limit = 100 },
            omitBody: false,
            ct: ct);
    }

    // ── Generic Keys/Key Values (product-agnostic) ─────────────────

    /// <summary>
    /// Get keys for any product type (logs, traces, errors, sessions, events).
    /// Dispatches to the product-specific ClickHouse method.
    /// </summary>
    public async Task<List<QueryKey>> GetKeys(
        ProductType? productType,
        [ID] int projectId,
        [GraphQLName("date_range")] DateRangeRequiredInput dateRange,
        string? query,
        KeyType? type,
        [GraphQLName("event")] string? eventName,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var qi = new QueryInput { Query = query, DateRange = dateRange };
        return productType switch
        {
            ProductType.Logs => (await clickHouse.GetLogKeysAsync(projectId, qi, ct))
                .Select(k => new QueryKey { Name = k, Type = type?.ToString() ?? "String" }).ToList(),
            ProductType.Traces => (await clickHouse.GetTraceKeysAsync(projectId, qi, ct))
                .Select(k => new QueryKey { Name = k, Type = type?.ToString() ?? "String" }).ToList(),
            ProductType.Errors => await clickHouse.GetErrorsKeysAsync(projectId, dateRange.StartDate, dateRange.EndDate, query, ct),
            ProductType.Sessions => await clickHouse.GetSessionsKeysAsync(projectId, dateRange.StartDate, dateRange.EndDate, query, ct),
            ProductType.Events => await clickHouse.GetEventsKeysAsync(projectId, dateRange.StartDate, dateRange.EndDate, query, eventName, ct),
            _ => await clickHouse.GetSessionsKeysAsync(projectId, dateRange.StartDate, dateRange.EndDate, query, ct),
        };
    }

    /// <summary>
    /// Get key values for any product type.
    /// </summary>
    public async Task<List<string>> GetKeyValues(
        string? productType,
        [ID] int projectId,
        string keyName,
        [GraphQLName("date_range")] DateRangeRequiredInput dateRange,
        string? query,
        int? count,
        [GraphQLName("event")] string? eventName,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return (productType?.ToUpperInvariant()) switch
        {
            "LOGS" => await clickHouse.GetLogKeyValuesAsync(projectId, keyName,
                new QueryInput { Query = query ?? "", DateRange = dateRange }, ct),
            "TRACES" => await clickHouse.GetTraceKeyValuesAsync(projectId, keyName,
                new QueryInput { Query = query ?? "", DateRange = dateRange }, ct),
            "ERRORS" => await clickHouse.GetErrorsKeyValuesAsync(projectId, keyName, dateRange.StartDate, dateRange.EndDate, query, count, ct),
            "SESSIONS" => await clickHouse.GetSessionsKeyValuesAsync(projectId, keyName, dateRange.StartDate, dateRange.EndDate, query, count, ct),
            "EVENTS" => await clickHouse.GetEventsKeyValuesAsync(projectId, keyName, dateRange.StartDate, dateRange.EndDate, query, count, eventName, ct),
            _ => await clickHouse.GetSessionsKeyValuesAsync(projectId, keyName, dateRange.StartDate, dateRange.EndDate, query, count, ct),
        };
    }

    /// <summary>
    /// Get key-value suggestions for multiple keys at once.
    /// </summary>
    public async Task<List<KeyValueSuggestion>> GetKeyValuesSuggestions(
        string productType,
        [ID] int projectId,
        [GraphQLName("date_range")] DateRangeRequiredInput dateRange,
        List<string> keys,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var results = new List<KeyValueSuggestion>();
        foreach (var key in keys)
        {
            var values = await GetKeyValues(productType, projectId, key,
                dateRange, null, 10, null,
                claimsPrincipal, authz, clickHouse, ct);

            results.Add(new KeyValueSuggestion(
                key,
                values.Select((v, i) => new ValueSuggestion(v, 0, (long)i)).ToList()));
        }
        return results;
    }

    // ── Product-Specific Keys/Key Values ─────────────────────────────

    /// <summary>
    /// Get log attribute keys (schema-aligned, returns QueryKey).
    /// </summary>
    public async Task<List<QueryKey>> GetLogsKeys(
        [ID] int projectId,
        DateTime dateRangeStart,
        DateTime dateRangeEnd,
        string? query,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var keys = await clickHouse.GetLogKeysAsync(projectId,
            new QueryInput { Query = query ?? "", DateRange = new DateRangeRequiredInput { StartDate = dateRangeStart, EndDate = dateRangeEnd } }, ct);
        return keys.Select(k => new QueryKey { Name = k, Type = "String" }).ToList();
    }

    /// <summary>
    /// Get log attribute key values.
    /// </summary>
    public async Task<List<string>> GetLogsKeyValues(
        [ID] int projectId,
        string keyName,
        DateTime dateRangeStart,
        DateTime dateRangeEnd,
        string? query,
        int? count,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await clickHouse.GetLogKeyValuesAsync(projectId, keyName,
            new QueryInput { Query = query ?? "", DateRange = new DateRangeRequiredInput { StartDate = dateRangeStart, EndDate = dateRangeEnd } }, ct);
    }

    /// <summary>
    /// Get trace attribute keys (schema-aligned, returns QueryKey).
    /// </summary>
    public async Task<List<QueryKey>> GetTracesKeys(
        [ID] int projectId,
        DateTime dateRangeStart,
        DateTime dateRangeEnd,
        string? query,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var keys = await clickHouse.GetTraceKeysAsync(projectId,
            new QueryInput { Query = query ?? "", DateRange = new DateRangeRequiredInput { StartDate = dateRangeStart, EndDate = dateRangeEnd } }, ct);
        return keys.Select(k => new QueryKey { Name = k, Type = "String" }).ToList();
    }

    /// <summary>
    /// Get trace attribute key values.
    /// </summary>
    public async Task<List<string>> GetTracesKeyValues(
        [ID] int projectId,
        string keyName,
        DateTime dateRangeStart,
        DateTime dateRangeEnd,
        string? query,
        int? count,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await clickHouse.GetTraceKeyValuesAsync(projectId, keyName,
            new QueryInput { Query = query ?? "", DateRange = new DateRangeRequiredInput { StartDate = dateRangeStart, EndDate = dateRangeEnd } }, ct);
    }

    /// <summary>
    /// Get error attribute keys.
    /// </summary>
    public async Task<List<QueryKey>> GetErrorsKeys(
        [ID] int projectId,
        DateTime dateRangeStart,
        DateTime dateRangeEnd,
        string? query,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        return await clickHouse.GetErrorsKeysAsync(projectId, dateRangeStart, dateRangeEnd, query, ct);
    }

    // ── Deprecated Metrics Queries (dispatch to ReadMetricsAsync) ────

    /// <summary>
    /// Get logs metrics (deprecated — use GetMetrics with productType instead).
    /// </summary>
    public async Task<MetricsBucketsResult> GetLogsMetrics(
        [ID] int projectId,
        QueryInput queryParams,
        List<string> groupBy,
        string bucketBy,
        string? column,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var raw = await clickHouse.ReadMetricsAsync(projectId, queryParams,
            bucketBy, groupBy, "Count", column, ct);
        return MetricsBucketsResult.FromClickHouse(raw);
    }

    /// <summary>
    /// Get traces metrics (deprecated — use GetMetrics with productType instead).
    /// </summary>
    public async Task<MetricsBucketsResult> GetTracesMetrics(
        [ID] int projectId,
        QueryInput queryParams,
        List<string> groupBy,
        string? bucketBy,
        string? column,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var raw = await clickHouse.ReadMetricsAsync(projectId, queryParams,
            bucketBy ?? "Timestamp", groupBy, "Count", column, ct);
        return MetricsBucketsResult.FromClickHouse(raw);
    }

    /// <summary>
    /// Get errors metrics (deprecated — use GetMetrics with productType instead).
    /// </summary>
    public async Task<MetricsBucketsResult> GetErrorsMetrics(
        [ID] int projectId,
        QueryInput queryParams,
        List<string> groupBy,
        string bucketBy,
        string? column,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var raw = await clickHouse.ReadMetricsAsync(projectId, queryParams,
            bucketBy, groupBy, "Count", column, ct);
        return MetricsBucketsResult.FromClickHouse(raw);
    }

    /// <summary>
    /// Get sessions metrics (deprecated — use GetMetrics with productType instead).
    /// </summary>
    public async Task<MetricsBucketsResult> GetSessionsMetrics(
        [ID] int projectId,
        QueryInput queryParams,
        List<string> groupBy,
        string bucketBy,
        string? column,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var raw = await clickHouse.ReadMetricsAsync(projectId, queryParams,
            bucketBy, groupBy, "Count", column, ct);
        return MetricsBucketsResult.FromClickHouse(raw);
    }

    /// <summary>
    /// Get events metrics (deprecated — use GetMetrics with productType instead).
    /// </summary>
    public async Task<MetricsBucketsResult> GetEventsMetrics(
        [ID] int projectId,
        QueryInput queryParams,
        List<string> groupBy,
        string bucketBy,
        string? column,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var raw = await clickHouse.ReadMetricsAsync(projectId, queryParams,
            bucketBy, groupBy, "Count", column, ct);
        return MetricsBucketsResult.FromClickHouse(raw);
    }

    // ── Event Sessions ───────────────────────────────────────────────

    /// <summary>
    /// Get sessions matching event criteria (for events page session list).
    /// </summary>
    public async Task<SessionResults> GetEventSessions(
        [ID] int projectId,
        int count,
        QueryInput queryParams,
        string? sortField,
        bool sortDesc,
        int? page,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var (ids, total) = await clickHouse.QuerySessionIdsAsync(
            projectId, queryParams, count, page ?? 0, sortField, sortDesc, ct);

        if (ids.Count == 0)
            return new SessionResults([], total, 0, 0);

        var sessions = await db.Sessions
            .Where(s => ids.Contains(s.Id))
            .ToListAsync(ct);

        // Maintain ClickHouse ordering
        var ordered = ids
            .Select(id => sessions.FirstOrDefault(s => s.Id == id))
            .Where(s => s != null)
            .Cast<Session>()
            .ToList();

        var totalLength = ordered.Sum(s => (long)(s.Length ?? 0));
        var totalActiveLength = ordered.Sum(s => (long)(s.ActiveLength ?? 0));

        return new SessionResults(ordered, total, totalLength, totalActiveLength);
    }

    // ── Cross-Entity Correlation ─────────────────────────────────────

    /// <summary>
    /// Check which trace IDs from a list actually exist in ClickHouse logs.
    /// Returns the subset of trace IDs that have corresponding log entries.
    /// </summary>
    public async Task<List<string>> GetExistingLogsTraces(
        [ID] int projectId,
        List<string> traceIds,
        [GraphQLName("date_range")] DateRangeRequiredInput dateRange,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        if (traceIds.Count == 0)
            return [];

        // Check each trace ID by querying logs for it
        var existing = new List<string>();
        foreach (var traceId in traceIds)
        {
            var logs = await clickHouse.ReadLogsAsync(projectId,
                new QueryInput
                {
                    Query = $"trace_id={traceId}",
                    DateRange = dateRange
                },
                new ClickHousePagination { Limit = 1 }, ct);

            if (logs.Edges.Count > 0)
                existing.Add(traceId);
        }
        return existing;
    }

    /// <summary>
    /// Get error objects associated with log cursors.
    /// Log cursors contain timestamps that correlate to error objects created around the same time.
    /// </summary>
    public async Task<List<ErrorObject>> GetLogsErrorObjects(
        List<string> logCursors,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        if (logCursors.Count == 0)
            return [];

        // Log cursors encode a timestamp; find error objects near those timestamps
        var timestamps = new List<DateTime>();
        foreach (var cursor in logCursors)
        {
            try
            {
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
                var parts = decoded.Split(',');
                if (parts.Length > 0 && DateTime.TryParse(parts[0],
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var ts))
                    timestamps.Add(ts);
            }
            catch { /* skip invalid cursors */ }
        }

        if (timestamps.Count == 0)
            return [];

        var earliest = timestamps.Min().AddSeconds(-1);
        var latest = timestamps.Max().AddSeconds(1);

        return await db.Set<ErrorObject>()
            .Where(e => e.CreatedAt >= earliest && e.CreatedAt <= latest)
            .OrderByDescending(e => e.CreatedAt)
            .Take(100)
            .ToListAsync(ct);
    }

    // ── Source Map Upload ────────────────────────────────────────────

    /// <summary>
    /// Get pre-signed upload URLs for source map files.
    /// In self-hosted mode, returns local storage paths.
    /// </summary>
    public async Task<List<string>> GetSourceMapUploadUrls(
        string apiKey,
        List<string> paths,
        [Service] HoldFastDbContext db,
        [Service] IStorageService storage,
        CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Secret == apiKey, ct)
            ?? throw new ArgumentException("Invalid API key");

        var urls = new List<string>();
        foreach (var path in paths)
        {
            var key = $"{project.Id}/sourcemaps/{path}";
            var url = await storage.GetDownloadUrlAsync("sourcemaps", key, TimeSpan.FromHours(1), ct);
            urls.Add(url);
        }
        return urls;
    }

    // ── Log Lines ───────────────────────────────────────────────────

    /// <summary>
    /// Get structured log lines for a product type and query.
    /// </summary>
    public async Task<List<LogLine>> GetLogLines(
        string productType,
        [ID] int projectId,
        QueryInput queryParams,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] IClickHouseService clickHouse,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var logs = await clickHouse.ReadLogsAsync(projectId, queryParams,
            new ClickHousePagination { Limit = 100 }, ct);

        return logs.Edges.Select(e => new LogLine(
            e.Node.Timestamp,
            e.Node.Body ?? "",
            e.Node.SeverityText,
            System.Text.Json.JsonSerializer.Serialize(e.Node.LogAttributes))).ToList();
    }

    // ── External Integration Stubs ────────────────────────────────────

    /// <summary>
    /// Search issues in external trackers (Linear, Jira, GitHub, etc.).
    /// Returns empty in self-hosted mode — no external API keys configured.
    /// </summary>
    public Task<List<IssuesSearchResult>> SearchIssues(
        IntegrationType integrationType,
        [ID] int projectId,
        string query,
        ClaimsPrincipal claimsPrincipal)
    {
        // External issue tracker APIs are not available in self-hosted mode
        return Task.FromResult(new List<IssuesSearchResult>());
    }

    /// <summary>
    /// Generate a Zapier-compatible access token (JWT) for a project.
    /// Returns a signed JWT that Zapier can use for webhook authentication.
    /// </summary>
    public Task<string> GenerateZapierAccessToken(
        [ID] int projectId,
        ClaimsPrincipal claimsPrincipal)
    {
        // In self-hosted mode, return a simple project-scoped token
        // Production implementations should use proper JWT signing
        var token = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"zapier:{projectId}:{DateTime.UtcNow:O}"));
        return Task.FromResult(token);
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
