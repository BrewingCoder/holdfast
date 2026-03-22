using HoldFast.Domain.Entities;

namespace HoldFast.Shared.ErrorGrouping;

/// <summary>
/// Fingerprint extracted from an error's stack trace.
/// </summary>
public record ErrorFingerprintEntry(
    string Type,    // "CODE", "META", or "JSON"
    string Value,
    int Index);

/// <summary>
/// Result of error grouping: the matched/created group + the new error object.
/// </summary>
public record ErrorGroupingResult(
    ErrorGroup ErrorGroup,
    ErrorObject ErrorObject,
    bool IsNewGroup);

/// <summary>
/// Service that fingerprints errors and groups them into ErrorGroups
/// using the classic fingerprint matching algorithm (ported from Go).
/// </summary>
public interface IErrorGroupingService
{
    /// <summary>
    /// Extract fingerprints from a structured stack trace (JSON array of frames).
    /// Returns CODE and META fingerprints for the top frames.
    /// </summary>
    List<ErrorFingerprintEntry> GetFingerprints(string? stackTraceJson);

    /// <summary>
    /// Find the best matching ErrorGroup for the given fingerprints, or null if none match.
    /// Uses the classic weighted scoring algorithm from the Go backend.
    /// </summary>
    Task<ErrorGroup?> FindMatchingGroupAsync(
        int projectId,
        string errorEvent,
        List<ErrorFingerprintEntry> fingerprints,
        CancellationToken ct);

    /// <summary>
    /// Full pipeline: fingerprint the error, find or create an ErrorGroup,
    /// create the ErrorObject, and store fingerprints.
    /// </summary>
    Task<ErrorGroupingResult> GroupErrorAsync(
        int projectId,
        string errorEvent,
        string errorType,
        string? stackTrace,
        DateTime timestamp,
        string? url,
        string? source,
        string? payload,
        string? environment,
        string? serviceName,
        string? serviceVersion,
        int? sessionId,
        string? traceExternalId,
        string? spanId,
        CancellationToken ct);
}
