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
/// Edge case and stress tests for ErrorGroupingService.
/// Forced failures, boundary conditions, Unicode, concurrent grouping, deep stacks.
/// </summary>
public class ErrorGroupingEdgeCaseTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly ErrorGroupingService _service;
    private readonly Project _project;

    public ErrorGroupingEdgeCaseTests()
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

        _project = new Project { Name = "EdgeCase", WorkspaceId = workspace.Id };
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

    // ── Fingerprint Edge Cases ─────────────────────────────────────────

    [Fact]
    public void GetFingerprints_NullJsonArray_HandlesGracefully()
    {
        // [null] deserializes to a list with one null StackFrame
        // GetFingerprints should handle this without crashing
        var result = _service.GetFingerprints("[null]");
        // null frame may produce empty fingerprints or be skipped
        Assert.NotNull(result); // Just verify no crash
    }

    [Fact]
    public void GetFingerprints_MixedValidInvalid()
    {
        // Array with one valid, one empty frame
        var stack = "[{\"FileName\":\"a.js\",\"FunctionName\":\"fn\"},{},{}]";
        var result = _service.GetFingerprints(stack);
        Assert.Single(result); // Only the valid frame produces a fingerprint
    }

    [Fact]
    public void GetFingerprints_OnlyLineContent_ProducesCODE()
    {
        var stack = JsonSerializer.Serialize(new[]
        {
            new { LineContent = "  throw new Error('boom');" }
        });
        var result = _service.GetFingerprints(stack);
        Assert.Single(result);
        Assert.Equal("CODE", result[0].Type);
        Assert.Contains("throw new Error", result[0].Value);
    }

    [Fact]
    public void GetFingerprints_OnlyLinesBeforeAfter_ProducesCODE()
    {
        var stack = JsonSerializer.Serialize(new[]
        {
            new { LinesBefore = "try {", LinesAfter = "} catch (e) {" }
        });
        var result = _service.GetFingerprints(stack);
        Assert.Single(result);
        Assert.Equal("CODE", result[0].Type);
        Assert.Contains("try {", result[0].Value);
    }

    [Fact]
    public void GetFingerprints_VeryLongFunctionName()
    {
        var longName = new string('x', 10_000);
        var stack = JsonSerializer.Serialize(new[]
        {
            new { FileName = "test.js", FunctionName = longName }
        });
        var result = _service.GetFingerprints(stack);
        Assert.Single(result);
        Assert.Contains(longName, result[0].Value);
    }

    [Fact]
    public void GetFingerprints_SpecialCharactersInFileName()
    {
        var stack = JsonSerializer.Serialize(new[]
        {
            new { FileName = "node_modules/@scope/lib/dist/index.min.js", FunctionName = "anonymous" }
        });
        var result = _service.GetFingerprints(stack);
        Assert.Contains("@scope", result[0].Value);
    }

    [Fact]
    public void GetFingerprints_ZeroLineNumber()
    {
        var stack = JsonSerializer.Serialize(new[]
        {
            new { FileName = "test.js", LineNumber = 0, ColumnNumber = 0 }
        });
        var result = _service.GetFingerprints(stack);
        Assert.Single(result);
        Assert.Contains("0", result[0].Value);
    }

    [Fact]
    public void GetFingerprints_NegativeLineNumber()
    {
        var stack = JsonSerializer.Serialize(new[]
        {
            new { FileName = "test.js", LineNumber = -1 }
        });
        var result = _service.GetFingerprints(stack);
        Assert.Single(result);
        Assert.Contains("-1", result[0].Value);
    }

    [Fact]
    public void GetFingerprints_UnicodeContent()
    {
        var stack = JsonSerializer.Serialize(new[]
        {
            new { FileName = "コンポーネント.tsx", FunctionName = "レンダー", LineContent = "エラーが発生しました" }
        });
        var result = _service.GetFingerprints(stack);
        Assert.Equal(2, result.Count); // CODE + META
    }

    [Fact]
    public void GetFingerprints_EmojisInContent()
    {
        var stack = JsonSerializer.Serialize(new[]
        {
            new { FileName = "🔥.js", FunctionName = "💥", LineContent = "🚀 launch()" }
        });
        var result = _service.GetFingerprints(stack);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetFingerprints_WhitespaceOnlyContent()
    {
        var stack = JsonSerializer.Serialize(new[]
        {
            new { LineContent = "   \t\n   " }
        });
        var result = _service.GetFingerprints(stack);
        Assert.Single(result); // Whitespace is still content
    }

    [Fact]
    public void GetFingerprints_ExactlyMaxFrames()
    {
        var frames = Enumerable.Range(0, 50).Select(i => new
        {
            FileName = $"file{i}.js", FunctionName = $"fn{i}"
        }).ToArray();
        var stack = JsonSerializer.Serialize(frames);
        var result = _service.GetFingerprints(stack);
        Assert.Equal(50, result.Count);
    }

    [Fact]
    public void GetFingerprints_OneOverMaxFrames()
    {
        var frames = Enumerable.Range(0, 51).Select(i => new
        {
            FileName = $"file{i}.js", FunctionName = $"fn{i}"
        }).ToArray();
        var stack = JsonSerializer.Serialize(frames);
        var result = _service.GetFingerprints(stack);
        Assert.Equal(50, result.Count); // Capped at max
    }

    // ── Grouping Edge Cases ────────────────────────────────────────────

    [Fact]
    public async Task GroupError_VeryLongStackTrace()
    {
        var frames = Enumerable.Range(0, 200).Select(i => new
        {
            FileName = $"file{i}.js",
            FunctionName = $"function{i}",
            LineNumber = i,
            LineContent = $"code at line {i}"
        }).ToArray();
        var stack = JsonSerializer.Serialize(frames);

        var result = await _service.GroupErrorAsync(
            _project.Id, "Deep Stack Error", "FRONTEND",
            stack, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.True(result.IsNewGroup);
        // Should have max 50*2 fingerprints (CODE + META per frame, capped at 50 frames)
        var fps = await _db.ErrorFingerprints.Where(f => f.ErrorGroupId == result.ErrorGroup.Id).CountAsync();
        Assert.True(fps <= 100);
        Assert.True(fps > 0);
    }

    [Fact]
    public async Task GroupError_SameStackDifferentEvent_CreatesNewGroup()
    {
        var stack = JsonSerializer.Serialize(new[]
        {
            new { FileName = "shared.js", FunctionName = "common", LineNumber = 1 }
        });

        var first = await _service.GroupErrorAsync(
            _project.Id, "Error Type A", "FRONTEND",
            stack, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        // Same stack but different event — fingerprint match alone gives 10 points
        // but threshold is also 10 (10 + max(0,0) - 1 = 9, but minScore floors to 10)
        // 10 is NOT > 10, so no match → new group
        var second = await _service.GroupErrorAsync(
            _project.Id, "Error Type B", "FRONTEND",
            stack, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.True(second.IsNewGroup);
        Assert.NotEqual(first.ErrorGroup.Id, second.ErrorGroup.Id);
    }

    [Fact]
    public async Task GroupError_ConcurrentSameEvent_SameGroup()
    {
        // Simulate concurrent errors with same event
        var tasks = Enumerable.Range(0, 5).Select(i =>
            _service.GroupErrorAsync(
                _project.Id, $"Concurrent Error {i % 2}", "FRONTEND",
                null, DateTime.UtcNow.AddMilliseconds(i), null, null, null, null, null, null, null, null, null,
                CancellationToken.None));

        var results = await Task.WhenAll(tasks);

        // Should have exactly 2 groups (one per unique event)
        var groupIds = results.Select(r => r.ErrorGroup.Id).Distinct().ToList();
        Assert.Equal(2, groupIds.Count);
        Assert.Equal(5, await _db.ErrorObjects.CountAsync());
    }

    [Fact]
    public async Task GroupError_MinTimestamp()
    {
        var result = await _service.GroupErrorAsync(
            _project.Id, "Min Time Error", "FRONTEND",
            null, DateTime.MinValue, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.Equal(DateTime.MinValue, result.ErrorObject.Timestamp);
    }

    [Fact]
    public async Task GroupError_MaxTimestamp()
    {
        var result = await _service.GroupErrorAsync(
            _project.Id, "Max Time Error", "FRONTEND",
            null, DateTime.MaxValue, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.Equal(DateTime.MaxValue, result.ErrorObject.Timestamp);
    }

    [Fact]
    public async Task GroupError_SpecialCharsInEvent()
    {
        var specialEvent = "Error: Can't read property 'foo' of null\n\tat Object.<anonymous> (test.js:1:1)";
        var result = await _service.GroupErrorAsync(
            _project.Id, specialEvent, "FRONTEND",
            null, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.Equal(specialEvent, result.ErrorGroup.Event);
    }

    [Fact]
    public async Task GroupError_SqlInjectionInEvent_Safe()
    {
        var maliciousEvent = "'; DROP TABLE error_groups; --";
        var result = await _service.GroupErrorAsync(
            _project.Id, maliciousEvent, "FRONTEND",
            null, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.True(result.IsNewGroup);
        Assert.Equal(maliciousEvent, result.ErrorGroup.Event);
        // Verify table still exists
        Assert.Equal(1, await _db.ErrorGroups.CountAsync());
    }

    [Fact]
    public async Task GroupError_SqlInjectionInStackTrace_Safe()
    {
        var maliciousStack = JsonSerializer.Serialize(new[]
        {
            new
            {
                FileName = "'; DROP TABLE error_fingerprints; --",
                FunctionName = "SELECT * FROM admins",
                LineNumber = 1
            }
        });

        var result = await _service.GroupErrorAsync(
            _project.Id, "SQL Injection Test", "FRONTEND",
            maliciousStack, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.True(result.IsNewGroup);
        // Verify tables still exist
        Assert.True(await _db.ErrorFingerprints.CountAsync() > 0);
    }

    [Fact]
    public async Task GroupError_HugePayload()
    {
        var hugePayload = new string('X', 1_000_000); // 1MB payload
        var result = await _service.GroupErrorAsync(
            _project.Id, "Huge Payload Error", "FRONTEND",
            null, DateTime.UtcNow, null, null, hugePayload, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.Equal(hugePayload, result.ErrorObject.Payload);
    }

    [Fact]
    public async Task GroupError_AllFieldsNull()
    {
        var result = await _service.GroupErrorAsync(
            _project.Id, "", "",
            null, default, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.True(result.IsNewGroup);
    }

    [Fact]
    public async Task GroupError_RapidFireSameError()
    {
        // 100 errors in rapid succession
        for (int i = 0; i < 100; i++)
        {
            await _service.GroupErrorAsync(
                _project.Id, "Rapid Fire Error", "FRONTEND",
                null, DateTime.UtcNow.AddTicks(i), null, null, null, null, null, null, null, null, null,
                CancellationToken.None);
        }

        Assert.Equal(1, await _db.ErrorGroups.CountAsync());
        Assert.Equal(100, await _db.ErrorObjects.CountAsync());
    }

    [Fact]
    public async Task GroupError_ManyDistinctErrors()
    {
        // 50 distinct errors
        for (int i = 0; i < 50; i++)
        {
            await _service.GroupErrorAsync(
                _project.Id, $"Distinct Error #{i}", "FRONTEND",
                null, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
                CancellationToken.None);
        }

        Assert.Equal(50, await _db.ErrorGroups.CountAsync());
        Assert.Equal(50, await _db.ErrorObjects.CountAsync());
    }

    [Fact]
    public async Task GroupError_StackTraceIsEmptyArray()
    {
        var result = await _service.GroupErrorAsync(
            _project.Id, "Empty Stack Error", "FRONTEND",
            "[]", DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.True(result.IsNewGroup);
        Assert.Equal(0, await _db.ErrorFingerprints.CountAsync());
    }

    [Fact]
    public async Task GroupError_StackTraceIsObject_NotArray()
    {
        var result = await _service.GroupErrorAsync(
            _project.Id, "Object Stack Error", "FRONTEND",
            "{\"frame\":\"data\"}", DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.True(result.IsNewGroup);
        Assert.Equal(0, await _db.ErrorFingerprints.CountAsync()); // Can't parse as array
    }

    [Fact]
    public async Task GroupError_StackTraceIsNumber()
    {
        var result = await _service.GroupErrorAsync(
            _project.Id, "Number Stack Error", "FRONTEND",
            "42", DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.True(result.IsNewGroup);
        Assert.Equal(0, await _db.ErrorFingerprints.CountAsync());
    }

    [Fact]
    public async Task GroupError_VeryLongUrl()
    {
        var longUrl = "https://example.com/" + new string('a', 50_000);
        var result = await _service.GroupErrorAsync(
            _project.Id, "Long URL Error", "FRONTEND",
            null, DateTime.UtcNow, longUrl, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        Assert.Equal(longUrl, result.ErrorObject.Url);
    }

    [Fact]
    public async Task GroupError_AllEnvironments()
    {
        // null and "" both produce "Env Error " event, so they match the same group
        var environments = new[] { "production", "staging", "development", "test", "local", null, "" };
        foreach (var env in environments)
        {
            await _service.GroupErrorAsync(
                _project.Id, $"Env Error {env}", "FRONTEND",
                null, DateTime.UtcNow, null, null, null, env, null, null, null, null, null,
                CancellationToken.None);
        }

        // "Env Error " and "Env Error " (null→"") match → 6 distinct groups, not 7
        Assert.Equal(6, await _db.ErrorGroups.CountAsync());
        Assert.Equal(7, await _db.ErrorObjects.CountAsync()); // But 7 error objects
    }

    [Fact]
    public async Task GroupError_BackendType()
    {
        var result = await _service.GroupErrorAsync(
            _project.Id, "Go panic", "BACKEND",
            null, DateTime.UtcNow, null, null, null, null, "backend-svc", "1.0", null, "trace-1", "span-1",
            CancellationToken.None);

        Assert.Equal("BACKEND", result.ErrorGroup.Type);
        Assert.Equal("BACKEND", result.ErrorObject.Type);
    }

    [Fact]
    public async Task FindMatchingGroup_WrongProject_NoMatch()
    {
        // Create group in our project
        await _service.GroupErrorAsync(
            _project.Id, "Project Scoped Error", "FRONTEND",
            null, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        // Search in nonexistent project
        var result = await _service.FindMatchingGroupAsync(
            99999, "Project Scoped Error", [], CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GroupError_Fingerprint_CodeSemicolonSeparation()
    {
        var stack = JsonSerializer.Serialize(new[]
        {
            new
            {
                LinesBefore = "line before",
                LineContent = "throw err;",
                LinesAfter = "line after",
                FileName = "test.js",
                FunctionName = "fn",
                LineNumber = 10
            }
        });

        await _service.GroupErrorAsync(
            _project.Id, "Semicolon Test", "FRONTEND",
            stack, DateTime.UtcNow, null, null, null, null, null, null, null, null, null,
            CancellationToken.None);

        var codeFp = await _db.ErrorFingerprints
            .FirstOrDefaultAsync(f => f.Type == "CODE");

        Assert.NotNull(codeFp);
        // Should be "line before;throw err;;line after" (semicolon separated)
        Assert.Equal("line before;throw err;;line after", codeFp!.Value);

        var metaFp = await _db.ErrorFingerprints
            .FirstOrDefaultAsync(f => f.Type == "META");

        Assert.NotNull(metaFp);
        Assert.Equal("test.js;fn;10", metaFp!.Value);
    }
}
