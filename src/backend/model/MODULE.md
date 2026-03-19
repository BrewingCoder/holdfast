# Model Package

## Purpose

The foundational data layer for HoldFast. This package defines every database entity (72 GORM models), handles schema migrations, and provides helper functions used across the entire backend. It is the **most imported package** in the backend — 133 files depend on it.

## Module Path

`github.com/BrewingCoder/holdfast/src/backend/model`

## Files

| File | Lines | Purpose |
|------|-------|---------|
| `model.go` | 2,516 | All 72 GORM models, migrations, DB setup, helper methods |
| `sessionalerts.go` | 100 | Session alert types and integration constants |
| `stringarray.go` | 35 | GraphQL marshaling for PostgreSQL string arrays |
| `timestamp.go` | 33 | RFC3339Nano timestamp serialization |
| `id.go` | 28 | Int64ID GraphQL marshaling |
| `pricing.go` | 20 | Product types and billing intervals (stubbed for HoldFast) |
| `model_test.go` | 11 | Single test — VerboseID round-trip |

## Entity Map

### Hierarchy

```
Workspace (org-level container)
├── AllWorkspaceSettings (feature flags, AI config, billing — all enabled)
├── Admin ←→ WorkspaceAdmin (many-to-many with roles + project ACLs)
├── Project (application being monitored)
│   ├── Session → SessionComment, SessionInterval, RageClickEvent, SessionExport
│   ├── ErrorGroup → ErrorObject, ErrorFingerprint, ErrorTag, ErrorGroupEmbeddings
│   ├── Alert → AlertDestination (unified alert system)
│   ├── Dashboard → DashboardMetric → DashboardMetricFilter
│   ├── Field (indexed property values for search)
│   ├── ProjectFilterSettings (sampling, rate limits)
│   ├── SetupEvent (first-data-received tracker)
│   └── Service (backend service metadata)
├── Graph, Visualization (ClickHouse query definitions)
├── SavedSegment (user-defined cohorts)
└── IntegrationWorkspaceMapping, VercelIntegrationConfig (OAuth + external)
```

### Key Models (72 total)

| Category | Models | Notes |
|----------|--------|-------|
| **Workspace & Project** | Workspace, Project, WorkspaceAdmin, WorkspaceInviteLink, AllWorkspaceSettings, ProjectFilterSettings | Workspace → many Projects. Admin access is per-workspace with optional per-project ACLs. |
| **User & Auth** | Admin, SSOClient, OAuthClientStore, OAuthOperation, EmailOptOut | Admin = dashboard user. SSOClient stores OIDC provider config by domain. |
| **Session Replay** | Session, SessionInterval, SessionComment, SessionCommentTag, RageClickEvent, EventChunk, SessionExport, SessionInsight | Session is the core entity. Comments support tags, replies, followers, Slack threads. |
| **Error Tracking** | ErrorGroup, ErrorObject, ErrorFingerprint, ErrorGroupEmbeddings, ErrorObjectEmbeddings, ErrorTag, ErrorGroupActivityLog | ErrorGroup → many ErrorObjects. Fingerprints support code, metadata, and JSON types. Embeddings via pgvector (Ada-1536 or gte-large-1024). |
| **Alerts** | Alert, AlertDestination, ErrorAlert*, SessionAlert*, LogAlert*, MetricMonitor | Unified Alert + AlertDestination is the current system. *Deprecated models kept for backward compat. |
| **Dashboards** | Dashboard, DashboardMetric, DashboardMetricFilter, Graph, Visualization | Custom dashboards with ClickHouse query backing. |
| **Analytics** | Field, DailySessionCount, DailyErrorCount, UserJourneyStep | Field stores indexed property triplets (name, type, value). Daily counts are materialized views. |

## Database Setup

```go
SetupDB(ctx, "holdfast")
// → PostgreSQL via GORM
// → 15 max open connections
// → Batch insert size 5000
// → Extensions: pgcrypto, vector (pgvector), uuid-ossp
```

### Migration Strategy

No separate `.sql` files — migrations are inline in `MigrateDB()`:
- GORM `AutoMigrate()` from struct tags
- Custom SQL for: extensions, functions, materialized views, indexes, constraint changes
- `secure_id_generator()` — PostgreSQL function generating base64-encoded 21-byte random tokens

### Materialized Views

