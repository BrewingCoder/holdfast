# Util Package

## Purpose

Shared utilities for the entire backend — runtime mode selection, distributed tracing, panic recovery, test infrastructure, and common helpers. Imported by **45 files** across the codebase. This is the "toolbox" package.

## Module Path

`github.com/BrewingCoder/holdfast/src/backend/util`

## Files

| File | Lines | Purpose |
|------|-------|---------|
| `runtime.go` | 84 | Runtime modes (All, Worker, PublicGraph, PrivateGraph) and handler types |
| `tracer.go` | 119 | OpenTelemetry span tracing abstraction (MultiSpan) |
| `tests.go` | 75 | Test DB utilities — isolated DB per test, auto-cleanup |
| `tracer-graphql.go` | 63 | gqlgen middleware for GraphQL request tracing |
| `request.go` | 45 | JSON HTTP client wrapper |
| `recovery.go` | 27 | Panic recovery handlers |
| `strings.go` | 23 | JSON/string parsing helpers |
| `random.go` | 15 | Random string generation |
| **Total** | **451** | |

## Key Components

### Runtime Modes (`runtime.go`)

The backend binary supports multiple runtime modes via the `-runtime` CLI flag. This file defines the valid modes and handler types:

**Runtime types:** `All`, `AllGraph`, `Worker`, `PublicGraph`, `PrivateGraph`

**Handler types (worker-specific):**
- DB migration, metric monitors, log alerts
- Kafka workers: main, batched, datasync, traces
- Metrics: sum, histogram, summary
- Auto-resolver, session delete, scheduled tasks

`GetRuntime()` parses CLI flags and returns the validated runtime/handler pair. Invalid values cause startup failure.

### Tracing (`tracer.go`)

`MultiSpan` wraps OpenTelemetry + Highlight SDK spans with a builder pattern:

```go
span, ctx := util.StartSpanFromContext(ctx, "operation-name",
    util.Tag("key", "value"),
    util.ResourceName("resource"),
)
defer span.Finish()
```

- Spans can be disabled via context (`ContextKeyHighlightTracingDisabled`)
- Nil-safe — all methods check for nil before using span
- Bridges into Highlight Go SDK for distributed tracing

### Test Infrastructure (`tests.go`)

```go
util.RunTestWithDBWipe(t, db, func(t *testing.T) {
    // test with isolated DB
})
```

- `CreateAndMigrateTestDB(name)` — creates a fresh PostgreSQL database with all migrations
- `ClearTablesInDB(db)` — truncates all 72 model tables between tests
- Used by 12+ test files across the backend

### Panic Recovery (`recovery.go`)

- `Recover()` — logs panic with stack trace, continues execution
- `RecoverAndCrash()` — logs panic, flushes Highlight SDK, exits with status 1

Used in worker goroutines and main() to prevent silent crashes.

## Dependencies

**What this imports:**
- `env` — environment config
- `model` — GORM models (for test DB setup)
- `highlight-go` SDK — distributed tracing
- `gqlgen/graphql` — GraphQL middleware
- `opentelemetry` — span API

**What imports this (45 files):**
- `main.go` — runtime mode selection
- `private-graph/`, `public-graph/` — GraphQL resolvers + tracing
- `worker/` — handler selection, panic recovery
- `clickhouse/` — span tracing on queries
- `store/`, `redis/`, `storage/` — span tracing
- `alerts/`, `jobs/` — tracing + recovery
- `kafka-queue/` — message processing spans
- 12+ test files — `RunTestWithDBWipe`, `CreateAndMigrateTestDB`

## Testing

**No tests for util itself.** The package provides test utilities used by others, but its own functions (runtime validation, tracing, recovery, string helpers) have zero coverage.

### Priority Test Targets

1. **`GetRuntime()`** — valid/invalid flag combinations
2. **Runtime/Handler validation** — `IsValid()`, `IsPublicGraph()`, `IsWorker()`, etc.
3. **`GenerateRandomString()`** — length, character set
4. **`JsonStringToStringArray()`** — valid JSON, invalid JSON, empty
5. **`RestRequest()`** — success, HTTP errors, JSON decode errors
6. **`MultiSpan`** — Finish with/without error, SetAttribute, disabled context

## Gotchas

- **Runtime modes control binary behavior** — a misconfigured `-runtime` flag means the wrong service starts. There's no health check to verify the mode matches the deployment intent.
- **Stripe handler stub** — `UsageReporting` handler type exists but billing is stripped. Don't reintroduce.
- **`tests.go` imports `model`** — creates a circular dependency risk. Test utilities know about all 72 models for table truncation.
- **No test for tracing** — the `MultiSpan` abstraction is used in every hot path but has zero tests.
- **`RecoverAndCrash` calls `os.Exit(1)`** — not testable without process-level testing.
- **`RestRequest` creates a new HTTP client per call** — no connection pooling for external API calls.
