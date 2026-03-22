using System.Security.Claims;
using HoldFast.Data;
using HoldFast.Domain;
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
        [ID] int workspaceId,
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
        [ID] int workspaceId,
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
        [ID] int workspaceId,
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
        [ID] int projectId,
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
        [GraphQLName("projectId")] [ID] int projectId,
        List<string>? excludedUsers,
        List<string>? errorFilters,
        int? rageClickWindowSeconds,
        int? rageClickRadiusPixels,
        int? rageClickCount,
        bool? filterChromeExtension,
        [GraphQLName("filterSessionsWithoutError")] bool? filterSessionsWithoutError,
        [GraphQLName("autoResolveStaleErrorsDayInterval")] int? autoResolveStaleErrorsDayInterval,
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
        [ID] int projectId,
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
        [ID] int workspaceId,
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
        [ID] int errorGroupId,
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
        [ID] int errorGroupId,
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
        [ID] int errorGroupId,
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
        [ID] int errorGroupId,
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
        [ID] int errorGroupId,
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
        [ID] int commentId,
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
        [ID] int projectId,
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
        [ID] int metricMonitorId,
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
        [ID] int metricMonitorId,
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
    public async Task<bool> UpdateAdminAboutYouDetails(
        [GraphQLName("adminDetails")] AdminAboutYouDetails adminDetails,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        var firstName = adminDetails.FirstName?.Trim();
        var lastName = adminDetails.LastName?.Trim();
        admin.Name = string.IsNullOrEmpty(lastName)
            ? firstName
            : $"{firstName} {lastName}";
        admin.UserDefinedRole = adminDetails.UserDefinedRole;
        admin.Referral = adminDetails.Referral;
        admin.AboutYouDetailsFilled = "true";

        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Workspace Admin Management ───────────────────────────────────

    /// <summary>
    /// Change which projects an admin can access. Requires ADMIN role.
    /// </summary>
    public async Task<bool> ChangeProjectMembership(
        [ID] int workspaceId,
        [ID] int adminId,
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
        [ID] int workspaceId,
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
        [ID] int projectId,
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
        [ID] int commentId,
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
        [ID] int projectId,
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
    /// Create a new alert (frontend uses this name).
    /// </summary>
    public async Task<Alert> CreateAlert(
        [ID] int projectId,
        string name,
        string productType,
        string? functionType,
        string? functionColumn,
        string? query,
        string? groupByKey,
        bool? defaultAlert,
        double? thresholdValue,
        int? thresholdWindow,
        int? thresholdCooldown,
        List<AlertDestinationInput>? destinations,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        var alert = new Alert
        {
            ProjectId = projectId,
            Name = name,
            ProductType = productType,
            FunctionType = functionType,
            FunctionColumn = functionColumn,
            Query = query,
            GroupByKey = groupByKey,
            Default = defaultAlert ?? false,
            AboveThreshold = thresholdValue,
            ThresholdWindow = thresholdWindow,
            ThresholdCooldown = thresholdCooldown,
            LastAdminToEditId = admin.Id,
        };

        db.Alerts.Add(alert);
        await db.SaveChangesAsync(ct);

        if (destinations != null)
        {
            foreach (var dest in destinations)
            {
                db.AlertDestinations.Add(new AlertDestination
                {
                    AlertId = alert.Id,
                    DestinationType = dest.DestinationType,
                    TypeId = dest.TypeId,
                    TypeName = dest.TypeName,
                });
            }
            await db.SaveChangesAsync(ct);
        }

        return alert;
    }

    /// <summary>
    /// Update an existing alert.
    /// </summary>
    public async Task<Alert> UpdateAlert(
        [ID] int projectId,
        [ID] int alertId,
        string? name,
        string? productType,
        string? functionType,
        string? functionColumn,
        string? query,
        string? groupByKey,
        double? thresholdValue,
        int? thresholdWindow,
        int? thresholdCooldown,
        List<AlertDestinationInput>? destinations,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        var alert = await db.Alerts.FindAsync(new object[] { alertId }, ct)
            ?? throw new GraphQLException("Alert not found");

        if (alert.ProjectId != projectId)
            throw new GraphQLException("Alert does not belong to this project");

        alert.Name = name ?? alert.Name;
        alert.ProductType = productType ?? alert.ProductType;
        alert.FunctionType = functionType ?? alert.FunctionType;
        alert.FunctionColumn = functionColumn ?? alert.FunctionColumn;
        alert.Query = query ?? alert.Query;
        alert.GroupByKey = groupByKey ?? alert.GroupByKey;
        alert.AboveThreshold = thresholdValue ?? alert.AboveThreshold;
        alert.ThresholdWindow = thresholdWindow ?? alert.ThresholdWindow;
        alert.ThresholdCooldown = thresholdCooldown ?? alert.ThresholdCooldown;
        alert.LastAdminToEditId = admin.Id;

        if (destinations != null)
        {
            var existing = await db.AlertDestinations
                .Where(d => d.AlertId == alertId).ToListAsync(ct);
            db.AlertDestinations.RemoveRange(existing);

            foreach (var dest in destinations)
            {
                db.AlertDestinations.Add(new AlertDestination
                {
                    AlertId = alertId,
                    DestinationType = dest.DestinationType,
                    TypeId = dest.TypeId,
                    TypeName = dest.TypeName,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        return alert;
    }

    /// <summary>
    /// Update alert disabled state. Requires project access.
    /// </summary>
    public async Task<bool> UpdateAlertDisabled(
        [ID] int alertId,
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
        [ID] int alertId,
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
        [ID] int projectId,
        string name,
        [ID] int? dashboardId,
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
        [ID] int dashboardId,
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
        [ID] int projectId,
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
        [ID] int segmentId,
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
        [ID] int segmentId,
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
        [ID] int workspaceId,
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
            Role = invite.InviteeRole ?? WorkspaceRoles.Member,
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
        [ID] int workspaceId,
        [ID] int adminId,
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
        [ID] int workspaceId,
        [ID] int adminId,
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
        [ID] int workspaceId,
        [ID] int adminId,
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
        [ID] int projectId,
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
        [ID] int projectId,
        [ID] int errorAlertId,
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
        [ID] int projectId,
        [ID] int errorAlertId,
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
        [ID] int id,
        [ID] int projectId,
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
        [ID] int id,
        [ID] int projectId,
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
        [ID] int projectId,
        [ID] int sessionAlertId,
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
        [ID] int id,
        [ID] int projectId,
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
        [ID] int id,
        [ID] int projectId,
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
        [ID] int projectId,
        [ID] int id,
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
        [ID] int id,
        [ID] int projectId,
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
        [ID] int id,
        [ID] int projectId,
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
        [ID] int commentId,
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
        [ID] int id,
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
        [ID] int id,
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
        [ID] int projectId,
        string? name,
        [ID] int? id,
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
        [ID] int id,
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
        [ID] int visualizationId,
        string title,
        string? productType,
        string? query,
        string? groupByKey,
        string? bucketByKey,
        int? bucketCount,
        int? limit,
        string? display,
        [ID] int? id,
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
        [ID] int id,
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
        [ID] int workspaceId,
        string email,
        string role,
        List<int>? projectIds,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAdmin(claimsPrincipal, workspaceId, authz, ct);

        if (role != WorkspaceRoles.Admin && role != WorkspaceRoles.Member)
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
        [ID] int workspaceId,
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
            Role = WorkspaceRoles.Member,
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
        [GraphQLName("projectID")] [ID] int projectId,
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

    // ── Admin Registration ───────────────────────────────────────────

    /// <summary>
    /// Create a new admin and workspace in one call (onboarding flow).
    /// </summary>
    public async Task<Workspace> UpdateAdminAndCreateWorkspace(
        string adminName,
        string workspaceName,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        admin.Name = adminName;

        var workspace = new Workspace
        {
            Name = workspaceName,
            PlanTier = "Enterprise",
            UnlimitedMembers = true,
        };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync(ct);

        db.WorkspaceAdmins.Add(new WorkspaceAdmin
        {
            AdminId = admin.Id,
            WorkspaceId = workspace.Id,
            Role = WorkspaceRoles.Admin,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        return workspace;
    }

    /// <summary>
    /// Create admin from authenticated identity (first login).
    /// </summary>
    public async Task<Admin> CreateAdmin(
        ClaimsPrincipal claimsPrincipal,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var uid = claimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new GraphQLException("No UID in claims");
        var email = claimsPrincipal.FindFirst(ClaimTypes.Email)?.Value;

        var existing = await db.Admins.FirstOrDefaultAsync(a => a.Uid == uid, ct);
        if (existing != null) return existing;

        var admin = new Admin
        {
            Uid = uid,
            Email = email,
            EmailVerified = email != null,
        };
        db.Admins.Add(admin);
        await db.SaveChangesAsync(ct);
        return admin;
    }

    /// <summary>
    /// Submit email signup.
    /// </summary>
    public async Task<string> EmailSignup(
        string email,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var existing = await db.EmailSignups.FirstOrDefaultAsync(e => e.Email == email, ct);
        if (existing != null) return email;

        db.EmailSignups.Add(new EmailSignup { Email = email });
        await db.SaveChangesAsync(ct);
        return email;
    }

    /// <summary>
    /// Submit registration survey data for a workspace.
    /// </summary>
    public async Task<bool> SubmitRegistrationForm(
        [ID] int workspaceId,
        string? teamSize,
        string? role,
        string? useCase,
        string? heardAbout,
        string? pun,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

        var existing = await db.RegistrationData
            .FirstOrDefaultAsync(r => r.WorkspaceId == workspaceId, ct);

        if (existing != null)
        {
            existing.TeamSize = teamSize ?? existing.TeamSize;
            existing.Role = role ?? existing.Role;
            existing.UseCase = useCase ?? existing.UseCase;
            existing.HeardAbout = heardAbout ?? existing.HeardAbout;
            existing.Pun = pun ?? existing.Pun;
        }
        else
        {
            db.RegistrationData.Add(new RegistrationData
            {
                WorkspaceId = workspaceId,
                TeamSize = teamSize,
                Role = role,
                UseCase = useCase,
                HeardAbout = heardAbout,
                Pun = pun,
            });
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Request access to a workspace.
    /// </summary>
    public async Task<bool> RequestAccess(
        [ID] int workspaceId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        var existing = await db.WorkspaceAccessRequests
            .FirstOrDefaultAsync(r => r.AdminId == admin.Id, ct);

        if (existing != null)
        {
            existing.LastRequestedWorkspace = workspaceId;
        }
        else
        {
            db.WorkspaceAccessRequests.Add(new WorkspaceAccessRequest
            {
                AdminId = admin.Id,
                LastRequestedWorkspace = workspaceId,
            });
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Integration Management ───────────────────────────────────────

    /// <summary>
    /// Add an integration mapping to a project.
    /// </summary>
    public async Task<bool> AddIntegrationToProject(
        IntegrationType integrationType,
        [ID] int projectId,
        string? code,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var existing = await db.IntegrationProjectMappings
            .FirstOrDefaultAsync(m => m.ProjectId == projectId
                && m.IntegrationType == integrationType.ToString(), ct);

        if (existing != null)
        {
            existing.ExternalId = code;
        }
        else
        {
            db.IntegrationProjectMappings.Add(new IntegrationProjectMapping
            {
                IntegrationType = integrationType.ToString(),
                ProjectId = projectId,
                ExternalId = code,
            });
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Remove an integration mapping from a project.
    /// </summary>
    public async Task<bool> RemoveIntegrationFromProject(
        IntegrationType integrationType,
        [ID] int projectId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var mapping = await db.IntegrationProjectMappings
            .FirstOrDefaultAsync(m => m.ProjectId == projectId
                && m.IntegrationType == integrationType.ToString(), ct);

        if (mapping != null)
        {
            db.IntegrationProjectMappings.Remove(mapping);
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    /// <summary>
    /// Add an integration mapping to a workspace.
    /// </summary>
    public async Task<bool> AddIntegrationToWorkspace(
        IntegrationType integrationType,
        [ID] int workspaceId,
        string? code,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

        var existing = await db.IntegrationWorkspaceMappings
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId
                && m.IntegrationType == integrationType.ToString(), ct);

        if (existing != null)
        {
            existing.AccessToken = code;
        }
        else
        {
            db.IntegrationWorkspaceMappings.Add(new IntegrationWorkspaceMapping
            {
                IntegrationType = integrationType.ToString(),
                WorkspaceId = workspaceId,
                AccessToken = code,
            });
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Remove an integration mapping from a workspace.
    /// </summary>
    public async Task<bool> RemoveIntegrationFromWorkspace(
        IntegrationType integrationType,
        [ID] int workspaceId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

        var mapping = await db.IntegrationWorkspaceMappings
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId
                && m.IntegrationType == integrationType.ToString(), ct);

        if (mapping != null)
        {
            db.IntegrationWorkspaceMappings.Remove(mapping);
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    /// <summary>
    /// Update integration project mappings for a workspace.
    /// </summary>
    public async Task<bool> UpdateIntegrationProjectMappings(
        [ID] int workspaceId,
        IntegrationType integrationType,
        List<IntegrationProjectMappingInput> projectMappings,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

        // Remove existing mappings for this integration type in this workspace
        var existing = await db.IntegrationProjectMappings
            .Include(m => m.Project)
            .Where(m => m.Project.WorkspaceId == workspaceId
                && m.IntegrationType == integrationType.ToString())
            .ToListAsync(ct);
        db.IntegrationProjectMappings.RemoveRange(existing);

        // Add new mappings
        foreach (var mapping in projectMappings)
        {
            db.IntegrationProjectMappings.Add(new IntegrationProjectMapping
            {
                IntegrationType = integrationType.ToString(),
                ProjectId = mapping.ProjectId,
                ExternalId = mapping.ExternalId,
            });
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Issue Linking ────────────────────────────────────────────────

    /// <summary>
    /// Create a session comment with an existing external issue.
    /// </summary>
    public async Task<SessionComment> CreateSessionCommentWithExistingIssue(
        [ID] int projectId,
        string sessionSecureId,
        int sessionTimestamp,
        string text,
        string? taggedAdmins,
        string? taggedSlackUsers,
        IntegrationType integrationType,
        string externalIssueUrl,
        string? issueTitle,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        var session = await db.Sessions.FirstOrDefaultAsync(s => s.SecureId == sessionSecureId, ct)
            ?? throw new GraphQLException($"Session not found: {sessionSecureId}");

        var comment = new SessionComment
        {
            ProjectId = projectId,
            AdminId = admin.Id,
            SessionId = session.Id,
            SessionSecureId = session.Id,
            Timestamp = sessionTimestamp,
            Text = text,
            Type = SessionCommentType.Admin.ToString(),
        };
        db.SessionComments.Add(comment);
        await db.SaveChangesAsync(ct);

        // Create external attachment
        db.ExternalAttachments.Add(new ExternalAttachment
        {
            SessionCommentId = comment.Id,
            IntegrationType = integrationType.ToString(),
            ExternalId = externalIssueUrl,
            Title = issueTitle,
        });
        await db.SaveChangesAsync(ct);

        return comment;
    }

    /// <summary>
    /// Create an error comment with an existing external issue.
    /// </summary>
    public async Task<ErrorComment> CreateErrorCommentForExistingIssue(
        [ID] int projectId,
        string errorGroupSecureId,
        string text,
        string? taggedAdmins,
        IntegrationType integrationType,
        string externalIssueUrl,
        string? issueTitle,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);
        var admin = await AuthHelper.GetRequiredAdmin(claimsPrincipal, authz, ct);

        var errorGroup = await db.ErrorGroups
            .FirstOrDefaultAsync(e => e.SecureId == errorGroupSecureId, ct)
            ?? throw new GraphQLException($"Error group not found: {errorGroupSecureId}");

        var comment = new ErrorComment
        {
            ErrorGroupId = errorGroup.Id,
            AdminId = admin.Id,
            Text = text,
        };
        db.ErrorComments.Add(comment);
        await db.SaveChangesAsync(ct);

        // Create external attachment
        db.ExternalAttachments.Add(new ExternalAttachment
        {
            ErrorGroupId = errorGroup.Id,
            IntegrationType = integrationType.ToString(),
            ExternalId = externalIssueUrl,
            Title = issueTitle,
        });
        await db.SaveChangesAsync(ct);

        return comment;
    }

    /// <summary>
    /// Remove an external issue link from an error group.
    /// </summary>
    public async Task<bool> RemoveErrorIssue(
        string errorGroupSecureId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var errorGroup = await db.ErrorGroups
            .FirstOrDefaultAsync(e => e.SecureId == errorGroupSecureId, ct)
            ?? throw new GraphQLException($"Error group not found: {errorGroupSecureId}");
        await AuthHelper.RequireProjectAccess(claimsPrincipal, errorGroup.ProjectId, authz, ct);

        var attachments = await db.ExternalAttachments
            .Where(a => a.ErrorGroupId == errorGroup.Id)
            .ToListAsync(ct);

        db.ExternalAttachments.RemoveRange(attachments);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Service GitHub Settings ──────────────────────────────────────

    /// <summary>
    /// Edit GitHub settings for a service (source map enrichment).
    /// </summary>
    public async Task<Service> EditServiceGithubSettings(
        int serviceId,
        string? githubRepoPath,
        string? buildPrefix,
        string? githubPrefix,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var service = await db.Services.FindAsync(new object[] { serviceId }, ct)
            ?? throw new GraphQLException($"Service not found: {serviceId}");
        await AuthHelper.RequireProjectAccess(claimsPrincipal, service.ProjectId, authz, ct);

        service.GithubRepoPath = githubRepoPath ?? service.GithubRepoPath;
        service.BuildPrefix = buildPrefix ?? service.BuildPrefix;
        service.GithubPrefix = githubPrefix ?? service.GithubPrefix;

        await db.SaveChangesAsync(ct);
        return service;
    }

    // ── Cloudflare Proxy ─────────────────────────────────────────────

    /// <summary>
    /// Set up a Cloudflare proxy for a workspace.
    /// </summary>
    public async Task<bool> CreateCloudflareProxy(
        [ID] int workspaceId,
        string proxySubdomain,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

        var workspace = await db.Workspaces.FindAsync(new object[] { workspaceId }, ct)
            ?? throw new GraphQLException("Workspace not found");

        workspace.CloudflareProxy = proxySubdomain;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Notification Channels ────────────────────────────────────────

    /// <summary>
    /// Create or update a Slack channel configuration for a workspace.
    /// </summary>
    public async Task<bool> UpsertSlackChannel(
        [ID] int workspaceId,
        string name,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

        // Store Slack channels as a JSON array in workspace.SlackChannels
        var workspace = await db.Workspaces.FindAsync(new object[] { workspaceId }, ct)
            ?? throw new GraphQLException("Workspace not found");

        var channels = string.IsNullOrEmpty(workspace.SlackChannels)
            ? new List<string>()
            : System.Text.Json.JsonSerializer.Deserialize<List<string>>(workspace.SlackChannels) ?? [];

        if (!channels.Contains(name))
        {
            channels.Add(name);
            workspace.SlackChannels = System.Text.Json.JsonSerializer.Serialize(channels);
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    /// <summary>
    /// Create or update a Discord channel configuration for a workspace.
    /// </summary>
    public async Task<bool> UpsertDiscordChannel(
        [ID] int workspaceId,
        string name,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

        // DiscordGuildId used as channel tracking — set it if not set
        var workspace = await db.Workspaces.FindAsync(new object[] { workspaceId }, ct)
            ?? throw new GraphQLException("Workspace not found");

        if (string.IsNullOrEmpty(workspace.DiscordGuildId))
        {
            workspace.DiscordGuildId = name;
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    // ── Slack Integration ────────────────────────────────────────────

    /// <summary>
    /// Sync Slack integration — stores access token on workspace.
    /// </summary>
    public async Task<bool> SyncSlackIntegration(
        [ID] int projectId,
        string? code,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var project = await db.Projects.Include(p => p.Workspace)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new GraphQLException("Project not found");

        if (!string.IsNullOrEmpty(code))
        {
            project.Workspace.SlackAccessToken = code;
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    // ── Issue Creation (Stubs) ───────────────────────────────────────

    /// <summary>
    /// Create an external issue for a session comment (stub — requires external API integration).
    /// Returns the session comment.
    /// </summary>
    public async Task<SessionComment> CreateIssueForSessionComment(
        [ID] int projectId,
        string sessionSecureId,
        int sessionCommentId,
        IntegrationType integrationType,
        string? issueTitle,
        string? issueDescription,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var comment = await db.SessionComments.FindAsync(new object[] { sessionCommentId }, ct)
            ?? throw new GraphQLException("Session comment not found");

        // Stub: create a placeholder ExternalAttachment
        db.ExternalAttachments.Add(new ExternalAttachment
        {
            SessionCommentId = comment.Id,
            IntegrationType = integrationType.ToString(),
            Title = issueTitle ?? "Issue",
        });
        await db.SaveChangesAsync(ct);

        return comment;
    }

    /// <summary>
    /// Link an existing external issue to a session comment.
    /// </summary>
    public async Task<SessionComment> LinkIssueForSessionComment(
        [ID] int projectId,
        int sessionCommentId,
        IntegrationType integrationType,
        string externalIssueUrl,
        string? issueTitle,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var comment = await db.SessionComments.FindAsync(new object[] { sessionCommentId }, ct)
            ?? throw new GraphQLException("Session comment not found");

        db.ExternalAttachments.Add(new ExternalAttachment
        {
            SessionCommentId = comment.Id,
            IntegrationType = integrationType.ToString(),
            ExternalId = externalIssueUrl,
            Title = issueTitle,
        });
        await db.SaveChangesAsync(ct);

        return comment;
    }

    /// <summary>
    /// Create an external issue for an error comment (stub — requires external API integration).
    /// </summary>
    public async Task<ErrorComment> CreateIssueForErrorComment(
        [ID] int projectId,
        string errorGroupSecureId,
        int errorCommentId,
        IntegrationType integrationType,
        string? issueTitle,
        string? issueDescription,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var comment = await db.ErrorComments.FindAsync(new object[] { errorCommentId }, ct)
            ?? throw new GraphQLException("Error comment not found");

        var errorGroup = await db.ErrorGroups
            .FirstOrDefaultAsync(e => e.SecureId == errorGroupSecureId, ct)
            ?? throw new GraphQLException("Error group not found");

        db.ExternalAttachments.Add(new ExternalAttachment
        {
            ErrorGroupId = errorGroup.Id,
            IntegrationType = integrationType.ToString(),
            Title = issueTitle ?? "Issue",
        });
        await db.SaveChangesAsync(ct);

        return comment;
    }

    /// <summary>
    /// Link an existing external issue to an error comment.
    /// </summary>
    public async Task<ErrorComment> LinkIssueForErrorComment(
        [ID] int projectId,
        int errorCommentId,
        IntegrationType integrationType,
        string externalIssueUrl,
        string? issueTitle,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var comment = await db.ErrorComments.FindAsync(new object[] { errorCommentId }, ct)
            ?? throw new GraphQLException("Error comment not found");

        db.ExternalAttachments.Add(new ExternalAttachment
        {
            ErrorGroupId = comment.ErrorGroupId,
            IntegrationType = integrationType.ToString(),
            ExternalId = externalIssueUrl,
            Title = issueTitle,
        });
        await db.SaveChangesAsync(ct);

        return comment;
    }

    /// <summary>
    /// Delete a workspace invite link by ID.
    /// </summary>
    public async Task<bool> DeleteInviteLinkFromWorkspace(
        [ID] int workspaceId,
        int inviteLinkId,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

        var invite = await db.WorkspaceInviteLinks.FindAsync(new object[] { inviteLinkId }, ct)
            ?? throw new GraphQLException("Invite link not found");

        if (invite.WorkspaceId != workspaceId)
            throw new GraphQLException("Invite link does not belong to this workspace");

        db.WorkspaceInviteLinks.Remove(invite);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Replace all Vercel project mappings for a project's workspace with the provided list.
    /// </summary>
    public async Task<bool> UpdateVercelProjectMappings(
        [ID] int projectId,
        List<VercelProjectMappingInput> mappings,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireProjectAccess(claimsPrincipal, projectId, authz, ct);

        var project = await db.Projects.FindAsync(new object[] { projectId }, ct)
            ?? throw new GraphQLException("Project not found");

        var existing = await db.VercelIntegrationConfigs
            .Where(v => v.WorkspaceId == project.WorkspaceId)
            .ToListAsync(ct);
        db.VercelIntegrationConfigs.RemoveRange(existing);

        foreach (var m in mappings.Where(m => m.ProjectId.HasValue))
        {
            db.VercelIntegrationConfigs.Add(new VercelIntegrationConfig
            {
                WorkspaceId = project.WorkspaceId,
                ProjectId = m.ProjectId!.Value,
                VercelProjectId = m.VercelProjectId,
            });
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Replace all ClickUp project mappings for a workspace with the provided list.
    /// </summary>
    public async Task<bool> UpdateClickUpProjectMappings(
        [ID] int workspaceId,
        List<ClickUpProjectMappingInput> mappings,
        ClaimsPrincipal claimsPrincipal,
        [Service] IAuthorizationService authz,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        await AuthHelper.RequireWorkspaceAccess(claimsPrincipal, workspaceId, authz, ct);

        // Delete all ClickUp mappings for projects belonging to this workspace
        var workspaceProjectIds = await db.Projects
            .Where(p => p.WorkspaceId == workspaceId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var existing = await db.IntegrationProjectMappings
            .Where(m => m.IntegrationType == "ClickUp" && workspaceProjectIds.Contains(m.ProjectId))
            .ToListAsync(ct);
        db.IntegrationProjectMappings.RemoveRange(existing);

        foreach (var m in mappings)
        {
            db.IntegrationProjectMappings.Add(new IntegrationProjectMapping
            {
                IntegrationType = "ClickUp",
                ProjectId = m.ProjectId,
                ExternalId = m.ClickUpSpaceId,
            });
        }

        await db.SaveChangesAsync(ct);
        return true;
    }
}
