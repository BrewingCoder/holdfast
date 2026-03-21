namespace HoldFast.Worker;

/// <summary>
/// Result of processing a session events payload.
/// </summary>
public record SessionEventsResult(
    int SessionId,
    int ChunksCreated,
    long TotalBytes);

/// <summary>
/// Service that decompresses and stores session replay events.
/// Ported from Go's ProcessPayload / ProcessCompressedPayload.
/// </summary>
public interface ISessionEventsProcessor
{
    /// <summary>
    /// Process a compressed session events payload:
    /// 1. Decompress (base64 + gzip)
    /// 2. Store raw events to object storage
    /// 3. Create EventChunk records
    /// 4. Update session metadata
    /// </summary>
    Task<SessionEventsResult> ProcessCompressedPayloadAsync(
        string sessionSecureId,
        long payloadId,
        string compressedData,
        CancellationToken ct);
}
