# HoldFast: Go → .NET 10 Migration Plan

## Why

The Go compiler repeatedly crashes when compiling gqlgen's 88,298-line `generated.go` file, freezing the Proxmox hypervisor host and taking down DNS/DHCP for the entire network. Three host crashes in 24 hours. The root cause is architectural — gqlgen produces a single massive file that is unreasonable for the Go compiler's memory model.

## Solution

Replace the Go backend with .NET 10 / EF Core 10 using Hot Chocolate for GraphQL. Hot Chocolate is code-first with **zero codegen** — the generated file problem cannot exist.

## Architecture

```
src/dotnet/
├── HoldFast.Backend.slnx           # Solution file
├── src/
│   ├── HoldFast.Api/               # ASP.NET host, startup, middleware
│   ├── HoldFast.Domain/            # Entity models, enums, interfaces
│   ├── HoldFast.Data/              # EF Core DbContext, migrations
│   ├── HoldFast.Data.ClickHouse/   # ClickHouse ADO.NET + Dapper
│   ├── HoldFast.GraphQL.Public/    # Hot Chocolate: data ingestion endpoint
│   ├── HoldFast.GraphQL.Private/   # Hot Chocolate: dashboard API endpoint
│   ├── HoldFast.Worker/            # Kafka consumers as BackgroundServices
│   ├── HoldFast.Integrations/      # Slack, Jira, Linear, GitHub, etc.
│   ├── HoldFast.Storage/           # S3/filesystem abstraction
│   └── HoldFast.Shared/            # Redis, Kafka, OTel, compression
└── tests/
    ├── HoldFast.Domain.Tests/
    ├── HoldFast.Data.Tests/
    ├── HoldFast.GraphQL.Tests/
    └── HoldFast.Worker.Tests/
```

## NuGet Packages (all mainstream, actively maintained)

| Component | NuGet Package | Replaces Go |
|-----------|--------------|-------------|
| GraphQL | HotChocolate.AspNetCore | gqlgen (88K-line codegen) |
| ORM | Microsoft.EntityFrameworkCore + Npgsql | GORM |
| ClickHouse | ClickHouse.Client + Dapper | clickhouse-go |
| Kafka | Confluent.Kafka | kafka-go |
| Redis | StackExchange.Redis | go-redis |
| S3 | AWSSDK.S3 | aws-sdk-go-v2 |
| OpenTelemetry | OpenTelemetry.* | go.opentelemetry.io |
| Logging | Serilog.AspNetCore | logrus |
| Slack | SlackNet | slack-go |
| Email | SendGrid | sendgrid-go |
| GitHub | Octokit | go-github |
| AI | OpenAI | go-openai |
| Auth | JwtBearer + OpenIddict | go-oauth2 + go-oidc |
| pgvector | Pgvector.EntityFrameworkCore | raw SQL |
| Health | AspNetCore.HealthChecks.NpgSql | custom |

## Migration Phases

### Phase 0: Scaffold (DONE)
- [x] .NET 10 solution with 14 projects
- [x] 50+ EF Core entity models mirroring GORM models
- [x] DbContext with snake_case mapping (matches existing PostgreSQL schema)
- [x] Hot Chocolate wired up with query + mutation types
- [x] 23 unit tests passing
- [x] Builds with 0 errors

### Phase 1: Private Graph (Dashboard API)
- [ ] Port all private GraphQL queries to Hot Chocolate
- [ ] Port all private GraphQL mutations
- [ ] Port subscriptions (WebSocket)
- [ ] Wire up EF Core + ClickHouse for data queries
- [ ] Auth middleware (JWT, OAuth, SSO)
- [ ] Run both Go and .NET endpoints, compare responses

### Phase 2: Public Graph (Data Ingestion)
- [ ] Port public mutations (initializeSession, pushPayload, etc.)
- [ ] Wire up Kafka producers
- [ ] Session replay event handling
- [ ] Shadow-traffic testing (dual writes)

### Phase 3: Workers
- [ ] Kafka consumers as BackgroundService
- [ ] Session processing pipeline
- [ ] Error grouping (embeddings)
- [ ] Alert evaluation
- [ ] Scheduled tasks (auto-resolve, cleanup, refresh)

### Phase 4: Integrations + Auth
- [ ] Slack, Jira, Linear, GitHub, GitLab, ClickUp, Height, Vercel
- [ ] Discord, Microsoft Teams
- [ ] OAuth2 server (OpenIddict)
- [ ] SSO/OIDC
- [ ] Firebase auth (if needed)

### Phase 5: Cut Over
- [ ] Remove Go backend
- [ ] Update Docker/deployment configs
- [ ] Update CI workflows
- [ ] Clean up dual-write infrastructure

## Key Differences from Go

| Aspect | Go | .NET |
|--------|----|----- |
| GraphQL | Codegen (88K-line file) | Code-first (zero files) |
| Null handling | `*int`, `*string` via pointy | `int?`, `string?` native |
| Error handling | `if err != nil` (~2000x) | Exceptions with stack traces |
| Concurrency | Goroutines + channels | async/await + Channel\<T\> |
| Serialization | encoding/gob (Go-only) | System.Text.Json (standard) |
| Build | Single binary | Single binary (AOT) or self-contained |
| Compilation | Crashes on 88K-line file | No codegen files to compile |

## Decision Log

| Date | Decision | Reason |
|------|----------|--------|
| 2026-03-20 | Begin .NET migration | 3rd Proxmox host crash from Go compiler |
| 2026-03-20 | Hot Chocolate over Apollo | Native .NET, code-first, best perf |
| 2026-03-20 | EF Core over Dapper-only | Migrations, LINQ, relationships |
| 2026-03-20 | Confluent.Kafka over custom | Official Apache Kafka .NET client |
| 2026-03-20 | Keep same PostgreSQL schema | Zero-downtime migration, shared DB |
