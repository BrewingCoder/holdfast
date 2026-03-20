using System.Security.Claims;
using HoldFast.Domain.Entities;

namespace HoldFast.Shared.Auth;

/// <summary>
/// Core auth service for token generation, validation, and authorization checks.
/// Mirrors the Go backend's auth_client.go + resolver authorization methods.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Generate a JWT token for an admin.
    /// </summary>
    string GenerateToken(Admin admin);

    /// <summary>
    /// Validate a token string and return claims, or null if invalid/expired.
    /// </summary>
    ClaimsPrincipal? ValidateToken(string token);

    /// <summary>
    /// Extract the admin UID from a ClaimsPrincipal.
    /// </summary>
    string? GetUid(ClaimsPrincipal principal);

    /// <summary>
    /// Extract the admin email from a ClaimsPrincipal.
    /// </summary>
    string? GetEmail(ClaimsPrincipal principal);
}
