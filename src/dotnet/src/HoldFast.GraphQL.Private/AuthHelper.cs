using System.Security.Claims;
using HoldFast.Domain.Entities;
using HoldFast.Shared.Auth;
using HotChocolate;

namespace HoldFast.GraphQL.Private;

/// <summary>
/// Helper to extract the current authenticated admin from the GraphQL resolver context.
/// Reduces boilerplate across all private queries/mutations.
/// </summary>
public static class AuthHelper
{
    /// <summary>
    /// Get the current admin from HttpContext.User claims.
    /// Throws GraphQLException if not authenticated.
    /// </summary>
    public static async Task<Admin> GetRequiredAdmin(
        ClaimsPrincipal? user,
        IAuthorizationService authorizationService,
        CancellationToken ct)
    {
        var uid = user?.FindFirst(HoldFastClaimTypes.Uid)?.Value;
        if (string.IsNullOrEmpty(uid))
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Not authenticated")
                    .SetCode("AUTH_NOT_AUTHENTICATED")
                    .Build());

        try
        {
            return await authorizationService.GetCurrentAdminAsync(uid, ct);
        }
        catch (UnauthorizedAccessException)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Not authenticated")
                    .SetCode("AUTH_NOT_AUTHENTICATED")
                    .Build());
        }
    }

    /// <summary>
    /// Verify the current admin has access to a workspace. Returns the admin.
    /// </summary>
    public static async Task<Admin> RequireWorkspaceAccess(
        ClaimsPrincipal? user,
        int workspaceId,
        IAuthorizationService authorizationService,
        CancellationToken ct)
    {
        var admin = await GetRequiredAdmin(user, authorizationService, ct);

        try
        {
            await authorizationService.IsAdminInWorkspaceAsync(admin.Id, workspaceId, ct);
        }
        catch (UnauthorizedAccessException)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Not authorized to access this workspace")
                    .SetCode("AUTH_NOT_AUTHORIZED")
                    .Build());
        }

        return admin;
    }

    /// <summary>
    /// Verify the current admin has ADMIN role in a workspace. Returns the admin.
    /// </summary>
    public static async Task<Admin> RequireWorkspaceAdmin(
        ClaimsPrincipal? user,
        int workspaceId,
        IAuthorizationService authorizationService,
        CancellationToken ct)
    {
        var admin = await GetRequiredAdmin(user, authorizationService, ct);

        try
        {
            await authorizationService.ValidateAdminRoleAsync(admin.Id, workspaceId, ct);
        }
        catch (UnauthorizedAccessException)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Admin role required for this operation")
                    .SetCode("AUTH_ADMIN_REQUIRED")
                    .Build());
        }

        return admin;
    }

    /// <summary>
    /// Verify the current admin has access to a project. Returns the admin.
    /// </summary>
    public static async Task<Admin> RequireProjectAccess(
        ClaimsPrincipal? user,
        int projectId,
        IAuthorizationService authorizationService,
        CancellationToken ct)
    {
        var admin = await GetRequiredAdmin(user, authorizationService, ct);

        try
        {
            await authorizationService.IsAdminInProjectAsync(admin.Id, projectId, ct);
        }
        catch (UnauthorizedAccessException)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Not authorized to access this project")
                    .SetCode("AUTH_NOT_AUTHORIZED")
                    .Build());
        }

        return admin;
    }
}
