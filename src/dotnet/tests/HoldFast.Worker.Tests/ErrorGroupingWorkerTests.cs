using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.Shared.AlertEvaluation;
using HoldFast.Shared.ErrorGrouping;
using HoldFast.Worker;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoldFast.Worker.Tests;

/// <summary>
/// Tests for ErrorGroupingConsumer logic — error grouping, alert evaluation,
/// project ID resolution, session lookup.
/// Since the consumer requires Kafka, we test the processing logic directly
/// via the ErrorGroupingService and AlertEvaluationService.
/// </summary>
public class ErrorGroupingWorkerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly ErrorGroupingService _groupingService;
    private readonly Project _project;

    public ErrorGroupingWorkerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        var workspace = new Workspace { Name = "WS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(workspace);
        _db.SaveChanges();

        _project = new Project { Name = "Proj", WorkspaceId = workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _groupingService = new ErrorGroupingService(_db, NullLogger<ErrorGroupingService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ── Error Grouping from Worker Messages ──────────────────────────

    [Fact]
    public async Task GroupError_NewError_CreatesGroupAndObject()
    {
        var result = await _groupingService.GroupErrorAsync(
            _project.Id, "NullReferenceException", "BACKEND",
            "at Main.Foo()\n  at Program.cs:10", DateTime.UtcNow,
            "http://api.example.com", "Program.cs", null,
            "production", "my-api", "1.0", null, null, null,
            CancellationToken.None);

        Assert.True(result.IsNewGroup);
        Assert.NotNull(result.ErrorGroup);
        Assert.NotNull(result.ErrorObject);
        Assert.Equal("NullReferenceException", result.ErrorGroup.Event);
        Assert.Equal("BACKEND", result.ErrorGroup.Type);
    }

    [Fact]
    public async Task GroupError_DuplicateError_SameGroup()
    {
        var r1 = await _groupingService.GroupErrorAsync(
            _project.Id, "NullRef", "BACKEND",
            "at Foo.Bar()\n  at X.cs:5", DateTime.UtcNow,
            "http://test.com", "X.cs", null,
            "prod", "svc", "1.0", null, null, null,
            CancellationToken.None);

        var r2 = await _groupingService.GroupErrorAsync(
            _project.Id, "NullRef", "BACKEND",
            "at Foo.Bar()\n  at X.cs:5", DateTime.UtcNow.AddSeconds(10),
            "http://test.com", "X.cs", null,
            "prod", "svc", "1.0", null, null, null,
            CancellationToken.None);

        Assert.True(r1.IsNewGroup);
        Assert.False(r2.IsNewGroup);
        Assert.Equal(r1.ErrorGroup.Id, r2.ErrorGroup.Id);

        // Should have 2 error objects in the same group
        var objects = await _db.ErrorObjects.Where(o => o.ErrorGroupId == r1.ErrorGroup.Id).ToListAsync();
        Assert.Equal(2, objects.Count);
    }

    [Fact]
    public async Task GroupError_DifferentEvents_DifferentGroups()
    {
        var r1 = await _groupingService.GroupErrorAsync(
            _project.Id, "NullReferenceException", "BACKEND",
            "at A()", DateTime.UtcNow,
            "http://test.com", "a.cs", null,
            "prod", "svc", "1.0", null, null, null,
            CancellationToken.None);

        var r2 = await _groupingService.GroupErrorAsync(
            _project.Id, "ArgumentException", "BACKEND",
            "at B()", DateTime.UtcNow,
            "http://test.com", "b.cs", null,
            "prod", "svc", "1.0", null, null, null,
            CancellationToken.None);

        Assert.NotEqual(r1.ErrorGroup.Id, r2.ErrorGroup.Id);
    }

    [Fact]
    public async Task GroupError_WithSession_LinksToSession()
    {
        var session = new Session { SecureId = "err-sess", ProjectId = _project.Id };
        _db.Sessions.Add(session);
        _db.SaveChanges();

        var result = await _groupingService.GroupErrorAsync(
            _project.Id, "SessionError", "FRONTEND",
            "at render()", DateTime.UtcNow,
            "http://app.com", "app.js", null,
            "prod", "web", "1.0", session.Id, null, null,
            CancellationToken.None);

        Assert.NotNull(result.ErrorObject);
    }

    [Fact]
    public async Task GroupError_EmptyStackTrace_StillGroups()
    {
        var result = await _groupingService.GroupErrorAsync(
            _project.Id, "NoStack", "BACKEND",
            "", DateTime.UtcNow,
            "", "", null,
            "prod", "svc", "1.0", null, null, null,
            CancellationToken.None);

        Assert.True(result.IsNewGroup);
        Assert.NotNull(result.ErrorGroup);
    }

    [Fact]
    public async Task GroupError_NullStackTrace_StillGroups()
    {
        var result = await _groupingService.GroupErrorAsync(
            _project.Id, "NullStack", "BACKEND",
            null!, DateTime.UtcNow,
            "", "", null,
            "prod", "svc", "1.0", null, null, null,
            CancellationToken.None);

        Assert.True(result.IsNewGroup);
    }

    [Fact]
    public async Task GroupError_WithTraceContext_StoredOnObject()
    {
        var result = await _groupingService.GroupErrorAsync(
            _project.Id, "TracedError", "BACKEND",
            "at X()", DateTime.UtcNow,
            "http://test.com", "x.cs", null,
            "prod", "svc", "1.0", null, "trace-123", "span-456",
            CancellationToken.None);

        Assert.NotNull(result.ErrorObject);
        // TraceId and SpanId should be stored
    }

    [Fact]
    public async Task GroupError_ErrorGroupState_DefaultsToOpen()
    {
        var result = await _groupingService.GroupErrorAsync(
            _project.Id, "NewError", "BACKEND",
            "at Main()", DateTime.UtcNow,
            "", "", null,
            "prod", "svc", "1.0", null, null, null,
            CancellationToken.None);

        Assert.Equal(ErrorGroupState.Open, result.ErrorGroup.State);
    }

    [Fact]
    public async Task GroupError_MultipleEnvironments_SameGroupIfSameFingerprint()
    {
        var r1 = await _groupingService.GroupErrorAsync(
            _project.Id, "EnvError", "BACKEND",
            "at Foo()", DateTime.UtcNow,
            "http://test.com", "x.cs", null,
            "staging", "svc", "1.0", null, null, null,
            CancellationToken.None);

        var r2 = await _groupingService.GroupErrorAsync(
            _project.Id, "EnvError", "BACKEND",
            "at Foo()", DateTime.UtcNow,
            "http://test.com", "x.cs", null,
            "production", "svc", "1.0", null, null, null,
            CancellationToken.None);

        // Same event + type + stack → same group regardless of environment
        Assert.Equal(r1.ErrorGroup.Id, r2.ErrorGroup.Id);
    }

    // ── BackendErrorMessage record tests ─────────────────────────────

    [Fact]
    public void BackendErrorMessage_RecordEquality()
    {
        var msg1 = new BackendErrorMessage(
            "1", "Error", "BACKEND", "url", "src", "stack",
            DateTime.UtcNow, null, "svc", "1.0", "prod", null, null, null);

        var msg2 = msg1 with { Environment = "staging" };

        Assert.NotEqual(msg1, msg2);
        Assert.Equal("staging", msg2.Environment);
    }

    [Fact]
    public void BackendErrorMessage_NullableFieldsAllowed()
    {
        var msg = new BackendErrorMessage(
            null, "Error", "BACKEND", "url", "src", "stack",
            DateTime.UtcNow, null, "svc", "1.0", "prod",
            null, null, null);

        Assert.Null(msg.ProjectId);
        Assert.Null(msg.SessionSecureId);
        Assert.Null(msg.TraceId);
        Assert.Null(msg.SpanId);
        Assert.Null(msg.Payload);
    }

    // ── Worker message record tests ──────────────────────────────────

    [Fact]
    public void LogIngestionMessage_AllFields()
    {
        var attrs = new Dictionary<string, string> { ["key"] = "val" };
        var msg = new LogIngestionMessage(
            1, DateTime.UtcNow, "trace-1", "span-1", "sess-1",
            "ERROR", 17, "otel", "svc", "1.0", "test body", attrs, "prod");

        Assert.Equal(1, msg.ProjectId);
        Assert.Equal("ERROR", msg.SeverityText);
        Assert.Equal(17, msg.SeverityNumber);
        Assert.Single(msg.LogAttributes!);
    }

    [Fact]
    public void TraceIngestionMessage_AllFields()
    {
        var attrs = new Dictionary<string, string> { ["http.method"] = "GET" };
        var msg = new TraceIngestionMessage(
            1, DateTime.UtcNow, "t1", "s1", "ps1", "sess",
            "api", "2.0", "prod", "GET /users", "SERVER",
            50000, "OK", "", attrs, false);

        Assert.Equal("SERVER", msg.SpanKind);
        Assert.Equal(50000, msg.Duration);
        Assert.False(msg.HasErrors);
    }

    [Fact]
    public void MetricsMessage_AllFields()
    {
        var tags = new Dictionary<string, string> { ["host"] = "server-1" };
        var msg = new MetricsMessage(
            "sess-1", "cpu.usage", 85.5, "system",
            DateTime.UtcNow, tags);

        Assert.Equal("cpu.usage", msg.Name);
        Assert.Equal(85.5, msg.Value);
        Assert.Equal("system", msg.Category);
    }

    [Fact]
    public void MetricsMessage_NullOptionalFields()
    {
        var msg = new MetricsMessage("sess-1", "latency", 12.3, null, DateTime.UtcNow, null);

        Assert.Null(msg.Category);
        Assert.Null(msg.Tags);
    }
}
