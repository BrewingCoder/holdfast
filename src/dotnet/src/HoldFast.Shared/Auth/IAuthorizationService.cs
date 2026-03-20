using HoldFast.Domain.Entities;

namespace HoldFast.Shared.Auth;

/// <summary>
/// Authorization checks mirroring Go's isUserInWorkspace/isUserInProject.
/// Separated from IAuthService so resolvers can inject just what they need.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Get the current admin from the request context. Creates if not found (matching Go behavior).
    /// Throws AuthenticationError if no valid auth context.
    /// </summary>
    Task<Admin> GetCurrentAdminAsync(string uid, CancellationToken ct = default);

    /// <summary>
    /// Verify admin is a member of the workspace (any role).
    /// Returns the workspace if authorized. Throws AuthorizationError otherwise.
    /// </summary>
    Task<Workspace> IsAdminInWorkspaceAsync(int adminId, int workspaceId, CancellationToken ct = default);

    /// <summary>
    /// Verify admin is an ADMIN (not MEMBER) in the workspace, or has full access (null ProjectIds).
    /// Returns the workspace. Throws AuthorizationError otherwise.
    /// </summary>
    Task<Workspace> IsAdminInWorkspaceFullAccessAsync(int adminId, int workspaceId, CancellationToken ct = default);

    /// <summary>
    /// Verify admin has access to the project (via workspace membership + project ID list).
    /// Returns the project if authorized. Throws AuthorizationError otherwise.
    /// </summary>
    Task<Project> IsAdminInProjectAsync(int adminId, int projectId, CancellationToken ct = default);

    /// <summary>
    /// Get the admin's role in a workspace.
    /// Returns (role, projectIds) or null if not a member.
    /// </summary>
    Task<(string Role, List<int>? ProjectIds)?> GetAdminRoleAsync(int adminId, int workspaceId, CancellationToken ct = default);

    /// <summary>
    /// Validate admin has ADMIN role in workspace. Throws AuthorizationError if not.
    /// </summary>
    Task ValidateAdminRoleAsync(int adminId, int workspaceId, CancellationToken ct = default);
}
