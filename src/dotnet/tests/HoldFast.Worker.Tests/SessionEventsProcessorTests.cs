using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Storage;
using HoldFast.Worker;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoldFast.Worker.Tests;

/// <summary>
/// Tests for SessionEventsProcessor: decompression, chunking, storage, metadata updates.
/// </summary>
public class SessionEventsProcessorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HoldFastDbContext _db;
    private readonly StubStorageService _storage;
    private readonly SessionEventsProcessor _processor;
    private readonly Session _session;

    public SessionEventsProcessorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection).Options;
        _db = new HoldFastDbContext(options);
        _db.Database.EnsureCreated();

        var workspace = new Workspace { Name = "WS", PlanTier = "Enterprise" };
        _db.Workspaces.Add(workspace);
        _db.SaveChanges();

        var project = new Project { Name = "Proj", WorkspaceId = workspace.Id };
        _db.Projects.Add(project);
        _db.SaveChanges();

        _session = new Session { ProjectId = project.Id, SecureId = "test-session-abc" };
        _db.Sessions.Add(_session);
        _db.SaveChanges();

        _storage = new StubStorageService();
        _processor = new SessionEventsProcessor(_db, _storage, NullLogger<SessionEventsProcessor>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private static string CompressToBase64Gzip(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }
        return Convert.ToBase64String(ms.ToArray());
    }

    // ── Decompression Tests ────────────────────────────────────────────

    [Fact]
    public void DecompressPayload_ValidData_ReturnsOriginal()
    {
        var original = "{\"test\": true}";
        var compressed = CompressToBase64Gzip(original);
        var result = SessionEventsProcessor.DecompressPayload(compressed);
        Assert.Equal(original, Encoding.UTF8.GetString(result));
    }

    [Fact]
    public void DecompressPayload_EmptyJson_Roundtrips()
    {
        var compressed = CompressToBase64Gzip("");
        var result = SessionEventsProcessor.DecompressPayload(compressed);
        Assert.Empty(result);
    }

    [Fact]
    public void DecompressPayload_LargePayload_Roundtrips()
    {
        var large = new string('A', 100_000);
        var compressed = CompressToBase64Gzip(large);
        var result = SessionEventsProcessor.DecompressPayload(compressed);
        Assert.Equal(large, Encoding.UTF8.GetString(result));
    }

    [Fact]
    public void DecompressPayload_InvalidBase64_Throws()
    {
        Assert.Throws<FormatException>(() =>
            SessionEventsProcessor.DecompressPayload("not-valid-base64!!!"));
    }

    [Fact]
    public void CompressBrotli_RoundTrips()
    {
        var original = Encoding.UTF8.GetBytes("Hello, Brotli compression test!");
        var compressed = SessionEventsProcessor.CompressBrotli(original);

        Assert.True(compressed.Length > 0);
        Assert.NotEqual(original, compressed); // Should actually be compressed

        // Decompress to verify
        using var ms = new MemoryStream(compressed);
        using var brotli = new BrotliStream(ms, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        Assert.Equal(original, output.ToArray());
    }

    [Fact]
    public void CompressBrotli_EmptyData()
    {
        var compressed = SessionEventsProcessor.CompressBrotli([]);
        Assert.True(compressed.Length > 0); // Brotli header even for empty
    }

    // ── Processing Tests ───────────────────────────────────────────────

    [Fact]
    public async Task ProcessCompressed_ValidPayload_CreatesChunkAndUpdatesSession()
    {
        var events = JsonSerializer.Serialize(new[]
        {
            new { type = 2, timestamp = 1700000000000L, data = new { } }
        });
        var compressed = CompressToBase64Gzip(events);

        var result = await _processor.ProcessCompressedPayloadAsync(
            "test-session-abc", 1, compressed, CancellationToken.None);

        Assert.Equal(_session.Id, result.SessionId);
        Assert.Equal(1, result.ChunksCreated);
        Assert.True(result.TotalBytes > 0);

        // Verify EventChunk created
        var chunks = await _db.EventChunks.Where(c => c.SessionId == _session.Id).ToListAsync();
        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal(1700000000000L, chunks[0].Timestamp);

        // Verify session metadata updated
        await _db.Entry(_session).ReloadAsync();
        Assert.True(_session.PayloadUpdated);
        Assert.True(_session.ObjectStorageEnabled);
        Assert.True(_session.PayloadSize > 0);
        Assert.NotNull(_session.LastUserInteractionTime);
    }

    [Fact]
    public async Task ProcessCompressed_UnknownSession_Returns0()
    {
        var compressed = CompressToBase64Gzip("[]");
        var result = await _processor.ProcessCompressedPayloadAsync(
            "nonexistent-session", 1, compressed, CancellationToken.None);

        Assert.Equal(0, result.SessionId);
        Assert.Equal(0, result.ChunksCreated);
    }

    [Fact]
    public async Task ProcessCompressed_InvalidCompressedData_ReturnsNoChunks()
    {
        // Valid base64 but not gzip
        var notGzip = Convert.ToBase64String(Encoding.UTF8.GetBytes("not gzip data"));
        var result = await _processor.ProcessCompressedPayloadAsync(
            "test-session-abc", 1, notGzip, CancellationToken.None);

        Assert.Equal(_session.Id, result.SessionId);
        Assert.Equal(0, result.ChunksCreated);
    }

    [Fact]
    public async Task ProcessCompressed_EmptyPayload_ReturnsNoChunks()
    {
        var compressed = CompressToBase64Gzip("");
        var result = await _processor.ProcessCompressedPayloadAsync(
            "test-session-abc", 1, compressed, CancellationToken.None);

        Assert.Equal(_session.Id, result.SessionId);
        Assert.Equal(0, result.ChunksCreated);
    }

    [Fact]
    public async Task ProcessCompressed_MultiplePayloads_IncrementChunkIndex()
    {
        var events1 = CompressToBase64Gzip("[{\"type\":2,\"timestamp\":1000}]");
        var events2 = CompressToBase64Gzip("[{\"type\":2,\"timestamp\":2000}]");
        var events3 = CompressToBase64Gzip("[{\"type\":2,\"timestamp\":3000}]");

        await _processor.ProcessCompressedPayloadAsync("test-session-abc", 1, events1, CancellationToken.None);
        await _processor.ProcessCompressedPayloadAsync("test-session-abc", 2, events2, CancellationToken.None);
        await _processor.ProcessCompressedPayloadAsync("test-session-abc", 3, events3, CancellationToken.None);

        var chunks = await _db.EventChunks
            .Where(c => c.SessionId == _session.Id)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync();

        Assert.Equal(3, chunks.Count);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal(1, chunks[1].ChunkIndex);
        Assert.Equal(2, chunks[2].ChunkIndex);
    }

    [Fact]
    public async Task ProcessCompressed_PayloadSizeAccumulates()
    {
        var events1 = CompressToBase64Gzip("[{\"type\":2,\"timestamp\":1000}]");
        var events2 = CompressToBase64Gzip("[{\"type\":2,\"timestamp\":2000,\"data\":{\"key\":\"value\"}}]");

        await _processor.ProcessCompressedPayloadAsync("test-session-abc", 1, events1, CancellationToken.None);
        var size1 = _session.PayloadSize;

        await _processor.ProcessCompressedPayloadAsync("test-session-abc", 2, events2, CancellationToken.None);
        await _db.Entry(_session).ReloadAsync();

        Assert.True(_session.PayloadSize > size1);
    }

    [Fact]
    public async Task ProcessCompressed_StorageUploadsCorrectKeys()
    {
        var events = CompressToBase64Gzip("[{\"type\":2}]");
        await _processor.ProcessCompressedPayloadAsync("test-session-abc", 42, events, CancellationToken.None);

        // Should have uploaded both raw events and chunk
        Assert.True(_storage.Uploads.Count >= 2);

        // Raw events key format
        var rawKey = _storage.Uploads.Keys
            .FirstOrDefault(k => k.Contains("events-42"));
        Assert.NotNull(rawKey);

        // Chunk key format
        var chunkKey = _storage.Uploads.Keys
            .FirstOrDefault(k => k.Contains("eventschunked0000"));
        Assert.NotNull(chunkKey);
    }

    [Fact]
    public async Task ProcessCompressed_NonJsonPayload_StillCreatesChunk()
    {
        // Valid gzip payload but not JSON
        var compressed = CompressToBase64Gzip("this is not json");
        var result = await _processor.ProcessCompressedPayloadAsync(
            "test-session-abc", 1, compressed, CancellationToken.None);

        // Should still create a chunk (timestamp falls back to current time)
        Assert.Equal(1, result.ChunksCreated);
        Assert.True(result.TotalBytes > 0);
    }

    [Fact]
    public async Task ProcessCompressed_TimestampExtraction_FromArray()
    {
        var events = JsonSerializer.Serialize(new[]
        {
            new { type = 2, timestamp = 1699999999999L },
            new { type = 3, timestamp = 1700000000000L },
        });
        var compressed = CompressToBase64Gzip(events);

        await _processor.ProcessCompressedPayloadAsync("test-session-abc", 1, compressed, CancellationToken.None);

        var chunk = await _db.EventChunks.FirstAsync(c => c.SessionId == _session.Id);
        Assert.Equal(1699999999999L, chunk.Timestamp); // First event's timestamp
    }

    [Fact]
    public async Task ProcessCompressed_TimestampExtraction_ObjectRoot_UsesCurrentTime()
    {
        // Object root (not array) - no timestamp extraction
        var events = CompressToBase64Gzip("{\"type\":2}");
        await _processor.ProcessCompressedPayloadAsync("test-session-abc", 1, events, CancellationToken.None);

        var chunk = await _db.EventChunks.FirstAsync(c => c.SessionId == _session.Id);
        // Timestamp should be "now-ish" (within last minute)
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.InRange(chunk.Timestamp, nowMs - 60_000, nowMs + 1000);
    }

    // ── Stub Storage Service ───────────────────────────────────────────

    private class StubStorageService : IStorageService
    {
        public Dictionary<string, byte[]> Uploads { get; } = new();

        public Task UploadAsync(string bucket, string key, Stream data, string? contentType, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            data.CopyTo(ms);
            Uploads[$"{bucket}/{key}"] = ms.ToArray();
            return Task.CompletedTask;
        }

        public Task<Stream?> DownloadAsync(string bucket, string key, CancellationToken ct)
        {
            if (Uploads.TryGetValue($"{bucket}/{key}", out var data))
                return Task.FromResult<Stream?>(new MemoryStream(data));
            return Task.FromResult<Stream?>(null);
        }

        public Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct)
            => Task.FromResult(Uploads.ContainsKey($"{bucket}/{key}"));

        public Task DeleteAsync(string bucket, string key, CancellationToken ct)
        {
            Uploads.Remove($"{bucket}/{key}");
            return Task.CompletedTask;
        }

        public Task<string> GetDownloadUrlAsync(string bucket, string key, TimeSpan expiry, CancellationToken ct)
            => Task.FromResult($"stub://{bucket}/{key}");
    }
}
