using HoldFast.Data;
using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;
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
/// Tests for DataSyncWorker: syncing PostgreSQL data to ClickHouse.
/// Uses a hand-rolled FakeClickHouseService instead of Moq.
/// </summary>
public class DataSyncWorkerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly FakeClickHouseService _fakeClickHouse;
    private readonly DataSyncWorker _worker;
    private readonly Workspace _workspace;
    private readonly Project _project;
    private readonly IServiceScopeFactory _scopeFactory;

    public DataSyncWorkerTests()
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

        _fakeClickHouse = new FakeClickHouseService();

        var services = new ServiceCollection();
        services.AddSingleton(new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options);
        services.AddScoped(sp => new HoldFastDbContext(sp.GetRequiredService<DbContextOptions<HoldFastDbContext>>()));
        services.AddSingleton<IClickHouseService>(_fakeClickHouse);

        _scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        _worker = new DataSyncWorker(_scopeFactory, NullLogger<DataSyncWorker>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ── Session sync tests ──────────────────────────────────────────────

    [Fact]
    public async Task ProcessedSessions_AreSynced()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "sync-sess-1",
            ProjectId = _project.Id,
            Processed = true,
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
        });
        _db.SaveChanges();

        await _worker.RunSyncAsync(CancellationToken.None);

        Assert.Single(_fakeClickHouse.WrittenSessions);
        Assert.Equal("sync-sess-1", _fakeClickHouse.WrittenSessions[0].SecureSessionId);
    }

    [Fact]
    public async Task UnprocessedSessions_AreSkipped()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "unprocessed-sess",
            ProjectId = _project.Id,
            Processed = false,
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
        });
        _db.SaveChanges();

        await _worker.RunSyncAsync(CancellationToken.None);

        Assert.Empty(_fakeClickHouse.WrittenSessions);
    }

    [Fact]
    public async Task SessionsOutsideSyncWindow_AreSkipped()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "old-processed-sess",
            ProjectId = _project.Id,
            Processed = true,
            UpdatedAt = DateTime.UtcNow.AddHours(-2), // outside 1-hour window
        });
        _db.SaveChanges();

        await _worker.RunSyncAsync(CancellationToken.None);

        Assert.Empty(_fakeClickHouse.WrittenSessions);
    }

    [Fact]
    public async Task SessionFields_MappedCorrectly()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "fields-sess",
            ProjectId = _project.Id,
            Processed = true,
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
            Identifier = "user@test.com",
            OSName = "Windows",
            BrowserName = "Chrome",
            City = "NYC",
            Environment = "production",
            ActiveLength = 120,
            Length = 300,
            PagesVisited = 5,
            HasErrors = true,
            HasRageClicks = false,
            FirstTime = 1,
        });
        _db.SaveChanges();

        await _worker.RunSyncAsync(CancellationToken.None);

        var row = Assert.Single(_fakeClickHouse.WrittenSessions);
        Assert.Equal("user@test.com", row.Identifier);
        Assert.Equal("Windows", row.OSName);
        Assert.Equal("Chrome", row.BrowserName);
        Assert.Equal("NYC", row.City);
        Assert.Equal("production", row.Environment);
        Assert.Equal(120, row.ActiveLength);
        Assert.Equal(300, row.Length);
        Assert.Equal(5, row.PagesVisited);
        Assert.True(row.HasErrors);
        Assert.False(row.HasRageClicks);
        Assert.True(row.FirstTime);
    }

    // ── Error group sync tests ──────────────────────────────────────────

    [Fact]
    public async Task ErrorGroups_AreSynced()
    {
        _db.ErrorGroups.Add(new ErrorGroup
        {
            ProjectId = _project.Id,
            Event = "NullRef",
            Type = "BACKEND",
            State = ErrorGroupState.Open,
            SecureId = "eg-1",
            UpdatedAt = DateTime.UtcNow.AddMinutes(-10),
        });
        _db.SaveChanges();

        await _worker.RunSyncAsync(CancellationToken.None);

        Assert.Single(_fakeClickHouse.WrittenErrorGroups);
        Assert.Equal("NullRef", _fakeClickHouse.WrittenErrorGroups[0].Event);
    }

    [Fact]
    public async Task ErrorGroupsOutsideSyncWindow_AreSkipped()
    {
        _db.ErrorGroups.Add(new ErrorGroup
        {
            ProjectId = _project.Id,
            Event = "OldError",
            Type = "BACKEND",
            State = ErrorGroupState.Open,
            SecureId = "eg-old",
            UpdatedAt = DateTime.UtcNow.AddHours(-2),
        });
        _db.SaveChanges();

        await _worker.RunSyncAsync(CancellationToken.None);

        Assert.Empty(_fakeClickHouse.WrittenErrorGroups);
    }

    // ── Error object sync tests ──────────────────────────────────────────

    [Fact]
    public async Task ErrorObjects_AreSynced()
    {
        var group = new ErrorGroup
        {
            ProjectId = _project.Id,
            Event = "Err",
            Type = "BACKEND",
            State = ErrorGroupState.Open,
            SecureId = "eg-for-obj",
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
        };
        _db.ErrorGroups.Add(group);
        _db.SaveChanges();

        _db.ErrorObjects.Add(new ErrorObject
        {
            ProjectId = _project.Id,
            ErrorGroupId = group.Id,
            Event = "NullReference",
            Timestamp = DateTime.UtcNow.AddMinutes(-3),
            CreatedAt = DateTime.UtcNow.AddMinutes(-3),
        });
        _db.SaveChanges();

        await _worker.RunSyncAsync(CancellationToken.None);

        Assert.Single(_fakeClickHouse.WrittenErrorObjects);
        Assert.Equal("NullReference", _fakeClickHouse.WrittenErrorObjects[0].Event);
    }

    [Fact]
    public async Task ErrorObjectsOutsideSyncWindow_AreSkipped()
    {
        var group = new ErrorGroup
        {
            ProjectId = _project.Id,
            Event = "Err",
            Type = "BACKEND",
            State = ErrorGroupState.Open,
            SecureId = "eg-for-old-obj",
            UpdatedAt = DateTime.UtcNow.AddHours(-2),
        };
        _db.ErrorGroups.Add(group);
        _db.SaveChanges();

        _db.ErrorObjects.Add(new ErrorObject
        {
            ProjectId = _project.Id,
            ErrorGroupId = group.Id,
            Event = "OldError",
            Timestamp = DateTime.UtcNow.AddHours(-2),
            CreatedAt = DateTime.UtcNow.AddHours(-2),
        });
        _db.SaveChanges();

        await _worker.RunSyncAsync(CancellationToken.None);

        Assert.Empty(_fakeClickHouse.WrittenErrorObjects);
    }

    // ── Edge cases ──────────────────────────────────────────────────────

    [Fact]
    public async Task NoData_NoCrash()
    {
        // Remove all data
        _db.Projects.RemoveRange(_db.Projects);
        _db.Workspaces.RemoveRange(_db.Workspaces);
        _db.SaveChanges();

        await _worker.RunSyncAsync(CancellationToken.None);

        Assert.Empty(_fakeClickHouse.WrittenSessions);
        Assert.Empty(_fakeClickHouse.WrittenErrorGroups);
        Assert.Empty(_fakeClickHouse.WrittenErrorObjects);
    }

    [Fact]
    public async Task BatchSizeLimiting_Works()
    {
        // Create more than BatchSize (500) processed sessions
        for (int i = 0; i < 510; i++)
        {
            _db.Sessions.Add(new Session
            {
                SecureId = $"batch-sess-{i}",
                ProjectId = _project.Id,
                Processed = true,
                UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
            });
        }
        _db.SaveChanges();

        await _worker.RunSyncAsync(CancellationToken.None);

        // Should be capped at BatchSize (500)
        Assert.Equal(DataSyncWorker.BatchSize, _fakeClickHouse.WrittenSessions.Count);
    }

    [Fact]
    public async Task MultipleDataTypes_AllSynced()
    {
        // Add a processed session
        _db.Sessions.Add(new Session
        {
            SecureId = "multi-sess",
            ProjectId = _project.Id,
            Processed = true,
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
        });

        // Add an error group
        var group = new ErrorGroup
        {
            ProjectId = _project.Id,
            Event = "MultiErr",
            Type = "FRONTEND",
            State = ErrorGroupState.Open,
            SecureId = "multi-eg",
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
        };
        _db.ErrorGroups.Add(group);
        _db.SaveChanges();

        // Add an error object
        _db.ErrorObjects.Add(new ErrorObject
        {
            ProjectId = _project.Id,
            ErrorGroupId = group.Id,
            Event = "MultiErrObj",
            Timestamp = DateTime.UtcNow.AddMinutes(-3),
            CreatedAt = DateTime.UtcNow.AddMinutes(-3),
        });
        _db.SaveChanges();

        await _worker.RunSyncAsync(CancellationToken.None);

        Assert.Single(_fakeClickHouse.WrittenSessions);
        Assert.Single(_fakeClickHouse.WrittenErrorGroups);
        Assert.Single(_fakeClickHouse.WrittenErrorObjects);
    }

    [Fact]
    public async Task NullSessionFields_HandleGracefully()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "null-fields-sess",
            ProjectId = _project.Id,
            Processed = true,
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
            // All nullable fields left null
            Identifier = null,
            OSName = null,
            BrowserName = null,
            City = null,
            Environment = null,
            ActiveLength = null,
            Length = null,
            PagesVisited = null,
            HasErrors = null,
            HasRageClicks = null,
            FirstTime = null,
        });
        _db.SaveChanges();

        await _worker.RunSyncAsync(CancellationToken.None);

        var row = Assert.Single(_fakeClickHouse.WrittenSessions);
        Assert.Equal(0, row.ActiveLength);
        Assert.Equal(0, row.Length);
        Assert.Equal(0, row.PagesVisited);
        Assert.False(row.HasErrors);
        Assert.False(row.HasRageClicks);
        Assert.False(row.FirstTime);
    }

    [Fact]
    public async Task ClickHouseWriteFailure_DoesNotCrashWorker()
    {
        _fakeClickHouse.FailOnWrite = true;

        _db.Sessions.Add(new Session
        {
            SecureId = "fail-sess",
            ProjectId = _project.Id,
            Processed = true,
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
        });
        _db.SaveChanges();

        // RunSyncAsync should throw, but ExecuteAsync would catch it
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _worker.RunSyncAsync(CancellationToken.None));
    }
}

