# Backend Module

## Purpose

The Go backend is the core of HoldFast — it handles all data ingestion, processing, storage, and serves the dashboard API. It runs as a single binary with configurable runtime modes (all-in-one, or split into public-graph, private-graph, and worker services).

## Architecture

- **Language**: Go 1.23+
- **HTTP Router**: Chi
- **GraphQL**: gqlgen with dual endpoints (public for ingestion, private for dashboard)
- **Database**: PostgreSQL (GORM) for application data, ClickHouse for analytics/time-series
- **Cache**: Redis
- **Message Queue**: Apache Kafka
- **Storage**: S3-compatible object storage for session recordings

## Module Path

`github.com/BrewingCoder/holdfast/src/backend`

## Key Packages

| Package | Purpose |
|---------|---------|
| `main.go` | Entrypoint — runtime mode selection, service initialization |
| `public-graph/` | GraphQL schema and resolvers for data ingestion from SDKs |
| `private-graph/` | GraphQL schema and resolvers for the dashboard frontend |
| `worker/` | Kafka consumer handlers for async processing |
| `model/` | GORM database models, migrations, workspace settings |
| `store/` | Data access layer — repository pattern over Postgres and ClickHouse |
| `clickhouse/` | ClickHouse-specific queries, schema, session/log/trace storage |
| `redis/` | Redis cache utilities |
| `storage/` | S3/object storage abstraction for session recordings |
| `otel/` | OpenTelemetry metric and trace extraction |
| `kafka-queue/` | Kafka queue abstraction and message types |
| `parser/` | ANTLR-based search query parser |
| `queryparser/` | Query language parsing utilities |
| `errorgroups/` | Error grouping and deduplication logic |
| `stacktraces/` | Stack trace processing, enhancement, and source mapping |
| `embeddings/` | Vector embeddings for error similarity search |
| `alerts/` | Alert rule engine (v1 and v2) with multi-destination delivery |
| `pricing/` | Stubbed billing module — `IsWithinQuota` always returns true |
| `email/` | SendGrid email integration for transactional alerts |
| `geolocation/` | IP-based geolocation lookups (MaxMind GeoLite2) |
| `enterprise/` | License validation and update checking |
| `env/` | Environment variable configuration (all service config lives here) |
| `integrations/` | OAuth integrations (GitHub, GitLab, Jira, ClickUp, Height, Cloudflare) |
| `lambda-functions/` | AWS Lambda handlers for session delete, export, insights, digests |
| `prompts/` | AI prompt templates for error suggestions and session insights |
| `openai_client/` | OpenAI API client (to be replaced with Anthropic — see ROADMAP Phase 4) |

## Dependencies

**What imports this module:**
- `go.work` includes it as a workspace module
- Docker builds compile it into the backend binary
- The frontend communicates with it via GraphQL

**What this module imports (internal):**
- `github.com/BrewingCoder/holdfast/sdk/highlight-go` — Go SDK for self-instrumentation

## Data Flow

```
Client SDKs → Public GraphQL (public-graph/) → Kafka → Worker (worker/) → ClickHouse + PostgreSQL
                                                                          ↓
Dashboard Frontend → Private GraphQL (private-graph/) → ClickHouse + PostgreSQL queries
```

## Configuration

All configuration is via environment variables loaded through `env/environment.go`. Key variables:

| Variable | Purpose |
|----------|---------|
| `PSQL_HOST`, `PSQL_PORT`, `PSQL_USER`, `PSQL_PASSWORD` | PostgreSQL connection |
| `CLICKHOUSE_ADDRESS`, `CLICKHOUSE_USERNAME` | ClickHouse connection |
| `KAFKA_SERVERS` | Kafka broker addresses |
| `REDIS_ADDRESS` | Redis connection |
| `REACT_APP_FRONTEND_URI` | Frontend URL (used in emails, alerts) |
| `OPENAI_API_KEY` | AI features (error suggestions, session insights) |
| `SENDGRID_API_KEY` | Transactional email |

