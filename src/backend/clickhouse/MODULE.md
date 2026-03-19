# ClickHouse Package

## Purpose

Analytics and time-series data layer. Stores and queries all high-volume observability data — logs, traces, sessions, errors, and metrics. Uses ClickHouse's columnar storage for fast aggregation over billions of rows. Imported by **23 files**.

## Module Path

`github.com/BrewingCoder/holdfast/src/backend/clickhouse`

## Files

| File | Lines | Purpose |
|------|-------|---------|
| `query.go` | 2,181 | Core metrics query engine and aggregation |
| `sessions.go` | 943 | Session read/write operations |
| `errors.go` | 881 | Error group/object queries |
| `traces.go` | 678 | Distributed trace operations |
| `logs.go` | 516 | Log ingestion and queries |
| `querybuilder.go` | 429 | Filter/rule parsing and SQL building |
| `metrics.go` | 344 | Metric row types and ingestion |
| `events.go` | 258 | Session event tracking |
| `alerts.go` | 200 | Alert state tracking |
| `log_row.go` | 199 | LogRow data structure and builders |
| `trace_row.go` | 182 | TraceRow data structure and builders |
| `clickhouse.go` | 147 | Connection initialization and migrations |
| `metric_history.go` | 135 | Metric state aggregation over time |
| `cursor.go` | 119 | Cursor-based pagination encoding |
| `fields.go` | 91 | Session custom field queries |
| `insert.go` | 41 | Timestamp insertion helpers |
| **Tests** | ~2,450 | 7 test files |
| **Total** | ~7,800+ | |

## Tables

| Table | Engine | Purpose |
|-------|--------|---------|
| `logs` | MergeTree | OpenTelemetry logs |
| `logs_sampling` | MergeTree | Sampled logs for fast queries (>20M rows) |
| `sessions` | MergeTree | Session metadata |
| `sessions_joined_vw` | MaterializedView | Pre-joined sessions with fields/error counts |
| `traces` / `traces_sampling_new` | MergeTree | Distributed trace spans |
| `error_groups` | ReplacingMergeTree | Grouped errors with state |
| `error_objects` | MergeTree | Individual error instances |
| `fields` / `fields_by_session` | ReplacingMergeTree | Session custom fields |
| `session_events` | MergeTree | Custom session events |
| `metrics_sum/histogram/summary` | MergeTree | OTel metrics by type |
| `metric_history` | MergeTree | Metric values over time windows |
| `alert_state_changes` | MergeTree | Alert state transitions |
| `*_keys` / `*_key_values` | AggregatingMergeTree | Dimension discovery (materialized views) |

## Connection

```go
type Client struct {
    conn         driver.Conn  // Read-write
    connReadonly driver.Conn  // Read-only (separate user)
}
```

- **Config:** `CLICKHOUSE_ADDRESS`, `CLICKHOUSE_DATABASE`, `CLICKHOUSE_USERNAME`, `CLICKHOUSE_PASSWORD`, `CLICKHOUSE_USERNAME_READONLY`
- **Pool:** 10 idle / 100 max connections
- **Compression:** ZSTD
- **TLS:** Auto-detected on port 9440
- **Health:** Background goroutine reports pool stats every 5s

## Migrations

**146 migrations** in `migrations/` directory, run via `golang-migrate` at startup. Uses custom `schema_migrations` MergeTree table (not TinyLog — doesn't work on ClickHouse Cloud).

Pattern: create new table → migrate data → `EXCHANGE TABLES` → drop old. Zero-downtime.

## Key Patterns

- **Sampling tables** — queries exceeding ~20M rows auto-switch to sampling table
- **Cursor pagination** — base64-encoded (timestamp + UUID) for bidirectional scrolling
- **Async inserts** — `async_insert=1, wait_for_async_insert=1` for batch efficiency
- **Bloom filter indexes** — on UUIDs, IDs, map keys/values for fast lookups
- **Delta + ZSTD compression** on all numeric/string columns
- **Materialized views** for real-time key/value aggregation (auto-updated on insert)
- **Builder pattern** for row objects (`NewTraceRow().WithSpanName().WithDuration()`)

## Dependencies

**Imports:** `clickhouse-go/v2`, `golang-migrate/v4`, `go-sqlbuilder`, `parser/`, `parser/listener/`, `model`, `private-graph/graph/model`

**Imported by (23 files):** `private-graph/` (dashboard queries), `public-graph/` (sampling), `worker/` (ingestion), `main.go`, `otel/`, `store/`, `kafka-queue/types.go`, `jobs/`, migration scripts

## Testing

7 test files (~2,450 lines). Most comprehensive test coverage in the backend:
- `logs_test.go` (1,550 lines) — pagination, filtering, histogram, metrics
- `sessions_test.go` (270 lines) — queries, histograms, field filtering
- `traces_test.go` (248 lines) — read, existence, span navigation
- `cursor_test.go` (217 lines) — encoding/decoding, bidirectional pagination
- `errors_test.go` (92 lines) — frequency, aggregation

### Priority Test Targets

1. Query builder SQL generation with complex filters
2. Sampling table selection logic
3. Write operations (batch insert correctness)
4. Connection failover (read-only fallback)
5. Migration idempotency

## Gotchas

- **`query.go` is 2,181 lines** — the largest file in the backend. Contains all metrics query logic. Splitting is desirable.
- **Sampling table threshold** — hardcoded at ~20M rows. May need tuning per deployment.
- **`FINAL` modifier** — used on ReplacingMergeTree reads. Performance impact on large tables.
- **Dual connections** — read-only user created in migration 000139. If missing, read-only queries fall back to read-write.
- **PostgreSQL bridge** — ClickHouse can query PostgreSQL via `postgresql(...)` function for SessionID → SecureID lookups. Rarely used.
- **TTL policies** — 30-90 day retention at partition level. `ttl_only_drop_parts=1` drops full partitions.
- **High-cardinality keys** — capped at 1M rows in key/key_value tables to prevent memory explosion.
- **Timestamp precision** — logs truncated to seconds, traces keep nanoseconds. Mismatched precision in joins.
