using System.Security.Claims;
using HoldFast.Domain.Entities;
using Microsoft.Extensions.Options;

namespace HoldFast.Shared.Auth;

/// <summary>
/// No-op auth for development/demo. All requests are treated as demo@example.com.
/// Mirrors Go's SimpleAuthClient.
/// </summary>
public class SimpleAuthService : IAuthService
{
    private const string DemoUid = "demo";
    private const string DemoEmail = "demo@example.com";

    public SimpleAuthService(IOptions<AuthOptions> _) { }

    public string GenerateToken(Admin admin) => "simple-demo-token";

    public ClaimsPrincipal? ValidateToken(string token)
    {
        // Simple mode: any non-empty token is valid
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var claims = new[]
        {
            new Claim(HoldFastClaimTypes.Uid, DemoUid),
            new Claim(HoldFastClaimTypes.Email, DemoEmail),
            new Claim(HoldFastClaimTypes.AdminId, "1"),
        };

        var identity = new ClaimsIdentity(claims, "Simple");
        return new ClaimsPrincipal(identity);
    }

    public string? GetUid(ClaimsPrincipal principal) =>
        principal.FindFirst(HoldFastClaimTypes.Uid)?.Value ?? DemoUid;

    public string? GetEmail(ClaimsPrincipal principal) =>
        principal.FindFirst(HoldFastClaimTypes.Email)?.Value ?? DemoEmail;
}