See `env/environment.go` for the full list.

## Runtime Modes

The binary supports multiple runtime modes via the `-runtime` flag:

| Mode | What it does |
|------|-------------|
| `all` | Combined mode — runs everything in one process |
| `public-graph` | Only the public GraphQL endpoint (data ingestion) |
| `private-graph` | Only the private GraphQL endpoint (dashboard API) |
| `worker` | Only the Kafka consumer and async processing |

## Testing

### Current State

Run backend tests:
```bash
cd src/backend && make test           # Full suite (needs Postgres + ClickHouse)
cd src/backend && make test-and-coverage  # With coverage report
```

Unit tests (no infrastructure needed):
```bash
cd src/backend && go test -short ./parser/... ./queryparser/... ./env/... \
  ./assets/... ./errorgroups/... ./vercel/... ./http/... ./alerts/... ./pricing/...
```

### Initial SonarQube Analysis (2026-03-19)

| Metric | Value | Notes |
|--------|-------|-------|
| **Coverage** | 5.7% | Inherited from upstream. Embarrassing for a project this size. |
| **Lines of Code** | 62,572 (198K total with tests/comments) | Large backend — GraphQL APIs, workers, storage, alerts |
| **Bugs** | 12 | Needs triage |
| **Vulnerabilities** | 1 | Needs immediate attention |
| **Code Smells** | 1,098 | Expected for inherited code with no prior static analysis |
| **Duplication** | 2.7% | Actually not bad |
| **Security Hotspots** | 0 | Clean |

This is where we start. The quality gate is set, the ratchet is in place, and every PR from this point forward must maintain or improve these numbers. Picking away at 5.7% coverage on 62K lines of Go is a herculean effort, but it's the right thing to do. See [ROADMAP Phase 7](../../docs/ROADMAP.md) for the test coverage investment plan.

The priority order for coverage improvement:
1. `public-graph/` resolvers — data ingestion path, bugs here = data loss
2. `private-graph/` resolvers — dashboard API, bugs here = broken UI
3. `worker/` handlers — async processing, silent failures
4. `clickhouse/` queries — wrong results = misleading dashboards
5. `store/` data access — every feature depends on this

Most of these need a running Postgres + ClickHouse + Redis + Kafka to test. The infrastructure for that (`docker-compose.test.yml`) is on the roadmap.

## Gotchas

- **`IsWithinQuota` always returns true** — billing was stripped, all ingestion is unlimited. Don't reintroduce quota checks.
- **Dual GraphQL endpoints** — public (ingestion) and private (dashboard) have separate schemas, resolvers, and middleware. Don't mix them.
- **Worker handler types** — there are 7+ worker types (main, batched, datasync, traces, metric-sum, metric-histogram, metric-summary). Each processes a specific Kafka topic.
- **ClickHouse vs PostgreSQL** — analytics queries (sessions, logs, traces, metrics) go to ClickHouse. Application state (workspaces, users, settings) lives in PostgreSQL. Know which to query.
- **`AllWorkspaceSettings` defaults** — all feature flags default to `true`. Don't add new gates without defaulting them to enabled.
- **GeoLite2-City.mmdb** — the MaxMind GeoIP database was removed during repo migration (corruption). Needs re-download from MaxMind for geolocation features to work.
- **Enterprise module** — checks for updates against `github.com/BrewingCoder/holdfast/releases`. License validation uses AES-CBC + RSA-PKCS1v15 with a baked-in public key.
- **Localhost SSL key in repo** — `localhostssl/server.key` is an embedded EC private key for dev HTTPS. SonarQube flags this as a security hotspot. It's a self-signed localhost cert, not a production credential, but should be removed and generated at runtime. See [#16](https://github.com/BrewingCoder/holdfast/issues/16).
- **OpenAI dependency** — AI features are OpenAI-only (GPT-3.5). Anthropic/Claude replacement is planned (ROADMAP Phase 4).
