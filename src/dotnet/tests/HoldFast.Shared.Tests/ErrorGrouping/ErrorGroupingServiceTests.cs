using System.Security.Claims;
using System.Text.Json;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.Shared.ErrorGrouping;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoldFast.Shared.Tests.ErrorGrouping;

/// <summary>
/// Comprehensive tests for ErrorGroupingService: fingerprinting, matching, and grouping.
/// </summary>
public class ErrorGroupingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly ErrorGroupingService _service;
    private readonly Project _project;

    public ErrorGroupingServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        var admin = new Admin { Uid = "eg-uid", Name = "Test", Email = "test@test.com" };
        _db.Admins.Add(admin);
        var workspace = new Workspace { Name = "WS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(workspace);
        _db.SaveChanges();

        _project = new Project { Name = "TestProj", WorkspaceId = workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _service = new ErrorGroupingService(_db, NullLogger<ErrorGroupingService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ── Fingerprinting Tests ───────────────────────────────────────────

    [Fact]
    public void GetFingerprints_NullStackTrace_ReturnsEmpty()
    {
        var result = _service.GetFingerprints(null);
        Assert.Empty(result);
    }

    [Fact]
    public void GetFingerprints_EmptyString_ReturnsEmpty()
    {
        var result = _service.GetFingerprints("");
        Assert.Empty(result);
    }

    [Fact]
    public void GetFingerprints_InvalidJson_ReturnsEmpty()
    {
        var result = _service.GetFingerprints("not-json{{{");
        Assert.Empty(result);
    }

    [Fact]
    public void GetFingerprints_EmptyArray_ReturnsEmpty()
    {
        var result = _service.GetFingerprints("[]");
        Assert.Empty(result);
    }

    [Fact]
    public void GetFingerprints_SingleFrame_CODE_And_META()
    {
        var stack = JsonSerializer.Serialize(new[]
        {
            new
            {
                FileName = "app.js",
                FunctionName = "onClick",
                LineNumber = 42,
                ColumnNumber = 10,
                LineContent = "throw new Error('test');",
                LinesBefore = "function onClick() {",
                LinesAfter = "}",
            }
        });

        var result = _service.GetFingerprints(stack);

        // Should have both CODE and META fingerprints
        Assert.Equal(2, result.Count);

        var code = result.First(f => f.Type == "CODE");
        Assert.Equal(0, code.Index);
        Assert.Contains("throw new Error", code.Value);
        Assert.Contains("function onClick", code.Value); // LinesBefore

        var meta = result.First(f => f.Type == "META");
        Assert.Equal(0, meta.Index);
        Assert.Contains("app.js", meta.Value);
        Assert.Contains("onClick", meta.Value);
        Assert.Contains("42", meta.Value);
    }

    [Fact]
    public void GetFingerprints_MultipleFrames_IndexesCorrect()
    {
        var stack = JsonSerializer.Serialize(new object[]
        {
            new { FileName = "a.js", FunctionName = "foo", LineNumber = 1 },
            new { FileName = "b.js", FunctionName = "bar", LineNumber = 2 },
            new { FileName = "c.js", FunctionName = "baz", LineNumber = 3 },
        });

        var result = _service.GetFingerprints(stack);

        // 3 META fingerprints (no code content)
        Assert.Equal(3, result.Count);
        Assert.All(result, f => Assert.Equal("META", f.Type));
        Assert.Equal(0, result[0].Index);
        Assert.Equal(1, result[1].Index);
        Assert.Equal(2, result[2].Index);
    }

    [Fact]
    public void GetFingerprints_CodeOnly_NoMeta()
    {
        var stack = JsonSerializer.Serialize(new[]
        {
            new { LineContent = "console.log('test');" }
        });

        var result = _service.GetFingerprints(stack);
        Assert.Single(result);
        Assert.Equal("CODE", result[0].Type);
    }

    [Fact]
    public void GetFingerprints_MetaOnly_NoCode()
    {
        var stack = JsonSerializer.Serialize(new[]
        {
            new { FileName = "test.js", FunctionName = "run" }
        });

        var result = _service.GetFingerprints(stack);
        Assert.Single(result);
        Assert.Equal("META", result[0].Type);
    }

    [Fact]
    public void GetFingerprints_EmptyFrame_Skipped()
    {
        var stack = JsonSerializer.Serialize(new[] { new { } });
        var result = _service.GetFingerprints(stack);
        Assert.Empty(result); // Empty frame produces no fingerprints
    }

    [Fact]
    public void GetFingerprints_MaxFrames_Capped()
    {
        var frames = Enumerable.Range(0, 100).Select(i => new
        {
            FileName = $"file{i}.js",
            FunctionName = $"fn{i}",
            LineNumber = i
        }).ToArray();
        var stack = JsonSerializer.Serialize(frames);

        var result = _service.GetFingerprints(stack);
        // Max 50 frames, each with 1 META fingerprint
        Assert.Equal(50, result.Count);
        Assert.Equal(49, result.Last().Index);
    }

    // ── Grouping Tests ──────────────────────────────────────────────────

    [Fact]
    public async Task GroupError_NewError_CreatesNewGroup()
    {
        var result = await _service.GroupErrorAsync(
            _project.Id, "TypeError: undefined", "FRONTEND",
            null, DateTime.UtcNow, "http://test.com", "app.js",
            null, "production", "web", "1.0.0", null, null, null,
            CancellationToken.None);

        Assert.True(result.IsNewGroup);
        Assert.Equal("TypeError: undefined", result.ErrorGroup.Event);
        Assert.Equal("FRONTEND", result.ErrorGroup.Type);
        Assert.Equal(_project.Id, result.ErrorGroup.ProjectId);
        Assert.Equal(ErrorGroupState.Open, result.ErrorGroup.State);
        Assert.NotEmpty(result.ErrorGroup.SecureId);

        // Verify ErrorObject created
        Assert.Equal(result.ErrorGroup.Id, result.ErrorObject.ErrorGroupId);
        Assert.Equal("production", result.ErrorObject.Environment);
    }

    [Fact]
    public async Task GroupError_SameEvent_MatchesExistingGroup()
    {
        // First error creates group
        var first = await _service.GroupErrorAsync(
            _project.Id, "ReferenceError: x is not defined", "FRONTEND",
            null, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        // Second error with same event matches
        var second = await _service.GroupErrorAsync(
            _project.Id, "ReferenceError: x is not defined", "FRONTEND",
            null, DateTime.UtcNow.AddSeconds(1), null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.False(second.IsNewGroup);
        Assert.Equal(first.ErrorGroup.Id, second.ErrorGroup.Id);

        // Two error objects, one group
        Assert.Equal(2, await _db.ErrorObjects.CountAsync());
        Assert.Equal(1, await _db.ErrorGroups.CountAsync());
    }

    [Fact]
    public async Task GroupError_DifferentEvent_CreatesNewGroup()
    {
        await _service.GroupErrorAsync(
            _project.Id, "Error A", "FRONTEND",
            null, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        var second = await _service.GroupErrorAsync(
            _project.Id, "Error B", "FRONTEND",
            null, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.True(second.IsNewGroup);
        Assert.Equal(2, await _db.ErrorGroups.CountAsync());
    }

    [Fact]
    public async Task GroupError_DifferentProject_CreatesNewGroup()
    {
        var otherProject = new Project { Name = "Other", WorkspaceId = _project.WorkspaceId };
        _db.Projects.Add(otherProject);
        await _db.SaveChangesAsync();

        await _service.GroupErrorAsync(
            _project.Id, "Same Error", "FRONTEND",
            null, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        var result = await _service.GroupErrorAsync(
            otherProject.Id, "Same Error", "FRONTEND",
            null, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.True(result.IsNewGroup); // Different project = new group
    }

    [Fact]
    public async Task GroupError_ResolvedGroup_Reopened()
    {
        var first = await _service.GroupErrorAsync(
            _project.Id, "Reopenable Error", "FRONTEND",
            null, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        // Resolve the group
        first.ErrorGroup.State = ErrorGroupState.Resolved;
        _db.ErrorGroups.Update(first.ErrorGroup);
        await _db.SaveChangesAsync();

        // New occurrence of same error
        var second = await _service.GroupErrorAsync(
            _project.Id, "Reopenable Error", "FRONTEND",
            null, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.False(second.IsNewGroup);
        Assert.Equal(first.ErrorGroup.Id, second.ErrorGroup.Id);
        Assert.Equal(ErrorGroupState.Open, second.ErrorGroup.State); // Reopened
    }

    [Fact]
    public async Task GroupError_IgnoredGroup_StaysIgnored()
    {
        var first = await _service.GroupErrorAsync(
            _project.Id, "Ignored Error", "FRONTEND",
            null, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        first.ErrorGroup.State = ErrorGroupState.Ignored;
        _db.ErrorGroups.Update(first.ErrorGroup);
        await _db.SaveChangesAsync();

        // New occurrence matches but doesn't change state
        var second = await _service.GroupErrorAsync(
            _project.Id, "Ignored Error", "FRONTEND",
            null, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.False(second.IsNewGroup);
        Assert.Equal(ErrorGroupState.Ignored, second.ErrorGroup.State);
    }

    [Fact]
    public async Task GroupError_WithStackTrace_StoresFingerprints()
    {
        var stack = JsonSerializer.Serialize(new[]
        {
            new { FileName = "main.js", FunctionName = "handleClick", LineNumber = 100, ColumnNumber = 5 },
            new { FileName = "util.js", FunctionName = "process", LineNumber = 50, ColumnNumber = 3 },
        });

        var result = await _service.GroupErrorAsync(
            _project.Id, "Stack Error", "FRONTEND",
            stack, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.True(result.IsNewGroup);

        var fingerprints = await _db.ErrorFingerprints
            .Where(f => f.ErrorGroupId == result.ErrorGroup.Id)
            .ToListAsync();

        Assert.True(fingerprints.Count >= 2); // At least 2 META fingerprints
        Assert.All(fingerprints, f => Assert.Equal(_project.Id, f.ProjectId));
    }

    [Fact]
    public async Task GroupError_FingerprintMatching_SameStack()
    {
        var stack = JsonSerializer.Serialize(new[]
        {
            new { FileName = "app.js", FunctionName = "doStuff", LineNumber = 42, ColumnNumber = 1 },
            new { FileName = "lib.js", FunctionName = "helper", LineNumber = 10, ColumnNumber = 2 },
        });

        var first = await _service.GroupErrorAsync(
            _project.Id, "Fingerprint Error", "FRONTEND",
            stack, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        // Same event + same stack = same group
        var second = await _service.GroupErrorAsync(
            _project.Id, "Fingerprint Error", "FRONTEND",
            stack, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.False(second.IsNewGroup);
        Assert.Equal(first.ErrorGroup.Id, second.ErrorGroup.Id);
    }

    [Fact]
    public async Task GroupError_LongEvent_Truncated()
    {
        var longEvent = new string('X', 20_000);

        var result = await _service.GroupErrorAsync(
            _project.Id, longEvent, "FRONTEND",
            null, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.Equal(10_000, result.ErrorGroup.Event!.Length);
        Assert.Equal(10_000, result.ErrorObject.Event!.Length);
    }

    [Fact]
    public async Task GroupError_WithSession_SetsSessionId()
    {
        var session = new Session { ProjectId = _project.Id, SecureId = "test-session" };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var result = await _service.GroupErrorAsync(
            _project.Id, "Session Error", "BACKEND",
            null, DateTime.UtcNow, null, null, null, null, "api-svc", "2.0", session.Id, "trace-123", "span-456",
            CancellationToken.None);

        Assert.Equal(session.Id, result.ErrorObject.SessionId);
        Assert.Equal("trace-123", result.ErrorObject.TraceExternalId);
        Assert.Equal("span-456", result.ErrorObject.SpanId);
        Assert.Equal("api-svc", result.ErrorObject.ServiceName);
    }

    [Fact]
    public async Task GroupError_MultipleOccurrences_CountsCorrectly()
    {
        for (int i = 0; i < 10; i++)
        {
            await _service.GroupErrorAsync(
                _project.Id, "Repeated Error", "FRONTEND",
                null, DateTime.UtcNow.AddMinutes(i), null, null, null, null, null, null, null, null, null,
                CancellationToken.None);
        }

        Assert.Equal(1, await _db.ErrorGroups.CountAsync());
        Assert.Equal(10, await _db.ErrorObjects.CountAsync());
    }

    [Fact]
    public async Task GroupError_SecureId_Unique()
    {
        var result1 = await _service.GroupErrorAsync(
            _project.Id, "Error One", "FRONTEND",
            null, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        var result2 = await _service.GroupErrorAsync(
            _project.Id, "Error Two", "FRONTEND",
            null, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.NotEqual(result1.ErrorGroup.SecureId, result2.ErrorGroup.SecureId);
        Assert.Equal(32, result1.ErrorGroup.SecureId.Length); // 16 bytes hex
    }

    [Fact]
    public async Task GroupError_ServiceName_StoredOnGroup()
    {
        var result = await _service.GroupErrorAsync(
            _project.Id, "Backend Error", "BACKEND",
            null, DateTime.UtcNow, null, null, null, null, "payment-svc", "3.1.0", null, null, null,
            CancellationToken.None);

        Assert.Equal("payment-svc", result.ErrorGroup.ServiceName);
    }

    [Fact]
    public async Task GroupError_NullOptionalFields_Works()
    {
        var result = await _service.GroupErrorAsync(
            _project.Id, "Minimal Error", "FRONTEND",
            null, DateTime.UtcNow,
            null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.True(result.IsNewGroup);
        Assert.Null(result.ErrorObject.Url);
        Assert.Null(result.ErrorObject.Environment);
        Assert.Null(result.ErrorObject.SessionId);
    }

    // ── Fingerprint Matching Edge Cases ─────────────────────────────────

    [Fact]
    public async Task FindMatchingGroup_NoGroups_ReturnsNull()
    {
        var result = await _service.FindMatchingGroupAsync(
            _project.Id, "Nonexistent", [], CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindMatchingGroup_EventMatchOnly_BelowThreshold()
    {
        // Create a group with event match but no fingerprint matches
        _db.ErrorGroups.Add(new ErrorGroup
        {
            ProjectId = _project.Id,
            Event = "Event Match Only",
            Type = "FRONTEND",
            State = ErrorGroupState.Open,
            SecureId = "test-secure-1",
        });
        await _db.SaveChangesAsync();

        // Event match gives 100 points but threshold is 10 + rest frames
        // With fingerprints requiring first frame match, this should still match
        // because threshold = 10 + 0 - 1 = 9, and score is 100 > 9
        var fingerprints = new List<ErrorFingerprintEntry>();
        var result = await _service.FindMatchingGroupAsync(
            _project.Id, "Event Match Only", fingerprints, CancellationToken.None);

        Assert.NotNull(result); // 100 > 10 threshold with no rest frames
    }

    [Fact]
    public async Task FindMatchingGroup_PreferNewerGroupOnTie()
    {
        var group1 = new ErrorGroup
        {
            ProjectId = _project.Id, Event = "Tied Error",
            Type = "FRONTEND", State = ErrorGroupState.Open, SecureId = "old-group",
        };
        _db.ErrorGroups.Add(group1);
        await _db.SaveChangesAsync();

        var group2 = new ErrorGroup
        {
            ProjectId = _project.Id, Event = "Tied Error",
            Type = "FRONTEND", State = ErrorGroupState.Open, SecureId = "new-group",
        };
        _db.ErrorGroups.Add(group2);
        await _db.SaveChangesAsync();

        var result = await _service.FindMatchingGroupAsync(
            _project.Id, "Tied Error", [], CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(group2.Id, result!.Id); // Newer group preferred
    }

    // ── Error Object Field Tests ────────────────────────────────────────

    [Fact]
    public async Task GroupError_AllFieldsPopulated()
    {
        var stack = JsonSerializer.Serialize(new[]
        {
            new { FileName = "api.js", FunctionName = "handler", LineNumber = 1 }
        });

        var timestamp = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var result = await _service.GroupErrorAsync(
            _project.Id,
            "TypeError: Cannot read property 'x'",
            "BACKEND",
            stack,
            timestamp,
            "https://api.example.com/endpoint",
            "api.js",
            "{\"extra\":\"data\"}",
            "staging",
            "user-service",
            "2.5.0",
            null,
            "trace-abc",
            "span-def",
            CancellationToken.None);

        var obj = result.ErrorObject;
        Assert.Equal("TypeError: Cannot read property 'x'", obj.Event);
        Assert.Equal("BACKEND", obj.Type);
        Assert.Equal(stack, obj.StackTrace);
        Assert.Equal(timestamp, obj.Timestamp);
        Assert.Equal("https://api.example.com/endpoint", obj.Url);
        Assert.Equal("api.js", obj.Source);
        Assert.Equal("{\"extra\":\"data\"}", obj.Payload);
        Assert.Equal("staging", obj.Environment);
        Assert.Equal("user-service", obj.ServiceName);
        Assert.Equal("2.5.0", obj.ServiceVersion);
        Assert.Equal("trace-abc", obj.TraceExternalId);
        Assert.Equal("span-def", obj.SpanId);
    }

    [Fact]
    public async Task GroupError_FingerprintsReplaced_OnNewMatch()
    {
        var stack1 = JsonSerializer.Serialize(new[]
        {
            new { FileName = "old.js", FunctionName = "oldFn", LineNumber = 1 }
        });

        var first = await _service.GroupErrorAsync(
            _project.Id, "Evolving Error", "FRONTEND",
            stack1, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        var firstFps = await _db.ErrorFingerprints
            .Where(f => f.ErrorGroupId == first.ErrorGroup.Id)
            .CountAsync();

        var stack2 = JsonSerializer.Serialize(new[]
        {
            new { FileName = "new.js", FunctionName = "newFn", LineNumber = 2 }
        });

        // Same event → matches same group, fingerprints get replaced
        await _service.GroupErrorAsync(
            _project.Id, "Evolving Error", "FRONTEND",
            stack2, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        var finalFps = await _db.ErrorFingerprints
            .Where(f => f.ErrorGroupId == first.ErrorGroup.Id)
            .ToListAsync();

        // Fingerprints should be from stack2, not stack1
        Assert.All(finalFps, f => Assert.Contains("new.js", f.Value));
    }

    [Fact]
    public async Task GroupError_EmptyEvent_StillWorks()
    {
        var result = await _service.GroupErrorAsync(
            _project.Id, "", "FRONTEND",
            null, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.True(result.IsNewGroup);
        Assert.Equal("", result.ErrorGroup.Event);
    }

    [Fact]
    public async Task GroupError_UnicodeEvent()
    {
        var result = await _service.GroupErrorAsync(
            _project.Id, "エラー: データが見つかりません 🔥", "FRONTEND",
            null, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.True(result.IsNewGroup);
        Assert.Contains("エラー", result.ErrorGroup.Event);
    }
}
