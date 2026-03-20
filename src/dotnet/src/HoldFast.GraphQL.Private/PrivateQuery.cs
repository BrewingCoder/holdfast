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
    /// <summary>
    /// Get a workspace by ID.
    /// </summary>
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
    /// Get a project by ID.
    /// </summary>
    [UseProjection]
    public async Task<Project?> GetProject(
        int id,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.Projects
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    /// <summary>
    /// List all projects in a workspace.
    /// </summary>
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
    /// Get an error group by ID.
    /// </summary>
    [UseProjection]
    public async Task<ErrorGroup?> GetErrorGroup(
        int id,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.ErrorGroups
            .FirstOrDefaultAsync(eg => eg.Id == id, ct);
    }

    /// <summary>
    /// List error groups for a project.
    /// </summary>
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
    /// Get a session by secure ID.
    /// </summary>
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
    /// Get workspace settings.
    /// </summary>
    public async Task<AllWorkspaceSettings?> GetWorkspaceSettings(
        int workspaceId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.AllWorkspaceSettings
            .FirstOrDefaultAsync(s => s.WorkspaceId == workspaceId, ct);
    }

    /// <summary>
    /// List alerts for a project.
    /// </summary>
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

    /// <summary>
    /// List dashboards for a project.
    /// </summary>
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

    /// <summary>
    /// Get the current admin by UID (from auth context).
    /// </summary>
    public async Task<Admin?> GetAdmin(
        string uid,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.Admins
            .FirstOrDefaultAsync(a => a.Uid == uid, ct);
    }
}
