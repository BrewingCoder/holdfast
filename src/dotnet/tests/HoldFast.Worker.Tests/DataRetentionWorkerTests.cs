using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.Worker;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoldFast.Worker.Tests;

/// <summary>
/// Tests for DataRetentionWorker: retention period cleanup of sessions and errors.
/// </summary>
public class DataRetentionWorkerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly DataRetentionWorker _worker;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public DataRetentionWorkerTests()
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
        services.AddScoped(sp => new HoldFastDbContext(sp.GetRequiredService<DbContextOptions<HoldFastDbContext>>()));

        var config = new ConfigurationBuilder().Build();
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        _worker = new DataRetentionWorker(scopeFactory, config, NullLogger<DataRetentionWorker>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    [Fact]
    public async Task Retention_OldUnviewedSession_Deleted()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "old-sess",
            ProjectId = _project.Id,
            CreatedAt = DateTime.UtcNow.AddDays(-60),
            ViewedByAdmins = 0,
        });
        _db.SaveChanges();

        await _worker.RunRetentionCleanupAsync(CancellationToken.None);

        Assert.Empty(await _db.Sessions.ToListAsync());
    }

    [Fact]
    public async Task Retention_OldViewedSession_NotDeleted()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "viewed-sess",
            ProjectId = _project.Id,
            CreatedAt = DateTime.UtcNow.AddDays(-60),
            ViewedByAdmins = 1,
        });
        _db.SaveChanges();

        await _worker.RunRetentionCleanupAsync(CancellationToken.None);

        Assert.Single(await _db.Sessions.ToListAsync());
    }

    [Fact]
    public async Task Retention_RecentSession_NotDeleted()
    {
        _db.Sessions.Add(new Session
        {
            SecureId = "recent-sess",
            ProjectId = _project.Id,
            CreatedAt = DateTime.UtcNow.AddDays(-5),
            ViewedByAdmins = 0,
        });
        _db.SaveChanges();

        await _worker.RunRetentionCleanupAsync(CancellationToken.None);

        Assert.Single(await _db.Sessions.ToListAsync());
    }

    [Fact]
    public async Task Retention_DeletesRelatedData()
    {
        var session = new Session
        {
            SecureId = "del-sess",
            ProjectId = _project.Id,
            CreatedAt = DateTime.UtcNow.AddDays(-60),
            ViewedByAdmins = 0,
        };
        _db.Sessions.Add(session);
        _db.SaveChanges();

        _db.EventChunks.Add(new EventChunk { SessionId = session.Id, ChunkIndex = 0, Timestamp = 1000 });
        _db.SessionIntervals.Add(new SessionInterval
        {
            SessionId = session.Id, StartTime = 0, EndTime = 100, Duration = 100, Active = true
        });
        _db.RageClickEvents.Add(new RageClickEvent
        {
            ProjectId = _project.Id, SessionId = session.Id,
            TotalClicks = 5, StartTimestamp = 0, EndTimestamp = 100,
        });
        _db.SaveChanges();

        await _worker.RunRetentionCleanupAsync(CancellationToken.None);

        Assert.Empty(await _db.EventChunks.ToListAsync());
        Assert.Empty(await _db.SessionIntervals.ToListAsync());
        Assert.Empty(await _db.RageClickEvents.ToListAsync());
    }

    [Fact]
    public async Task Retention_ResolvedErrorGroups_Deleted()
    {
        var group = new ErrorGroup
        {
            ProjectId = _project.Id,
            Event = "OldError",
            Type = "BACKEND",
            State = ErrorGroupState.Resolved,
            SecureId = "g1",
            UpdatedAt = DateTime.UtcNow.AddDays(-60),
        };
        _db.ErrorGroups.Add(group);
        _db.SaveChanges();

        await _worker.RunRetentionCleanupAsync(CancellationToken.None);

        Assert.Empty(await _db.ErrorGroups.ToListAsync());
    }

    [Fact]
    public async Task Retention_OpenErrorGroups_NotDeleted()
    {
        var group = new ErrorGroup
        {
            ProjectId = _project.Id,
            Event = "ActiveError",
            Type = "BACKEND",
            State = ErrorGroupState.Open,
            SecureId = "g2",
            UpdatedAt = DateTime.UtcNow.AddDays(-60),
        };
        _db.ErrorGroups.Add(group);
        _db.SaveChanges();

        await _worker.RunRetentionCleanupAsync(CancellationToken.None);

        Assert.Single(await _db.ErrorGroups.ToListAsync());
    }

    // ── GetRetentionCutoff tests ─────────────────────────────────────────

    [Theory]
    [InlineData(RetentionPeriod.SevenDays, 7)]
    [InlineData(RetentionPeriod.ThirtyDays, 30)]
    public void GetRetentionCutoff_CorrectDays(RetentionPeriod period, int expectedDaysAgo)
    {
        var cutoff = DataRetentionWorker.GetRetentionCutoff(period);
        var expectedApprox = DateTime.UtcNow.AddDays(-expectedDaysAgo);
        Assert.True(Math.Abs((cutoff - expectedApprox).TotalMinutes) < 1);
    }

    [Fact]
    public void GetRetentionCutoff_SixMonths_Approximate()
    {
        var cutoff = DataRetentionWorker.GetRetentionCutoff(RetentionPeriod.SixMonths);
        Assert.True(cutoff < DateTime.UtcNow.AddDays(-150)); // ~6 months
    }

    [Fact]
    public async Task Retention_NoWorkspaces_NoCrash()
    {
        // Remove all workspaces
        _db.Projects.RemoveRange(_db.Projects);
        _db.Workspaces.RemoveRange(_db.Workspaces);
        _db.SaveChanges();

        await _worker.RunRetentionCleanupAsync(CancellationToken.None);
        // Should complete without error
    }
}
