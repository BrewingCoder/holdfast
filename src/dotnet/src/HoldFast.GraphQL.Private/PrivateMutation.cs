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

        // Log activity
        db.ErrorGroupActivityLogs.Add(new ErrorGroupActivityLog
        {
            ErrorGroupId = errorGroupId,
            Action = state.ToString(),
        });
        await db.SaveChangesAsync(ct);

        return errorGroup;
    }

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
}
