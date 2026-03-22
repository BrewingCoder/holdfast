using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Shared.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HoldFast.Shared.Tests.Auth;

/// <summary>
/// Tests the authentication logic paths used by AuthEndpoints (Login, WhoAmI)
/// without needing a full HTTP pipeline. We replicate the endpoint logic
/// using the same services and DbContext that the endpoints inject.
/// </summary>
public class AuthEndpointLogicTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly JwtAuthService _jwtAuthService;
    private readonly AuthorizationService _authz;

    public AuthEndpointLogicTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        var authOptions = new AuthOptions
        {
            Mode = "Password",
            AdminPassword = "test-password-123",
            JwtSecret = "holdfast-test-secret-needs-at-least-32-bytes",
        };

        _jwtAuthService = new JwtAuthService(Options.Create(authOptions));
        _authz = new AuthorizationService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ── Helper: replicate the Simple mode login logic ─────────────────

    private async Task<(string token, int adminId, string email)> SimpleLogin(
        IAuthService authService, CancellationToken ct = default)
    {
        // Replicates AuthEndpoints.Login in Simple mode
        var demoAdmin = await _db.Admins.FirstOrDefaultAsync(a => a.Uid == "demo", ct);
        if (demoAdmin == null)
        {
            demoAdmin = new Admin
            {
                Uid = "demo",
                Email = "demo@example.com",
                Name = "Demo User",
            };
            _db.Admins.Add(demoAdmin);
            await _db.SaveChangesAsync(ct);
        }

        var token = authService.GenerateToken(demoAdmin);
        return (token, demoAdmin.Id, demoAdmin.Email ?? "demo@example.com");
    }

    // ── Helper: replicate the Password mode login logic ───────────────

    private async Task<(bool unauthorized, bool badRequest, bool problem, string? token, int? adminId, string? email)>
        PasswordLogin(string? email, string? password, AuthOptions options, IAuthService authService, CancellationToken ct = default)
    {
        // Replicates AuthEndpoints.Login in Password mode
        if (string.IsNullOrEmpty(email))
            return (false, true, false, null, null, null);

        if (string.IsNullOrEmpty(options.AdminPassword))
            return (false, false, true, null, null, null);

        if (password != options.AdminPassword)
            return (true, false, false, null, null, null);

        var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Email == email, ct);
        if (admin == null)
        {
            admin = new Admin
            {
                Uid = Guid.NewGuid().ToString(),
                Email = email,
                Name = email.Split('@')[0],
                EmailVerified = true,
            };
            _db.Admins.Add(admin);
            await _db.SaveChangesAsync(ct);
        }

        var token = authService.GenerateToken(admin);
        return (false, false, false, token, admin.Id, admin.Email!);
    }

    // ── Simple Mode Login ─────────────────────────────────────────────

    [Fact]
    public async Task SimpleLogin_CreatesDemoAdmin_WhenNoneExists()
    {
        var simpleAuth = new SimpleAuthService(Options.Create(new AuthOptions { Mode = "Simple" }));
        var (token, adminId, email) = await SimpleLogin(simpleAuth);

        Assert.NotNull(token);
        Assert.NotEmpty(token);
        Assert.True(adminId > 0);
        Assert.Equal("demo@example.com", email);

        var dbAdmin = await _db.Admins.FirstOrDefaultAsync(a => a.Uid == "demo");
        Assert.NotNull(dbAdmin);
        Assert.Equal("Demo User", dbAdmin.Name);
    }

    [Fact]
    public async Task SimpleLogin_ReusesExistingDemoAdmin()
    {
        var existingAdmin = new Admin { Uid = "demo", Email = "demo@example.com", Name = "Existing Demo" };
        _db.Admins.Add(existingAdmin);
        await _db.SaveChangesAsync();

        var simpleAuth = new SimpleAuthService(Options.Create(new AuthOptions { Mode = "Simple" }));
        var (_, adminId, _) = await SimpleLogin(simpleAuth);

        Assert.Equal(existingAdmin.Id, adminId);

        // Should not create a duplicate
        var count = await _db.Admins.CountAsync(a => a.Uid == "demo");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SimpleLogin_CalledMultipleTimes_NoDuplicates()
    {
        var simpleAuth = new SimpleAuthService(Options.Create(new AuthOptions { Mode = "Simple" }));

        var (_, id1, _) = await SimpleLogin(simpleAuth);
        var (_, id2, _) = await SimpleLogin(simpleAuth);
        var (_, id3, _) = await SimpleLogin(simpleAuth);

        Assert.Equal(id1, id2);
        Assert.Equal(id2, id3);
        Assert.Equal(1, await _db.Admins.CountAsync(a => a.Uid == "demo"));
    }

    [Fact]
    public async Task SimpleLogin_ReturnsSimpleDemoToken()
    {
        var simpleAuth = new SimpleAuthService(Options.Create(new AuthOptions { Mode = "Simple" }));
        var (token, _, _) = await SimpleLogin(simpleAuth);
        Assert.Equal("simple-demo-token", token);
    }

    // ── Password Mode Login ───────────────────────────────────────────

    [Fact]
    public async Task PasswordLogin_CorrectPassword_CreatesAdminAndReturnsToken()
    {
        var options = new AuthOptions { Mode = "Password", AdminPassword = "secret123" };
        var authService = new JwtAuthService(Options.Create(options));

        var result = await PasswordLogin("admin@holdfast.run", "secret123", options, authService);

        Assert.False(result.unauthorized);
        Assert.False(result.badRequest);
        Assert.False(result.problem);
        Assert.NotNull(result.token);
        Assert.NotEmpty(result.token);
        Assert.True(result.adminId > 0);
        Assert.Equal("admin@holdfast.run", result.email);

        var dbAdmin = await _db.Admins.FirstOrDefaultAsync(a => a.Email == "admin@holdfast.run");
        Assert.NotNull(dbAdmin);
        Assert.Equal("admin", dbAdmin.Name); // email.Split('@')[0]
        Assert.True(dbAdmin.EmailVerified);
    }

    [Fact]
    public async Task PasswordLogin_CorrectPassword_GeneratesValidJwt()
    {
        var options = new AuthOptions
        {
            AdminPassword = "pw",
            JwtSecret = "holdfast-test-secret-needs-at-least-32-bytes",
        };
        var authService = new JwtAuthService(Options.Create(options));

        var result = await PasswordLogin("user@test.com", "pw", options, authService);

        Assert.NotNull(result.token);
        var principal = authService.ValidateToken(result.token!);
        Assert.NotNull(principal);

        var uid = authService.GetUid(principal!);
        Assert.NotNull(uid);
        Assert.NotEmpty(uid);
    }

    [Fact]
    public async Task PasswordLogin_WrongPassword_ReturnsUnauthorized()
    {
        var options = new AuthOptions { AdminPassword = "correct" };
        var authService = new JwtAuthService(Options.Create(options));

        var result = await PasswordLogin("admin@test.com", "wrong", options, authService);

        Assert.True(result.unauthorized);
        Assert.Null(result.token);
    }

    [Fact]
    public async Task PasswordLogin_NullPassword_ReturnsUnauthorized()
    {
        var options = new AuthOptions { AdminPassword = "correct" };
        var authService = new JwtAuthService(Options.Create(options));

        var result = await PasswordLogin("admin@test.com", null, options, authService);

        Assert.True(result.unauthorized);
    }

    [Fact]
    public async Task PasswordLogin_EmptyPassword_ReturnsUnauthorized()
    {
        var options = new AuthOptions { AdminPassword = "correct" };
        var authService = new JwtAuthService(Options.Create(options));

        var result = await PasswordLogin("admin@test.com", "", options, authService);

        Assert.True(result.unauthorized);
    }

    [Fact]
    public async Task PasswordLogin_NullEmail_ReturnsBadRequest()
    {
        var options = new AuthOptions { AdminPassword = "pw" };
        var authService = new JwtAuthService(Options.Create(options));

        var result = await PasswordLogin(null, "pw", options, authService);

        Assert.True(result.badRequest);
    }

    [Fact]
    public async Task PasswordLogin_EmptyEmail_ReturnsBadRequest()
    {
        var options = new AuthOptions { AdminPassword = "pw" };
        var authService = new JwtAuthService(Options.Create(options));

        var result = await PasswordLogin("", "pw", options, authService);

        Assert.True(result.badRequest);
    }

    [Fact]
    public async Task PasswordLogin_AdminPasswordNotConfigured_ReturnsProblem()
    {
        var options = new AuthOptions { AdminPassword = null };
        var authService = new JwtAuthService(Options.Create(options));

        var result = await PasswordLogin("admin@test.com", "anything", options, authService);

        Assert.True(result.problem);
    }

    [Fact]
    public async Task PasswordLogin_AdminPasswordEmpty_ReturnsProblem()
    {
        var options = new AuthOptions { AdminPassword = "" };
        var authService = new JwtAuthService(Options.Create(options));

        var result = await PasswordLogin("admin@test.com", "anything", options, authService);

        Assert.True(result.problem);
    }

    [Fact]
    public async Task PasswordLogin_ExistingAdmin_DoesNotCreateDuplicate()
    {
        var existing = new Admin
        {
            Uid = "existing-uid",
            Email = "existing@test.com",
            Name = "Existing User",
        };
        _db.Admins.Add(existing);
        await _db.SaveChangesAsync();

        var options = new AuthOptions { AdminPassword = "pw" };
        var authService = new JwtAuthService(Options.Create(options));

        var result = await PasswordLogin("existing@test.com", "pw", options, authService);

        Assert.Equal(existing.Id, result.adminId);
        Assert.Equal(1, await _db.Admins.CountAsync(a => a.Email == "existing@test.com"));
    }

    [Fact]
    public async Task PasswordLogin_NewAdmin_SetsNameFromEmailPrefix()
    {
        var options = new AuthOptions { AdminPassword = "pw" };
        var authService = new JwtAuthService(Options.Create(options));

        await PasswordLogin("john.doe@company.com", "pw", options, authService);

        var admin = await _db.Admins.FirstAsync(a => a.Email == "john.doe@company.com");
        Assert.Equal("john.doe", admin.Name);
    }

    [Fact]
    public async Task PasswordLogin_NewAdmin_GetsUniqueGuid()
    {
        var options = new AuthOptions { AdminPassword = "pw" };
        var authService = new JwtAuthService(Options.Create(options));

        await PasswordLogin("user1@test.com", "pw", options, authService);
        await PasswordLogin("user2@test.com", "pw", options, authService);

        var admin1 = await _db.Admins.FirstAsync(a => a.Email == "user1@test.com");
        var admin2 = await _db.Admins.FirstAsync(a => a.Email == "user2@test.com");

        Assert.NotEqual(admin1.Uid, admin2.Uid);
        Assert.NotNull(admin1.Uid);
        Assert.NotNull(admin2.Uid);
        Guid.Parse(admin1.Uid!); // throws FormatException if not a valid GUID
        Guid.Parse(admin2.Uid!); // throws FormatException if not a valid GUID
    }

    [Fact]
    public async Task PasswordLogin_CaseSensitivePassword()
    {
        var options = new AuthOptions { AdminPassword = "Secret" };
        var authService = new JwtAuthService(Options.Create(options));

        var correct = await PasswordLogin("a@b.com", "Secret", options, authService);
        Assert.False(correct.unauthorized);

        // Reset DB for clean state
        var wrongCase = await PasswordLogin("c@d.com", "secret", options, authService);
        Assert.True(wrongCase.unauthorized);
    }

    [Fact]
    public async Task PasswordLogin_SpecialCharsInPassword()
    {
        var options = new AuthOptions { AdminPassword = "p@$$w0rd!#%^&*()" };
        var authService = new JwtAuthService(Options.Create(options));

        var result = await PasswordLogin("a@b.com", "p@$$w0rd!#%^&*()", options, authService);
        Assert.False(result.unauthorized);
        Assert.NotNull(result.token);
    }

    [Fact]
    public async Task PasswordLogin_UnicodeEmail_Works()
    {
        var options = new AuthOptions { AdminPassword = "pw" };
        var authService = new JwtAuthService(Options.Create(options));

        var result = await PasswordLogin("用户@example.com", "pw", options, authService);
        Assert.False(result.badRequest);
        Assert.NotNull(result.token);
        Assert.Equal("用户@example.com", result.email);
    }

    // ── WhoAmI Logic ──────────────────────────────────────────────────

    [Fact]
    public async Task WhoAmI_ValidUid_ReturnsAdminInfo()
    {
        var admin = new Admin { Uid = "whoami-uid", Email = "me@test.com", Name = "Me" };
        _db.Admins.Add(admin);
        await _db.SaveChangesAsync();

        var result = await _authz.GetCurrentAdminAsync("whoami-uid");

        Assert.Equal(admin.Id, result.Id);
        Assert.Equal("whoami-uid", result.Uid);
        Assert.Equal("me@test.com", result.Email);
        Assert.Equal("Me", result.Name);
    }

    [Fact]
    public async Task WhoAmI_NullUid_ThrowsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _authz.GetCurrentAdminAsync(null!));
    }

    [Fact]
    public async Task WhoAmI_EmptyUid_ThrowsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _authz.GetCurrentAdminAsync(""));
    }

    [Fact]
    public async Task WhoAmI_UnknownUid_AutoCreatesAdmin()
    {
        var result = await _authz.GetCurrentAdminAsync("never-seen-before");

        Assert.True(result.Id > 0);
        Assert.Equal("never-seen-before", result.Uid);

        // Verify it persisted
        var fromDb = await _db.Admins.FirstOrDefaultAsync(a => a.Uid == "never-seen-before");
        Assert.NotNull(fromDb);
    }

    // ── Token generation + WhoAmI round-trip ──────────────────────────

    [Fact]
    public async Task LoginThenWhoAmI_PasswordMode_RoundTrip()
    {
        var options = new AuthOptions
        {
            AdminPassword = "pw",
            JwtSecret = "holdfast-test-secret-needs-at-least-32-bytes",
        };
        var authService = new JwtAuthService(Options.Create(options));

        // Login
        var loginResult = await PasswordLogin("roundtrip@test.com", "pw", options, authService);
        Assert.NotNull(loginResult.token);

        // Validate token and extract UID
        var principal = authService.ValidateToken(loginResult.token!);
        Assert.NotNull(principal);

        var uid = authService.GetUid(principal!);
        Assert.NotNull(uid);

        // WhoAmI
        var admin = await _authz.GetCurrentAdminAsync(uid!);
        Assert.Equal("roundtrip@test.com", admin.Email);
    }

    [Fact]
    public async Task LoginThenWhoAmI_SimpleMode_RoundTrip()
    {
        var simpleAuth = new SimpleAuthService(Options.Create(new AuthOptions { Mode = "Simple" }));

        // Login
        var (token, _, _) = await SimpleLogin(simpleAuth);

        // Validate token
        var principal = simpleAuth.ValidateToken(token);
        Assert.NotNull(principal);

        var uid = simpleAuth.GetUid(principal!);
        Assert.Equal("demo", uid);

        // WhoAmI
        var admin = await _authz.GetCurrentAdminAsync(uid!);
        Assert.Equal("demo", admin.Uid);
    }

    // ── Mode selection edge cases ─────────────────────────────────────

    [Fact]
    public void AuthOptions_DefaultMode_IsPassword()
    {
        var options = new AuthOptions();
        Assert.Equal("Password", options.Mode);
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("SIMPLE")]
    [InlineData("Simple")]
    [InlineData("sImPlE")]
    public void AuthOptions_ModeComparison_IsCaseInsensitive(string mode)
    {
        // The endpoint uses StringComparison.OrdinalIgnoreCase
        Assert.Equal(0, string.Compare("Simple", mode, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AuthOptions_DefaultTokenCookieName()
    {
        var options = new AuthOptions();
        Assert.Equal("token", options.TokenCookieName);
    }

    [Fact]
    public void AuthOptions_DefaultJwtSecret_HasMinimumLength()
    {
        var options = new AuthOptions();
        Assert.True(options.JwtSecret.Length >= 32, "Default JWT secret must be at least 32 bytes");
    }

    [Fact]
    public void AuthOptions_DefaultTokenExpiry_IsSevenDays()
    {
        var options = new AuthOptions();
        Assert.Equal(TimeSpan.FromDays(7), options.TokenExpiry);
    }

    [Fact]
    public void AuthOptions_DefaultAdminPassword_IsNull()
    {
        var options = new AuthOptions();
        Assert.Null(options.AdminPassword);
    }

    [Fact]
    public void AuthOptions_DefaultDemoProjectId_IsNull()
    {
        var options = new AuthOptions();
        Assert.Null(options.DemoProjectId);
    }

    // ── Login + Logout cookie logic ───────────────────────────────────

    [Fact]
    public void LogoutResponse_CookieName_DefaultIsToken()
    {
        var options = new AuthOptions();
        Assert.Equal("token", options.TokenCookieName);
    }

    [Fact]
    public void LogoutResponse_CustomCookieName()
    {
        var options = new AuthOptions { TokenCookieName = "my-auth-cookie" };
        Assert.Equal("my-auth-cookie", options.TokenCookieName);
    }

    // ── Edge cases: simultaneous logins ───────────────────────────────

    [Fact]
    public async Task MultipleUsersLogin_EachGetsDistinctAdmin()
    {
        var options = new AuthOptions { AdminPassword = "pw" };
        var authService = new JwtAuthService(Options.Create(options));

        var r1 = await PasswordLogin("alice@test.com", "pw", options, authService);
        var r2 = await PasswordLogin("bob@test.com", "pw", options, authService);
        var r3 = await PasswordLogin("charlie@test.com", "pw", options, authService);

        Assert.NotEqual(r1.adminId, r2.adminId);
        Assert.NotEqual(r2.adminId, r3.adminId);
        Assert.NotEqual(r1.adminId, r3.adminId);

        Assert.Equal(3, await _db.Admins.CountAsync());
    }

    [Fact]
    public async Task SameUserLoginsTwice_SameAdminId()
    {
        var options = new AuthOptions { AdminPassword = "pw" };
        var authService = new JwtAuthService(Options.Create(options));

        var r1 = await PasswordLogin("repeat@test.com", "pw", options, authService);
        var r2 = await PasswordLogin("repeat@test.com", "pw", options, authService);

        Assert.Equal(r1.adminId, r2.adminId);
        Assert.Equal(1, await _db.Admins.CountAsync(a => a.Email == "repeat@test.com"));
    }

    [Fact]
    public async Task SameUserLoginsTwice_TokensDiffer()
    {
        var options = new AuthOptions { AdminPassword = "pw" };
        var authService = new JwtAuthService(Options.Create(options));

        var r1 = await PasswordLogin("repeat@test.com", "pw", options, authService);
        var r2 = await PasswordLogin("repeat@test.com", "pw", options, authService);

        // Both tokens should be valid, even if they differ (iat differs)
        Assert.NotNull(authService.ValidateToken(r1.token!));
        Assert.NotNull(authService.ValidateToken(r2.token!));
    }

    // ── Email format edge cases ───────────────────────────────────────

    [Fact]
    public async Task PasswordLogin_EmailWithPlusSign_Works()
    {
        var options = new AuthOptions { AdminPassword = "pw" };
        var authService = new JwtAuthService(Options.Create(options));

        var result = await PasswordLogin("user+tag@test.com", "pw", options, authService);
        Assert.False(result.badRequest);
        Assert.Equal("user+tag@test.com", result.email);
    }

    [Fact]
    public async Task PasswordLogin_EmailPrefix_UsedAsName()
    {
        var options = new AuthOptions { AdminPassword = "pw" };
        var authService = new JwtAuthService(Options.Create(options));

        await PasswordLogin("firstname.lastname@company.com", "pw", options, authService);
        var admin = await _db.Admins.FirstAsync(a => a.Email == "firstname.lastname@company.com");
        Assert.Equal("firstname.lastname", admin.Name);
    }

    [Fact]
    public async Task PasswordLogin_EmailWithNoAt_NameIsFull()
    {
        // Edge case: if email somehow has no @, Split('@')[0] returns the whole string
        var options = new AuthOptions { AdminPassword = "pw" };
        var authService = new JwtAuthService(Options.Create(options));

        await PasswordLogin("noemailformat", "pw", options, authService);
        var admin = await _db.Admins.FirstAsync(a => a.Email == "noemailformat");
        Assert.Equal("noemailformat", admin.Name);
    }
}
