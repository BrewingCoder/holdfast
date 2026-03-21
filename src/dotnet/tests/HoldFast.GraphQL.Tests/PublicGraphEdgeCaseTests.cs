using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.GraphQL.Public;
using HoldFast.GraphQL.Public.InputTypes;
using HotChocolate;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Edge case tests for PublicMutation and PublicQuery — boundary conditions,
/// error paths, and unusual inputs that exercise defensive logic.
/// </summary>
public class PublicGraphEdgeCaseTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly PublicMutation _mutation;
    private readonly PublicQuery _query;
    private readonly Project _project;
    private readonly Workspace _workspace;
    private readonly Admin _admin;

    public PublicGraphEdgeCaseTests()
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

        // Admin with Id = 0 needed for FK on SessionComment (default AdminId = 0 → FK fails)
        _admin = new Admin { Uid = "system", Email = "system@test.com", Name = "System" };
        _db.Admins.Add(_admin);
        _db.SaveChanges();

        _project = new Project { Name = "TestProj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _mutation = new PublicMutation();
        _query = new PublicQuery();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ══════════════════════════════════════════════════════════════════
    // MarkBackendSetup — edge cases
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MarkBackendSetup_ByProjectId_SetsFlag()
    {
        var result = await _mutation.MarkBackendSetup(
            _project.Id.ToString(), null, null, _db, default);

        Assert.True(result);

        var updated = await _db.Projects.FindAsync(_project.Id);
        Assert.True(updated!.BackendSetup);
    }

    [Fact]
    public async Task MarkBackendSetup_NonexistentProjectId_ReturnsTrue()
    {
        var result = await _mutation.MarkBackendSetup("999999", null, null, _db, default);
        Assert.True(result); // Does not throw, just doesn't update
    }

    [Fact]
    public async Task MarkBackendSetup_InvalidProjectIdFormat_ReturnsTrue()
    {
        var result = await _mutation.MarkBackendSetup("not-a-number", null, null, _db, default);
        Assert.True(result); // int.TryParse fails gracefully
    }

    [Fact]
    public async Task MarkBackendSetup_NullProjectIdAndNullSessionId_ReturnsTrue()
    {
        var result = await _mutation.MarkBackendSetup(null, null, null, _db, default);
        Assert.True(result); // No-op but doesn't crash
    }

    [Fact]
    public async Task MarkBackendSetup_BySessionSecureId_SetsFlag()
    {
        var session = new Session
        {
            SecureId = "mark-backend-test",
            ProjectId = _project.Id,
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var result = await _mutation.MarkBackendSetup(null, "mark-backend-test", null, _db, default);

        Assert.True(result);
        var updated = await _db.Projects.FindAsync(_project.Id);
        Assert.True(updated!.BackendSetup);
    }

    [Fact]
    public async Task MarkBackendSetup_NonexistentSession_ReturnsTrue()
    {
        var result = await _mutation.MarkBackendSetup(null, "nonexistent-session", null, _db, default);
        Assert.True(result); // No-op
    }

    // ══════════════════════════════════════════════════════════════════
    // AddSessionFeedback — edge cases
    // ══════════════════════════════════════════════════════════════════

    // Note: AddSessionFeedback tests require AdminId FK fix (SessionComment.AdminId is non-nullable
    // but SDK feedback has no admin). Tested in PublicMutationSessionTests with FK workaround.

    [Fact]
    public async Task AddSessionFeedback_NonexistentSession_ThrowsGraphQLException()
    {
        var input = new AddSessionFeedbackInput(
            "nonexistent", null, null, "feedback", DateTime.UtcNow);

        await Assert.ThrowsAsync<GraphQLException>(
            () => _mutation.AddSessionFeedback(input, _db, default));
    }

    // ══════════════════════════════════════════════════════════════════
    // AddSessionProperties — edge cases
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AddSessionProperties_ValidSession_ReturnsSecureId()
    {
        var session = new Session { SecureId = "props-test", ProjectId = _project.Id };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var input = new AddSessionPropertiesInput("props-test",
            System.Text.Json.JsonSerializer.SerializeToElement(new { role = "admin" }));
        var result = await _mutation.AddSessionProperties(input, _db, default);

        Assert.Equal("props-test", result);
    }

    [Fact]
    public async Task AddSessionProperties_NonexistentSession_ThrowsGraphQLException()
    {
        var input = new AddSessionPropertiesInput("nonexistent", null);

        await Assert.ThrowsAsync<GraphQLException>(
            () => _mutation.AddSessionProperties(input, _db, default));
    }

    // ══════════════════════════════════════════════════════════════════
    // PublicQuery — edge cases
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void PublicQuery_Ignore_ReturnsNull()
    {
        var result = _query.Ignore(42);
        Assert.Null(result);
    }

    [Fact]
    public void PublicQuery_Ignore_AnyId_ReturnsNull()
    {
        Assert.Null(_query.Ignore(0));
        Assert.Null(_query.Ignore(-1));
        Assert.Null(_query.Ignore(int.MaxValue));
    }

    // ══════════════════════════════════════════════════════════════════
    // Input type construction — edge cases
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void InitializeSessionInput_AllFieldsSet()
    {
        var input = new InitializeSessionInput(
            "secure-id", "key", "1og24", true, true,
            "1.0", "1.0", "{}", "production", "2.0", "my-service",
            "fp-123", "client-id", ["example.com"], false, "strict");

        Assert.Equal("secure-id", input.SessionSecureId);
        Assert.Equal("key", input.SessionKey);
        Assert.Equal("1og24", input.OrganizationVerboseId);
        Assert.True(input.EnableStrictPrivacy);
        Assert.Equal("strict", input.PrivacySetting);
        Assert.Single(input.NetworkRecordingDomains!);
    }

    [Fact]
    public void InitializeSessionInput_NullableFieldsNull()
    {
        var input = new InitializeSessionInput(
            "sid", null, "vid", false, false,
            "1.0", "1.0", "{}", "dev", null, null,
            "fp", "cid", null, null, null);

        Assert.Null(input.SessionKey);
        Assert.Null(input.AppVersion);
        Assert.Null(input.ServiceName);
        Assert.Null(input.NetworkRecordingDomains);
        Assert.Null(input.DisableSessionRecording);
        Assert.Null(input.PrivacySetting);
    }

    [Fact]
    public void ErrorObjectInput_Construction()
    {
        var frames = new List<StackFrameInput>
        {
            new("main", "app.js", 42, 10, false, false, "source"),
        };

        var input = new ErrorObjectInput(
            "TypeError: x is undefined", "TypeError",
            "https://example.com/app", "app.js",
            42, 10, frames, DateTime.UtcNow, "{\"extra\":\"data\"}");

        Assert.Equal("TypeError: x is undefined", input.Event);
        Assert.Single(input.StackTrace);
        Assert.Equal("main", input.StackTrace[0].FunctionName);
    }

    [Fact]
    public void BackendErrorObjectInput_WithTraceContext()
    {
        var svc = new ServiceInput("api-server", "1.0");
        var input = new BackendErrorObjectInput(
            "sess-1", "req-123", "trace-abc", "span-xyz", "cursor-1",
            "Error occurred", "InternalError",
            "/api/data", "backend", "at Program.Main()",
            DateTime.UtcNow, "{}", svc, "production");

        Assert.Equal("sess-1", input.SessionSecureId);
        Assert.Equal("trace-abc", input.TraceId);
        Assert.Equal("span-xyz", input.SpanId);
        Assert.Equal("api-server", input.Service.Name);
    }

    [Fact]
    public void InitializeSessionResponse_Construction()
    {
        var resp = new InitializeSessionResponse("secure-id-123", 42);
        Assert.Equal("secure-id-123", resp.SecureId);
        Assert.Equal(42, resp.ProjectId);
    }

    [Fact]
    public void StackFrameInput_AllNullable()
    {
        var frame = new StackFrameInput(null, null, null, null, null, null, null);
        Assert.Null(frame.FunctionName);
        Assert.Null(frame.FileName);
        Assert.Null(frame.LineNumber);
    }
}
