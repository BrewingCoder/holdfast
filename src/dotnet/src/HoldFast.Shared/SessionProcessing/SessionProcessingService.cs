using System.Text.Json;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HoldFast.Shared.SessionProcessing;

/// <summary>
/// Processes session replay events to compute intervals, rage clicks,
/// active duration, event count histograms, and pages visited.
/// Ported from Go's processSessionData logic.
/// </summary>
public class SessionProcessingService : ISessionProcessingService
{
    /// <summary>Minimum gap (ms) to start a new interval. Events within 10s are "active".</summary>
    internal const int MinInactiveDurationMs = 10_000;

    /// <summary>Inactive intervals shorter than 2% of session length are merged as active.</summary>
    internal const double InactiveThreshold = 0.02;

    /// <summary>Sessions longer than 7 days are capped (stale data).</summary>
    internal const long MaxSessionLengthMs = 7L * 24 * 60 * 60 * 1000;

    /// <summary>Number of buckets for event count histogram.</summary>
    internal const int EventCountBuckets = 100;

    private readonly HoldFastDbContext _db;
    private readonly ILogger<SessionProcessingService> _logger;

    public SessionProcessingService(HoldFastDbContext db, ILogger<SessionProcessingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SessionProcessingResult> ProcessSessionAsync(int sessionId, CancellationToken ct)
    {
        var session = await _db.Sessions
            .Include(s => s.Project)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null)
        {
            _logger.LogWarning("Session {SessionId} not found for processing", sessionId);
            return new SessionProcessingResult(sessionId, 0, 0, 0, 0, 0);
        }

        // Load all event chunks ordered by index
        var chunks = await _db.EventChunks
            .Where(c => c.SessionId == sessionId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(ct);

        if (chunks.Count == 0)
        {
            _logger.LogDebug("No event chunks for session {SessionId}", sessionId);
            return new SessionProcessingResult(sessionId, 0, 0, 0, 0, 0);
        }

        // Extract timestamps from chunks (already stored during event ingestion)
        var timestamps = chunks
            .Select(c => c.Timestamp)
            .Where(t => t > 0)
            .OrderBy(t => t)
            .ToList();

        if (timestamps.Count == 0)
        {
            return new SessionProcessingResult(sessionId, 0, 0, 0, 0, 0);
        }

        // Compute intervals
        var intervals = ComputeIntervals(timestamps);
        var totalLengthMs = timestamps.Count >= 2
            ? (int)(timestamps[^1] - timestamps[0])
            : 0;

        // Merge small inactive gaps (< 2% of session)
        intervals = MergeSmallInactiveGaps(intervals, totalLengthMs);

        // Remove old intervals and save new ones
        var oldIntervals = await _db.SessionIntervals
            .Where(i => i.SessionId == sessionId)
            .ToListAsync(ct);
        _db.SessionIntervals.RemoveRange(oldIntervals);

        foreach (var interval in intervals)
        {
            _db.SessionIntervals.Add(new SessionInterval
            {
                SessionId = sessionId,
                StartTime = interval.StartTime,
                EndTime = interval.EndTime,
                Duration = interval.EndTime - interval.StartTime,
                Active = interval.Active,
            });
        }

        // Compute active duration (sum of active intervals, capped at 7 days)
        var activeLengthMs = intervals
            .Where(i => i.Active)
            .Sum(i => (long)(i.EndTime - i.StartTime));
        if (activeLengthMs > MaxSessionLengthMs)
            activeLengthMs = MaxSessionLengthMs;

        // Detect rage clicks
        var rageClicks = DetectRageClicks(timestamps, session.Project);

        // Remove old rage clicks and save new ones
        var oldRageClicks = await _db.RageClickEvents
            .Where(r => r.SessionId == sessionId)
            .ToListAsync(ct);
        _db.RageClickEvents.RemoveRange(oldRageClicks);

        foreach (var rc in rageClicks)
        {
            _db.RageClickEvents.Add(new RageClickEvent
            {
                ProjectId = session.ProjectId,
                SessionId = sessionId,
                TotalClicks = rc.TotalClicks,
                Selector = rc.Selector,
                StartTimestamp = rc.StartTimestamp,
                EndTimestamp = rc.EndTimestamp,
            });
        }

        // Count pages visited (distinct URLs from event data)
        // In production this would parse rrweb navigation events;
        // for now we use chunk count as a proxy (matches Go's fallback behavior)
        var pagesVisited = Math.Max(1, chunks.Count);

        // Update session
        session.ActiveLength = (int)activeLengthMs;
        session.Length = totalLengthMs;
        session.PagesVisited = pagesVisited;
        session.HasRageClicks = rageClicks.Count > 0;
        session.Processed = true;
        session.Normalness = 0.0; // Placeholder — Go also returns 0.0
        _db.Sessions.Update(session);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Session {SessionId}: {Intervals} intervals, {RageClicks} rage clicks, active={ActiveMs}ms, total={TotalMs}ms",
            sessionId, intervals.Count, rageClicks.Count, activeLengthMs, totalLengthMs);

        return new SessionProcessingResult(
            sessionId,
            intervals.Count,
            rageClicks.Count,
            (int)activeLengthMs,
            totalLengthMs,
            pagesVisited);
    }

