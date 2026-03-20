using System.Security.Claims;
using HoldFast.Domain.Entities;
using HoldFast.Shared.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HoldFast.Shared.Tests.Auth;

public class AuthMiddlewareTests
{
    private static AuthOptions DefaultOptions => new();

    private static JwtAuthService CreateJwtService(AuthOptions? options = null) =>
        new(Options.Create(options ?? DefaultOptions));

    private static AuthMiddleware CreateMiddleware(
        RequestDelegate next,
        IAuthService? authService = null,
        AuthOptions? options = null)
    {
        return new AuthMiddleware(
            next,
            authService ?? CreateJwtService(options),
            Options.Create(options ?? DefaultOptions),
            NullLogger<AuthMiddleware>.Instance);
    }

    private static DefaultHttpContext CreateContext(string path = "/private", Action<DefaultHttpContext>? configure = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        configure?.Invoke(context);
        return context;
    }

    // ── Path skipping ───────────────────────────────────────────────

    [Fact]
    public async Task SkipsAuth_ForHealthEndpoint()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        var context = CreateContext("/health");
        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task SkipsAuth_ForPublicEndpoint()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        var context = CreateContext("/public");
        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task SkipsAuth_ForAuthEndpoint()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        var context = CreateContext("/auth/login");
        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/public")]
    [InlineData("/public/graphql")]
    [InlineData("/auth/login")]
    [InlineData("/auth/logout")]
    public async Task SkipsAuth_ForAllSkippedPaths(string path)
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = CreateContext(path);
        await middleware.InvokeAsync(context);
        // Should not throw or set user
    }

    // ── Token from header ───────────────────────────────────────────

    [Fact]
    public async Task ExtractsToken_FromTokenHeader()
    {
        var authService = CreateJwtService();
        var admin = new Admin { Id = 1, Uid = "header-uid", Email = "a@b.com" };
        var token = authService.GenerateToken(admin);

        var middleware = CreateMiddleware(_ => Task.CompletedTask, authService);

        var context = CreateContext("/private", ctx =>
            ctx.Request.Headers["token"] = token);

        await middleware.InvokeAsync(context);

        Assert.True(context.User.Identity?.IsAuthenticated);
        Assert.Equal("header-uid", context.User.FindFirst(HoldFastClaimTypes.Uid)?.Value);
    }

    // ── Token from cookie ───────────────────────────────────────────

    [Fact]
    public async Task ExtractsToken_FromCookie()
    {
        var authService = CreateJwtService();
        var admin = new Admin { Id = 1, Uid = "cookie-uid", Email = "a@b.com" };
        var token = authService.GenerateToken(admin);

        var middleware = CreateMiddleware(_ => Task.CompletedTask, authService);

        var context = CreateContext("/private", ctx =>
            ctx.Request.Headers["Cookie"] = $"token={token}");

        await middleware.InvokeAsync(context);

        Assert.True(context.User.Identity?.IsAuthenticated);
    }

    // ── Token from Bearer header ────────────────────────────────────

    [Fact]
    public async Task ExtractsToken_FromBearerHeader()
    {
        var authService = CreateJwtService();
        var admin = new Admin { Id = 1, Uid = "bearer-uid", Email = "a@b.com" };
        var token = authService.GenerateToken(admin);

        var middleware = CreateMiddleware(_ => Task.CompletedTask, authService);

        var context = CreateContext("/private", ctx =>
            ctx.Request.Headers.Authorization = $"Bearer {token}");

        await middleware.InvokeAsync(context);

        Assert.True(context.User.Identity?.IsAuthenticated);
        Assert.Equal("bearer-uid", context.User.FindFirst(HoldFastClaimTypes.Uid)?.Value);
    }

    // ── Invalid/missing tokens ──────────────────────────────────────

    [Fact]
    public async Task NoToken_ContinuesWithoutAuth()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        var context = CreateContext("/private");
        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task InvalidToken_ContinuesWithoutAuth()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        var context = CreateContext("/private", ctx =>
            ctx.Request.Headers["token"] = "totally-invalid-jwt");

        await middleware.InvokeAsync(context);

        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task ExpiredToken_ContinuesWithoutAuth()
    {
        var options = new AuthOptions { TokenExpiry = TimeSpan.FromMinutes(-5) };
        var authService = CreateJwtService(options);
        var token = authService.GenerateToken(new Admin { Id = 1, Uid = "expired-uid" });

        // Validate with normal service (which checks expiry)
        var normalService = CreateJwtService();
        var middleware = CreateMiddleware(_ => Task.CompletedTask, normalService);

        var context = CreateContext("/private", ctx =>
            ctx.Request.Headers["token"] = token);

        await middleware.InvokeAsync(context);

        // Expired token should not authenticate
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
    }

    // ── Priority order ──────────────────────────────────────────────

    [Fact]
    public async Task TokenHeader_TakesPriority_OverBearerHeader()
    {
        var authService = CreateJwtService();
        var admin1 = new Admin { Id = 1, Uid = "header-uid", Email = "a@b.com" };
        var admin2 = new Admin { Id = 2, Uid = "bearer-uid", Email = "b@b.com" };
        var headerToken = authService.GenerateToken(admin1);
        var bearerToken = authService.GenerateToken(admin2);

        var middleware = CreateMiddleware(_ => Task.CompletedTask, authService);

        var context = CreateContext("/private", ctx =>
        {
            ctx.Request.Headers["token"] = headerToken;
            ctx.Request.Headers.Authorization = $"Bearer {bearerToken}";
        });

        await middleware.InvokeAsync(context);

        Assert.Equal("header-uid", context.User.FindFirst(HoldFastClaimTypes.Uid)?.Value);
    }

    // ── Custom cookie name ──────────────────────────────────────────

    [Fact]
    public async Task CustomCookieName_ExtractsFromCorrectHeader()
    {
        var options = new AuthOptions { TokenCookieName = "my-custom-token" };
        var authService = CreateJwtService(options);
        var token = authService.GenerateToken(new Admin { Id = 1, Uid = "custom-uid", Email = "a@b.com" });

        var middleware = CreateMiddleware(_ => Task.CompletedTask, authService, options);

        var context = CreateContext("/private", ctx =>
            ctx.Request.Headers["my-custom-token"] = token);

        await middleware.InvokeAsync(context);

        Assert.True(context.User.Identity?.IsAuthenticated);
    }

    // ── Next always called ──────────────────────────────────────────

    [Fact]
    public async Task AlwaysCallsNext_EvenOnAuthFailure()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        var context = CreateContext("/private", ctx =>
            ctx.Request.Headers["token"] = "garbage");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }
}
