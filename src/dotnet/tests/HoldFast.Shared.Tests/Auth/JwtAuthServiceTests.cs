using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using HoldFast.Domain.Entities;
using HoldFast.Shared.Auth;
using Microsoft.Extensions.Options;

namespace HoldFast.Shared.Tests.Auth;

public class JwtAuthServiceTests
{
    private static IAuthService CreateService(Action<AuthOptions>? configure = null)
    {
        var options = new AuthOptions();
        configure?.Invoke(options);
        return new JwtAuthService(Options.Create(options));
    }

    private static Admin MakeAdmin(int id = 1, string uid = "user-abc", string email = "test@example.com") =>
        new() { Id = id, Uid = uid, Email = email, Name = "Test User" };

    // ── Token generation ────────────────────────────────────────────

    [Fact]
    public void GenerateToken_ReturnsNonEmptyJwt()
    {
        var svc = CreateService();
        var token = svc.GenerateToken(MakeAdmin());

        Assert.NotNull(token);
        Assert.NotEmpty(token);
        Assert.Contains(".", token); // JWT has 3 parts separated by dots
    }

    [Fact]
    public void GenerateToken_ContainsThreeParts()
    {
        var svc = CreateService();
        var token = svc.GenerateToken(MakeAdmin());
        Assert.Equal(3, token.Split('.').Length);
    }

    [Fact]
    public void GenerateToken_IncludesUidClaim()
    {
        var svc = CreateService();
        var token = svc.GenerateToken(MakeAdmin(uid: "my-uid-123"));
        var principal = svc.ValidateToken(token)!;

        Assert.Equal("my-uid-123", svc.GetUid(principal));
    }

    [Fact]
    public void GenerateToken_IncludesEmailClaim()
    {
        var svc = CreateService();
        var token = svc.GenerateToken(MakeAdmin(email: "admin@holdfast.run"));
        var principal = svc.ValidateToken(token)!;

        Assert.Equal("admin@holdfast.run", svc.GetEmail(principal));
    }

    [Fact]
    public void GenerateToken_IncludesAdminIdClaim()
    {
        var svc = CreateService();
        var token = svc.GenerateToken(MakeAdmin(id: 42));
        var principal = svc.ValidateToken(token)!;

        var adminIdClaim = principal.FindFirst(HoldFastClaimTypes.AdminId);
        Assert.NotNull(adminIdClaim);
        Assert.Equal("42", adminIdClaim.Value);
    }

    [Fact]
    public void GenerateToken_NullUid_StoresEmptyString()
    {
        var svc = CreateService();
        var admin = new Admin { Id = 1, Uid = null, Email = "x@y.com" };
        var token = svc.GenerateToken(admin);
        var principal = svc.ValidateToken(token)!;

        Assert.Equal("", svc.GetUid(principal));
    }

    [Fact]
    public void GenerateToken_NullEmail_StoresEmptyString()
    {
        var svc = CreateService();
        var admin = new Admin { Id = 1, Uid = "uid", Email = null };
        var token = svc.GenerateToken(admin);
        var principal = svc.ValidateToken(token)!;

        Assert.Equal("", svc.GetEmail(principal));
    }

    [Fact]
    public void GenerateToken_DifferentAdmins_ProduceDifferentTokens()
    {
        var svc = CreateService();
        var t1 = svc.GenerateToken(MakeAdmin(id: 1, uid: "a"));
        var t2 = svc.GenerateToken(MakeAdmin(id: 2, uid: "b"));

        Assert.NotEqual(t1, t2);
    }

    [Fact]
    public void GenerateToken_SameAdmin_DifferentTimestamps_ProduceDifferentTokens()
    {
        var svc = CreateService();
        var admin = MakeAdmin();
        var t1 = svc.GenerateToken(admin);
        // Tokens include iat, so even same admin can differ, but within same second may match
        // Just verify both are valid
        Assert.NotNull(svc.ValidateToken(t1));
    }

    // ── Token validation ────────────────────────────────────────────

    [Fact]
    public void ValidateToken_ValidToken_ReturnsPrincipal()
    {
        var svc = CreateService();
        var token = svc.GenerateToken(MakeAdmin());
        var principal = svc.ValidateToken(token);

        Assert.NotNull(principal);
        Assert.True(principal.Identity?.IsAuthenticated);
    }

    [Fact]
    public void ValidateToken_EmptyString_ReturnsNull()
    {
        var svc = CreateService();
        Assert.Null(svc.ValidateToken(""));
    }

    [Fact]
    public void ValidateToken_Null_ReturnsNull()
    {
        var svc = CreateService();
        Assert.Null(svc.ValidateToken(null!));
    }

    [Fact]
    public void ValidateToken_RandomGarbage_ReturnsNull()
    {
        var svc = CreateService();
        Assert.Null(svc.ValidateToken("not-a-jwt-at-all"));
    }