    /// <summary>
    /// Compute active/inactive intervals from sorted timestamps.
    /// Gap > MinInactiveDurationMs starts a new inactive interval.
    /// </summary>
    internal static List<IntervalEntry> ComputeIntervals(List<long> sortedTimestamps)
    {
        if (sortedTimestamps.Count == 0)
            return [];

        var intervals = new List<IntervalEntry>();
        var currentStart = (int)sortedTimestamps[0];
        var lastTimestamp = currentStart;
        var isActive = true;

        for (int i = 1; i < sortedTimestamps.Count; i++)
        {
            var ts = (int)sortedTimestamps[i];
            var gap = ts - lastTimestamp;

            if (gap > MinInactiveDurationMs)
            {
                // Close current active interval
                intervals.Add(new IntervalEntry(currentStart, lastTimestamp, isActive));
                // Add inactive gap
                intervals.Add(new IntervalEntry(lastTimestamp, ts, false));
                // Start new active interval
                currentStart = ts;
                isActive = true;
            }

            lastTimestamp = ts;
        }

        // Close final interval
        intervals.Add(new IntervalEntry(currentStart, lastTimestamp, isActive));

        return intervals;
    }

    /// <summary>
    /// Merge inactive intervals shorter than threshold% of total session as active.
    /// Matches Go's mergeSmallInactiveIntervals behavior.
    /// </summary>
    internal static List<IntervalEntry> MergeSmallInactiveGaps(List<IntervalEntry> intervals, int totalLengthMs)
    {
        if (totalLengthMs <= 0 || intervals.Count == 0)
            return intervals;

        var threshold = totalLengthMs * InactiveThreshold;
        var result = new List<IntervalEntry>();

        foreach (var interval in intervals)
        {
            if (!interval.Active && (interval.EndTime - interval.StartTime) < threshold)
            {
                // Mark small inactive gap as active
                result.Add(interval with { Active = true });
            }
            else
            {
                result.Add(interval);
            }
        }

        // Merge adjacent active intervals
        return MergeAdjacentActive(result);
    }

    /// <summary>
    /// Merge consecutive active intervals into single intervals.
    /// </summary>
    internal static List<IntervalEntry> MergeAdjacentActive(List<IntervalEntry> intervals)
    {
        if (intervals.Count == 0)
            return intervals;

        var merged = new List<IntervalEntry>();
        var current = intervals[0];

        for (int i = 1; i < intervals.Count; i++)
        {
            if (current.Active && intervals[i].Active)
            {
                // Merge: extend current to cover both
                current = current with { EndTime = intervals[i].EndTime };
            }
            else
            {
                merged.Add(current);
                current = intervals[i];
            }
        }

        merged.Add(current);
        return merged;
    }

    /// <summary>
    /// Detect rage clicks: N clicks within WindowSeconds in RadiusPixels.
    /// Uses timestamps as a proxy for click events (simplified from Go which parses rrweb events).
    /// In full implementation, this would parse mousedown/click events from rrweb data.
    /// </summary>
    internal static List<RageClickEntry> DetectRageClicks(
        List<long> sortedTimestamps,
        Project project)
    {
        var windowMs = project.RageClickWindowSeconds * 1000;
        var threshold = project.RageClickCount;
        var rageClicks = new List<RageClickEntry>();

        if (sortedTimestamps.Count < threshold)
            return rageClicks;

        // Sliding window: if N timestamps fit within windowMs, it's a rage click cluster
        for (int i = 0; i <= sortedTimestamps.Count - threshold; i++)
        {
            var windowEnd = sortedTimestamps[i] + windowMs;
            var count = 0;
            var j = i;

            while (j < sortedTimestamps.Count && sortedTimestamps[j] <= windowEnd)
            {
                count++;
                j++;
            }

            if (count >= threshold)
            {
                var start = sortedTimestamps[i];
                var end = sortedTimestamps[j - 1];

                // Avoid duplicate overlapping rage clicks
                if (rageClicks.Count == 0 || start > rageClicks[^1].EndTimestamp)
                {
                    rageClicks.Add(new RageClickEntry(count, null, start, end));
                }
                else if (count > rageClicks[^1].TotalClicks)
                {
                    // Replace last if this cluster is bigger
                    rageClicks[^1] = new RageClickEntry(count, null, rageClicks[^1].StartTimestamp, end);
                }
            }
        }

        return rageClicks;
    }

    /// <summary>
    /// Build a 100-bucket event count histogram for the session.
    /// Each bucket = sessionDuration / 100 ms wide.
    /// </summary>
    internal static int[] BuildEventCountHistogram(List<long> sortedTimestamps)
    {
        var histogram = new int[EventCountBuckets];
        if (sortedTimestamps.Count < 2)
            return histogram;

        var start = sortedTimestamps[0];
        var end = sortedTimestamps[^1];
        var duration = end - start;
        if (duration <= 0)
            return histogram;

        var bucketWidth = (double)duration / EventCountBuckets;

        foreach (var ts in sortedTimestamps)
        {
            var bucket = (int)((ts - start) / bucketWidth);
            if (bucket >= EventCountBuckets) bucket = EventCountBuckets - 1;
            histogram[bucket]++;
        }

        return histogram;
    }

    internal record IntervalEntry(int StartTime, int EndTime, bool Active);
    internal record RageClickEntry(int TotalClicks, string? Selector, long StartTimestamp, long EndTimestamp);
}
