using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Shared.SessionProcessing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoldFast.Shared.Tests.SessionProcessing;

/// <summary>
/// Tests for SessionInitializationService: session creation, device detection,
/// identify, backfill, duplicate handling, service registration.
/// </summary>
public class SessionInitializationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly SessionInitializationService _service;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public SessionInitializationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _workspace = new Workspace { Name = "WS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _project = new Project { Name = "Proj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _service = new SessionInitializationService(_db, NullLogger<SessionInitializationService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ── InitializeSessionAsync tests ─────────────────────────────────────

    [Fact]
    public async Task Initialize_CreatesSession()
    {
        var result = await _service.InitializeSessionAsync(
            "sess-1", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "production", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        Assert.True(result.IsNew);
        Assert.False(result.IsDuplicate);
        Assert.Equal("sess-1", result.Session.SecureId);
        Assert.Equal(_project.Id, result.Session.ProjectId);
    }

    [Fact]
    public async Task Initialize_SetsDeviceInfo_Chrome()
    {
        var ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36";
        var result = await _service.InitializeSessionAsync(
            "sess-chrome", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            ua, "en-US", "1.2.3.4", CancellationToken.None);

        Assert.Equal("Chrome", result.Session.BrowserName);
        Assert.Equal("120.0.0.0", result.Session.BrowserVersion);
        Assert.Equal("Windows", result.Session.OSName);
        Assert.Equal("en-US", result.Session.Language);
        Assert.Equal("1.2.3.4", result.Session.IP);
    }

    [Fact]
    public async Task Initialize_SetsDeviceInfo_Firefox()
    {
        var ua = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:121.0) Gecko/20100101 Firefox/121.0";
        var result = await _service.InitializeSessionAsync(
            "sess-ff", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            ua, null, null, CancellationToken.None);

        Assert.Equal("Firefox", result.Session.BrowserName);
        Assert.Equal("121.0", result.Session.BrowserVersion);
        Assert.Equal("Mac OS X", result.Session.OSName);
    }

    [Fact]
    public async Task Initialize_SetsDeviceInfo_Safari()
    {
        var ua = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Safari/605.1.15";
        var result = await _service.InitializeSessionAsync(
            "sess-safari", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            ua, null, null, CancellationToken.None);

        Assert.Equal("Safari", result.Session.BrowserName);
        Assert.Equal("17.2", result.Session.BrowserVersion);
    }

    [Fact]
    public async Task Initialize_SetsDeviceInfo_Edge()
    {
        var ua = "Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0";
        var result = await _service.InitializeSessionAsync(
            "sess-edge", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            ua, null, null, CancellationToken.None);

        Assert.Equal("Edge", result.Session.BrowserName);
    }

    [Fact]
    public async Task Initialize_NullUserAgent_NoDevice()
    {
        var result = await _service.InitializeSessionAsync(
            "sess-noua", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        Assert.Null(result.Session.BrowserName);
        Assert.Null(result.Session.OSName);
    }

    [Fact]
    public async Task Initialize_DuplicateSession_ReturnsDuplicate()
    {
        await _service.InitializeSessionAsync(
            "sess-dup", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        var result = await _service.InitializeSessionAsync(
            "sess-dup", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        Assert.True(result.IsDuplicate);
        Assert.False(result.IsNew);
    }

    [Fact]
    public async Task Initialize_SetsPrivacySettings()
    {
        var result = await _service.InitializeSessionAsync(
            "sess-priv", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            "{\"test\":true}", "staging", "2.0", "api-svc", true, true, "strict",
            null, null, null, CancellationToken.None);

        Assert.True(result.Session.EnableStrictPrivacy);
        Assert.True(result.Session.EnableRecordingNetworkContents);
        Assert.Equal("strict", result.Session.PrivacySetting);
        Assert.Equal("staging", result.Session.Environment);
        Assert.Equal("2.0", result.Session.AppVersion);
        Assert.Equal("api-svc", result.Session.ServiceName);
    }

    [Fact]
    public async Task Initialize_CreatesSetupEvent()
    {
        await _service.InitializeSessionAsync(
            "sess-setup", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        var setupEvent = await _db.SetupEvents
            .FirstOrDefaultAsync(e => e.ProjectId == _project.Id && e.Type == "session");
        Assert.NotNull(setupEvent);
    }

    [Fact]
    public async Task Initialize_WithServiceName_RegistersService()
    {
        await _service.InitializeSessionAsync(
            "sess-svc", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, "my-api", false, false, null,
            null, null, null, CancellationToken.None);

        var service = await _db.Services.FirstOrDefaultAsync(s => s.Name == "my-api");
        Assert.NotNull(service);
        Assert.Equal(_project.Id, service!.ProjectId);
    }

    [Fact]
    public async Task Initialize_CreatesDeviceFields()
    {
        var ua = "Mozilla/5.0 (Windows NT 10.0) Chrome/120.0.0.0";
        await _service.InitializeSessionAsync(
            "sess-fields", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "production", null, null, false, false, null,
            ua, null, null, CancellationToken.None);

        var fields = await _db.Fields
            .Where(f => f.ProjectId == _project.Id && f.Type == "session")
            .ToListAsync();

        Assert.Contains(fields, f => f.Name == "browser_name" && f.Value == "Chrome");
        Assert.Contains(fields, f => f.Name == "os_name" && f.Value == "Windows");
        Assert.Contains(fields, f => f.Name == "environment" && f.Value == "production");
    }

    [Fact]
    public async Task Initialize_SetsWithinBillingQuota()
    {
        var result = await _service.InitializeSessionAsync(
            "sess-quota", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        Assert.True(result.Session.WithinBillingQuota);
    }

    [Fact]
    public async Task Initialize_SetsProcessedFalse()
    {
        var result = await _service.InitializeSessionAsync(
            "sess-proc", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        Assert.False(result.Session.Processed);
    }

    // ── IdentifySessionAsync tests ───────────────────────────────────────

    [Fact]
    public async Task Identify_SetsIdentifier()
    {
        await _service.InitializeSessionAsync(
            "sess-id", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        var session = await _service.IdentifySessionAsync("sess-id", "user@example.com", null, CancellationToken.None);

        Assert.Equal("user@example.com", session.Identifier);
    }

    [Fact]
    public async Task Identify_DetectsEmail()
    {
        await _service.InitializeSessionAsync(
            "sess-email", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        await _service.IdentifySessionAsync("sess-email", "john@acme.com", null, CancellationToken.None);

        var fields = await _db.Fields.Where(f => f.Type == "user").ToListAsync();
        Assert.Contains(fields, f => f.Name == "email" && f.Value == "john@acme.com");
        Assert.Contains(fields, f => f.Name == "domain" && f.Value == "acme.com");
    }

    [Fact]
    public async Task Identify_NonEmail_NoEmailField()
    {
        await _service.InitializeSessionAsync(
            "sess-noemail", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        await _service.IdentifySessionAsync("sess-noemail", "user123", null, CancellationToken.None);

        var emailField = await _db.Fields.FirstOrDefaultAsync(f => f.Name == "email");
        Assert.Null(emailField);
    }

    [Fact]
    public async Task Identify_FirstTimeUser_SetsFirstTime()
    {
        await _service.InitializeSessionAsync(
            "sess-first", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        var session = await _service.IdentifySessionAsync("sess-first", "newuser", null, CancellationToken.None);
        Assert.Equal(1, session.FirstTime); // First time = true
    }

    [Fact]
    public async Task Identify_ReturningUser_NotFirstTime()
    {
        // Create first session
        await _service.InitializeSessionAsync(
            "sess-ret1", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);
        await _service.IdentifySessionAsync("sess-ret1", "returning", null, CancellationToken.None);

        // Create second session
        await _service.InitializeSessionAsync(
            "sess-ret2", null, _project.Id, "fp2", "client2", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);
        var session2 = await _service.IdentifySessionAsync("sess-ret2", "returning", null, CancellationToken.None);

        Assert.Equal(0, session2.FirstTime); // Not first time
    }

    [Fact]
    public async Task Identify_BackfillsUnidentifiedSessions()
    {
        // Create two sessions with same client ID but no identifier
        await _service.InitializeSessionAsync(
            "sess-bf1", null, _project.Id, "fp1", "shared-client", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);
        await _service.InitializeSessionAsync(
            "sess-bf2", null, _project.Id, "fp1", "shared-client", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        // Identify first session
        await _service.IdentifySessionAsync("sess-bf1", "backfill-user", null, CancellationToken.None);

        // Second session should be backfilled
        var session2 = await _db.Sessions.FirstAsync(s => s.SecureId == "sess-bf2");
        Assert.Equal("backfill-user", session2.Identifier);
    }

    [Fact]
    public async Task Identify_WithUserObject_StoresProperties()
    {
        await _service.InitializeSessionAsync(
            "sess-props", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        var userObj = new { name = "John", company = "Acme" };
        await _service.IdentifySessionAsync("sess-props", "john", userObj, CancellationToken.None);

        var fields = await _db.Fields.Where(f => f.Type == "user").ToListAsync();
        Assert.Contains(fields, f => f.Name == "name");
        Assert.Contains(fields, f => f.Name == "company");
    }

    [Fact]
    public async Task Identify_NonexistentSession_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.IdentifySessionAsync("nonexistent", "user", null, CancellationToken.None));
    }

    // ── DeviceParser unit tests ──────────────────────────────────────────

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) Chrome/120.0.0.0", "Chrome", "120.0.0.0", "Windows")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Firefox/121.0", "Firefox", "121.0", "Mac OS X")]
    [InlineData("Mozilla/5.0 (Linux; Android 14) Chrome/120.0.0.0 Mobile", "Chrome", "120.0.0.0", "Android")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_2 like Mac OS X) Safari/605.1.15 Version/17.2", "Safari", "17.2", "iOS")]
    public void DeviceParser_CommonBrowsers(string ua, string browser, string version, string os)
    {
        var result = DeviceParser.Parse(ua);
        Assert.Equal(browser, result.BrowserName);
        Assert.Equal(version, result.BrowserVersion);
        Assert.Equal(os, result.OSName);
    }

    [Fact]
    public void DeviceParser_NullUserAgent_ReturnsEmpty()
    {
        var result = DeviceParser.Parse(null);
        Assert.Null(result.BrowserName);
        Assert.Null(result.OSName);
    }

    [Fact]
    public void DeviceParser_EmptyUserAgent_ReturnsEmpty()
    {
        var result = DeviceParser.Parse("");
        Assert.Null(result.BrowserName);
    }

    [Fact]
    public void DeviceParser_Opera_Detected()
    {
        var ua = "Mozilla/5.0 (Windows NT 10.0) Chrome/120.0.0.0 OPR/106.0.0.0";
        var result = DeviceParser.Parse(ua);
        Assert.Equal("Opera", result.BrowserName);
    }

    [Fact]
    public void DeviceParser_LinuxOS_Detected()
    {
        var ua = "Mozilla/5.0 (X11; Linux x86_64) Firefox/121.0";
        var result = DeviceParser.Parse(ua);
        Assert.Equal("Linux", result.OSName);
    }
}
