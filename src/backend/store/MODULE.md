# Store Package

## Purpose

Data access layer (DAL) that wraps PostgreSQL (GORM), Redis, ClickHouse, Kafka, and storage backends. All business-level data operations flow through Store methods. Imported by **18 files** including both GraphQL resolvers and the worker.

## Module Path

`github.com/BrewingCoder/holdfast/src/backend/store`

## Files

| File | Lines | Purpose |
|------|-------|---------|
| `stacktraces.go` | 299 | GitHub source enhancement, file fetching |
| `error_groups.go` | 199 | Error state management, activity logging |
| `services.go` | 198 | Service registry with cursor pagination |
| `project_filter_settings.go` | 108 | Sampling, auto-resolve, exclusion rules |
| `workspace_settings.go` | 51 | Workspace feature flags |
| `client_sampling_configuration.go` | 50 | OTel tracing sampling rules |
| `sessions.go` | 33 | Session retrieval with secure ID caching |
| `workspaces.go` | 33 | Workspace retrieval with project preload |
| `store.go` | 31 | Store struct definition |
| `project_assets.go` | 29 | Asset URL transformation |
| `issues.go` | 27 | Issue title/description formatting |
| `error_group_activity_logs.go` | 26 | Activity audit trail |
| `projects.go` | 24 | Project retrieval with Redis caching |
| `sso_clients.go` | 23 | SSO client lookup |
| `oauth.go` | 23 | OAuth client store retrieval |
| `system_configuration.go` | 19 | Global system config caching |
| `util.go` | 13 | GORM error handling helper |
| **Tests** | ~1,199 | 13 test files |
| **Total** | ~2,385 | |

## Store Struct

```go
type Store struct {
    DB                 *gorm.DB
    Redis              *redis.Client
    IntegrationsClient *integrations.Client
    StorageClient      storage.Client
    DataSyncQueue      kafka_queue.MessageQueue
    ClickhouseClient   *clickhouse.Client
}
```

All data backends injected at construction. Methods on `*Store` receiver handle domain logic.

## Caching Strategy

All reads use Redis cache-aside via `redis.CachedEval[T]`:

| Entity | Cache Key | Min TTL | Max TTL | Notes |
|--------|-----------|---------|---------|-------|
| Session | `session-<id>` | 1s | 1s | Hot path — very short TTL |
| Session (secure ID) | `session-secure-<id>` | 1s | 1s | |
| Project | `project-<id>` | 1s | 1min | |
| Workspace | `workspace-<id>` | 1s | 1min | Preloads Projects |
| WorkspaceSettings | `workspace-settings-<id>` | 1s | 1min | |
| SystemConfiguration | `system-config` | 250ms | 1min | Global singleton |
| Service | `service-<name>-<projectId>` | 1s | 1min | |
| OAuth | `oauth-<id>` | 1s | 1min | |
| ProjectFilterSettings | `project-filter-<id>` | 1s | 1min | FirstOrCreate |
| ProjectAssetTransform | `project-asset-<id>` | - | - | WithStoreNil(true) |

## Key Domains

### Error Groups (largest — 199 lines)
- `UpdateErrorGroupStateByAdmin()` — writes PostgreSQL → ClickHouse → Kafka queue for consistency
- `CreateErrorGroupActivityLog()` — audit trail for state changes
- `ListErrorObjects()` — preloads sessions and error groups, builds lookup maps

### Stacktraces (GitHub integration — 299 lines)
- `FetchFileFromGitHub()` — downloads source with rate limit caching
- `EnhanceTraceWithGitHub()` — resolves SHA, fetches file, extracts context lines
- `GitHubGitSHA()` — caches commit SHA lookups

### Services (cursor pagination — 198 lines)
- `UpsertService()` — GORM ON CONFLICT upsert
- `ListServices()` — cursor pagination with ordering and ILIKE search

## Dependencies

**Imports:** `gorm`, `model`, `redis`, `clickhouse`, `kafka-queue`, `storage`, `integrations`, `integrations/github`, `queryparser`, `stacktraces`, `util`

**Imported by (18 files):** `private-graph/` resolvers, `public-graph/` resolvers, `worker/`, `main.go`, `event-parse/`, `otel/`, migration scripts

## Testing

13 test files, 1,199 lines. Uses `TestMain` with isolated DB + Redis + ClickHouse:
- `stacktraces_test.go` (417 lines) — most comprehensive
- `services_test.go` (193 lines) — pagination, ordering
- `error_groups_test.go` (186 lines) — state transitions

### Priority Test Targets

1. Error group dual-write consistency (PostgreSQL + ClickHouse + Kafka)
2. Cache invalidation after writes
3. Service upsert concurrency
4. Pagination edge cases (empty, single item, boundary cursors)

## Gotchas

- **No optimistic locking** — concurrent updates are last-write-wins
- **Manual cache invalidation** — writes must explicitly delete Redis keys. Missing invalidation = stale reads.
- **Dual-write to ClickHouse** — `UpdateErrorGroupStateByAdmin` writes PostgreSQL, then ClickHouse, then Kafka. No transaction across systems.
- **Inconsistent preloading** — some methods use GORM Preload(), others build maps manually
- **WithStoreNil** — only `GetProjectAssetTransform` caches nil results. Other methods re-query on every miss.
- **GitHub rate limiting** — cached as boolean in Redis. If hit, all GitHub requests blocked until expiry.
- **Workspace settings gate sampling** — `UpdateProjectFilterSettings` checks workspace-level flags before allowing project-level changes.
