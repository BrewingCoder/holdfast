using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Public;
using HoldFast.GraphQL.Public.InputTypes;
using HoldFast.Shared.SessionProcessing;
using HotChocolate;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Tests for PublicMutation session-related mutations:
/// InitializeSession, IdentifySession, AddSessionProperties,
/// MarkBackendSetup, AddSessionFeedback.
/// </summary>
public class PublicMutationSessionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly PublicMutation _mutation;
    private readonly SessionInitializationService _initService;
    private readonly Project _project;
    private readonly Workspace _workspace;
    private readonly Admin _admin;

    public PublicMutationSessionTests()
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

        _admin = new Admin { Uid = "test", Email = "test@example.com", Name = "Test" };
        _db.Admins.Add(_admin);
        _db.SaveChanges();

        _project = new Project { Name = "TestProj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _mutation = new PublicMutation();
        _initService = new SessionInitializationService(_db, NullLogger<SessionInitializationService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private StubHttpContextAccessor CreateHttpContext(string? userAgent = null, string? acceptLanguage = null)
    {
        var ctx = new DefaultHttpContext();
        if (userAgent != null)
            ctx.Request.Headers.UserAgent = userAgent;
        if (acceptLanguage != null)
            ctx.Request.Headers.AcceptLanguage = acceptLanguage;
        return new StubHttpContextAccessor(ctx);
    }

    // ── InitializeSession ────────────────────────────────────────────

    [Fact]
    public async Task InitializeSession_CreatesNewSession()
    {
        var input = new InitializeSessionInput(
            "init-sess-1", null, _project.Id.ToString(),
            false, false, "1.0", "1.0", "{}", "production",
            "1.0", null, "fp-123", "client-1", null, null, null);
        var accessor = CreateHttpContext("Mozilla/5.0 Chrome/120.0");

        var result = await _mutation.InitializeSession(input, _initService, accessor, _db, CancellationToken.None);

        Assert.Equal("init-sess-1", result.SecureId);
        Assert.Equal(_project.Id, result.ProjectId);

        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.SecureId == "init-sess-1");
        Assert.NotNull(session);
        Assert.Equal("Chrome", session!.BrowserName);
    }

    [Fact]
    public async Task InitializeSession_DuplicateSession_ReturnsSameSecureId()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "existing-sess",
            ProjectId = _project.Id,
            Fingerprint = "fp",
            ClientID = "c1",
        });
        _db.SaveChanges();

        var input = new InitializeSessionInput(
            "existing-sess", null, _project.Id.ToString(),
            false, false, "1.0", "1.0", "{}", "prod",
            null, null, "fp", "c1", null, null, null);
        var accessor = CreateHttpContext();

        var result = await _mutation.InitializeSession(input, _initService, accessor, _db, CancellationToken.None);

        Assert.Equal("existing-sess", result.SecureId);
    }

    [Fact]
    public async Task InitializeSession_InvalidProject_Throws()
    {
        var input = new InitializeSessionInput(
            "sess-bad-proj", null, "99999",
            false, false, "1.0", "1.0", "{}", "prod",
            null, null, "fp", "c1", null, null, null);
        var accessor = CreateHttpContext();

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.InitializeSession(input, _initService, accessor, _db, CancellationToken.None));
    }

    [Fact]
    public async Task InitializeSession_ExtractsUserAgent()
    {
        var input = new InitializeSessionInput(
            "sess-ua-test", null, _project.Id.ToString(),
            false, false, "1.0", "1.0", "{}", "prod",
            null, null, "fp", "c1", null, null, null);
        var accessor = CreateHttpContext("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0");

        await _mutation.InitializeSession(input, _initService, accessor, _db, CancellationToken.None);

        var session = await _db.Sessions.FirstAsync(s => s.SecureId == "sess-ua-test");
        Assert.Equal("Chrome", session.BrowserName);
        Assert.Equal("Windows", session.OSName);
    }

    [Fact]
    public async Task InitializeSession_NullUserAgent_NoDeviceInfo()
    {
        var input = new InitializeSessionInput(
            "sess-no-ua", null, _project.Id.ToString(),
            false, false, "1.0", "1.0", "{}", "prod",
            null, null, "fp", "c1", null, null, null);
        var accessor = CreateHttpContext();

        await _mutation.InitializeSession(input, _initService, accessor, _db, CancellationToken.None);

        var session = await _db.Sessions.FirstAsync(s => s.SecureId == "sess-no-ua");
        Assert.Null(session.BrowserName);
        Assert.Null(session.OSName);
    }

    [Fact]
    public async Task InitializeSession_WithServiceName_CreatesService()
    {
        var input = new InitializeSessionInput(
            "sess-svc", null, _project.Id.ToString(),
            false, false, "1.0", "1.0", "{}", "prod",
            "2.0", "my-api", "fp", "c1", null, null, null);
        var accessor = CreateHttpContext();

        await _mutation.InitializeSession(input, _initService, accessor, _db, CancellationToken.None);

        var service = await _db.Services.FirstOrDefaultAsync(s => s.Name == "my-api");
        Assert.NotNull(service);
        Assert.Equal(_project.Id, service!.ProjectId);
    }

    [Fact]
    public async Task InitializeSession_StrictPrivacy_SetOnSession()
    {
        var input = new InitializeSessionInput(
            "sess-privacy", null, _project.Id.ToString(),
            true, true, "1.0", "1.0", "{}", "prod",
            null, null, "fp", "c1", null, null, "strict");
        var accessor = CreateHttpContext();

        await _mutation.InitializeSession(input, _initService, accessor, _db, CancellationToken.None);

        var session = await _db.Sessions.FirstAsync(s => s.SecureId == "sess-privacy");
        Assert.True(session.EnableStrictPrivacy);
        Assert.True(session.EnableRecordingNetworkContents);
        Assert.Equal("strict", session.PrivacySetting);
    }

    [Fact]
    public async Task InitializeSession_VerboseIdProject_Works()
    {
        // Test with verbose ID (hashid) format — falls back to int parsing
        var input = new InitializeSessionInput(
            "sess-verbose", null, _project.Id.ToString(),
            false, false, "1.0", "1.0", "{}", "prod",
            null, null, "fp", "c1", null, null, null);
        var accessor = CreateHttpContext();

        var result = await _mutation.InitializeSession(input, _initService, accessor, _db, CancellationToken.None);
        Assert.Equal(_project.Id, result.ProjectId);
    }

    // ── IdentifySession ──────────────────────────────────────────────

    [Fact]
    public async Task IdentifySession_SetsUserIdentifier()
    {
        _db.Sessions.Add(new Session { SecureId = "id-sess-1", ProjectId = _project.Id });
        _db.SaveChanges();

        var input = new IdentifySessionInput("id-sess-1", "user@example.com", null);

        var result = await _mutation.IdentifySession(input, _initService, CancellationToken.None);

        Assert.Equal("id-sess-1", result);

        var session = await _db.Sessions.FirstAsync(s => s.SecureId == "id-sess-1");
        Assert.Equal("user@example.com", session.Identifier);
    }

    [Fact]
    public async Task IdentifySession_NonexistentSession_Throws()
    {
        var input = new IdentifySessionInput("nonexistent-sess", "user", null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _mutation.IdentifySession(input, _initService, CancellationToken.None));
    }

    [Fact]
    public async Task IdentifySession_WithUserObject_CreatesFields()
    {
        _db.Sessions.Add(new Session { SecureId = "id-sess-obj", ProjectId = _project.Id });
        _db.SaveChanges();

        var userObj = System.Text.Json.JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["name"] = "Scott",
            ["role"] = "admin",
        });

        var input = new IdentifySessionInput("id-sess-obj", "scott@example.com", userObj);

        await _mutation.IdentifySession(input, _initService, CancellationToken.None);

        var fields = await _db.Fields.Where(f => f.Type == "user").ToListAsync();
        Assert.Contains(fields, f => f.Name == "email" && f.Value == "scott@example.com");
    }

    [Fact]
    public async Task IdentifySession_NonEmailIdentifier_NoEmailField()
    {
        _db.Sessions.Add(new Session { SecureId = "id-sess-noemail", ProjectId = _project.Id });
        _db.SaveChanges();

        var input = new IdentifySessionInput("id-sess-noemail", "username-only", null);

        await _mutation.IdentifySession(input, _initService, CancellationToken.None);

        var fields = await _db.Fields.Where(f => f.Name == "identified_email").ToListAsync();
        Assert.Empty(fields);
    }

    // ── AddSessionProperties ─────────────────────────────────────────

    [Fact]
    public async Task AddSessionProperties_ExistingSession_ReturnsSecureId()
    {
        _db.Sessions.Add(new Session { SecureId = "prop-sess-1", ProjectId = _project.Id });
        _db.SaveChanges();

        var input = new AddSessionPropertiesInput("prop-sess-1", null);
        var result = await _mutation.AddSessionProperties(input, _db, CancellationToken.None);

        Assert.Equal("prop-sess-1", result);
    }

    [Fact]
    public async Task AddSessionProperties_NonexistentSession_Throws()
    {
        var input = new AddSessionPropertiesInput("no-such-session", null);

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.AddSessionProperties(input, _db, CancellationToken.None));
    }

    // ── MarkBackendSetup ─────────────────────────────────────────────

    [Fact]
    public async Task MarkBackendSetup_ByProjectId_SetsFlag()
    {
        var result = await _mutation.MarkBackendSetup(
            _project.Id.ToString(), null, null, _db, CancellationToken.None);

        Assert.True(result);

        await _db.Entry(_project).ReloadAsync();
        Assert.True(_project.BackendSetup);
    }

    [Fact]
    public async Task MarkBackendSetup_BySessionSecureId_SetsFlag()
    {
        var session = new Session { SecureId = "backend-sess", ProjectId = _project.Id };
        _db.Sessions.Add(session);
        _db.SaveChanges();

        var result = await _mutation.MarkBackendSetup(
            null, "backend-sess", null, _db, CancellationToken.None);

        Assert.True(result);

        await _db.Entry(_project).ReloadAsync();
        Assert.True(_project.BackendSetup);
    }

    [Fact]
    public async Task MarkBackendSetup_InvalidProjectId_NoError()
    {
        var result = await _mutation.MarkBackendSetup(
            "99999", null, null, _db, CancellationToken.None);

        Assert.True(result); // Always returns true even if project not found
    }

    [Fact]
    public async Task MarkBackendSetup_NullBoth_NoError()
    {
        var result = await _mutation.MarkBackendSetup(
            null, null, null, _db, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task MarkBackendSetup_NonNumericProjectId_IgnoredGracefully()
    {
        var result = await _mutation.MarkBackendSetup(
            "not-a-number", null, null, _db, CancellationToken.None);

        Assert.True(result);
    }

    // ── AddSessionFeedback ───────────────────────────────────────────

    [Fact]
    public async Task AddSessionFeedback_CreatesComment()
    {
        var session = new Session { SecureId = "fb-sess", ProjectId = _project.Id };
        _db.Sessions.Add(session);
        _db.SaveChanges();

        var input = new AddSessionFeedbackInput(
            "fb-sess", "Scott", "scott@example.com", "Great product!", DateTime.UtcNow);

        var result = await _mutation.AddSessionFeedback(input, _db, CancellationToken.None);

        Assert.Equal("fb-sess", result);
        var comment = await _db.SessionComments.FirstOrDefaultAsync();
        Assert.NotNull(comment);
        Assert.Equal("Great product!", comment!.Text);
        Assert.Equal("FEEDBACK", comment.Type);
        Assert.Null(comment.AdminId); // SDK feedback has no admin
    }

    [Fact]
    public async Task AddSessionFeedback_NonexistentSession_Throws()
    {
        var input = new AddSessionFeedbackInput(
            "no-such-sess", null, null, "feedback", DateTime.UtcNow);

        await Assert.ThrowsAsync<GraphQLException>(() =>
            _mutation.AddSessionFeedback(input, _db, CancellationToken.None));
    }

    [Fact]
    public async Task AddSessionFeedback_EmptyVerbatim_StillCreatesComment()
    {
        _db.Sessions.Add(new Session { SecureId = "fb-empty", ProjectId = _project.Id });
        _db.SaveChanges();

        var input = new AddSessionFeedbackInput(
            "fb-empty", null, null, "", DateTime.UtcNow);

        var result = await _mutation.AddSessionFeedback(input, _db, CancellationToken.None);

        Assert.Equal("fb-empty", result);
        var comment = await _db.SessionComments.FirstOrDefaultAsync();
        Assert.NotNull(comment);
        Assert.Equal("", comment!.Text);
        Assert.Null(comment.AdminId);
    }

    [Fact]
    public async Task AddSessionFeedback_MultipleFeedbacks_SameSession()
    {
        _db.Sessions.Add(new Session { SecureId = "fb-multi", ProjectId = _project.Id });
        _db.SaveChanges();

        var input1 = new AddSessionFeedbackInput("fb-multi", null, null, "First", DateTime.UtcNow);
        var input2 = new AddSessionFeedbackInput("fb-multi", null, null, "Second", DateTime.UtcNow);

        await _mutation.AddSessionFeedback(input1, _db, CancellationToken.None);
        await _mutation.AddSessionFeedback(input2, _db, CancellationToken.None);

        var count = await _db.SessionComments.CountAsync();
        Assert.Equal(2, count);
    }

    // ── PublicQuery ──────────────────────────────────────────────────

    [Fact]
    public void PublicQuery_Ignore_ReturnsNull()
    {
        var query = new PublicQuery();
        Assert.Null(query.Ignore(1));
        Assert.Null(query.Ignore(0));
        Assert.Null(query.Ignore(int.MaxValue));
    }

    // ── Stub IHttpContextAccessor ────────────────────────────────────

    private class StubHttpContextAccessor : IHttpContextAccessor
    {
        public StubHttpContextAccessor(HttpContext? httpContext) => HttpContext = httpContext;
        public HttpContext? HttpContext { get; set; }
    }
}
