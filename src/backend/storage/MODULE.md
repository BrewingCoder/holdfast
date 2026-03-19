# Storage Package

## Purpose

Object storage abstraction for session replay data, source maps, and assets. Supports S3 (production/cloud) and local filesystem (development/self-hosted). All session recordings flow through this package. Imported by **18 files**.

## Module Path

`github.com/BrewingCoder/holdfast/src/backend/storage`

## Files

| File | Lines | Purpose |
|------|-------|---------|
| `storage.go` | 1,106 | Client interface, S3Client, FilesystemClient, all operations |
| **Total** | **1,106** | Single file package |

## Storage Backends

| Backend | When Used | Config |
|---------|-----------|--------|
| **S3Client** | `IsProduction()` or `AWS_ROLE_ARN` set | `AWS_S3_BUCKET_NAME_NEW`, `AWS_S3_SOURCE_MAP_BUCKET_NAME_NEW`, `AWS_S3_RESOURCES_BUCKET` |
| **FilesystemClient** | Everything else (default for self-hosted) | `OBJECT_STORAGE_FS` (default: `/tmp`) |

Both implement the same `Client` interface — all callers are backend-agnostic.

## Key Operations

### Session Replay Data
| Function | Purpose |
|----------|---------|
| `PushFiles()` | Batch push all payload types (events, resources, timeline, websockets) |
| `PushCompressedFile()` | Push individual Brotli-compressed file with metadata |
| `ReadCompressedEvents()` | Read and decompress session events |
| `ReadSessionEvents()` / `ReadResources()` / `ReadWebSocketEvents()` | Typed wrappers |
| `PushRawEvents()` / `GetRawData()` | Store/retrieve raw events as Gob-encoded sorted sets |
| `DeleteSessionData()` | Complete session purge (all payloads + raw events) |

### Source Maps
| Function | Purpose |
|----------|---------|
| `PushSourceMapFile()` | Upload versioned source map |
| `ReadSourceMapFileCached()` | Read with Redis caching (1s lazy, 1min TTL) |
| `GetSourcemapVersions()` / `GetSourcemapFiles()` | List versions/files |
| `GetSourceMapUploadUrl()` | Generate presigned upload URL |

### Assets & Downloads
| Function | Purpose |
|----------|---------|
| `GetAssetURL()` | Generate access URL (CloudFront signed or direct) |
| `GetDirectDownloadURL()` | Presigned S3 download URL |

## Key Path Structures

| Type | S3 Path | Filesystem Path |
|------|---------|-----------------|
| Session events | `[v2/][dev/]{projectId}/{sessionId}/{payloadType}` | `{fsRoot}/{projectId}/{sessionId}/{payloadType}` |
| Source maps | `sourcemaps/{projectId}/{version}/{fileName}` | same |
| Assets | `assets/{projectId}/{hashVal}` | same |
| Raw events | `raw-events/{projectId}/{sessionId}/[type]-[uuid]` | same |
| GitHub metadata | `{repoPath}/{version}/{fileName}` | same |

## Compression

All session payloads use **Brotli** compression (`Content-Encoding: br`). Compression on write, decompression on read. Raw events use **Gob** encoding (Go binary serialization).

## Dependencies

**Imports:** `env`, `model`, `payload`, `redis`, `util`, `aws-sdk-go-v2`, `brotli`, `chi`, `cors`

**Imported by (18 files):** `main.go`, `store/`, `private-graph/`, `public-graph/`, `worker/`, `stacktraces/`, `lambda-functions/`, migration scripts

## Testing

**No dedicated tests.** Tested indirectly via resolver and store integration tests using `FilesystemClient`.

### Priority Test Targets

1. S3Client / FilesystemClient parity — same inputs produce same outputs
2. Brotli compression round-trip
3. Gob encoding round-trip for raw events
4. Session bucket versioning (`v2/` prefix for sessions >= 150M)
5. Source map caching with Redis
6. CloudFront URL signing

## Gotchas

- **Single 1,106-line file** — should be split into S3Client, FilesystemClient, and shared types.
- **Session bucket versioning** — sessions with ID >= 150,000,000 use `v2/` prefix. Don't change `UseNewSessionBucket()`.
- **Filesystem is the default** — self-hosted deployments use `/tmp` unless `OBJECT_STORAGE_FS` is set. `/tmp` is not durable across restarts.
- **Raw events expire separately** — S3 uses lifecycle rules, filesystem uses `CleanupRawEvents()` (1-day retention).
- **CloudFront optional** — URL signing only if `AWS_CLOUDFRONT_PRIVATE_KEY` is configured. Falls back to direct S3 URLs.
- **Gob encoding for raw events** — not JSON. Can't be inspected without Go. Historical choice for performance.
- **No streaming** — entire files loaded into memory on read. Large sessions could OOM.
- **CORS on filesystem** — `FilesystemClient` sets up an HTTP listener with CORS for local dev. Don't expose in production.
- **path-style S3** — `UsePathStyle = true`. Required for MinIO and other S3-compatible stores (good for self-hosted).
