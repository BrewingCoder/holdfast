# HoldFast .NET Backend

.NET 10 replacement for the Go backend, providing the same GraphQL API, Kafka workers, and OTel ingestion endpoints.

## Solution Structure

| Project | Purpose |
|---------|---------|
| `HoldFast.Api` | ASP.NET Core host — HTTP endpoints, middleware, OTel ingestion, health checks |
| `HoldFast.Data` | EF Core DbContext, migrations, PostgreSQL configuration |
| `HoldFast.Data.ClickHouse` | ClickHouse read/write service (Dapper + ClickHouse.Client) |
| `HoldFast.Domain` | Entity models, enums, value objects — no dependencies on infrastructure |
| `HoldFast.GraphQL.Private` | Private (dashboard) GraphQL queries and mutations (Hot Chocolate) |
| `HoldFast.GraphQL.Public` | Public (SDK ingestion) GraphQL mutations and input types |
| `HoldFast.Integrations` | External integration adapters (Slack, Discord, Teams, etc.) |
| `HoldFast.Shared` | Cross-cutting services: auth, error grouping, session processing, alerts, Kafka, Redis |
| `HoldFast.Storage` | File storage abstraction (filesystem + S3) |
| `HoldFast.Worker` | Kafka background workers: error grouping, session events, log/trace/metric ingestion, auto-resolve, data retention, data sync |

## Prerequisites

- .NET 10 SDK
- PostgreSQL
- ClickHouse
- Kafka
- Redis (optional, for caching)

## Running

```bash
cd src/dotnet
dotnet build
dotnet run --project src/HoldFast.Api
```

## Testing

```bash
cd src/dotnet
dotnet test
```

2,305 tests across 6 test projects covering domain logic, data access, GraphQL resolvers, worker consumers, auth, OTel parsing, and service integration.

## Key Design Decisions

**Auth:** JWT tokens with `uid` and `admin_id` claims. `AuthMiddleware` validates tokens and populates `ClaimsPrincipal`. `AuthorizationService` checks workspace/project membership via EF Core. `AuthHelper` provides convenience methods for GraphQL resolvers.

**GraphQL:** Hot Chocolate 15 code-first. `[Service]` attribute for DI injection into resolvers. Private graph has 76 queries + 63 mutations. Public graph has 8 mutations for SDK data ingestion.

**ClickHouse:** Read/write split. Reads use Dapper for cursor-based pagination (Go-compatible cursor format). Writes use bulk insert via ClickHouse.Client. Models in `HoldFast.Data.ClickHouse/Models/`.

**Kafka:** `KafkaConsumerService<T>` base class handles deserialization and error recovery. Workers register as `BackgroundService` instances. Topics defined in `KafkaTopics` constants.

**Error Grouping:** Classic fingerprint matching (CODE + META hashes from stack frames). New errors create groups; matching errors increment existing groups. Alert evaluation runs inline after grouping.

**Session Processing:** Interval calculation (10s inactive threshold), rage click detection (project-configurable), active duration computation (7-day cap), event count histograms.

**OTel Ingestion:** POST endpoints at `/otel/v1/{logs,traces,metrics}` accept both OTel JSON (ExportLogsServiceRequest, etc.) and simple array formats. Supports gzip and snappy content encoding.

## Environment Variables

All configuration is via environment variables — no hardcoded domains. See the main `CLAUDE.md` for the full list. Key variables:

- `PSQL_HOST`, `PSQL_PORT`, `PSQL_USER`, `PSQL_PASSWORD` — PostgreSQL
- `CLICKHOUSE_ADDRESS` — ClickHouse
- `KAFKA_SERVERS` — Kafka brokers
- `REDIS_ADDRESS` — Redis
- `AUTH_MODE` — `jwt` (production) or `simple` (development)
