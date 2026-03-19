# Redis Package

## Purpose

Cache layer and real-time session data store. Provides tiered caching (local LFU + Redis), distributed locking, session event storage with compression, and async processing queues. Imported by **36 files** — every data access path touches Redis.

## Module Path

`github.com/BrewingCoder/holdfast/src/backend/redis`

## Files

| File | Lines | Purpose |
|------|-------|---------|
| `utils.go` | 696 | Client setup, all cache/session/queue operations |
| `cache.go` | 74 | Generic `CachedEval[T]` with lock-based deduplication |
| `utils_test.go` | 102 | Tests for CachedEval and distributed locking |
| **Total** | **872** | |

## Client Architecture

```go
type Client struct {
    Client  redis.Cmdable      // go-redis (single or cluster)
    Cache   *cache.Cache       // Tiered: local LFU (100K entries, 5s TTL) + Redis
    Redsync *redsync.Redsync   // Distributed lock manager
}
```

**Dev/Test:** Single Redis instance, 256 connection pool, LFU disabled in test env.

**Production:** Redis Cluster with background goroutine reporting pool stats (hits, misses, idle/stale/total conns, timeouts) every second.

**Config:** `REDIS_EVENTS_STAGING_ENDPOINT` (address), `REDIS_PASSWORD` (auth).

## Key Operations

### Caching

**`CachedEval[T](ctx, redis, key, lockTimeout, ttl, fn)`** — Generic cache-aside with thundering herd protection:
- Acquires distributed lock before calling `fn()`
- Options: `WithBypassCache()`, `WithIgnoreError()`, `WithStoreNil()`
- Used for session lookups, subscription details, expensive queries

### Session Event Storage

Session events (DOM snapshots, network, console, etc.) are stored as Redis sorted sets with timestamp scores and Snappy compression:

| Function | Purpose |
|----------|---------|
| `AddPayload()` | Store events/resources/WebSocket events. Auto-expires 8h20m. Snappy compressed. |
| `GetSessionData()` | Retrieve all events/resources from sorted set. Handles beacon payloads. |
| `GetEventObjects()` | Cursor-based pagination for live session streaming. |
| `GetEvents()` / `GetResources()` | Parse full event/resource arrays from JSON. |
| `AddSessionFields()` | Store field updates as list (LPUSH). Deduplicated on retrieval. |
| `GetSessionFields()` | Retrieve and deduplicate fields across all payloads. |

### Async Processing Queue

Delayed session processing via sorted sets with Lua scripts for atomicity:

| Function | Purpose |
|----------|---------|
| `AddSessionToProcess()` | Add session to queue with timestamp score |
| `GetSessionsToProcess()` | Lua script: fetch N ready sessions, lock with `.5` offset |
| `RemoveSessionToProcess()` | Lua script: remove only if locked (non-integer score) |

The `.5` score offset trick prevents double-processing: a session at score `1710000000.5` is "locked", while `1710000000` is "ready".

### Flags & Rate Limiting

| Function | TTL | Purpose |
|----------|-----|---------|
| `SetIsPendingSession` / `IsPendingSession` | 24h | Track session initialization |
| `SetBillingQuotaExceeded` / `IsBillingQuotaExceeded` | 1min | Quota throttling (tristate: nil/true/false) |
| `SetGithubRateLimitExceeded` / `GetGithubRateLimitExceeded` | variable | GitHub API rate limiting |
| `SetGitHubFileError` / `GetGitHubFileError` | 1h | Cache GitHub file fetch errors |
| `IncrementServiceErrorCount` / `ResetServiceErrorCount` | auto | Error rate tracking with auto-expiry |

### Distributed Locking

```go
mutex, err := redis.AcquireLock(ctx, "lock-key", 5*time.Second)
defer mutex.Unlock()
```

- Uses Redsync (Redis-based distributed mutex)
- 100ms poll interval, 25s expiry (ECS grace period)
- Used in `CachedEval` and session processing

## Dependencies

**What this imports:**
- `env` — connection config
- `model` — session/event types
- `util` — span tracing
- `highlight-go/metric` — telemetry metrics
- `go-redis/v9` — Redis client
- `go-redsync/v4` — distributed locks
- `go-redis/cache/v9` — tiered caching
- `golang/snappy` — compression

**What imports this (36 files):**
- `store/` — 12 files (sessions, projects, workspaces, etc.)
- `private-graph/`, `public-graph/` — GraphQL resolvers
- `worker/` — async processing
- `main.go` — client initialization
- `storage/`, `stacktraces/`, `otel/` — supporting services
- `integrations/github/` — rate limit caching
- `pricing/` — quota checks
- Migration scripts

## Testing

3 tests in `utils_test.go`:
- `Test_CachedEval` — cache-aside with lock dedup, error caching, StoreNil/IgnoreError options
- `TestLock` — concurrent lock acquisition and serialization
- `BenchmarkLock` — lock throughput

### Coverage Gaps

- **No tests for session operations** — AddPayload, GetSessionData, GetEventObjects (hot path)
- **No tests for processing queue** — Lua scripts untested
- **No tests for flag operations** — tristate billing quota, rate limiting
- **No tests for Snappy compression** — fallback on decode failure untested
- **No tests for beacon payload filtering** — decimal score logic

### Priority Test Targets

1. **Session payload round-trip** — AddPayload → GetSessionData with compression
2. **Processing queue** — AddSessionToProcess → GetSessionsToProcess → RemoveSessionToProcess
3. **Beacon filtering** — decimal score handling in GetSessionData
4. **CachedEval edge cases** — concurrent access, expired cache, nil storage
5. **Flag tristate** — nil vs true vs false for billing quota

## Gotchas

- **Snappy fallback is silent** — if decompression fails, raw bytes are used. Could mask data corruption.
- **Beacon payload decimal scores** — non-integer scores in sorted sets mark "beacon" (final client transmission) payloads. The filtering logic skips decimals except the last entry. Counterintuitive.
- **Lua scripts for atomicity** — session processing uses inline Lua. Hard to test, hard to debug. The `.5` offset trick for locking is clever but undocumented.
- **FlushDB/Del are no-ops in production** — silently return nil error. Could mislead callers.
- **Background pool stats goroutine** — started in `NewClient()`, never cleaned up. Leaks on graceful shutdown.
- **LFU disabled in tests** — test env hits Redis on every call. Tests are slower but more accurate.
- **8h20m expiry** — session data TTL. If a session runs longer than this, early events are evicted from Redis. The system falls back to S3/filesystem for old events.
- **Connection pool: 256 max** — adequate for dev, may need tuning for high-traffic production deployments.
