using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Shared.SessionProcessing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoldFast.Shared.Tests.SessionProcessing;

/// <summary>
/// Edge case and stress tests for SessionProcessingService.
/// </summary>
public class SessionProcessingEdgeCaseTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly SessionProcessingService _service;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public SessionProcessingEdgeCaseTests()
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

        _project = new Project
        {
            Name = "Proj", WorkspaceId = _workspace.Id,
            RageClickWindowSeconds = 5, RageClickRadiusPixels = 8, RageClickCount = 5,
        };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _service = new SessionProcessingService(_db, NullLogger<SessionProcessingService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private Session CreateSession()
    {
        var session = new Session { SecureId = Guid.NewGuid().ToString("N"), ProjectId = _project.Id };
        _db.Sessions.Add(session);
        _db.SaveChanges();
        return session;
    }

    private void AddChunks(int sessionId, params long[] timestamps)
    {
        for (int i = 0; i < timestamps.Length; i++)
            _db.EventChunks.Add(new EventChunk { SessionId = sessionId, ChunkIndex = i, Timestamp = timestamps[i] });
        _db.SaveChanges();
    }

    // ── Interval edge cases ──────────────────────────────────────────────

    [Fact]
    public void ComputeIntervals_TwoTimestampsExactly10sApart_SingleActive()
    {
        var result = SessionProcessingService.ComputeIntervals([0, 10000]);
        Assert.Single(result);
        Assert.True(result[0].Active);
    }

    [Fact]
    public void ComputeIntervals_TwoTimestamps10001msApart_ThreeIntervals()
    {
        var result = SessionProcessingService.ComputeIntervals([0, 10001]);
        Assert.Equal(3, result.Count);
        Assert.True(result[0].Active);
        Assert.False(result[1].Active);
        Assert.True(result[2].Active);
    }

    [Fact]
    public void ComputeIntervals_DuplicateTimestamps_SingleInterval()
    {
        var result = SessionProcessingService.ComputeIntervals([5000, 5000, 5000, 5000]);
        Assert.Single(result);
        Assert.Equal(5000, result[0].StartTime);
        Assert.Equal(5000, result[0].EndTime);
    }

    [Fact]
    public void ComputeIntervals_AlternatingActiveInactive_ManyIntervals()
    {
        // Active 5s, gap 15s, active 5s, gap 15s, active 5s
        var timestamps = new List<long>
        {
            0, 1000, 2000, 3000, 4000, 5000,      // active
            25000, 26000, 27000, 28000, 29000,      // active after gap
            50000, 51000, 52000, 53000, 54000,      // active after gap
        };
        var result = SessionProcessingService.ComputeIntervals(timestamps);
        var inactiveCount = result.Count(i => !i.Active);
        Assert.Equal(2, inactiveCount);
    }

    [Fact]
    public void ComputeIntervals_1000Timestamps_NoStackOverflow()
    {
        var timestamps = Enumerable.Range(0, 1000).Select(i => (long)(i * 100)).ToList();
        var result = SessionProcessingService.ComputeIntervals(timestamps);
        Assert.NotEmpty(result);
    }

    // ── Merge edge cases ─────────────────────────────────────────────────

    [Fact]
    public void MergeSmallInactiveGaps_ExactlyAtThreshold_NotMerged()
    {
        // Session 100000ms, gap 2000ms (exactly 2% = threshold)
        var intervals = new List<SessionProcessingService.IntervalEntry>
        {
            new(0, 49000, true),
            new(49000, 51000, false), // 2000ms = 2% of 100000
            new(51000, 100000, true),
        };
        var result = SessionProcessingService.MergeSmallInactiveGaps(intervals, 100000);
        // At exactly threshold, should NOT be merged (< threshold, not <=)
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void MergeSmallInactiveGaps_JustBelowThreshold_Merged()
    {
        var intervals = new List<SessionProcessingService.IntervalEntry>
        {
            new(0, 49000, true),
            new(49000, 50999, false), // 1999ms < 2% of 100000 = 2000
            new(50999, 100000, true),
        };
        var result = SessionProcessingService.MergeSmallInactiveGaps(intervals, 100000);
        Assert.Single(result); // Merged into single active
    }

    [Fact]
    public void MergeAdjacentActive_SingleInactive_StaysInactive()
    {
        var intervals = new List<SessionProcessingService.IntervalEntry>
        {
            new(0, 5000, false),
        };
        var result = SessionProcessingService.MergeAdjacentActive(intervals);
        Assert.Single(result);
        Assert.False(result[0].Active);
    }

    // ── Rage click edge cases ────────────────────────────────────────────

    [Fact]
    public void DetectRageClicks_ExactlyAtWindowBoundary_Detected()
    {
        // 5 clicks at t=0, 1000, 2000, 3000, 4000 (4s gap, window=5s)
        var timestamps = new List<long> { 1000, 2000, 3000, 4000, 5000 };
        var result = SessionProcessingService.DetectRageClicks(timestamps, _project);
        Assert.Single(result);
    }

    [Fact]
    public void DetectRageClicks_JustOutsideWindow_NotDetected()
    {
        // 5 clicks spread over 5001ms (window=5000ms)
        var timestamps = new List<long> { 1000, 2250, 3500, 4750, 6001 };
        var result = SessionProcessingService.DetectRageClicks(timestamps, _project);
        Assert.Empty(result);
    }

    [Fact]
    public void DetectRageClicks_LargeCluster_CountsAll()
    {
        // 20 rapid clicks
        var timestamps = Enumerable.Range(0, 20).Select(i => (long)(1000 + i * 100)).ToList();
        var result = SessionProcessingService.DetectRageClicks(timestamps, _project);
        Assert.NotEmpty(result);
        Assert.True(result[0].TotalClicks >= 5);
    }

    [Fact]
    public void DetectRageClicks_HighThresholdProject_NoFalsePositives()
    {
        var strictProject = new Project
        {
            Name = "Strict", WorkspaceId = _workspace.Id,
            RageClickWindowSeconds = 1, RageClickRadiusPixels = 2, RageClickCount = 20,
        };
        var timestamps = Enumerable.Range(0, 15).Select(i => (long)(1000 + i * 50)).ToList();
        var result = SessionProcessingService.DetectRageClicks(timestamps, strictProject);
        Assert.Empty(result); // 15 < 20 threshold
    }

    [Fact]
    public void DetectRageClicks_SingleClick_NoRageClick()
    {
        var result = SessionProcessingService.DetectRageClicks([5000], _project);
        Assert.Empty(result);
    }

    // ── Histogram edge cases ─────────────────────────────────────────────

    [Fact]
    public void BuildHistogram_VeryShotSession_HandlesCorrectly()
    {
        // 2 events 1ms apart
        var result = SessionProcessingService.BuildEventCountHistogram([1000, 1001]);
        Assert.Equal(100, result.Length);
        Assert.Equal(2, result.Sum()); // Both events counted
    }

    [Fact]
    public void BuildHistogram_LastEventOnBoundary_InLastBucket()
    {
        var timestamps = new List<long> { 0, 50000, 100000 };
        var result = SessionProcessingService.BuildEventCountHistogram(timestamps);
        Assert.Equal(1, result[99]); // Last timestamp in last bucket
    }

    [Fact]
    public void BuildHistogram_1000Events_AllCounted()
    {
        var timestamps = Enumerable.Range(0, 1000).Select(i => (long)(i * 100)).ToList();
        var result = SessionProcessingService.BuildEventCountHistogram(timestamps);
        Assert.Equal(1000, result.Sum());
    }

    // ── Full processing edge cases ───────────────────────────────────────

    [Fact]
    public async Task ProcessSession_AllTimestampsZero_FilteredOut()
    {
        var session = CreateSession();
        AddChunks(session.Id, 0, 0, 0);
        var result = await _service.ProcessSessionAsync(session.Id, CancellationToken.None);
        // All filtered by t > 0
        Assert.Equal(0, result.IntervalsCreated);
    }

    [Fact]
    public async Task ProcessSession_SingleTimestamp_MinimalResult()
    {
        var session = CreateSession();
        AddChunks(session.Id, 5000);
        var result = await _service.ProcessSessionAsync(session.Id, CancellationToken.None);
        Assert.Equal(0, result.TotalLengthMs); // Only 1 timestamp, no duration
    }

    [Fact]
    public async Task ProcessSession_ChunksOutOfOrder_SortedCorrectly()
    {
        var session = CreateSession();
        // Add chunks with out-of-order timestamps
        _db.EventChunks.Add(new EventChunk { SessionId = session.Id, ChunkIndex = 2, Timestamp = 3000 });
        _db.EventChunks.Add(new EventChunk { SessionId = session.Id, ChunkIndex = 0, Timestamp = 1000 });
        _db.EventChunks.Add(new EventChunk { SessionId = session.Id, ChunkIndex = 1, Timestamp = 2000 });
        _db.SaveChanges();

        var result = await _service.ProcessSessionAsync(session.Id, CancellationToken.None);
        Assert.Equal(2000, result.TotalLengthMs);
    }

    [Fact]
    public async Task ProcessSession_MultipleProjects_Independent()
    {
        var project2 = new Project
        {
            Name = "P2", WorkspaceId = _workspace.Id,
            RageClickWindowSeconds = 5, RageClickRadiusPixels = 8, RageClickCount = 5,
        };
        _db.Projects.Add(project2);
        _db.SaveChanges();

        var session1 = new Session { SecureId = "s1", ProjectId = _project.Id };
        var session2 = new Session { SecureId = "s2", ProjectId = project2.Id };
        _db.Sessions.AddRange(session1, session2);
        _db.SaveChanges();

        AddChunks(session1.Id, 1000, 2000, 3000);
        AddChunks(session2.Id, 1000, 5000, 10000);

        var result1 = await _service.ProcessSessionAsync(session1.Id, CancellationToken.None);
        var result2 = await _service.ProcessSessionAsync(session2.Id, CancellationToken.None);

        Assert.Equal(2000, result1.TotalLengthMs);
        Assert.Equal(9000, result2.TotalLengthMs);
    }

    [Fact]
    public async Task ProcessSession_Idempotent_SameResultsOnReprocess()
    {
        var session = CreateSession();
        AddChunks(session.Id, 1000, 2000, 3000, 4000, 5000);

        var result1 = await _service.ProcessSessionAsync(session.Id, CancellationToken.None);
        var result2 = await _service.ProcessSessionAsync(session.Id, CancellationToken.None);

        Assert.Equal(result1.IntervalsCreated, result2.IntervalsCreated);
        Assert.Equal(result1.ActiveLengthMs, result2.ActiveLengthMs);
        Assert.Equal(result1.TotalLengthMs, result2.TotalLengthMs);
        Assert.Equal(result1.RageClicksDetected, result2.RageClicksDetected);
    }

    [Fact]
    public async Task ProcessSession_RageClicksStoredWithCorrectProjectId()
    {
        var session = CreateSession();
        AddChunks(session.Id, 1000, 1200, 1400, 1600, 1800, 2000);

        await _service.ProcessSessionAsync(session.Id, CancellationToken.None);

        var rageClicks = await _db.RageClickEvents
            .Where(r => r.SessionId == session.Id)
            .ToListAsync();

        Assert.All(rageClicks, rc => Assert.Equal(_project.Id, rc.ProjectId));
    }

    [Fact]
    public async Task ProcessSession_IntervalDurationsCorrect()
    {
        var session = CreateSession();
        AddChunks(session.Id, 1000, 2000, 3000);

        await _service.ProcessSessionAsync(session.Id, CancellationToken.None);

        var intervals = await _db.SessionIntervals
            .Where(i => i.SessionId == session.Id)
            .ToListAsync();

        Assert.All(intervals, i => Assert.Equal(i.EndTime - i.StartTime, i.Duration));
    }
}
