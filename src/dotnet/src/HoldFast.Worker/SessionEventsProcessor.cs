using System.IO.Compression;
using System.Text.Json;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HoldFast.Worker;

/// <summary>
/// Decompresses, stores, and chunks session replay events.
/// Ported from Go's ProcessPayload / ProcessCompressedPayload / writeToEventChunk.
/// </summary>
public class SessionEventsProcessor : ISessionEventsProcessor
{
    private const string SessionBucket = "sessions";

    private readonly HoldFastDbContext _db;
    private readonly IStorageService _storage;
    private readonly ILogger<SessionEventsProcessor> _logger;

    public SessionEventsProcessor(
        HoldFastDbContext db,
        IStorageService storage,
        ILogger<SessionEventsProcessor> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SessionEventsResult> ProcessCompressedPayloadAsync(
        string sessionSecureId,
        long payloadId,
        string compressedData,
        CancellationToken ct)
    {
        // Resolve session
        var session = await _db.Sessions
            .FirstOrDefaultAsync(s => s.SecureId == sessionSecureId, ct);

        if (session == null)
        {
            _logger.LogWarning("Session not found for SecureId {SecureId}", sessionSecureId);
            return new SessionEventsResult(0, 0, 0);
        }

        // Decompress: base64 → gzip → raw JSON bytes
        byte[] rawBytes;
        try
        {
            rawBytes = DecompressPayload(compressedData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decompress payload for session {SecureId}", sessionSecureId);
            return new SessionEventsResult(session.Id, 0, 0);
        }

        if (rawBytes.Length == 0)
        {
            _logger.LogDebug("Empty payload for session {SecureId}", sessionSecureId);
            return new SessionEventsResult(session.Id, 0, 0);
        }

        // Store raw events to object storage
        var eventsKey = $"{session.ProjectId}/{session.Id}/events-{payloadId}.json.br";
        byte[] brotliBytes = CompressBrotli(rawBytes);
        using var eventsStream = new MemoryStream(brotliBytes);
        await _storage.UploadAsync(SessionBucket, eventsKey, eventsStream, "application/octet-stream", ct);

        // Create event chunks from the payload
        int chunksCreated = await CreateEventChunksAsync(session, payloadId, rawBytes, brotliBytes, ct);

        // Update session metadata
        session.PayloadUpdated = true;
        session.ObjectStorageEnabled = true;
        session.PayloadSize = (session.PayloadSize ?? 0) + rawBytes.Length;
        session.LastUserInteractionTime = DateTime.UtcNow.ToString("o");
        _db.Sessions.Update(session);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Processed {Bytes} bytes in {Chunks} chunks for session {SessionId}",
            rawBytes.Length, chunksCreated, session.Id);

        return new SessionEventsResult(session.Id, chunksCreated, rawBytes.Length);
    }

    /// <summary>
    /// Decompress base64 + gzip payload to raw bytes.
    /// </summary>
    internal static byte[] DecompressPayload(string compressedData)
    {
        var gzipBytes = Convert.FromBase64String(compressedData);
        using var inputStream = new MemoryStream(gzipBytes);
        using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();
        gzipStream.CopyTo(outputStream);
        return outputStream.ToArray();
    }

    /// <summary>
    /// Compress data with Brotli (matches Go's brotli output format).
    /// </summary>
    internal static byte[] CompressBrotli(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var brotli = new BrotliStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            brotli.Write(data, 0, data.Length);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Parse events JSON and create EventChunk records.
    /// One chunk per payload in Phase 2; full snapshot-based chunking in Phase 3.
    /// </summary>
    private async Task<int> CreateEventChunksAsync(
        Session session,
        long payloadId,
        byte[] rawBytes,
        byte[] compressedBytes,
        CancellationToken ct)
    {
        // Get the next chunk index for this session
        var maxChunkIndex = await _db.EventChunks
            .Where(c => c.SessionId == session.Id)
            .MaxAsync(c => (int?)c.ChunkIndex, ct) ?? -1;

        var chunkIndex = maxChunkIndex + 1;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Try to extract timestamp from the events JSON
        try
        {
            using var doc = JsonDocument.Parse(rawBytes);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var firstEvent = doc.RootElement.EnumerateArray().FirstOrDefault();
                if (firstEvent.TryGetProperty("timestamp", out var ts) && ts.TryGetInt64(out var t))
                    timestamp = t;
            }
        }
        catch (JsonException)
        {
            // Use current time if parsing fails
        }

        var chunk = new EventChunk
        {
            SessionId = session.Id,
            ChunkIndex = chunkIndex,
            Timestamp = timestamp,
        };
        _db.EventChunks.Add(chunk);

        // Store chunk to object storage
        var chunkKey = $"{session.ProjectId}/{session.Id}/eventschunked{chunkIndex:D4}.json.br";
        using var chunkStream = new MemoryStream(compressedBytes);
        await _storage.UploadAsync(SessionBucket, chunkKey, chunkStream, "application/octet-stream", ct);

        return 1;
    }
}
