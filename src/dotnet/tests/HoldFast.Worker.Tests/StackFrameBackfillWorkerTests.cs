using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.Worker;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoldFast.Worker.Tests;

/// <summary>
/// Tests for StackFrameBackfillWorker: backfills MappedStackTrace from StackTrace
/// for ErrorObjects and their parent ErrorGroups that have no mapped trace yet.
/// </summary>
public class StackFrameBackfillWorkerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly StackFrameBackfillWorker _worker;
    private readonly Workspace _workspace;
    private readonly Project _project;
    private readonly IServiceScopeFactory _scopeFactory;

    public StackFrameBackfillWorkerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        _workspace = new Workspace
        {
            Name = "WS",
            PlanTier = "Enterprise",
            RetentionPeriod = RetentionPeriod.ThirtyDays,
            ErrorsRetentionPeriod = RetentionPeriod.ThirtyDays,
        };
        _db.Workspaces.Add(_workspace);
        _db.SaveChanges();

        _project = new Project { Name = "Proj", WorkspaceId = _workspace.Id };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        var services = new ServiceCollection();
        services.AddSingleton(new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options);
        services.AddScoped(sp => new HoldFastDbContext(
            sp.GetRequiredService<DbContextOptions<HoldFastDbContext>>()));

        _scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        _worker = new StackFrameBackfillWorker(
            _scopeFactory,
            NullLogger<StackFrameBackfillWorker>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private ErrorGroup AddErrorGroup(string? stackTrace = null, string? mappedStackTrace = null)
    {
        var group = new ErrorGroup
        {
            ProjectId = _project.Id,
            Event = "TestError",
            Type = "BACKEND",
            State = ErrorGroupState.Open,
            SecureId = Guid.NewGuid().ToString("N"),
            StackTrace = stackTrace,
            MappedStackTrace = mappedStackTrace,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.ErrorGroups.Add(group);
        _db.SaveChanges();
        return group;
    }

    private ErrorObject AddErrorObject(ErrorGroup group, string? stackTrace = null, string? mappedStackTrace = null)
    {
        var obj = new ErrorObject
        {
            ProjectId = _project.Id,
            ErrorGroupId = group.Id,
            Event = "TestError",
            Timestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            StackTrace = stackTrace,
            MappedStackTrace = mappedStackTrace,
        };
        _db.ErrorObjects.Add(obj);
        _db.SaveChanges();
        return obj;
    }

    // ── Happy-path tests ────────────────────────────────────────────────

    [Fact]
    public async Task RunBackfillAsync_ObjectWithStackTrace_MappedStackTraceCopied()
    {
        var group = AddErrorGroup();
        var obj = AddErrorObject(group, stackTrace: "at Foo()\nat Bar()");

        var count = await _worker.RunBackfillAsync(CancellationToken.None);

        Assert.Equal(1, count);
        _db.Entry(obj).Reload();
        Assert.Equal("at Foo()\nat Bar()", obj.MappedStackTrace);
    }

    [Fact]
    public async Task RunBackfillAsync_AlsoUpdatesParentErrorGroup()
    {
        var group = AddErrorGroup(stackTrace: null, mappedStackTrace: null);
        AddErrorObject(group, stackTrace: "at Main()");

        await _worker.RunBackfillAsync(CancellationToken.None);

        _db.Entry(group).Reload();
        Assert.Equal("at Main()", group.MappedStackTrace);
    }

    [Fact]
    public async Task RunBackfillAsync_GroupAlreadyHasMappedTrace_GroupNotOverwritten()
    {
        var group = AddErrorGroup(mappedStackTrace: "existing-mapped");
        AddErrorObject(group, stackTrace: "new-trace");

        await _worker.RunBackfillAsync(CancellationToken.None);

        _db.Entry(group).Reload();
        // Group already has MappedStackTrace — should not be overwritten
        Assert.Equal("existing-mapped", group.MappedStackTrace);
    }

    // ── Skip conditions ─────────────────────────────────────────────────

    [Fact]
    public async Task RunBackfillAsync_ObjectWithNullStackTrace_Skipped()
    {
        var group = AddErrorGroup();
        AddErrorObject(group, stackTrace: null, mappedStackTrace: null);

        var count = await _worker.RunBackfillAsync(CancellationToken.None);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task RunBackfillAsync_ObjectAlreadyHasMappedTrace_Skipped()
    {
        var group = AddErrorGroup();
        AddErrorObject(group, stackTrace: "trace", mappedStackTrace: "already-mapped");

        var count = await _worker.RunBackfillAsync(CancellationToken.None);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task RunBackfillAsync_EmptyDatabase_ReturnsZero()
    {
        var count = await _worker.RunBackfillAsync(CancellationToken.None);
        Assert.Equal(0, count);
    }

    // ── Multiple objects / batch tests ──────────────────────────────────

    [Fact]
    public async Task RunBackfillAsync_MultipleObjects_AllProcessed()
    {
        var group = AddErrorGroup();
        AddErrorObject(group, stackTrace: "trace-1");
        AddErrorObject(group, stackTrace: "trace-2");
        AddErrorObject(group, stackTrace: "trace-3");

        var count = await _worker.RunBackfillAsync(CancellationToken.None);

        Assert.Equal(3, count);
        var objects = _db.ErrorObjects.AsNoTracking().ToList();
        Assert.All(objects, o => Assert.NotNull(o.MappedStackTrace));
    }

    [Fact]
    public async Task RunBackfillAsync_BatchSizeLimiting_CapsAtBatchSize()
    {
        var group = AddErrorGroup();
        for (int i = 0; i < StackFrameBackfillWorker.BatchSize + 10; i++)
            AddErrorObject(group, stackTrace: $"trace-{i}");

        var count = await _worker.RunBackfillAsync(CancellationToken.None);

        Assert.Equal(StackFrameBackfillWorker.BatchSize, count);
    }

    [Fact]
    public async Task RunBackfillAsync_MixOfProcessedAndUnprocessed_OnlyUnprocessedHandled()
    {
        var group = AddErrorGroup();
        AddErrorObject(group, stackTrace: "needs-mapping");
        AddErrorObject(group, stackTrace: "already-done", mappedStackTrace: "done");
        AddErrorObject(group, stackTrace: null);

        var count = await _worker.RunBackfillAsync(CancellationToken.None);

        Assert.Equal(1, count); // Only the one needing mapping
    }

    // ── Multiple error groups ────────────────────────────────────────────

    [Fact]
    public async Task RunBackfillAsync_MultipleGroups_EachGetsFirstSampleTrace()
    {
        var group1 = AddErrorGroup();
        var group2 = AddErrorGroup();

        AddErrorObject(group1, stackTrace: "trace-group1");
        AddErrorObject(group2, stackTrace: "trace-group2");

        await _worker.RunBackfillAsync(CancellationToken.None);

        _db.Entry(group1).Reload();
        _db.Entry(group2).Reload();
        Assert.Equal("trace-group1", group1.MappedStackTrace);
        Assert.Equal("trace-group2", group2.MappedStackTrace);
    }

    // ── Edge cases ───────────────────────────────────────────────────────

    [Fact]
    public async Task RunBackfillAsync_SecondCallAfterFirstProcessed_ReturnsZero()
    {
        var group = AddErrorGroup();
        AddErrorObject(group, stackTrace: "trace");

        var first = await _worker.RunBackfillAsync(CancellationToken.None);
        var second = await _worker.RunBackfillAsync(CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(0, second); // All already have MappedStackTrace
    }

    [Fact]
    public async Task RunBackfillAsync_CancellationToken_PropagatedToDb()
    {
        var group = AddErrorGroup();
        AddErrorObject(group, stackTrace: "trace");

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancelled

        // EF will throw OperationCanceledException on pre-cancelled token
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _worker.RunBackfillAsync(cts.Token));
    }

    [Fact]
    public void Worker_HasCorrectIntervals()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), StackFrameBackfillWorker.Interval);
        Assert.Equal(200, StackFrameBackfillWorker.BatchSize);
    }
}