/// <summary>
/// Hand-rolled fake for IClickHouseService that records all write calls.
/// </summary>
internal class FakeClickHouseService : IClickHouseService
{
    public List<SessionRowInput> WrittenSessions { get; } = [];
    public List<ErrorGroupRowInput> WrittenErrorGroups { get; } = [];
    public List<ErrorObjectRowInput> WrittenErrorObjects { get; } = [];
    public bool FailOnWrite { get; set; }

    public Task WriteSessionsAsync(IEnumerable<SessionRowInput> sessions, CancellationToken ct)
    {
        if (FailOnWrite) throw new InvalidOperationException("Simulated ClickHouse write failure");
        WrittenSessions.AddRange(sessions);
        return Task.CompletedTask;
    }

    public Task WriteErrorGroupsAsync(IEnumerable<ErrorGroupRowInput> errorGroups, CancellationToken ct)
    {
        if (FailOnWrite) throw new InvalidOperationException("Simulated ClickHouse write failure");
        WrittenErrorGroups.AddRange(errorGroups);
        return Task.CompletedTask;
    }

    public Task WriteErrorObjectsAsync(IEnumerable<ErrorObjectRowInput> errorObjects, CancellationToken ct)
    {
        if (FailOnWrite) throw new InvalidOperationException("Simulated ClickHouse write failure");
        WrittenErrorObjects.AddRange(errorObjects);
        return Task.CompletedTask;
    }

