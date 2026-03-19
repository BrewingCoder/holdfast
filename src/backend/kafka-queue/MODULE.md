# Kafka Queue Package

## Purpose

Async message queue abstraction over Apache Kafka. Handles all data ingestion (sessions, errors, logs, traces, metrics) from the public GraphQL endpoint through to worker processing. Imported by **24 files**.

## Module Path

`github.com/BrewingCoder/holdfast/src/backend/kafka-queue`

## Files

| File | Lines | Purpose |
|------|-------|---------|
| `kafkaqueue.go` | 578 | Producer/consumer logic, serialization, OTel context propagation |
| `types.go` | 427 | Message types, payload structs, RetryableMessage interface |
| `kafkaqueue_test.go` | 159 | Integration tests (requires live Kafka) |
| `aws.go` | 64 | ECS rack resolver for broker locality |
| `balancerwrapper.go` | 23 | Rebalance callback wrapper |
| **Total** | **1,251** | |

## Topics

Topics are derived from `KAFKA_TOPIC` env var with optional prefix:

| TopicType | Suffix | Purpose |
|-----------|--------|---------|
| Default | (none) | Main data ingestion |
| Batched | `_batched` | Session events, user properties |
| DataSync | `_datasync` | PostgreSQL sync (single partition for ordering) |
| Traces | `_traces` | Distributed trace spans |
| MetricSum | `_metric-sum` | OTel sum metrics |
| MetricHistogram | `_metric-histogram` | OTel histogram metrics |
| MetricSummary | `_metric-summary` | OTel summary metrics |

## Message Types

20+ payload types including: `InitializeSession`, `PushPayload`, `PushCompressedPayload`, `PushBackendPayload`, `PushLogs`, `PushTraces`, `PushOTeLMetricSum/Histogram/Summary`, `SessionDataSync`, `ErrorGroupDataSync`, `HealthCheck`.

All implement `RetryableMessage` interface (GetType, GetFailures, SetFailures, GetMaxRetries).

## Producer Config

- **Balancer:** Hash-based (partition by key)
- **Compression:** Zstandard
- **Batch:** 10,000 messages / 256 MiB / 500ms flush
- **Acks:** RequireOne
- **Auth:** SASL/SCRAM-SHA512 + TLS 1.2 in production; plain TCP in Docker

## Consumer Config

- **Group:** `group-default_<topic>`
- **Prefetch:** 1,000 messages
- **Commit:** Every 1 second (auto) + explicit after processing
- **Rebalance:** 60s production / 1s dev
- **Max bytes:** 256 MiB

## OTel Context Propagation

W3C Trace Context injected into Kafka message headers on produce, extracted on consume. Enables end-to-end tracing from SDK → producer → Kafka → consumer → database.

## Dependencies

**Imports:** `segmentio/kafka-go`, `env`, `util`, `clickhouse` (row types), `public-graph/graph/model`

**Imported by (24 files):** `main.go` (8 producer instances), `worker/`, `public-graph/`, `private-graph/`, `store/`, `otel/`, migration scripts

## Testing

2 tests:
- `TestQueue_Submit` — **skipped by default** (needs live Kafka). Tests multi-writer/reader with 1,120 messages.
- `TestPartitionKey` — deterministic hash balancer verification.

### Priority Test Targets

1. Message serialization/deserialization round-trip (all 20+ types)
2. Topic name generation with prefixes
3. OTel context injection/extraction
4. Config override behavior (batch size, message size, async mode)

## Gotchas

- **No retry logic** — `TaskRetries = 0`. Failed messages are logged and dropped.
- **Synchronous by default** — each `Submit()` blocks on broker ack. Only `AsyncProducerQueue` overrides this.
- **256 MiB max message** — much larger than typical. Supports batch flushes of 10K+ rows.
- **DataSync is single-partition** — ensures ordering for PostgreSQL sync messages. Don't change.
- **Dev/test auto-creates topics** — 8 partitions, RF=1. Production topics must pre-exist.
- **Background stats goroutine** — logs every 5s, never cleaned up on shutdown.
- **Deprecated HubSpot types** — enum values 12-16 are no-ops. Don't reuse.
