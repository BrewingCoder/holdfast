using System.Text.Json;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Shared.SessionProcessing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoldFast.Shared.Tests.SessionProcessing;

/// <summary>
/// Edge case and negative path tests for SessionInitializationService.
/// Covers: invalid JSON user objects, backfill skip for empty ClientID,
/// duplicate service registration, field deduplication, multi-session
/// identify ordering, and malformed email handling.
/// </summary>
public class SessionInitializationEdgeCaseTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly SessionInitializationService _service;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public SessionInitializationEdgeCaseTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _workspace = new Workspace { Name = "Edge WS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _project = new Project { Name = "Edge Project", WorkspaceId = _workspace.Id };
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

    // ── IdentifySession: invalid JSON userObject ────────────────────────

    [Fact]
    public async Task Identify_InvalidJsonUserObject_SkipsProperties()
    {
        await _service.InitializeSessionAsync(
            "sess-badjson", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        // Pass a string that isn't valid JSON as userObject
        var session = await _service.IdentifySessionAsync(
            "sess-badjson", "user1", "not valid json {{{", CancellationToken.None);

        // Should still set the identifier, just skip user properties
        Assert.Equal("user1", session.Identifier);
        var userFields = await _db.Fields.Where(f => f.Type == "user").ToListAsync();
        Assert.Empty(userFields);
    }

    [Fact]
    public async Task Identify_NullUserObject_SetsIdentifierOnly()
    {
        await _service.InitializeSessionAsync(
            "sess-nullobj", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        var session = await _service.IdentifySessionAsync(
            "sess-nullobj", "just-a-user", null, CancellationToken.None);

        Assert.Equal("just-a-user", session.Identifier);
    }

    [Fact]
    public async Task Identify_EmptyObjectUserObject_NoFields()
    {
        await _service.InitializeSessionAsync(
            "sess-emptyobj", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        var session = await _service.IdentifySessionAsync(
            "sess-emptyobj", "user2", new { }, CancellationToken.None);

        Assert.Equal("user2", session.Identifier);
    }

    // ── IdentifySession: JsonElement userObject ─────────────────────────

    [Fact]
    public async Task Identify_JsonElementUserObject_ExtractsProperties()
    {
        await _service.InitializeSessionAsync(
            "sess-jsonelement", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>("""{"role":"admin","team":"backend"}""");
        var session = await _service.IdentifySessionAsync(
            "sess-jsonelement", "admin-user", json, CancellationToken.None);

        Assert.Equal("admin-user", session.Identifier);
        var fields = await _db.Fields.Where(f => f.Type == "user").ToListAsync();
        Assert.Contains(fields, f => f.Name == "role" && f.Value == "admin");
        Assert.Contains(fields, f => f.Name == "team" && f.Value == "backend");
    }

    // ── IdentifySession: email edge cases ───────────────────────────────

    [Fact]
    public async Task Identify_PlusAddressEmail_Detected()
    {
        await _service.InitializeSessionAsync(
            "sess-plus", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        await _service.IdentifySessionAsync(
            "sess-plus", "user+tag@example.com", null, CancellationToken.None);

        var fields = await _db.Fields.Where(f => f.Type == "user").ToListAsync();
        Assert.Contains(fields, f => f.Name == "email" && f.Value == "user+tag@example.com");
        Assert.Contains(fields, f => f.Name == "domain" && f.Value == "example.com");
    }

    [Fact]
    public async Task Identify_SubdomainEmail_ExtractsDomain()
    {
        await _service.InitializeSessionAsync(
            "sess-subdomain", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        await _service.IdentifySessionAsync(
            "sess-subdomain", "admin@mail.corp.example.com", null, CancellationToken.None);

        var fields = await _db.Fields.Where(f => f.Type == "user").ToListAsync();
        Assert.Contains(fields, f => f.Name == "domain" && f.Value == "mail.corp.example.com");
    }

    [Fact]
    public async Task Identify_NotAnEmail_NoEmailFields()
    {
        await _service.InitializeSessionAsync(
            "sess-noemail2", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        await _service.IdentifySessionAsync(
            "sess-noemail2", "user_12345", null, CancellationToken.None);

        var emailFields = await _db.Fields.Where(f => f.Name == "email").ToListAsync();
        Assert.Empty(emailFields);
        var domainFields = await _db.Fields.Where(f => f.Name == "domain").ToListAsync();
        Assert.Empty(domainFields);
    }

    // ── Backfill edge cases ─────────────────────────────────────────────

    [Fact]
    public async Task Identify_EmptyClientID_SkipsBackfill()
    {
        // Create sessions with empty client ID
        await _service.InitializeSessionAsync(
            "sess-noclient1", null, _project.Id, "fp1", "", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);
        await _service.InitializeSessionAsync(
            "sess-noclient2", null, _project.Id, "fp1", "", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        await _service.IdentifySessionAsync("sess-noclient1", "user-x", null, CancellationToken.None);

        // Second session should NOT be backfilled because ClientID is empty
        var session2 = await _db.Sessions.FirstAsync(s => s.SecureId == "sess-noclient2");
        Assert.Null(session2.Identifier);
    }

    [Fact]
    public async Task Identify_AlreadyIdentifiedSessions_NotBackfilled()
    {
        // Create two sessions with same client ID, both identified
        await _service.InitializeSessionAsync(
            "sess-idbf1", null, _project.Id, "fp1", "shared-client-x", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);
        await _service.InitializeSessionAsync(
            "sess-idbf2", null, _project.Id, "fp1", "shared-client-x", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        // Identify second session first
        await _service.IdentifySessionAsync("sess-idbf2", "user-b", null, CancellationToken.None);

        // Now identify first session — second should keep its own identifier
        await _service.IdentifySessionAsync("sess-idbf1", "user-a", null, CancellationToken.None);

        var session2 = await _db.Sessions.FirstAsync(s => s.SecureId == "sess-idbf2");
        Assert.Equal("user-b", session2.Identifier); // Should NOT be overwritten
    }

    [Fact]
    public async Task Identify_BackfilledSession_SetsFirstTimeZero()
    {
        await _service.InitializeSessionAsync(
            "sess-ft1", null, _project.Id, "fp1", "ft-client", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);
        await _service.InitializeSessionAsync(
            "sess-ft2", null, _project.Id, "fp1", "ft-client", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        // Identify first session
        await _service.IdentifySessionAsync("sess-ft1", "first-timer", null, CancellationToken.None);

        // Backfilled session should have FirstTime = 0 (not first time)
        var backfilled = await _db.Sessions.FirstAsync(s => s.SecureId == "sess-ft2");
        Assert.Equal(0, backfilled.FirstTime);
    }

    // ── Service registration edge cases ─────────────────────────────────

    [Fact]
    public async Task Initialize_DuplicateServiceName_NoError()
    {
        // Create two sessions with the same service name
        await _service.InitializeSessionAsync(
            "sess-svc1", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, "shared-service", false, false, null,
            null, null, null, CancellationToken.None);

        // Second session with same service — should not throw
        await _service.InitializeSessionAsync(
            "sess-svc2", null, _project.Id, "fp2", "client2", "1.0", "1.0",
            null, "prod", null, "shared-service", false, false, null,
            null, null, null, CancellationToken.None);

        // Only one service record should exist
        var services = await _db.Services.Where(s => s.Name == "shared-service").ToListAsync();
        Assert.Single(services);
    }

    [Fact]
    public async Task Initialize_NoServiceName_SkipsRegistration()
    {
        await _service.InitializeSessionAsync(
            "sess-nosvc", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        var services = await _db.Services.ToListAsync();
        Assert.Empty(services);
    }

    [Fact]
    public async Task Initialize_EmptyServiceName_SkipsRegistration()
    {
        await _service.InitializeSessionAsync(
            "sess-emptysvc", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, "", false, false, null,
            null, null, null, CancellationToken.None);

        var services = await _db.Services.ToListAsync();
        Assert.Empty(services);
    }

    // ── Field deduplication ─────────────────────────────────────────────

    [Fact]
    public async Task Initialize_DuplicateFieldValues_NotDuplicated()
    {
        var ua = "Mozilla/5.0 (Windows NT 10.0) Chrome/120.0.0.0";

        // Two sessions with same device + environment → should not create duplicate fields
        await _service.InitializeSessionAsync(
            "sess-dup1", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "production", null, null, false, false, null,
            ua, null, null, CancellationToken.None);

        await _service.InitializeSessionAsync(
            "sess-dup2", null, _project.Id, "fp2", "client2", "1.0", "1.0",
            null, "production", null, null, false, false, null,
            ua, null, null, CancellationToken.None);

        // Each (type, name, value) combo should only appear once
        var chromeFields = await _db.Fields
            .Where(f => f.Type == "session" && f.Name == "browser_name" && f.Value == "Chrome")
            .ToListAsync();
        Assert.Single(chromeFields);
    }

    // ── SetupEvent deduplication ────────────────────────────────────────

    [Fact]
    public async Task Initialize_SecondSession_DoesNotDuplicateSetupEvent()
    {
        await _service.InitializeSessionAsync(
            "sess-se1", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        await _service.InitializeSessionAsync(
            "sess-se2", null, _project.Id, "fp2", "client2", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        var setupEvents = await _db.SetupEvents
            .Where(e => e.ProjectId == _project.Id && e.Type == "session")
            .ToListAsync();
        Assert.Single(setupEvents);
    }

    // ── DeviceParser used by Initialize: field storage ───────────────────

    [Fact]
    public async Task Initialize_StoresServiceNameField_WhenPresent()
    {
        var ua = "Mozilla/5.0 (Windows NT 10.0) Chrome/120.0.0.0";
        await _service.InitializeSessionAsync(
            "sess-svcfield", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "production", null, "my-service", false, false, null,
            ua, null, null, CancellationToken.None);

        var svcField = await _db.Fields
            .FirstOrDefaultAsync(f => f.Type == "session" && f.Name == "service_name" && f.Value == "my-service");
        Assert.NotNull(svcField);
    }

    [Fact]
    public async Task Initialize_StoresDeviceIdField()
    {
        await _service.InitializeSessionAsync(
            "sess-devid", null, _project.Id, "my-fingerprint", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            "Mozilla/5.0 (Windows NT 10.0) Chrome/120.0.0.0", null, null, CancellationToken.None);

        var deviceField = await _db.Fields
            .FirstOrDefaultAsync(f => f.Type == "session" && f.Name == "device_id" && f.Value == "my-fingerprint");
        Assert.NotNull(deviceField);
    }

    [Fact]
    public async Task Initialize_SkipsEmptyDeviceFields()
    {
        // No UA → no browser/os fields; no environment or fingerprint fields with empty values
        await _service.InitializeSessionAsync(
            "sess-skipempty", null, _project.Id, "", "client1", "1.0", "1.0",
            null, "", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        var fields = await _db.Fields.Where(f => f.ProjectId == _project.Id).ToListAsync();
        // Should have no fields since all values are empty/null
        Assert.Empty(fields);
    }

    // ── Identify with user properties that have special characters ───────

    [Fact]
    public async Task Identify_UserPropsWithSpecialChars()
    {
        await _service.InitializeSessionAsync(
            "sess-special", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        var userObj = new { display_name = "O'Brien & Co.", url = "https://example.com?q=test&lang=en" };
        var session = await _service.IdentifySessionAsync(
            "sess-special", "obrien", userObj, CancellationToken.None);

        Assert.Equal("obrien", session.Identifier);
        var fields = await _db.Fields.Where(f => f.Type == "user").ToListAsync();
        Assert.Contains(fields, f => f.Name == "display_name");
        Assert.Contains(fields, f => f.Name == "url");
    }

    // ── Identify: re-identification of same session ─────────────────────

    [Fact]
    public async Task Identify_SameSessionTwice_UpdatesIdentifier()
    {
        await _service.InitializeSessionAsync(
            "sess-reiden", null, _project.Id, "fp1", "client1", "1.0", "1.0",
            null, "prod", null, null, false, false, null,
            null, null, null, CancellationToken.None);

        await _service.IdentifySessionAsync("sess-reiden", "old-user", null, CancellationToken.None);
        var session = await _service.IdentifySessionAsync("sess-reiden", "new-user", null, CancellationToken.None);

        Assert.Equal("new-user", session.Identifier);
    }
}
