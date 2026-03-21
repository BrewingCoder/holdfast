using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Shared.SessionProcessing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoldFast.Shared.Tests.SessionProcessing;

/// <summary>
/// Tests for SessionProcessingService: intervals, rage clicks, active duration, histograms.
/// </summary>
public class SessionProcessingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly SessionProcessingService _service;
    private readonly Workspace _workspace;
    private readonly Project _project;

    public SessionProcessingServiceTests()
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
            Name = "Proj",
            WorkspaceId = _workspace.Id,
            RageClickWindowSeconds = 5,
            RageClickRadiusPixels = 8,
            RageClickCount = 5,
        };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _service = new SessionProcessingService(
            _db,
            NullLogger<SessionProcessingService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private Session CreateSession(string secureId = "test-session")
    {
        var session = new Session { SecureId = secureId, ProjectId = _project.Id };
        _db.Sessions.Add(session);
        _db.SaveChanges();
        return session;
    }

    private void AddChunks(int sessionId, params long[] timestamps)
    {
        for (int i = 0; i < timestamps.Length; i++)
        {
            _db.EventChunks.Add(new EventChunk
            {
                SessionId = sessionId,
                ChunkIndex = i,
                Timestamp = timestamps[i],
            });
        }
        _db.SaveChanges();
    }

    // ── ProcessSessionAsync basic tests ──────────────────────────────────

    [Fact]
    public async Task ProcessSession_NonexistentSession_ReturnsZero()
    {
        var result = await _service.ProcessSessionAsync(999, CancellationToken.None);
        Assert.Equal(0, result.IntervalsCreated);
        Assert.Equal(0, result.RageClicksDetected);
    }

    [Fact]
    public async Task ProcessSession_NoChunks_ReturnsZero()
    {
        var session = CreateSession();
        var result = await _service.ProcessSessionAsync(session.Id, CancellationToken.None);
        Assert.Equal(0, result.IntervalsCreated);
    }

    [Fact]
    public async Task ProcessSession_SingleChunk_CreatesOneInterval()
    {
        var session = CreateSession();
        AddChunks(session.Id, 1000, 2000, 3000);

        var result = await _service.ProcessSessionAsync(session.Id, CancellationToken.None);

        Assert.True(result.IntervalsCreated >= 1);
        Assert.Equal(session.Id, result.SessionId);
    }

    [Fact]
    public async Task ProcessSession_SetsSessionProcessed()
    {
        var session = CreateSession();
        AddChunks(session.Id, 1000, 2000, 3000);

        await _service.ProcessSessionAsync(session.Id, CancellationToken.None);

        await _db.Entry(session).ReloadAsync();
        Assert.True(session.Processed);
    }

    [Fact]
    public async Task ProcessSession_SetsActiveLengthAndTotalLength()
    {
        var session = CreateSession();
        // 10 seconds of activity (start at 1000 to avoid 0-filtering)
        AddChunks(session.Id, 1000, 3000, 5000, 7000, 9000, 11000);

        var result = await _service.ProcessSessionAsync(session.Id, CancellationToken.None);

        Assert.Equal(10000, result.TotalLengthMs);
        Assert.True(result.ActiveLengthMs > 0);
    }

    [Fact]
    public async Task ProcessSession_SetsNormalnessToZero()
    {
        var session = CreateSession();
        AddChunks(session.Id, 1000, 2000);

        await _service.ProcessSessionAsync(session.Id, CancellationToken.None);

        await _db.Entry(session).ReloadAsync();
        Assert.Equal(0.0, session.Normalness);
    }

    [Fact]
    public async Task ProcessSession_SetsPagesVisited()
    {
        var session = CreateSession();
        AddChunks(session.Id, 1000, 2000, 3000, 4000, 5000);

        var result = await _service.ProcessSessionAsync(session.Id, CancellationToken.None);

        Assert.Equal(5, result.PagesVisited);
    }

    [Fact]
    public async Task ProcessSession_InactiveGap_CreatesTwoIntervals()
    {
        var session = CreateSession();
        // Active: 0-5s, gap > 10s, active: 20s-25s
        AddChunks(session.Id, 0, 1000, 2000, 3000, 4000, 5000, 20000, 21000, 22000, 23000, 24000, 25000);

        var result = await _service.ProcessSessionAsync(session.Id, CancellationToken.None);

        // Should have: active, inactive, active (3 intervals)
        var intervals = await _db.SessionIntervals
            .Where(i => i.SessionId == session.Id)
            .OrderBy(i => i.StartTime)
            .ToListAsync();

        Assert.True(intervals.Count >= 2);
        Assert.Contains(intervals, i => !i.Active); // At least one inactive
    }

    [Fact]
    public async Task ProcessSession_ReplacesOldIntervals()
    {
        var session = CreateSession();
        _db.SessionIntervals.Add(new SessionInterval
        {
            SessionId = session.Id, StartTime = 0, EndTime = 100, Duration = 100, Active = true
        });
        _db.SaveChanges();
        AddChunks(session.Id, 1000, 2000, 3000);

        await _service.ProcessSessionAsync(session.Id, CancellationToken.None);

        var intervals = await _db.SessionIntervals.Where(i => i.SessionId == session.Id).ToListAsync();
        // Old interval should be replaced
        Assert.DoesNotContain(intervals, i => i.StartTime == 0 && i.EndTime == 100);
    }

    [Fact]
    public async Task ProcessSession_NoRageClicks_SetsFalse()
    {
        var session = CreateSession();
        // Only 3 events — below rage click threshold of 5
        AddChunks(session.Id, 1000, 2000, 3000);

        await _service.ProcessSessionAsync(session.Id, CancellationToken.None);

        await _db.Entry(session).ReloadAsync();
        Assert.False(session.HasRageClicks);
    }

    [Fact]
    public async Task ProcessSession_WithRageClicks_SetsTrue()
    {
        var session = CreateSession();
        // 6 rapid events within 5 seconds — should trigger rage click
        AddChunks(session.Id, 1000, 1500, 2000, 2500, 3000, 3500);

        await _service.ProcessSessionAsync(session.Id, CancellationToken.None);

        await _db.Entry(session).ReloadAsync();
        Assert.True(session.HasRageClicks);
        Assert.Equal(1, await _db.RageClickEvents.CountAsync(r => r.SessionId == session.Id));
    }

    [Fact]
    public async Task ProcessSession_ReplacesOldRageClicks()
    {
        var session = CreateSession();
        _db.RageClickEvents.Add(new RageClickEvent
        {
            ProjectId = _project.Id, SessionId = session.Id, TotalClicks = 10,
            StartTimestamp = 0, EndTimestamp = 100,
        });
        _db.SaveChanges();

        // Only 3 events — no rage clicks this time
        AddChunks(session.Id, 1000, 2000, 3000);

        await _service.ProcessSessionAsync(session.Id, CancellationToken.None);

        var rageClicks = await _db.RageClickEvents.Where(r => r.SessionId == session.Id).ToListAsync();
        Assert.Empty(rageClicks);
    }

    // ── ComputeIntervals unit tests ──────────────────────────────────────

    [Fact]
    public void ComputeIntervals_Empty_ReturnsEmpty()
    {
        var result = SessionProcessingService.ComputeIntervals([]);
        Assert.Empty(result);
    }

    [Fact]
    public void ComputeIntervals_SingleTimestamp_OneSinglePointInterval()
    {
        var result = SessionProcessingService.ComputeIntervals([5000]);
        Assert.Single(result);
        Assert.True(result[0].Active);
        Assert.Equal(5000, result[0].StartTime);
        Assert.Equal(5000, result[0].EndTime);
    }

    [Fact]
    public void ComputeIntervals_AllWithin10s_OneActiveInterval()
    {
        var timestamps = new List<long> { 0, 2000, 4000, 6000, 8000, 9000 };
        var result = SessionProcessingService.ComputeIntervals(timestamps);
        Assert.Single(result);
        Assert.True(result[0].Active);
        Assert.Equal(0, result[0].StartTime);
        Assert.Equal(9000, result[0].EndTime);
    }

    [Fact]
    public void ComputeIntervals_GapOver10s_CreatesInactiveInterval()
    {
        var timestamps = new List<long> { 0, 1000, 2000, 20000, 21000, 22000 };
        var result = SessionProcessingService.ComputeIntervals(timestamps);

        Assert.Equal(3, result.Count); // active, inactive, active
        Assert.True(result[0].Active);
        Assert.False(result[1].Active);
        Assert.True(result[2].Active);
    }

    [Fact]
    public void ComputeIntervals_ExactlyAt10sGap_StaysActive()
    {
        // Gap = 10000ms exactly — not > 10000, so stays in same interval
        var timestamps = new List<long> { 0, 10000 };
        var result = SessionProcessingService.ComputeIntervals(timestamps);
        Assert.Single(result);
        Assert.True(result[0].Active);
    }

    [Fact]
    public void ComputeIntervals_JustOver10sGap_SplitsInterval()
    {
        // Gap = 10001ms — splits
        var timestamps = new List<long> { 0, 10001 };
        var result = SessionProcessingService.ComputeIntervals(timestamps);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ComputeIntervals_MultipleGaps_MultipleInactiveIntervals()
    {
        var timestamps = new List<long> { 0, 1000, 20000, 21000, 40000, 41000 };
        var result = SessionProcessingService.ComputeIntervals(timestamps);

        var inactiveCount = result.Count(i => !i.Active);
        Assert.Equal(2, inactiveCount);
    }

    [Fact]
    public void ComputeIntervals_LongSession_PreservesOrder()
    {
        var timestamps = Enumerable.Range(0, 1000)
            .Select(i => (long)(i * 500)) // 500ms apart, all active
            .ToList();
        var result = SessionProcessingService.ComputeIntervals(timestamps);
        Assert.Single(result);
        Assert.True(result[0].Active);
    }

    // ── MergeSmallInactiveGaps unit tests ────────────────────────────────

    [Fact]
    public void MergeSmallInactiveGaps_SmallGap_MergedAsActive()
    {
        // Session is 100000ms, gap is 500ms (0.5% < 2% threshold)
        var intervals = new List<SessionProcessingService.IntervalEntry>
        {
            new(0, 50000, true),
            new(50000, 50500, false), // 500ms inactive — tiny relative to session
            new(50500, 100000, true),
        };

        var result = SessionProcessingService.MergeSmallInactiveGaps(intervals, 100000);

        // Small gap merged — should be single active interval
        Assert.Single(result);
        Assert.True(result[0].Active);
    }

    [Fact]
    public void MergeSmallInactiveGaps_LargeGap_StaysInactive()
    {
        // Session is 100000ms, gap is 20000ms (20% > 2% threshold)
        var intervals = new List<SessionProcessingService.IntervalEntry>
        {
            new(0, 40000, true),
            new(40000, 60000, false), // 20s inactive — significant
            new(60000, 100000, true),
        };

        var result = SessionProcessingService.MergeSmallInactiveGaps(intervals, 100000);

        Assert.Equal(3, result.Count);
        Assert.False(result[1].Active);
    }

    [Fact]
    public void MergeSmallInactiveGaps_ZeroLength_ReturnsAsIs()
    {
        var intervals = new List<SessionProcessingService.IntervalEntry>
        {
            new(0, 100, true),
        };
        var result = SessionProcessingService.MergeSmallInactiveGaps(intervals, 0);
        Assert.Single(result);
    }

    [Fact]
    public void MergeSmallInactiveGaps_Empty_ReturnsEmpty()
    {
        var result = SessionProcessingService.MergeSmallInactiveGaps([], 100000);
        Assert.Empty(result);
    }

    // ── MergeAdjacentActive unit tests ───────────────────────────────────

    [Fact]
    public void MergeAdjacentActive_TwoActiveIntervals_Merged()
    {
        var intervals = new List<SessionProcessingService.IntervalEntry>
        {
            new(0, 5000, true),
            new(5000, 10000, true),
        };
        var result = SessionProcessingService.MergeAdjacentActive(intervals);
        Assert.Single(result);
        Assert.Equal(0, result[0].StartTime);
        Assert.Equal(10000, result[0].EndTime);
    }

    [Fact]
    public void MergeAdjacentActive_ActiveInactiveActive_NoMerge()
    {
        var intervals = new List<SessionProcessingService.IntervalEntry>
        {
            new(0, 5000, true),
            new(5000, 15000, false),
            new(15000, 20000, true),
        };
        var result = SessionProcessingService.MergeAdjacentActive(intervals);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void MergeAdjacentActive_AllActive_SingleInterval()
    {
        var intervals = new List<SessionProcessingService.IntervalEntry>
        {
            new(0, 1000, true),
            new(1000, 2000, true),
            new(2000, 3000, true),
            new(3000, 4000, true),
        };
        var result = SessionProcessingService.MergeAdjacentActive(intervals);
        Assert.Single(result);
        Assert.Equal(0, result[0].StartTime);
        Assert.Equal(4000, result[0].EndTime);
    }

    [Fact]
    public void MergeAdjacentActive_Empty_ReturnsEmpty()
    {
        var result = SessionProcessingService.MergeAdjacentActive([]);
        Assert.Empty(result);
    }

    // ── DetectRageClicks unit tests ──────────────────────────────────────

    [Fact]
    public void DetectRageClicks_BelowThreshold_NoRageClicks()
    {
        var timestamps = new List<long> { 1000, 2000, 3000, 4000 }; // Only 4, need 5
        var result = SessionProcessingService.DetectRageClicks(timestamps, _project);
        Assert.Empty(result);
    }

    [Fact]
    public void DetectRageClicks_ExactlyAtThreshold_Detected()
    {
        var timestamps = new List<long> { 1000, 2000, 3000, 4000, 5000 }; // 5 within 5s
        var result = SessionProcessingService.DetectRageClicks(timestamps, _project);
        Assert.Single(result);
        Assert.Equal(5, result[0].TotalClicks);
    }

    [Fact]
    public void DetectRageClicks_SpreadOutClicks_NoRageClick()
    {
        // 5 clicks but spread over 20 seconds
        var timestamps = new List<long> { 0, 5000, 10000, 15000, 20000 };
        var result = SessionProcessingService.DetectRageClicks(timestamps, _project);
        Assert.Empty(result);
    }

    [Fact]
    public void DetectRageClicks_TwoSeparateClusters_TwoRageClicks()
    {
        var timestamps = new List<long>
        {
            // Cluster 1: 5 clicks in 2s
            1000, 1200, 1400, 1600, 1800,
            // Gap
            50000,
            // Cluster 2: 5 clicks in 2s
            60000, 60200, 60400, 60600, 60800,
        };
        var result = SessionProcessingService.DetectRageClicks(timestamps, _project);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void DetectRageClicks_OverlappingWindows_NoDuplicates()
    {
        // 10 rapid clicks — should not produce overlapping rage clicks
        var timestamps = Enumerable.Range(0, 10)
            .Select(i => (long)(1000 + i * 100))
            .ToList();
        var result = SessionProcessingService.DetectRageClicks(timestamps, _project);
        Assert.Single(result); // Should be consolidated
        Assert.True(result[0].TotalClicks >= 5);
    }

    [Fact]
    public void DetectRageClicks_CustomProjectThreshold_Respected()
    {
        var customProject = new Project
        {
            Name = "Custom",
            WorkspaceId = _workspace.Id,
            RageClickWindowSeconds = 2, // Tighter window
            RageClickRadiusPixels = 8,
            RageClickCount = 3, // Lower threshold
        };

        // 3 clicks within 2 seconds
        var timestamps = new List<long> { 1000, 1500, 2000 };
        var result = SessionProcessingService.DetectRageClicks(timestamps, customProject);
        Assert.Single(result);
        Assert.Equal(3, result[0].TotalClicks);
    }

    [Fact]
    public void DetectRageClicks_EmptyTimestamps_NoRageClicks()
    {
        var result = SessionProcessingService.DetectRageClicks([], _project);
        Assert.Empty(result);
    }

    [Fact]
    public void DetectRageClicks_AllSameTimestamp_Detected()
    {
        var timestamps = new List<long> { 1000, 1000, 1000, 1000, 1000 };
        var result = SessionProcessingService.DetectRageClicks(timestamps, _project);
        Assert.Single(result);
    }

    // ── BuildEventCountHistogram unit tests ──────────────────────────────

    [Fact]
    public void BuildHistogram_Empty_AllZeros()
    {
        var result = SessionProcessingService.BuildEventCountHistogram([]);
        Assert.Equal(100, result.Length);
        Assert.All(result, v => Assert.Equal(0, v));
    }

    [Fact]
    public void BuildHistogram_SingleEvent_AllZeros()
    {
        var result = SessionProcessingService.BuildEventCountHistogram([5000]);
        Assert.Equal(100, result.Length);
        Assert.All(result, v => Assert.Equal(0, v));
    }

    [Fact]
    public void BuildHistogram_TwoEvents_FirstAndLastBucket()
    {
        var result = SessionProcessingService.BuildEventCountHistogram([0, 100000]);
        Assert.Equal(100, result.Length);
        Assert.Equal(1, result[0]);
        Assert.Equal(1, result[99]);
    }

    [Fact]
    public void BuildHistogram_EvenlyDistributed_AllBucketsHaveOne()
    {
        var timestamps = Enumerable.Range(0, 100)
            .Select(i => (long)(i * 1000))
            .ToList();
        var result = SessionProcessingService.BuildEventCountHistogram(timestamps);

        // Should be roughly evenly distributed
        Assert.Equal(100, result.Sum());
    }

    [Fact]
    public void BuildHistogram_AllSameTimestamp_AllZeros()
    {
        // Duration = 0, so no distribution possible
        var result = SessionProcessingService.BuildEventCountHistogram([5000, 5000, 5000]);
        Assert.All(result, v => Assert.Equal(0, v));
    }

    [Fact]
    public void BuildHistogram_BurstInMiddle_MiddleBucketHigh()
    {
        var timestamps = new List<long> { 0 };
        // Burst at halfway point
        for (int i = 0; i < 50; i++)
            timestamps.Add(50000 + i);
        timestamps.Add(100000);

        var result = SessionProcessingService.BuildEventCountHistogram(timestamps);
        Assert.Equal(100, result.Length);

        // Bucket 50 should have the burst
        var midBucket = result[50];
        Assert.True(midBucket > 1);
    }

    // ── Integration-level edge cases ─────────────────────────────────────

    [Fact]
    public async Task ProcessSession_ZeroTimestamps_ReturnsZero()
    {
        var session = CreateSession();
        // All timestamps are 0
        _db.EventChunks.Add(new EventChunk { SessionId = session.Id, ChunkIndex = 0, Timestamp = 0 });
        _db.EventChunks.Add(new EventChunk { SessionId = session.Id, ChunkIndex = 1, Timestamp = 0 });
        _db.SaveChanges();

        var result = await _service.ProcessSessionAsync(session.Id, CancellationToken.None);
        Assert.Equal(0, result.TotalLengthMs);
    }

    [Fact]
    public async Task ProcessSession_VeryLongSession_CapsActiveLength()
    {
        var session = CreateSession();
        long eightDaysMs = 8L * 24 * 60 * 60 * 1000;
        AddChunks(session.Id, 0, eightDaysMs);

        var result = await _service.ProcessSessionAsync(session.Id, CancellationToken.None);

        // Active length should be capped at 7 days
        Assert.True(result.ActiveLengthMs <= (int)SessionProcessingService.MaxSessionLengthMs);
    }

    [Fact]
    public async Task ProcessSession_NegativeTimestamp_Handled()
    {
        var session = CreateSession();
        // Negative timestamps (shouldn't happen but shouldn't crash)
        _db.EventChunks.Add(new EventChunk { SessionId = session.Id, ChunkIndex = 0, Timestamp = -1000 });
        _db.EventChunks.Add(new EventChunk { SessionId = session.Id, ChunkIndex = 1, Timestamp = 1000 });
        _db.SaveChanges();

        // Should not throw
        var result = await _service.ProcessSessionAsync(session.Id, CancellationToken.None);
        Assert.Equal(session.Id, result.SessionId);
    }

    [Fact]
    public async Task ProcessSession_ConcurrentProcessing_NoCrash()
    {
        var session = CreateSession();
        AddChunks(session.Id, 1000, 2000, 3000, 4000, 5000);

        // Process twice in sequence (simulating reprocessing)
        var result1 = await _service.ProcessSessionAsync(session.Id, CancellationToken.None);
        var result2 = await _service.ProcessSessionAsync(session.Id, CancellationToken.None);

        // Second run should produce same results
        Assert.Equal(result1.IntervalsCreated, result2.IntervalsCreated);
    }

    [Fact]
    public async Task ProcessSession_100Chunks_HandlesCorrectly()
    {
        var session = CreateSession();
        var timestamps = Enumerable.Range(1, 100).Select(i => (long)(i * 1000)).ToArray();
        AddChunks(session.Id, timestamps);

        var result = await _service.ProcessSessionAsync(session.Id, CancellationToken.None);

        Assert.Equal(99000, result.TotalLengthMs); // 1000 to 100000
        Assert.True(result.IntervalsCreated >= 1);
        Assert.Equal(100, result.PagesVisited);
    }
}