- `daily_session_counts_view` — 3-month rolling aggregation
- `daily_error_counts_view` — 3-month rolling aggregation
- Used to avoid scanning millions of rows for date histograms

## Key Patterns

### Secure IDs

Sessions and ErrorGroups use `secure_id_generator()` as default for their `SecureID` field — URL-safe base64 tokens (+ → 0, / → 1, = stripped). Separate from `VerboseID` which is a hashids-encoded obfuscated integer for backward-compatible URLs.

### JSONB Custom Type

`JSONB` implements `driver.Valuer` and `sql.Scanner` for storing arbitrary JSON in PostgreSQL JSONB columns. Used by WorkspaceSettings, AlertIntegrations, and others.

### Vector Embeddings

`Vector` type wraps `[]float32` for pgvector columns. Used in `ErrorGroupEmbeddings` (1536-dim Ada or 1024-dim gte-large) for error similarity search and deduplication.

### Workspace Settings (AllWorkspaceSettings)

One row per workspace. Every feature flag **defaults to enabled** for HoldFast:
- AI features: `AIApplication`, `AIInsights`, `AIQueryBuilder`
- Embedding: `ErrorEmbeddingsGroup`, `ErrorEmbeddingsTagGroup`, threshold 0.2
- Unlimited: dashboards, projects, retention, seats — all true
- Billing gates: all true (no-op, billing is stripped)

### GORM Hooks

- `Project.BeforeCreate` — generates random `Secret`
- `Workspace.BeforeCreate` — generates random `Secret`
- `Session.BeforeCreate` — sets `LastUserInteractionTime`
- `SystemConfiguration.BeforeCreate` — initializes error filters and ignored files

## Dependencies

**What imports this package (133 files):**
- `store/` — data access layer
- `private-graph/graph/` — dashboard GraphQL resolvers
- `public-graph/graph/` — ingestion GraphQL resolvers
- `worker/` — async processing
- `clickhouse/` — analytics queries
- `alerts/` — alert engine
- `embeddings/` — error grouping
- `kafka-queue/` — message types
- `email/` — billing notifications
- Everything else in the backend

**What this package imports:**
- `env` — configuration
- `email` — email types
- `private-graph/graph/model` — GraphQL input types
- External: GORM, pq, pgvector, Slack SDK, SendGrid, hashids, ttlcache

## Testing

### Current State

1 test file, 1 test — `TestVerboseID` round-trip conversion.

**Coverage is essentially zero.** This is a problem because:
- 72 models with complex relationships
- Migration logic with raw SQL
- Helper methods (Slack formatting, email dedup, property serialization)
- GORM hooks with side effects
- Custom type marshaling (JSONB, Vector, Timestamp, Int64ID)

### Priority Test Targets

1. **Custom types** — JSONB, Vector, Timestamp, Int64ID marshaling/unmarshaling
2. **VerboseID** — already tested, but add edge cases
3. **GORM hooks** — BeforeCreate on Project, Workspace, Session, SystemConfiguration
4. **Helper methods** — `GetUserProperties`, `SetUserProperties`, `GetSlackAttachment`, `AdminEmailAddresses`
5. **Migration** — verify `secure_id_generator()` function output format
6. **AllWorkspaceSettings defaults** — ensure all flags default to enabled

## Gotchas

- **`model.go` is 2,516 lines** — everything is in one file. Splitting it is desirable but risky due to init() ordering and circular reference potential.
- **No foreign key constraints at DB level** — GORM disables FK constraints during migration by default. Relationships are enforced in application code only.
- **Deprecated alert models** — `ErrorAlert`, `SessionAlert`, `LogAlert` are deprecated but still in the schema. New code should use `Alert` + `AlertDestination`.
- **Partition boundary** — `PARTITION_SESSION_ID = 30_000_000`. Sessions above this ID are in a separate partition. Don't change this constant.
- **`Int64Model` vs `Model`** — Error-related tables use `Int64Model` (bigint PK). Everything else uses `Model` (int32 PK). Don't mix them.
- **`gorm:"-:migration"` tags** — Some fields (e.g., `ErrorGroup.ErrorTagID`) have this tag to prevent GORM from creating columns. Don't remove without understanding why.
- **`SendBillingNotifications`** — Uses a 15-min TTL cache + DB dedup. This is a no-op in HoldFast (billing stripped) but the code remains.
- **Pricing constants** — `pricing.go` defines product types and intervals. All stubbed — don't reintroduce billing logic.
