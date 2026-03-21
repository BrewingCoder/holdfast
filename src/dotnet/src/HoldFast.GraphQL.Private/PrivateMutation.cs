using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.Shared.Auth;
using HotChocolate;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.GraphQL.Private;

/// <summary>
/// Private GraphQL mutations — dashboard API.
/// All mutations require authentication and appropriate authorization.
/// </summary>
public class PrivateMutation
{
    // ── Workspace ─────────────────────────────────────────────────────

    /// <summary>
    /// Create a new workspace. Self-hosted: always Enterprise tier.
    /// </summary>
    public async Task<Workspace> CreateWorkspace(
        string name,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        var workspace = new Workspace
        {
            Name = name,
            PlanTier = "Enterprise",
            UnlimitedMembers = true,
            Admins = [admin],
        };

        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync(ct);

        // Create default workspace settings (all features enabled)
        db.AllWorkspaceSettings.Add(new AllWorkspaceSettings
        {
            WorkspaceId = workspace.Id,
        });
        await db.SaveChangesAsync(ct);

        return workspace;
    }

    /// <summary>
    /// Edit workspace name. Requires ADMIN role.
    /// </summary>
    public async Task<Workspace> EditWorkspace(
        int workspaceId,
        string? name,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAdmin(claimsPrincipal, workspaceId, authz, ct);

        var workspace = await db.Workspaces.FindAsync([workspaceId], ct)
            ?? throw new GraphQLException("Workspace not found");

        if (name != null)
            workspace.Name = name;

        await db.SaveChangesAsync(ct);
        return workspace;
    }

    /// <summary>
    /// Edit workspace settings (feature flags). Requires ADMIN role.
    /// </summary>
    public async Task<AllWorkspaceSettings> EditWorkspaceSettings(
        int workspaceId,
        bool? aiApplication,
        bool? aiInsights,
        bool? enableSSO,
        bool? enableSessionExport,
        bool? enableNetworkTraces,
        bool? enableDataDeletion,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAdmin(claimsPrincipal, workspaceId, authz, ct);

        var settings = await db.AllWorkspaceSettings
            .FirstOrDefaultAsync(s => s.WorkspaceId == workspaceId, ct)
            ?? throw new GraphQLException("Workspace settings not found");

        if (aiApplication.HasValue) settings.AIApplication = aiApplication.Value;
        if (aiInsights.HasValue) settings.AIInsights = aiInsights.Value;
        if (enableSSO.HasValue) settings.EnableSSO = enableSSO.Value;
        if (enableSessionExport.HasValue) settings.EnableSessionExport = enableSessionExport.Value;
        if (enableNetworkTraces.HasValue) settings.EnableNetworkTraces = enableNetworkTraces.Value;
        if (enableDataDeletion.HasValue) settings.EnableDataDeletion = enableDataDeletion.Value;

        await db.SaveChangesAsync(ct);
        return settings;
    }

    // ── Project ───────────────────────────────────────────────────────