    // ── Read methods (not used by DataSyncWorker, stub out) ─────────────

    public Task<LogConnection> ReadLogsAsync(int projectId, QueryInput query, ClickHousePagination pagination, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<List<HistogramBucket>> ReadLogsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<List<string>> GetLogKeysAsync(int projectId, QueryInput query, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<List<string>> GetLogKeyValuesAsync(int projectId, string key, QueryInput query, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<TraceConnection> ReadTracesAsync(int projectId, QueryInput query, ClickHousePagination pagination, bool omitBody, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<List<HistogramBucket>> ReadTracesHistogramAsync(int projectId, QueryInput query, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<List<string>> GetTraceKeysAsync(int projectId, QueryInput query, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<List<string>> GetTraceKeyValuesAsync(int projectId, string key, QueryInput query, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<List<HistogramBucket>> ReadSessionsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<(List<int> Ids, long Total)> QuerySessionIdsAsync(int projectId, QueryInput query, int count, int page, string? sortField, bool sortDesc, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<(List<int> Ids, long Total)> QueryErrorGroupIdsAsync(int projectId, QueryInput query, int count, int page, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<List<HistogramBucket>> ReadErrorObjectsHistogramAsync(int projectId, QueryInput query, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<MetricsBuckets> ReadMetricsAsync(int projectId, QueryInput query, string bucketBy, List<string>? groupBy, string aggregator, string? column, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<List<QueryKey>> GetSessionsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<List<string>> GetSessionsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<List<QueryKey>> GetErrorsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<List<string>> GetErrorsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<List<QueryKey>> GetEventsKeysAsync(int projectId, DateTime startDate, DateTime endDate, string? query, string? eventName, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<List<string>> GetEventsKeyValuesAsync(int projectId, string keyName, DateTime startDate, DateTime endDate, string? query, int? count, string? eventName, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task WriteMetricAsync(int projectId, string metricName, double metricValue, string? category, DateTime timestamp, Dictionary<string, string>? tags, string? sessionSecureId, CancellationToken ct) =>
        Task.CompletedTask;

    public Task WriteLogsAsync(IEnumerable<LogRowInput> logs, CancellationToken ct) =>
        Task.CompletedTask;

    public Task WriteTracesAsync(IEnumerable<TraceRowInput> traces, CancellationToken ct) =>
        Task.CompletedTask;
}
