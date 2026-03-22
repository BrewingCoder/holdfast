using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoldFast.Shared.Auth;

/// <summary>
/// ASP.NET Core middleware that extracts auth tokens from cookies/headers
/// and populates HttpContext.User with claims.
/// Mirrors Go's PrivateMiddleware in middleware.go.
/// </summary>
public class AuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IAuthService _authService;
    private readonly AuthOptions _options;
    private readonly ILogger<AuthMiddleware> _logger;

    public AuthMiddleware(
        RequestDelegate next,
        IAuthService authService,
        IOptions<AuthOptions> options,
        ILogger<AuthMiddleware> logger)
    {
        _next = next;
        _authService = authService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip auth for health checks, public endpoint, and login
        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/health") || path.StartsWith("/public") || path.StartsWith("/auth"))
        {
            await _next(context);
            return;
        }

        // Try to extract token from (in priority order):
        // 1. Token header
        // 2. Token cookie
        // 3. Authorization: Bearer header
        var token = GetTokenFromHeader(context)
                    ?? GetTokenFromCookie(context)
                    ?? GetTokenFromBearerHeader(context);

        if (!string.IsNullOrWhiteSpace(token))
        {
            var principal = _authService.ValidateToken(token);
            if (principal != null)
            {
                context.User = principal;
                _logger.LogDebug("Authenticated user {Uid}", _authService.GetUid(principal));
            }
        }

        await _next(context);
    }

    private string? GetTokenFromHeader(HttpContext context)
    {
        return context.Request.Headers.TryGetValue(_options.TokenCookieName, out var value)
            ? value.ToString()
            : null;
    }

    private string? GetTokenFromCookie(HttpContext context)
    {
        return context.Request.Cookies.TryGetValue(_options.TokenCookieName, out var value)
            ? value
            : null;
    }

    private static string? GetTokenFromBearerHeader(HttpContext context)
    {
        var auth = context.Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth["Bearer ".Length..].Trim();
        return null;
    }
}
