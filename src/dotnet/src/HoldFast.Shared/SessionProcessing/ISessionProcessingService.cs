namespace HoldFast.Shared.SessionProcessing;

/// <summary>
/// Result of full session processing (intervals, rage clicks, metrics).
/// </summary>
public record SessionProcessingResult(
    int SessionId,
    int IntervalsCreated,
    int RageClicksDetected,
    int ActiveLengthMs,
    int TotalLengthMs,
    int PagesVisited);

/// <summary>
/// Service that processes session replay events to compute intervals,
/// rage clicks, active duration, and event count histograms.
/// Ported from Go's processSessionData / computeSessionIntervals / detectRageClicks.
/// </summary>
public interface ISessionProcessingService
{
    /// <summary>
    /// Process all events for a session to compute intervals, rage clicks, and metrics.
    /// Called after all payloads are received (session finalized).
    /// </summary>
    Task<SessionProcessingResult> ProcessSessionAsync(int sessionId, CancellationToken ct);
}