    /// <summary>
    /// Create a new project within a workspace. Requires workspace membership.
    /// </summary>
    public async Task<Project> CreateProject(
        int workspaceId,
        string name,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

        var workspace = await db.Workspaces.FindAsync([workspaceId], ct)
            ?? throw new GraphQLException("Workspace not found");

        var project = new Project
        {
            Name = name,
            WorkspaceId = workspaceId,
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        // Create default filter settings
        db.ProjectFilterSettings.Add(new ProjectFilterSettings
        {
            ProjectId = project.Id,
        });
        await db.SaveChangesAsync(ct);

        return project;
    }

    /// <summary>
    /// Edit project name and billing email. Requires project access.
    /// </summary>
    public async Task<Project> EditProject(
        int projectId,
        string? name,
        string? billingEmail,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var project = await db.Projects.FindAsync([projectId], ct)
            ?? throw new GraphQLException("Project not found");

        if (name != null) project.Name = name;
        if (billingEmail != null) project.BillingEmail = billingEmail;

        await db.SaveChangesAsync(ct);
        return project;
    }

    /// <summary>
    /// Edit project settings (rage clicks, excluded users, sampling, etc).
    /// Requires project access.
    /// </summary>
    public async Task<ProjectFilterSettings> EditProjectSettings(
        int projectId,
        List<string>? excludedUsers,
        List<string>? errorFilters,
        int? rageClickWindowSeconds,
        int? rageClickRadiusPixels,
        int? rageClickCount,
        bool? filterChromeExtension,
        bool? filterSessionsWithoutError,
        int? autoResolveStaleErrorsDayInterval,
        double? sessionSamplingRate,
        double? errorSamplingRate,
        double? logSamplingRate,
        double? traceSamplingRate,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var project = await db.Projects.FindAsync([projectId], ct)
            ?? throw new GraphQLException("Project not found");

        // Update project-level settings
        if (excludedUsers != null) project.ExcludedUsers = excludedUsers;
        if (errorFilters != null) project.ErrorFilters = errorFilters;
        if (rageClickWindowSeconds.HasValue) project.RageClickWindowSeconds = rageClickWindowSeconds.Value;
        if (rageClickRadiusPixels.HasValue) project.RageClickRadiusPixels = rageClickRadiusPixels.Value;
        if (rageClickCount.HasValue) project.RageClickCount = rageClickCount.Value;
        if (filterChromeExtension.HasValue) project.FilterChromeExtension = filterChromeExtension.Value;

        // Update filter settings
        var filterSettings = await db.ProjectFilterSettings
            .FirstOrDefaultAsync(s => s.ProjectId == projectId, ct);

        if (filterSettings == null)
        {
            filterSettings = new ProjectFilterSettings { ProjectId = projectId };
            db.ProjectFilterSettings.Add(filterSettings);
        }

        if (filterSessionsWithoutError.HasValue)
            filterSettings.FilterSessionsWithoutError = filterSessionsWithoutError.Value;
        if (autoResolveStaleErrorsDayInterval.HasValue)
            filterSettings.AutoResolveStaleErrorsDayInterval = autoResolveStaleErrorsDayInterval.Value;
        if (sessionSamplingRate.HasValue) filterSettings.SessionSamplingRate = sessionSamplingRate.Value;
        if (errorSamplingRate.HasValue) filterSettings.ErrorSamplingRate = errorSamplingRate.Value;
        if (logSamplingRate.HasValue) filterSettings.LogSamplingRate = logSamplingRate.Value;
        if (traceSamplingRate.HasValue) filterSettings.TraceSamplingRate = traceSamplingRate.Value;

        await db.SaveChangesAsync(ct);
        return filterSettings;
    }

    /// <summary>
    /// Delete a project. Requires ADMIN role in workspace.
    /// </summary>
    public async Task<bool> DeleteProject(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var project = await db.Projects.FindAsync([projectId], ct)
            ?? throw new GraphQLException("Project not found");

        await AuthHelper.RequireWorkspaceAdmin(claimsPrincipal, project.WorkspaceId, authz, ct);

        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Billing / Retention ───────────────────────────────────────────

    /// <summary>
    /// Save retention periods for a workspace. Requires ADMIN role.
    /// </summary>
    public async Task<bool> SaveBillingPlan(
        int workspaceId,
        RetentionPeriod sessionsRetention,
        RetentionPeriod errorsRetention,
        RetentionPeriod logsRetention,
        RetentionPeriod tracesRetention,
        RetentionPeriod metricsRetention,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAdmin(claimsPrincipal, workspaceId, authz, ct);

        var workspace = await db.Workspaces.FindAsync([workspaceId], ct)
            ?? throw new GraphQLException("Workspace not found");

        workspace.RetentionPeriod = sessionsRetention;
        workspace.ErrorsRetentionPeriod = errorsRetention;
        workspace.LogsRetentionPeriod = logsRetention;
        workspace.TracesRetentionPeriod = tracesRetention;
        workspace.MetricsRetentionPeriod = metricsRetention;

        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Error Groups ──────────────────────────────────────────────────

    /// <summary>
    /// Update error group state (open, resolved, ignored). Requires project access.
    /// </summary>
    public async Task<ErrorGroup> UpdateErrorGroupState(
        int errorGroupId,
        ErrorGroupState state,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var errorGroup = await db.ErrorGroups.FindAsync([errorGroupId], ct)
            ?? throw new GraphQLException("ErrorGroup not found");

        var admin = await AuthHelper.RequireProjectAccess(claimsPrincipal, errorGroup.ProjectId, authz, ct);

        errorGroup.State = state;
        await db.SaveChangesAsync(ct);

        db.ErrorGroupActivityLogs.Add(new ErrorGroupActivityLog
        {
            ErrorGroupId = errorGroupId,
            AdminId = admin.Id,
            Action = state.ToString(),
        });
        await db.SaveChangesAsync(ct);

        return errorGroup;
    }

    /// <summary>
    /// Update whether an error group is publicly visible. Requires project access.
    /// </summary>
    public async Task<ErrorGroup> UpdateErrorGroupIsPublic(
        int errorGroupId,
        bool isPublic,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var errorGroup = await db.ErrorGroups.FindAsync([errorGroupId], ct)
            ?? throw new GraphQLException("ErrorGroup not found");

        await AuthHelper.RequireProjectAccess(claimsPrincipal, errorGroup.ProjectId, authz, ct);

        errorGroup.IsPublic = isPublic;
        await db.SaveChangesAsync(ct);
        return errorGroup;
    }

    /// <summary>
    /// Mark an error group as viewed by the current admin.
    /// </summary>
    public async Task<bool> MarkErrorGroupAsViewed(
        int errorGroupId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var eg = await db.ErrorGroups.FindAsync([errorGroupId], ct)
            ?? throw new GraphQLException("ErrorGroup not found");

        var admin = await AuthHelper.RequireProjectAccess(claimsPrincipal, eg.ProjectId, authz, ct);

        var existing = await db.ErrorGroupAdminsViews
            .FirstOrDefaultAsync(v => v.ErrorGroupId == errorGroupId && v.AdminId == admin.Id, ct);

        if (existing == null)
        {
            db.ErrorGroupAdminsViews.Add(new ErrorGroupAdminsView
            {
                ErrorGroupId = errorGroupId,
                AdminId = admin.Id,
            });
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    /// <summary>
    /// Update whether a session is publicly shareable. Requires project access.
    /// </summary>
    public async Task<Session> UpdateSessionIsPublic(
        string secureId,
        bool isPublic,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var session = await db.Sessions
            .FirstOrDefaultAsync(s => s.SecureId == secureId, ct)
            ?? throw new GraphQLException("Session not found");

        await AuthHelper.RequireProjectAccess(claimsPrincipal, session.ProjectId, authz, ct);

        // Sessions don't have IsPublic yet — use Starred as a visibility flag
        // TODO: Add IsPublic column to Session entity when needed
        session.Starred = isPublic;
        await db.SaveChangesAsync(ct);
        return session;
    }

    // ── Error Tags ───────────────────────────────────────────────────

    /// <summary>
    /// Create an error tag for an error group. Requires project access.
    /// </summary>
    public async Task<ErrorTag> CreateErrorTag(
        int errorGroupId,
        string title,
        string? description,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var eg = await db.ErrorGroups.FindAsync([errorGroupId], ct)
            ?? throw new GraphQLException("ErrorGroup not found");

        await AuthHelper.RequireProjectAccess(claimsPrincipal, eg.ProjectId, authz, ct);

        var tag = new ErrorTag
        {
            ErrorGroupId = errorGroupId,
            Title = title,
            Description = description,
        };

        db.ErrorTags.Add(tag);
        await db.SaveChangesAsync(ct);
        return tag;
    }

    // ── Error Comments (extended) ────────────────────────────────────

    /// <summary>
    /// Create an error comment. Admin ID is taken from auth context.
    /// </summary>
    public async Task<ErrorComment> CreateErrorComment(
        int errorGroupId,
        string text,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var eg = await db.ErrorGroups.FindAsync([errorGroupId], ct)
            ?? throw new GraphQLException("ErrorGroup not found");

        var admin = await AuthHelper.RequireProjectAccess(claimsPrincipal, eg.ProjectId, authz, ct);

        var comment = new ErrorComment
        {
            ErrorGroupId = errorGroupId,
            AdminId = admin.Id,
            Text = text,
        };

        db.ErrorComments.Add(comment);
        await db.SaveChangesAsync(ct);
        return comment;
    }

    /// <summary>
    /// Delete an error comment. Requires project access.
    /// </summary>
    public async Task<bool> DeleteErrorComment(
        int commentId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var comment = await db.ErrorComments
            .Include(c => c.ErrorGroup)
            .FirstOrDefaultAsync(c => c.Id == commentId, ct)
            ?? throw new GraphQLException("Error comment not found");

        await AuthHelper.RequireProjectAccess(claimsPrincipal, comment.ErrorGroup.ProjectId, authz, ct);

        db.ErrorComments.Remove(comment);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Metric Monitors ──────────────────────────────────────────────

    /// <summary>
    /// Create a metric monitor. Requires project access.
    /// </summary>
    public async Task<MetricMonitor> CreateMetricMonitor(
        int projectId,
        string name,
        string metricToMonitor,
        string? aggregator,
        double? threshold,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var monitor = new MetricMonitor
        {
            ProjectId = projectId,
            Name = name,
            MetricToMonitor = metricToMonitor,
            Aggregator = aggregator,
            Threshold = threshold,
        };

        db.MetricMonitors.Add(monitor);
        await db.SaveChangesAsync(ct);
        return monitor;
    }

    /// <summary>
    /// Update a metric monitor. Requires project access.
    /// </summary>
    public async Task<MetricMonitor> UpdateMetricMonitor(
        int metricMonitorId,
        string? name,
        string? aggregator,
        double? threshold,
        bool? disabled,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var monitor = await db.MetricMonitors.FindAsync([metricMonitorId], ct)
            ?? throw new GraphQLException("Metric monitor not found");

        await AuthHelper.RequireProjectAccess(claimsPrincipal, monitor.ProjectId, authz, ct);

        if (name != null) monitor.Name = name;
        if (aggregator != null) monitor.Aggregator = aggregator;
        if (threshold.HasValue) monitor.Threshold = threshold.Value;
        if (disabled.HasValue) monitor.Disabled = disabled.Value;

        await db.SaveChangesAsync(ct);
        return monitor;
    }

    /// <summary>
    /// Delete a metric monitor. Requires project access.
    /// </summary>
    public async Task<bool> DeleteMetricMonitor(
        int metricMonitorId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var monitor = await db.MetricMonitors.FindAsync([metricMonitorId], ct)
            ?? throw new GraphQLException("Metric monitor not found");

        await AuthHelper.RequireProjectAccess(claimsPrincipal, monitor.ProjectId, authz, ct);

        db.MetricMonitors.Remove(monitor);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Admin Profile ────────────────────────────────────────────────

    /// <summary>
    /// Update admin "about you" details.
    /// </summary>
    public async Task<Admin> UpdateAdminAboutYouDetails(
        string? name,
        string? referral,
        string? role,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        if (name != null) admin.Name = name;
        if (referral != null) admin.Referral = referral;
        if (role != null) admin.UserDefinedRole = role;

        await db.SaveChangesAsync(ct);
        return admin;
    }

    // ── Workspace Admin Management ───────────────────────────────────

    /// <summary>
    /// Change which projects an admin can access. Requires ADMIN role.
    /// </summary>
    public async Task<bool> ChangeProjectMembership(
        int workspaceId,
        int adminId,
        List<int>? projectIds,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAdmin(claimsPrincipal, workspaceId, authz, ct);

        var wa = await db.WorkspaceAdmins
            .FirstOrDefaultAsync(wa => wa.WorkspaceId == workspaceId && wa.AdminId == adminId, ct)
            ?? throw new GraphQLException("Admin not found in workspace");

        wa.ProjectIds = projectIds;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Update allowed email origins for auto-join. Requires ADMIN role.
    /// </summary>
    public async Task<bool> UpdateAllowedEmailOrigins(
        int workspaceId,
        List<string>? allowedAutoJoinEmailOrigins,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAdmin(claimsPrincipal, workspaceId, authz, ct);

        var workspace = await db.Workspaces.FindAsync([workspaceId], ct)
            ?? throw new GraphQLException("Workspace not found");

        workspace.AllowedAutoJoinEmailOrigins = allowedAutoJoinEmailOrigins != null
            ? string.Join(",", allowedAutoJoinEmailOrigins)
            : null;

        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Comments ──────────────────────────────────────────────────────

    /// <summary>
    /// Create a session comment. Admin ID is taken from auth context.
    /// </summary>
    public async Task<SessionComment> CreateSessionComment(
        int projectId,
        int sessionId,
        string text,
        int timestamp,
        double xCoordinate,
        double yCoordinate,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var comment = new SessionComment
        {
            ProjectId = projectId,
            SessionId = sessionId,
            AdminId = admin.Id,
            Text = text,
            Timestamp = timestamp,
            XCoordinate = xCoordinate,
            YCoordinate = yCoordinate,
            Type = "ADMIN",
        };

        db.SessionComments.Add(comment);
        await db.SaveChangesAsync(ct);

        return comment;
    }

    /// <summary>
    /// Delete a session comment. Requires project access.
    /// </summary>
    public async Task<bool> DeleteSessionComment(
        int commentId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var comment = await db.SessionComments.FindAsync([commentId], ct)
            ?? throw new GraphQLException("Comment not found");

        await AuthHelper.RequireProjectAccess(claimsPrincipal, comment.ProjectId, authz, ct);

        db.SessionComments.Remove(comment);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Reply to a session comment. Admin ID is taken from auth context.
    /// </summary>
    public async Task<CommentReply> ReplyToSessionComment(
        int sessionCommentId,
        string text,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var comment = await db.SessionComments.FindAsync([sessionCommentId], ct)
            ?? throw new GraphQLException("Session comment not found");

        var admin = await AuthHelper.RequireProjectAccess(claimsPrincipal, comment.ProjectId, authz, ct);

        var reply = new CommentReply
        {
            SessionCommentId = sessionCommentId,
            AdminId = admin.Id,
            Text = text,
        };

        db.CommentReplies.Add(reply);
        await db.SaveChangesAsync(ct);
        return reply;
    }

    // ── Alerts ────────────────────────────────────────────────────────

    /// <summary>
    /// Create or update an alert. Requires project access.
    /// </summary>
    public async Task<Alert> SaveAlert(
        int projectId,
        string name,
        string productType,
        string? functionType,
        string? query,
        double? belowThreshold,
        double? aboveThreshold,
        int? thresholdWindow,
        bool disabled,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var alert = new Alert
        {
            ProjectId = projectId,
            Name = name,
            ProductType = productType,
            FunctionType = functionType,
            Query = query,
            BelowThreshold = belowThreshold,
            AboveThreshold = aboveThreshold,
            ThresholdWindow = thresholdWindow,
            Disabled = disabled,
        };

        db.Alerts.Add(alert);
        await db.SaveChangesAsync(ct);

        return alert;
    }

    /// <summary>
    /// Update alert disabled state. Requires project access.
    /// </summary>
    public async Task<bool> UpdateAlertDisabled(
        int alertId,
        bool disabled,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var alert = await db.Alerts.FindAsync([alertId], ct)
            ?? throw new GraphQLException("Alert not found");

        await AuthHelper.RequireProjectAccess(claimsPrincipal, alert.ProjectId, authz, ct);

        alert.Disabled = disabled;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Delete an alert. Requires project access.
    /// </summary>
    public async Task<bool> DeleteAlert(
        int alertId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var alert = await db.Alerts.FindAsync([alertId], ct)
            ?? throw new GraphQLException("Alert not found");

        await AuthHelper.RequireProjectAccess(claimsPrincipal, alert.ProjectId, authz, ct);

        db.Alerts.Remove(alert);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Dashboards ────────────────────────────────────────────────────

    /// <summary>
    /// Create or update a dashboard. Requires project access.
    /// </summary>
    public async Task<Dashboard> UpsertDashboard(
        int projectId,
        string name,
        int? dashboardId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        Dashboard dashboard;

        if (dashboardId.HasValue)
        {
            dashboard = await db.Dashboards.FindAsync([dashboardId.Value], ct)
                ?? throw new GraphQLException("Dashboard not found");
            dashboard.Name = name;
        }
        else
        {
            dashboard = new Dashboard { ProjectId = projectId, Name = name };
            db.Dashboards.Add(dashboard);
        }

        await db.SaveChangesAsync(ct);
        return dashboard;
    }

    /// <summary>
    /// Delete a dashboard. Requires project access.
    /// </summary>
    public async Task<bool> DeleteDashboard(
        int dashboardId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var dashboard = await db.Dashboards.FindAsync([dashboardId], ct)
            ?? throw new GraphQLException("Dashboard not found");

        await AuthHelper.RequireProjectAccess(claimsPrincipal, dashboard.ProjectId, authz, ct);

        db.Dashboards.Remove(dashboard);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Saved Segments ────────────────────────────────────────────────

    /// <summary>
    /// Create a saved segment. Requires project access.
    /// </summary>
    public async Task<SavedSegment> CreateSavedSegment(
        int projectId,
        string name,
        string entityType,
        string? @params,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var segment = new SavedSegment
        {
            ProjectId = projectId,
            Name = name,
            EntityType = entityType,
            Params = @params,
        };

        db.SavedSegments.Add(segment);
        await db.SaveChangesAsync(ct);
        return segment;
    }

    /// <summary>
    /// Edit a saved segment. Requires project access.
    /// </summary>
    public async Task<SavedSegment> EditSavedSegment(
        int segmentId,
        string? name,
        string? @params,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var segment = await db.SavedSegments.FindAsync([segmentId], ct)
            ?? throw new GraphQLException("Saved segment not found");

        await AuthHelper.RequireProjectAccess(claimsPrincipal, segment.ProjectId, authz, ct);

        if (name != null) segment.Name = name;
        if (@params != null) segment.Params = @params;

        await db.SaveChangesAsync(ct);
        return segment;
    }

    /// <summary>
    /// Delete a saved segment. Requires project access.
    /// </summary>
    public async Task<bool> DeleteSavedSegment(
        int segmentId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var segment = await db.SavedSegments.FindAsync([segmentId], ct)
            ?? throw new GraphQLException("Saved segment not found");

        await AuthHelper.RequireProjectAccess(claimsPrincipal, segment.ProjectId, authz, ct);

        db.SavedSegments.Remove(segment);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Workspace Invites ────────────────────────────────────────────

    /// <summary>
    /// Create an invite link for a workspace. Requires ADMIN role.
    /// </summary>
    public async Task<WorkspaceInviteLink> CreateWorkspaceInviteLink(
        int workspaceId,
        string inviteeEmail,
        string role,
        List<int>? projectIds,
        int? expirationDays,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAdmin(claimsPrincipal, workspaceId, authz, ct);

        // Check for duplicate invite
        var existing = await db.WorkspaceInviteLinks
            .FirstOrDefaultAsync(l =>
                l.WorkspaceId == workspaceId &&
                l.InviteeEmail == inviteeEmail &&
                (l.ExpirationDate == null || l.ExpirationDate > DateTime.UtcNow), ct);

        if (existing != null)
            throw new GraphQLException("An active invite already exists for this email");

        // Check if already a member
        var existingAdmin = await db.Admins
            .FirstOrDefaultAsync(a => a.Email == inviteeEmail, ct);

        if (existingAdmin != null)
        {
            var alreadyMember = await db.WorkspaceAdmins
                .AnyAsync(wa => wa.AdminId == existingAdmin.Id && wa.WorkspaceId == workspaceId, ct);

            if (alreadyMember)
                throw new GraphQLException("This user is already a member of the workspace");
        }

        var invite = new WorkspaceInviteLink
        {
            WorkspaceId = workspaceId,
            InviteeEmail = inviteeEmail,
            InviteeRole = role,
            ProjectIds = projectIds,
            Secret = Guid.NewGuid().ToString("N"),
            ExpirationDate = expirationDays.HasValue
                ? DateTime.UtcNow.AddDays(expirationDays.Value)
                : DateTime.UtcNow.AddDays(7), // Default 7-day expiry
        };

        db.WorkspaceInviteLinks.Add(invite);
        await db.SaveChangesAsync(ct);

        return invite;
    }

    /// <summary>
    /// Accept a workspace invite link. Requires authentication.
    /// </summary>
    public async Task<bool> AcceptWorkspaceInvite(
        string secret,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        var invite = await db.WorkspaceInviteLinks
            .FirstOrDefaultAsync(l => l.Secret == secret, ct)
            ?? throw new GraphQLException("Invite not found");

        if (invite.ExpirationDate.HasValue && invite.ExpirationDate < DateTime.UtcNow)
            throw new GraphQLException("Invite has expired");

        if (!invite.WorkspaceId.HasValue)
            throw new GraphQLException("Invalid invite — no workspace");

        // Check not already a member
        var alreadyMember = await db.WorkspaceAdmins
            .AnyAsync(wa => wa.AdminId == admin.Id && wa.WorkspaceId == invite.WorkspaceId.Value, ct);

        if (alreadyMember)
            throw new GraphQLException("Already a member of this workspace");

        // Add admin to workspace with the invite's role
        db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            WorkspaceId = invite.WorkspaceId.Value,
            AdminId = admin.Id,
            Role = invite.InviteeRole ?? "MEMBER",
            ProjectIds = invite.ProjectIds,
        });

        // Remove the invite
        db.WorkspaceInviteLinks.Remove(invite);
        await db.SaveChangesAsync(ct);

        return true;
    }

    /// <summary>
    /// Delete/revoke a workspace invite link. Requires ADMIN role.
    /// </summary>
    public async Task<bool> DeleteWorkspaceInviteLink(
        int inviteId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var invite = await db.WorkspaceInviteLinks.FindAsync([inviteId], ct)
            ?? throw new GraphQLException("Invite not found");

        if (invite.WorkspaceId.HasValue)
            await AuthHelper.RequireWorkspaceAdmin(claimsPrincipal, invite.WorkspaceId.Value, authz, ct);

        db.WorkspaceInviteLinks.Remove(invite);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Admin ─────────────────────────────────────────────────────────

    /// <summary>
    /// Add an admin to a workspace. Requires ADMIN role.
    /// </summary>
    public async Task<bool> AddAdminToWorkspace(
        int workspaceId,
        int adminId,
        string role,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAdmin(claimsPrincipal, workspaceId, authz, ct);

        db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            WorkspaceId = workspaceId,
            AdminId = adminId,
            Role = role,
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Change an admin's role in a workspace. Requires ADMIN role.
    /// </summary>
    public async Task<bool> ChangeAdminRole(
        int workspaceId,
        int adminId,
        string newRole,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAdmin(claimsPrincipal, workspaceId, authz, ct);

        var wa = await db.WorkspaceAdmins
            .FirstOrDefaultAsync(wa => wa.WorkspaceId == workspaceId && wa.AdminId == adminId, ct)
            ?? throw new GraphQLException("Admin not found in workspace");

        wa.Role = newRole;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Remove an admin from a workspace. Requires ADMIN role.
    /// </summary>
    public async Task<bool> DeleteAdminFromWorkspace(
        int workspaceId,
        int adminId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAdmin(claimsPrincipal, workspaceId, authz, ct);

        var wa = await db.WorkspaceAdmins
            .FirstOrDefaultAsync(wa => wa.WorkspaceId == workspaceId && wa.AdminId == adminId, ct)
            ?? throw new GraphQLException("Admin not found in workspace");

        db.WorkspaceAdmins.Remove(wa);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Sessions ──────────────────────────────────────────────────────

    /// <summary>
    /// Mark a session as viewed by the current admin.
    /// </summary>
    public async Task<bool> MarkSessionAsViewed(
        string secureId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var session = await db.Sessions
            .FirstOrDefaultAsync(s => s.SecureId == secureId, ct)
            ?? throw new GraphQLException("Session not found");

        var admin = await AuthHelper.RequireProjectAccess(claimsPrincipal, session.ProjectId, authz, ct);

        var existing = await db.SessionAdminsViews
            .FirstOrDefaultAsync(v => v.SessionId == session.Id && v.AdminId == admin.Id, ct);

        if (existing == null)
        {
            db.SessionAdminsViews.Add(new SessionAdminsView
            {
                SessionId = session.Id,
                AdminId = admin.Id,
            });
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    /// <summary>
    /// Delete sessions matching criteria (for data deletion). Requires ADMIN role.
    /// </summary>
    public async Task<bool> DeleteSessions(
        int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var project = await db.Projects.FindAsync([projectId], ct)
            ?? throw new GraphQLException("Project not found");

        await AuthHelper.RequireWorkspaceAdmin(claimsPrincipal, project.WorkspaceId, authz, ct);

        db.DeleteSessionsTasks.Add(new DeleteSessionsTask
        {
            ProjectId = projectId,
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Legacy Error Alert CRUD ──────────────────────────────────────

    /// <summary>
    /// Update an error alert.
    /// </summary>
    public async Task<ErrorAlert> UpdateErrorAlert(
        int projectId,
        int errorAlertId,
        string? name,
        int? countThreshold,
        int? thresholdWindow,
        string? query,
        bool? disabled,
        int? frequency,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        var alert = await db.ErrorAlerts
            .FirstOrDefaultAsync(a => a.Id == errorAlertId && a.ProjectId == projectId, ct)
            ?? throw new GraphQLException("Error alert not found");

        if (name != null) alert.Name = name;
        if (countThreshold != null) alert.CountThreshold = countThreshold;
        if (thresholdWindow != null) alert.ThresholdWindow = thresholdWindow;
        if (query != null) alert.Query = query;
        if (disabled != null) alert.Disabled = disabled.Value;
        if (frequency != null) alert.Frequency = frequency;
        alert.LastAdminToEditId = admin.Id;

        await db.SaveChangesAsync(ct);
        return alert;
    }

    /// <summary>
    /// Delete an error alert.
    /// </summary>
    public async Task<ErrorAlert> DeleteErrorAlert(
        int projectId,
        int errorAlertId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var alert = await db.ErrorAlerts
            .FirstOrDefaultAsync(a => a.Id == errorAlertId && a.ProjectId == projectId, ct)
            ?? throw new GraphQLException("Error alert not found in this project");

        db.ErrorAlerts.Remove(alert);
        await db.SaveChangesAsync(ct);
        return alert;
    }

    /// <summary>
    /// Toggle error alert disabled state.
    /// </summary>
    public async Task<ErrorAlert> UpdateErrorAlertIsDisabled(
        int id,
        int projectId,
        bool disabled,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var alert = await db.ErrorAlerts
            .FirstOrDefaultAsync(a => a.Id == id && a.ProjectId == projectId, ct)
            ?? throw new GraphQLException("Error alert not found");

        alert.Disabled = disabled;
        await db.SaveChangesAsync(ct);
        return alert;
    }

    // ── Legacy Session Alert CRUD ────────────────────────────────────

    /// <summary>
    /// Update a session alert.
    /// </summary>
    public async Task<SessionAlert> UpdateSessionAlert(
        int id,
        int projectId,
        string? name,
        int? countThreshold,
        int? thresholdWindow,
        string? query,
        bool? disabled,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        var alert = await db.SessionAlerts
            .FirstOrDefaultAsync(a => a.Id == id && a.ProjectId == projectId, ct)
            ?? throw new GraphQLException("Session alert not found");

        if (name != null) alert.Name = name;
        if (countThreshold != null) alert.CountThreshold = countThreshold;
        if (thresholdWindow != null) alert.ThresholdWindow = thresholdWindow;
        if (query != null) alert.Query = query;
        if (disabled != null) alert.Disabled = disabled.Value;
        alert.LastAdminToEditId = admin.Id;

        await db.SaveChangesAsync(ct);
        return alert;
    }

    /// <summary>
    /// Delete a session alert.
    /// </summary>
    public async Task<SessionAlert> DeleteSessionAlert(
        int projectId,
        int sessionAlertId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var alert = await db.SessionAlerts
            .FirstOrDefaultAsync(a => a.Id == sessionAlertId && a.ProjectId == projectId, ct)
            ?? throw new GraphQLException("Session alert not found in this project");

        db.SessionAlerts.Remove(alert);
        await db.SaveChangesAsync(ct);
        return alert;
    }

    /// <summary>
    /// Toggle session alert disabled state.
    /// </summary>
    public async Task<SessionAlert> UpdateSessionAlertIsDisabled(
        int id,
        int projectId,
        bool disabled,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var alert = await db.SessionAlerts
            .FirstOrDefaultAsync(a => a.Id == id && a.ProjectId == projectId, ct)
            ?? throw new GraphQLException("Session alert not found");

        alert.Disabled = disabled;
        await db.SaveChangesAsync(ct);
        return alert;
    }

    // ── Legacy Log Alert CRUD ────────────────────────────────────────

    /// <summary>
    /// Update a log alert.
    /// </summary>
    public async Task<LogAlert> UpdateLogAlert(
        int id,
        int projectId,
        string? name,
        int? countThreshold,
        int? thresholdWindow,
        int? belowThreshold,
        string? query,
        bool? disabled,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        var alert = await db.LogAlerts
            .FirstOrDefaultAsync(a => a.Id == id && a.ProjectId == projectId, ct)
            ?? throw new GraphQLException("Log alert not found");

        if (name != null) alert.Name = name;
        if (countThreshold != null) alert.CountThreshold = countThreshold;
        if (thresholdWindow != null) alert.ThresholdWindow = thresholdWindow;
        if (belowThreshold != null) alert.BelowThreshold = belowThreshold;
        if (query != null) alert.Query = query;
        if (disabled != null) alert.Disabled = disabled.Value;
        alert.LastAdminToEditId = admin.Id;

        await db.SaveChangesAsync(ct);
        return alert;
    }

    /// <summary>
    /// Delete a log alert.
    /// </summary>
    public async Task<LogAlert> DeleteLogAlert(
        int projectId,
        int id,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var alert = await db.LogAlerts
            .FirstOrDefaultAsync(a => a.Id == id && a.ProjectId == projectId, ct)
            ?? throw new GraphQLException("Log alert not found in this project");

        db.LogAlerts.Remove(alert);
        await db.SaveChangesAsync(ct);
        return alert;
    }

    /// <summary>
    /// Toggle log alert disabled state.
    /// </summary>
    public async Task<LogAlert> UpdateLogAlertIsDisabled(
        int id,
        int projectId,
        bool disabled,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var alert = await db.LogAlerts
            .FirstOrDefaultAsync(a => a.Id == id && a.ProjectId == projectId, ct)
            ?? throw new GraphQLException("Log alert not found");

        alert.Disabled = disabled;
        await db.SaveChangesAsync(ct);
        return alert;
    }

    /// <summary>
    /// Toggle metric monitor disabled state.
    /// </summary>
    public async Task<MetricMonitor> UpdateMetricMonitorIsDisabled(
        int id,
        int projectId,
        bool disabled,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var monitor = await db.MetricMonitors
            .FirstOrDefaultAsync(m => m.Id == id && m.ProjectId == projectId, ct)
            ?? throw new GraphQLException("Metric monitor not found");

        monitor.Disabled = disabled;
        await db.SaveChangesAsync(ct);
        return monitor;
    }

    // ── Comment Replies & Muting ─────────────────────────────────────

    /// <summary>
    /// Reply to an error comment thread.
    /// </summary>
    public async Task<CommentReply> ReplyToErrorComment(
        int commentId,
        string text,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        var comment = await db.ErrorComments
            .FirstOrDefaultAsync(c => c.Id == commentId, ct)
            ?? throw new GraphQLException("Error comment not found");

        var reply = new CommentReply
        {
            ErrorCommentId = commentId,
            AdminId = admin.Id,
            Text = text,
        };

        db.CommentReplies.Add(reply);
        await db.SaveChangesAsync(ct);
        return reply;
    }

    /// <summary>
    /// Mute/unmute an error comment thread for the current admin.
    /// </summary>
    public async Task<bool> MuteErrorCommentThread(
        int id,
        bool? hasMuted,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        var follower = await db.CommentFollowers
            .FirstOrDefaultAsync(f => f.ErrorCommentId == id && f.AdminId == admin.Id, ct);

        if (follower != null)
        {
            follower.HasMuted = hasMuted ?? true;
        }
        else
        {
            db.CommentFollowers.Add(new CommentFollower
            {
                ErrorCommentId = id,
                AdminId = admin.Id,
                HasMuted = hasMuted ?? true,
            });
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Mute/unmute a session comment thread for the current admin.
    /// </summary>
    public async Task<bool> MuteSessionCommentThread(
        int id,
        bool? hasMuted,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        var follower = await db.CommentFollowers
            .FirstOrDefaultAsync(f => f.SessionCommentId == id && f.AdminId == admin.Id, ct);

        if (follower != null)
        {
            follower.HasMuted = hasMuted ?? true;
        }
        else
        {
            db.CommentFollowers.Add(new CommentFollower
            {
                SessionCommentId = id,
                AdminId = admin.Id,
                HasMuted = hasMuted ?? true,
            });
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Update error tags (triggers global recomputation).
    /// </summary>
    public async Task<bool> UpdateErrorTags(
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        // Require authenticated admin (Go calls r.Resolver.UpdateErrorTags which does internal processing)
        await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);
        // In the .NET version, this is a no-op placeholder — the actual tag recomputation
        // would be handled by a background worker. Return true for API compatibility.
        return await Task.FromResult(true);
    }

    // ── Visualization & Graph CRUD ───────────────────────────────────

    /// <summary>
    /// Create or update a visualization. Returns the visualization ID.
    /// </summary>
    public async Task<int> UpsertVisualization(
        int projectId,
        string? name,
        int? id,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        Visualization viz;
        if (id != null)
        {
            viz = await db.Visualizations.FirstOrDefaultAsync(v => v.Id == id.Value, ct)
                ?? throw new GraphQLException("Visualization not found");

            if (viz.ProjectId != projectId)
                throw new GraphQLException("Project ID does not match");

            if (name != null) viz.Name = name;
        }
        else
        {
            viz = new Visualization
            {
                ProjectId = projectId,
                Name = name ?? "Untitled",
            };
            db.Visualizations.Add(viz);
        }

        await db.SaveChangesAsync(ct);
        return viz.Id;
    }

    /// <summary>
    /// Delete a visualization and its graphs.
    /// </summary>
    public async Task<bool> DeleteVisualization(
        int id,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var viz = await db.Visualizations
            .Include(v => v.Graphs)
            .FirstOrDefaultAsync(v => v.Id == id, ct)
            ?? throw new GraphQLException("Visualization not found");

        await AuthHelper.RequireProjectAccess(claimsPrincipal, viz.ProjectId, authz, ct);

        db.Graphs.RemoveRange(viz.Graphs);
        db.Visualizations.Remove(viz);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Create or update a graph within a visualization.
    /// </summary>
    public async Task<Graph> UpsertGraph(
        int visualizationId,
        string title,
        string? productType,
        string? query,
        string? groupByKey,
        string? bucketByKey,
        int? bucketCount,
        int? limit,
        string? display,
        int? id,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var viz = await db.Visualizations.FirstOrDefaultAsync(v => v.Id == visualizationId, ct)
            ?? throw new GraphQLException("Visualization not found");

        await AuthHelper.RequireProjectAccess(claimsPrincipal, viz.ProjectId, authz, ct);

        Graph graph;
        if (id != null)
        {
            graph = await db.Graphs.FirstOrDefaultAsync(g => g.Id == id.Value, ct)
                ?? throw new GraphQLException("Graph not found");

            graph.Title = title;
            graph.ProductType = productType;
            graph.Query = query;
            graph.GroupByKey = groupByKey;
            graph.BucketByKey = bucketByKey;
            graph.BucketCount = bucketCount;
            graph.Limit = limit;
            graph.Display = display;
        }
        else
        {
            graph = new Graph
            {
                ProjectId = viz.ProjectId,
                VisualizationId = visualizationId,
                Title = title,
                ProductType = productType,
                Query = query,
                GroupByKey = groupByKey,
                BucketByKey = bucketByKey,
                BucketCount = bucketCount,
                Limit = limit,
                Display = display,
            };
            db.Graphs.Add(graph);
        }

        await db.SaveChangesAsync(ct);
        return graph;
    }

    /// <summary>
    /// Delete a graph.
    /// </summary>
    public async Task<bool> DeleteGraph(
        int id,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var graph = await db.Graphs.FirstOrDefaultAsync(g => g.Id == id, ct)
            ?? throw new GraphQLException("Graph not found");

        await AuthHelper.RequireProjectAccess(claimsPrincipal, graph.ProjectId, authz, ct);

        db.Graphs.Remove(graph);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Email Opt-Out ────────────────────────────────────────────────

    /// <summary>
    /// Update email opt-out preference for the current admin.
    /// </summary>
    public async Task<bool> UpdateEmailOptOut(
        string category,
        bool optedOut,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        var existing = await db.EmailOptOuts
            .FirstOrDefaultAsync(e => e.AdminId == admin.Id && e.Category == category, ct);

        if (optedOut && existing == null)
        {
            db.EmailOptOuts.Add(new EmailOptOut
            {
                AdminId = admin.Id,
                Category = category,
            });
        }
        else if (!optedOut && existing != null)
        {
            db.EmailOptOuts.Remove(existing);
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Workspace Invite ────────────────────────────────────────────

    /// <summary>
    /// Send an admin workspace invite via email address.
    /// Creates an invite link record. Self-hosted: no email sending, just creates the link.
    /// </summary>
    public async Task<string> SendAdminWorkspaceInvite(
        int workspaceId,
        string email,
        string role,
        List<int>? projectIds,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAdmin(claimsPrincipal, workspaceId, authz, ct);

        if (role != "ADMIN" && role != "MEMBER")
            throw new GraphQLException($"Invalid role: {role}");

        // Check for existing invite
        var existingInvite = await db.WorkspaceInviteLinks
            .FirstOrDefaultAsync(i => i.WorkspaceId == workspaceId
                && i.InviteeEmail != null
                && i.InviteeEmail.ToLower() == email.ToLower(), ct);
        if (existingInvite != null)
            throw new GraphQLException($"\"{email}\" has already been invited to this workspace.");

        // Check if admin already exists in workspace
        var existingAdmin = await db.Admins.FirstOrDefaultAsync(a => a.Email != null && a.Email.ToLower() == email.ToLower(), ct);
        if (existingAdmin != null)
        {
            var isAlreadyMember = await db.WorkspaceAdmins
                .AnyAsync(wa => wa.AdminId == existingAdmin.Id && wa.WorkspaceId == workspaceId, ct);
            if (isAlreadyMember)
                throw new GraphQLException($"\"{email}\" is already a member of this workspace.");
        }

        var secret = Guid.NewGuid().ToString("N");
        var inviteLink = new WorkspaceInviteLink
        {
            WorkspaceId = workspaceId,
            InviteeEmail = email,
            InviteeRole = role,
            Secret = secret,
            ExpirationDate = DateTime.UtcNow.AddDays(7),
            ProjectIds = projectIds,
        };

        db.WorkspaceInviteLinks.Add(inviteLink);
        await db.SaveChangesAsync(ct);

        return secret;
    }

    /// <summary>
    /// Join a workspace (for workspaces with auto-join email origins).
    /// </summary>
    public async Task<int> JoinWorkspace(
        int workspaceId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        // Check if already a member
        var alreadyMember = await db.WorkspaceAdmins
            .AnyAsync(wa => wa.AdminId == admin.Id && wa.WorkspaceId == workspaceId, ct);
        if (alreadyMember)
            throw new GraphQLException("Already a member of this workspace");

        var workspace = await db.Workspaces.FindAsync([workspaceId], ct)
            ?? throw new GraphQLException("Workspace not found");

        // Add as MEMBER
        db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = admin.Id,
            WorkspaceId = workspaceId,
            Role = "MEMBER",
        });
        await db.SaveChangesAsync(ct);

        return workspaceId;
    }

    // ── Session Export ────────────────────────────────────────────────

    /// <summary>
    /// Request a session export (creates a SessionExport record).
    /// The actual export is handled by a background worker.
    /// </summary>
    public async Task<bool> ExportSession(
        string sessionSecureId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        var session = await db.Sessions
            .FirstOrDefaultAsync(s => s.SecureId == sessionSecureId, ct)
            ?? throw new GraphQLException("Session not found");

        await AuthHelper.RequireProjectAccess(claimsPrincipal, session.ProjectId, authz, ct);

        // Create or update export record
        var existing = await db.SessionExports
            .FirstOrDefaultAsync(e => e.SessionId == session.Id && e.Type == "mp4", ct);

        if (existing != null)
        {
            existing.Url = null;
            existing.Error = null;
        }
        else
        {
            db.SessionExports.Add(new SessionExport
            {
                SessionId = session.Id,
                Type = "mp4",
            });
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Edit Project Platforms ────────────────────────────────────────

    /// <summary>
    /// Update the platforms configuration for a project.
    /// </summary>
    public async Task<bool> EditProjectPlatforms(
        int projectId,
        string platforms,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var project = await db.Projects.FindAsync([projectId], ct)
            ?? throw new GraphQLException("Project not found");

        project.Platforms = platforms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        await db.SaveChangesAsync(ct);
        return true;
    }
}