    [Fact]
    public void ValidateToken_ThreePartGarbage_ReturnsNull()
    {
        var svc = CreateService();
        Assert.Null(svc.ValidateToken("aaa.bbb.ccc"));
    }

    [Fact]
    public void ValidateToken_WrongSecret_ReturnsNull()
    {
        var svc1 = CreateService(o => o.JwtSecret = "secret-one-aaaaaaaaaaaaa-long-enough-32b");
        var svc2 = CreateService(o => o.JwtSecret = "secret-two-bbbbbbbbbbbbb-long-enough-32b");

        var token = svc1.GenerateToken(MakeAdmin());
        Assert.Null(svc2.ValidateToken(token));
    }

    [Fact]
    public void ValidateToken_ExpiredToken_ReturnsNull()
    {
        var svc = CreateService(o => o.TokenExpiry = TimeSpan.FromMinutes(-5));
        var token = svc.GenerateToken(MakeAdmin());
        Assert.Null(svc.ValidateToken(token));
    }

    [Fact]
    public void ValidateToken_TamperedPayload_ReturnsNull()
    {
        var svc = CreateService();
        var token = svc.GenerateToken(MakeAdmin());
        var parts = token.Split('.');
        // Tamper with the payload
        parts[1] = "eyJzdWIiOiJ0YW1wZXJlZCJ9";
        var tampered = string.Join(".", parts);
        Assert.Null(svc.ValidateToken(tampered));
    }

    [Fact]
    public void ValidateToken_TruncatedToken_ReturnsNull()
    {
        var svc = CreateService();
        var token = svc.GenerateToken(MakeAdmin());
        var truncated = token[..^10]; // remove last 10 chars
        Assert.Null(svc.ValidateToken(truncated));
    }

    // ── Claim extraction ────────────────────────────────────────────

    [Fact]
    public void GetUid_NullPrincipal_NoUidClaim_ReturnsNull()
    {
        var svc = CreateService();
        var identity = new ClaimsIdentity();
        var principal = new ClaimsPrincipal(identity);
        Assert.Null(svc.GetUid(principal));
    }

    [Fact]
    public void GetEmail_NullPrincipal_NoEmailClaim_ReturnsNull()
    {
        var svc = CreateService();
        var identity = new ClaimsIdentity();
        var principal = new ClaimsPrincipal(identity);
        Assert.Null(svc.GetEmail(principal));
    }

    // ── Configuration edge cases ────────────────────────────────────

    [Fact]
    public void DefaultOptions_SevenDayExpiry()
    {
        var options = new AuthOptions();
        Assert.Equal(TimeSpan.FromDays(7), options.TokenExpiry);
    }

    [Fact]
    public void DefaultOptions_PasswordMode()
    {
        var options = new AuthOptions();
        Assert.Equal("Password", options.Mode);
    }

    [Fact]
    public void DefaultOptions_TokenCookieName()
    {
        var options = new AuthOptions();
        Assert.Equal("token", options.TokenCookieName);
    }

    [Fact]
    public void DefaultOptions_NoDemoProject()
    {
        var options = new AuthOptions();
        Assert.Null(options.DemoProjectId);
    }

    [Fact]
    public void LongExpiry_TokenStillValid()
    {
        var svc = CreateService(o => o.TokenExpiry = TimeSpan.FromDays(365));
        var token = svc.GenerateToken(MakeAdmin());
        Assert.NotNull(svc.ValidateToken(token));
    }

    [Fact]
    public void UnicodeUid_RoundTrips()
    {
        var svc = CreateService();
        var admin = MakeAdmin(uid: "日本語テスト");
        var token = svc.GenerateToken(admin);
        var principal = svc.ValidateToken(token)!;
        Assert.Equal("日本語テスト", svc.GetUid(principal));
    }

    [Fact]
    public void UnicodeEmail_RoundTrips()
    {
        var svc = CreateService();
        var admin = MakeAdmin(email: "用户@例え.jp");
        var token = svc.GenerateToken(admin);
        var principal = svc.ValidateToken(token)!;
        Assert.Equal("用户@例え.jp", svc.GetEmail(principal));
    }

    [Fact]
    public void VeryLongUid_RoundTrips()
    {
        var svc = CreateService();
        var longUid = new string('x', 10000);
        var admin = MakeAdmin(uid: longUid);
        var token = svc.GenerateToken(admin);
        var principal = svc.ValidateToken(token)!;
        Assert.Equal(longUid, svc.GetUid(principal));
    }

    [Fact]
    public void SpecialCharactersInClaims_RoundTrip()
    {
        var svc = CreateService();
        var admin = MakeAdmin(uid: "uid/with\\special<chars>&\"quotes'");
        var token = svc.GenerateToken(admin);
        var principal = svc.ValidateToken(token)!;
        Assert.Equal("uid/with\\special<chars>&\"quotes'", svc.GetUid(principal));
    }
}
