using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HotChocolate;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.GraphQL.Private;

/// <summary>
/// Private GraphQL mutations — dashboard API.
/// </summary>
public class PrivateMutation
{
    // ── Workspace ─────────────────────────────────────────────────────

    /// <summary>
    /// Create a new workspace. Self-hosted: always Enterprise tier.
    /// </summary>
    public async Task<Workspace> CreateWorkspace(
        string name,
        int adminId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var admin = await db.Admins.FindAsync([adminId], ct)
            ?? throw new GraphQLException("Admin not found");

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
    /// Edit workspace name.
    /// </summary>
    public async Task<Workspace> EditWorkspace(
        int workspaceId,
        string? name,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var workspace = await db.Workspaces.FindAsync([workspaceId], ct)
            ?? throw new GraphQLException("Workspace not found");

        if (name != null)
            workspace.Name = name;

        await db.SaveChangesAsync(ct);
        return workspace;
    }

    /// <summary>
    /// Edit workspace settings (feature flags).
    /// </summary>
    public async Task<AllWorkspaceSettings> EditWorkspaceSettings(
        int workspaceId,
        bool? aiApplication,
        bool? aiInsights,
        bool? enableSSO,
        bool? enableSessionExport,
        bool? enableNetworkTraces,
        bool? enableDataDeletion,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
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
    /// Create a new project within a workspace.
    /// </summary>
    public async Task<Project> CreateProject(
        int workspaceId,
        string name,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
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
    /// Edit project name and billing email.
    /// </summary>
    public async Task<Project> EditProject(
        int projectId,
        string? name,
        string? billingEmail,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var project = await db.Projects.FindAsync([projectId], ct)
            ?? throw new GraphQLException("Project not found");

        if (name != null) project.Name = name;
        if (billingEmail != null) project.BillingEmail = billingEmail;

        await db.SaveChangesAsync(ct);
        return project;
    }

    /// <summary>
    /// Edit project settings (rage clicks, excluded users, sampling, etc).
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
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
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
    /// Delete a project.
    /// </summary>
    public async Task<bool> DeleteProject(
        int projectId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var project = await db.Projects.FindAsync([projectId], ct)
            ?? throw new GraphQLException("Project not found");

        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Billing / Retention ───────────────────────────────────────────

    /// <summary>
    /// Save retention periods for a workspace.
    /// No billing limits in self-hosted mode — only retention is configurable.
    /// </summary>
    public async Task<bool> SaveBillingPlan(
        int workspaceId,
        RetentionPeriod sessionsRetention,
        RetentionPeriod errorsRetention,
        RetentionPeriod logsRetention,
        RetentionPeriod tracesRetention,
        RetentionPeriod metricsRetention,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
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
    /// Update error group state (open, resolved, ignored).
    /// </summary>
    public async Task<ErrorGroup> UpdateErrorGroupState(
        int errorGroupId,
        ErrorGroupState state,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var errorGroup = await db.ErrorGroups.FindAsync([errorGroupId], ct)
            ?? throw new GraphQLException("ErrorGroup not found");

        errorGroup.State = state;
        await db.SaveChangesAsync(ct);

        db.ErrorGroupActivityLogs.Add(new ErrorGroupActivityLog
        {
            ErrorGroupId = errorGroupId,
            Action = state.ToString(),
        });
        await db.SaveChangesAsync(ct);

        return errorGroup;
    }

    /// <summary>
    /// Update whether an error group is publicly visible.
    /// </summary>
    public async Task<ErrorGroup> UpdateErrorGroupIsPublic(
        int errorGroupId,
        bool isPublic,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var errorGroup = await db.ErrorGroups.FindAsync([errorGroupId], ct)
            ?? throw new GraphQLException("ErrorGroup not found");

        errorGroup.IsPublic = isPublic;
        await db.SaveChangesAsync(ct);
        return errorGroup;
    }

    // ── Comments ──────────────────────────────────────────────────────

    /// <summary>
    /// Create a session comment.
    /// </summary>
    public async Task<SessionComment> CreateSessionComment(
        int projectId,
        int sessionId,
        int adminId,
        string text,
        int timestamp,
        double xCoordinate,
        double yCoordinate,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var comment = new SessionComment
        {
            ProjectId = projectId,
            SessionId = sessionId,
            AdminId = adminId,
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
    /// Delete a session comment.
    /// </summary>
    public async Task<bool> DeleteSessionComment(
        int commentId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var comment = await db.SessionComments.FindAsync([commentId], ct)
            ?? throw new GraphQLException("Comment not found");

        db.SessionComments.Remove(comment);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Reply to a session comment.
    /// </summary>
    public async Task<CommentReply> ReplyToSessionComment(
        int sessionCommentId,
        int adminId,
        string text,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var reply = new CommentReply
        {
            SessionCommentId = sessionCommentId,
            AdminId = adminId,
            Text = text,
        };

        db.CommentReplies.Add(reply);
        await db.SaveChangesAsync(ct);
        return reply;
    }

    // ── Alerts ────────────────────────────────────────────────────────

    /// <summary>
    /// Create or update an alert.
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
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
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
    /// Update alert disabled state.
    /// </summary>
    public async Task<bool> UpdateAlertDisabled(
        int alertId,
        bool disabled,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var alert = await db.Alerts.FindAsync([alertId], ct)
            ?? throw new GraphQLException("Alert not found");

        alert.Disabled = disabled;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Delete an alert.
    /// </summary>
    public async Task<bool> DeleteAlert(
        int alertId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var alert = await db.Alerts.FindAsync([alertId], ct)
            ?? throw new GraphQLException("Alert not found");

        db.Alerts.Remove(alert);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Dashboards ────────────────────────────────────────────────────

    /// <summary>
    /// Create or update a dashboard.
    /// </summary>
    public async Task<Dashboard> UpsertDashboard(
        int projectId,
        string name,
        int? dashboardId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
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
    /// Delete a dashboard.
    /// </summary>
    public async Task<bool> DeleteDashboard(
        int dashboardId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var dashboard = await db.Dashboards.FindAsync([dashboardId], ct)
            ?? throw new GraphQLException("Dashboard not found");

        db.Dashboards.Remove(dashboard);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Saved Segments ────────────────────────────────────────────────

    /// <summary>
    /// Create a saved segment.
    /// </summary>
    public async Task<SavedSegment> CreateSavedSegment(
        int projectId,
        string name,
        string entityType,
        string? @params,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
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
    /// Edit a saved segment.
    /// </summary>
    public async Task<SavedSegment> EditSavedSegment(
        int segmentId,
        string? name,
        string? @params,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var segment = await db.SavedSegments.FindAsync([segmentId], ct)
            ?? throw new GraphQLException("Saved segment not found");

        if (name != null) segment.Name = name;
        if (@params != null) segment.Params = @params;

        await db.SaveChangesAsync(ct);
        return segment;
    }

    /// <summary>
    /// Delete a saved segment.
    /// </summary>
    public async Task<bool> DeleteSavedSegment(
        int segmentId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var segment = await db.SavedSegments.FindAsync([segmentId], ct)
            ?? throw new GraphQLException("Saved segment not found");

        db.SavedSegments.Remove(segment);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Admin ─────────────────────────────────────────────────────────

    /// <summary>
    /// Add an admin to a workspace.
    /// </summary>
    public async Task<bool> AddAdminToWorkspace(
        int workspaceId,
        int adminId,
        string role,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
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
    /// Change an admin's role in a workspace.
    /// </summary>
    public async Task<bool> ChangeAdminRole(
        int workspaceId,
        int adminId,
        string newRole,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var wa = await db.WorkspaceAdmins
            .FirstOrDefaultAsync(wa => wa.WorkspaceId == workspaceId && wa.AdminId == adminId, ct)
            ?? throw new GraphQLException("Admin not found in workspace");

        wa.Role = newRole;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Remove an admin from a workspace.
    /// </summary>
    public async Task<bool> DeleteAdminFromWorkspace(
        int workspaceId,
        int adminId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var wa = await db.WorkspaceAdmins
            .FirstOrDefaultAsync(wa => wa.WorkspaceId == workspaceId && wa.AdminId == adminId, ct)
            ?? throw new GraphQLException("Admin not found in workspace");

        db.WorkspaceAdmins.Remove(wa);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Sessions ──────────────────────────────────────────────────────

    /// <summary>
    /// Mark a session as viewed by an admin.
    /// </summary>
    public async Task<bool> MarkSessionAsViewed(
        string secureId,
        int adminId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        var session = await db.Sessions
            .FirstOrDefaultAsync(s => s.SecureId == secureId, ct)
            ?? throw new GraphQLException("Session not found");

        var existing = await db.SessionAdminsViews
            .FirstOrDefaultAsync(v => v.SessionId == session.Id && v.AdminId == adminId, ct);

        if (existing == null)
        {
            db.SessionAdminsViews.Add(new SessionAdminsView
            {
                SessionId = session.Id,
                AdminId = adminId,
            });
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    /// <summary>
    /// Delete sessions matching criteria (for data deletion).
    /// </summary>
    public async Task<bool> DeleteSessions(
        int projectId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        // Create a delete task that will be processed by the worker
        db.DeleteSessionsTasks.Add(new DeleteSessionsTask
        {
            ProjectId = projectId,
        });
        await db.SaveChangesAsync(ct);
        return true;
    }
}
