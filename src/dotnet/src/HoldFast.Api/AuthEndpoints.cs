using HoldFast.Data;
using HoldFast.Shared.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HoldFast.Api;

/// <summary>
/// REST endpoints for authentication (login, logout, whoami).
/// Maps to /auth/* — separate from GraphQL.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>
    /// Register /auth/login, /auth/logout, and /auth/whoami endpoints.
    /// </summary>
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/auth").RequireCors("Private");

        auth.MapPost("/login", Login);
        auth.MapPost("/logout", Logout);
        auth.MapGet("/whoami", WhoAmI);

        // Also register at /private/* to match the frontend's expected URL shape.
        // These must be registered before the GraphQL middleware captures /private.
        var priv = app.MapGroup("/private").RequireCors("Private");
        priv.MapPost("/login", Login);
        priv.MapGet("/validate-token", WhoAmI);
    }

    /// <summary>
    /// POST /auth/login — authenticate with email + password (Password mode)
    /// or just email (Simple mode). Returns JWT token as cookie + JSON.
    /// </summary>
    private static async Task<IResult> Login(
        LoginRequest request,
        IAuthService authService,
        IOptions<AuthOptions> authOptions,
        HoldFastDbContext db,
        CancellationToken ct)
    {
        var options = authOptions.Value;

        // Simple mode: any login succeeds
        if (options.Mode.Equals("Simple", StringComparison.OrdinalIgnoreCase))
        {
            var demoAdmin = await db.Admins.FirstOrDefaultAsync(a => a.Uid == "demo", ct);
            if (demoAdmin == null)
            {
                demoAdmin = new HoldFast.Domain.Entities.Admin
                {
                    Uid = "demo",
                    Email = "demo@example.com",
                    Name = "Demo User",
                };
                db.Admins.Add(demoAdmin);
                await db.SaveChangesAsync(ct);
            }

            var demoToken = authService.GenerateToken(demoAdmin);
            return Results.Ok(new LoginResponse(demoToken, new LoginUserInfo(demoAdmin.Uid ?? "demo", demoAdmin.Email ?? "demo@example.com")));
        }

        // Password mode: validate password
        if (string.IsNullOrEmpty(request.Email))
            return Results.BadRequest(new { error = "Email is required" });

        if (string.IsNullOrEmpty(options.AdminPassword))
            return Results.Problem("Admin password not configured. Set Auth:AdminPassword in configuration.");

        if (request.Password != options.AdminPassword)
            return Results.Unauthorized();

        // Find or create admin
        var admin = await db.Admins.FirstOrDefaultAsync(a => a.Email == request.Email, ct);
        if (admin == null)
        {
            admin = new HoldFast.Domain.Entities.Admin
            {
                Uid = Guid.NewGuid().ToString(),
                Email = request.Email,
                Name = request.Email.Split('@')[0],
                EmailVerified = true,
            };
            db.Admins.Add(admin);
            await db.SaveChangesAsync(ct);
        }

        var token = authService.GenerateToken(admin);

        return Results.Ok(new LoginResponse(token, new LoginUserInfo(admin.Uid ?? admin.Email!, admin.Email!)));
    }

    /// <summary>
    /// POST /auth/logout — clear token cookie.
    /// </summary>
    private static IResult Logout(
        HttpContext context,
        IOptions<AuthOptions> authOptions)
    {
        context.Response.Cookies.Delete(authOptions.Value.TokenCookieName);
        return Results.Ok(new { message = "Logged out" });
    }

    /// <summary>
    /// GET /auth/whoami — return current authenticated admin info.
    /// </summary>
    private static async Task<IResult> WhoAmI(
        HttpContext context,
        IAuthService authService,
        HoldFast.Shared.Auth.IAuthorizationService authz,
        CancellationToken ct)
    {
        var uid = authService.GetUid(context.User);
        if (string.IsNullOrEmpty(uid))
            return Results.Unauthorized();

        try
        {
            var admin = await authz.GetCurrentAdminAsync(uid, ct);
            return Results.Ok(new { admin.Id, admin.Uid, admin.Email, admin.Name });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
    }

    /// <summary>Request body for the /auth/login endpoint.</summary>
    public record LoginRequest(string? Email, string? Password);
    /// <summary>User object nested inside LoginResponse — matches the shape the frontend expects.</summary>
    public record LoginUserInfo(string Uid, string Email);
    /// <summary>Response body from /auth/login — {token, user: {uid, email}}.</summary>
    public record LoginResponse(string Token, LoginUserInfo User);
}
