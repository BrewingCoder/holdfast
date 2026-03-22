using HoldFast.Data;
using HoldFast.Domain;
using HoldFast.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.Shared.Auth;

/// <summary>
/// EF Core-backed authorization service.
/// Mirrors Go's isUserInWorkspace, isUserInProject, getCurrentAdmin, getAdminRole, validateAdminRole.
/// </summary>
public class AuthorizationService : IAuthorizationService
{
    private readonly HoldFastDbContext _db;

    public AuthorizationService(HoldFastDbContext db)
    {
        _db = db;
    }

    public async Task<Admin> GetCurrentAdminAsync(string uid, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(uid))
            throw AuthErrors.AuthenticationError;

        var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Uid == uid, ct);
        if (admin == null)
        {
            // Auto-create admin on first login (matching Go behavior)
            admin = new Admin { Uid = uid };
            _db.Admins.Add(admin);
            await _db.SaveChangesAsync(ct);
        }

        return admin;
    }

    public async Task<Workspace> IsAdminInWorkspaceAsync(int adminId, int workspaceId, CancellationToken ct = default)
    {
        // Check if admin is a member of this workspace (any role)
        var membership = await _db.WorkspaceAdmins
            .FirstOrDefaultAsync(wa => wa.AdminId == adminId && wa.WorkspaceId == workspaceId, ct);

        if (membership == null)
            throw AuthErrors.AuthorizationError;

        var workspace = await _db.Workspaces.FindAsync([workspaceId], ct)
            ?? throw AuthErrors.AuthorizationError;

        return workspace;
    }

    public async Task<Workspace> IsAdminInWorkspaceFullAccessAsync(int adminId, int workspaceId, CancellationToken ct = default)
    {
        // Check admin has ADMIN role OR null ProjectIds (full workspace access)
        // Mirrors Go: wa.Role == 'ADMIN' OR wa.ProjectIds IS NULL
        var membership = await _db.WorkspaceAdmins
            .FirstOrDefaultAsync(wa =>
                wa.AdminId == adminId &&
                wa.WorkspaceId == workspaceId &&
                (wa.Role == WorkspaceRoles.Admin || wa.ProjectIds == null),
                ct);

        if (membership == null)
            throw AuthErrors.AuthorizationError;

        var workspace = await _db.Workspaces.FindAsync([workspaceId], ct)
            ?? throw AuthErrors.AuthorizationError;

        return workspace;
    }

    public async Task<Project> IsAdminInProjectAsync(int adminId, int projectId, CancellationToken ct = default)
    {
        var project = await _db.Projects.FindAsync([projectId], ct)
            ?? throw AuthErrors.AuthorizationError;

        // Check workspace membership
        var membership = await _db.WorkspaceAdmins
            .FirstOrDefaultAsync(wa =>
                wa.AdminId == adminId &&
                wa.WorkspaceId == project.WorkspaceId,
                ct);

        if (membership == null)
            throw AuthErrors.AuthorizationError;

        // If ProjectIds is set, check the project is in the allowed list
        if (membership.ProjectIds != null && !membership.ProjectIds.Contains(projectId))
            throw AuthErrors.AuthorizationError;

        return project;
    }

    public async Task<(string Role, List<int>? ProjectIds)?> GetAdminRoleAsync(
        int adminId, int workspaceId, CancellationToken ct = default)
    {
        var membership = await _db.WorkspaceAdmins
            .FirstOrDefaultAsync(wa => wa.AdminId == adminId && wa.WorkspaceId == workspaceId, ct);

        if (membership == null)
            return null;

        return (membership.Role ?? WorkspaceRoles.Member, membership.ProjectIds);
    }

    public async Task ValidateAdminRoleAsync(int adminId, int workspaceId, CancellationToken ct = default)
    {
        var role = await GetAdminRoleAsync(adminId, workspaceId, ct);
        if (role == null || role.Value.Role != WorkspaceRoles.Admin)
            throw AuthErrors.AuthorizationError;
    }
}
