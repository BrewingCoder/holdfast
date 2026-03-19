# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Identity

**This is HoldFast** — a clean, fully open-source fork of Highlight.io, licensed under AGPL-3.0.

**HoldFast is on-premise / self-hosted only. There is no SaaS model.** The goal of this project is not to replace Highlight.io's SaaS offering but to preserve and improve on a fully OSS, free, self-hosted observability platform. All features are enabled by default with no billing, no tiers, and no feature gates.

**Key implications for all code changes:**
- **No hardcoded domains.** Every service URL (public GraphQL, private GraphQL, OpenTelemetry collector, frontend, etc.) must be configurable via environment variables. The platform must be deployable to any domain, IP, or localhost.
- **No SaaS billing code.** Stripe, AWS Marketplace metering, and all pricing tier logic has been removed. Do not reintroduce billing gates or usage quotas.
- **No telemetry home.** The platform must not phone home to any external service. All telemetry stays within the user's deployment.
- **No marketing integrations.** HubSpot, Apollo.io, Clearbit, and similar lead-gen services have been removed. Do not reintroduce them.
- **Deployment-agnostic.** Code must support Docker Compose, Helm charts, and other self-hosted deployment methods. Configuration is via environment variables, not hardcoded values.

See `docs/HOLDFAST-NOTES.md` for fork history, `docs/CHANGELOG-FORK.md` for detailed changes from upstream, and `docs/GOVERNANCE.md` for contribution rules.

## Repository Overview

HoldFast is a full-stack observability platform that provides session replay, error monitoring, logging, and distributed tracing capabilities. The repository is structured as a monorepo containing:

- **Backend** (`src/backend/`): Go-based GraphQL API server with dual public/private GraphQL endpoints
- **Frontend** (`src/frontend/`): React/TypeScript dashboard application built with Vite
- **SDKs** (`sdk/`): Multi-language client libraries for integrating with HoldFast
- **RRWeb** (`rrweb/`): Forked session replay recording library (included as regular files)
- **Infrastructure** (`infra/docker/`, `infra/deploy/`): Docker compose and deployment configurations
- **Tests** (`tests/cypress/`, `tests/e2e/`): End-to-end and integration tests
- **Tools** (`tools/antlr/`, `tools/bin/`, `tools/scripts/`): Build and development utilities
- **Packages** (`packages/`): Shared packages including render and sourcemap-uploader

## Key Development Commands

### Backend Development
```bash
# In /src/backend directory
make start            # Start backend with doppler (recommended)
make start-no-doppler # Start backend without doppler
make debug            # Start with debugger attached
make test             # Run all tests with race detection
make migrate          # Run database migrations
make public-gen       # Generate public GraphQL schema
make private-gen      # Generate private GraphQL schema
```

### Frontend Development
```bash
# In /src/frontend directory
yarn dev              # Start development server
yarn build            # Build production bundle
yarn test             # Run tests
yarn test:watch       # Run tests in watch mode
yarn types:check      # TypeScript type checking
yarn lint             # Run ESLint
yarn codegen          # Generate GraphQL types from schema
```

### Monorepo Commands
```bash
# In root directory
yarn build:all        # Build all packages
yarn test:all         # Run all tests
yarn dev              # Start all dev services (frontend + backend)
yarn dev:frontend     # Start only frontend
yarn dev:backend      # Start only backend
yarn lint             # Run linting across all packages
```

### Docker Development
```bash
# In /infra/docker directory
docker-compose up     # Start all infrastructure services
# or use the convenience script:
./run-hobby.sh        # Start hobby deployment
```

## Architecture Overview

### Backend Architecture
- **Language**: Go 1.23+ with Chi HTTP router
- **Database**: PostgreSQL (GORM) for application data, ClickHouse for analytics/time-series data
- **Message Queue**: Apache Kafka for async processing
- **Cache**: Redis for caching and session management
- **GraphQL**: Dual endpoints - public (data ingestion) and private (dashboard)
- **Runtime Modes**: Can run as all-in-one, or split into public-graph, private-graph, and worker services

### Frontend Architecture
- **Framework**: React 18 with TypeScript
- **Build Tool**: Vite with SWC for transpilation
- **State Management**: Apollo Client for GraphQL state
- **Styling**: Tailwind CSS + CSS modules
- **Routing**: React Router v6
- **Monorepo**: Yarn workspaces with Turborepo

### Key Data Flow
1. **Data Ingestion**: Client SDKs → Public GraphQL → Kafka → Worker processes → Database
2. **Dashboard**: Frontend → Private GraphQL → Database queries
3. **Session Replay**: RRWeb recording → Compression → S3/filesystem storage
4. **Real-time**: WebSocket subscriptions for live data updates

## Configuration Philosophy

**All service endpoints and external URLs must be configurable via environment variables.** No hardcoded domains. The platform must work at any address — `localhost`, a private IP, a custom domain, or behind a reverse proxy.

Key environment variables for deployment:
- `PSQL_HOST`, `PSQL_PORT`, `PSQL_USER`, `PSQL_PASSWORD`: PostgreSQL connection
- `CLICKHOUSE_ADDRESS`, `CLICKHOUSE_USERNAME`: ClickHouse connection
- `KAFKA_SERVERS`: Kafka broker addresses
- `REDIS_ADDRESS`: Redis connection
- `REACT_APP_PUBLIC_GRAPH_URI`: Public GraphQL endpoint URL
- `REACT_APP_PRIVATE_GRAPH_URI`: Private GraphQL endpoint URL
- `REACT_APP_FRONTEND_URI`: Frontend dashboard URL
- `COLLECTOR_OTLP_ENDPOINT`: OpenTelemetry collector endpoint

When adding new features or integrations, **always** make external URLs configurable. Never assume a domain name.

## Common Development Patterns

### GraphQL Schema Generation
After modifying GraphQL schemas, regenerate types:
```bash
# For backend changes
make public-gen   # or make private-gen
# For frontend changes
yarn codegen
```

### Testing Strategy
- **Backend**: Go tests with race detection enabled
- **Frontend**: Vitest for unit tests, includes setup for GraphQL mocking
- **Database**: Test-specific database setup with environment variables

### Code Generation
- **Backend**: Uses gqlgen for GraphQL code generation
- **Frontend**: Uses graphql-codegen for TypeScript types
- **Build System**: Turborepo handles dependencies and caching

## Database Operations

### Development Database Setup
```bash
# Backend migrations
make migrate

# ClickHouse migrations run automatically on startup
```

## Development Workflow

### Getting Started
1. **Prerequisites**: Go 1.23+, Node.js 18+, Docker, Doppler CLI
2. **Start Infrastructure**: `cd infra/docker && docker-compose up`
3. **Backend Setup**: `cd src/backend && make migrate && make start`
4. **Frontend Setup**: `cd src/frontend && yarn dev`

### Making Changes
1. **Backend GraphQL**: Modify schema → `make public-gen` or `make private-gen`
2. **Frontend**: TypeScript changes trigger hot reload via Vite
3. **Database**: Add migration files, run `make migrate`

### Testing
```bash
# Run backend tests
cd src/backend && make test

# Run frontend tests
cd src/frontend && yarn test

# Run all tests
yarn test:all
```

## Build and Deployment

### Production Build
```bash
# Build everything
yarn build:all

# Build specific parts
yarn build:frontend
yarn build:backend
yarn build:sdk
```

### Docker Deployment
- **Hobby**: Single-node deployment with `infra/docker/run-hobby.sh`
- **Enterprise**: Scalable deployment with separate services
- **Development**: Local services via `cd infra/docker && docker-compose up`

## Code Organization

### Backend Structure
- `main.go`: Application entrypoint with runtime configuration
- `public-graph/`: GraphQL schema and resolvers for data ingestion
- `private-graph/`: GraphQL schema and resolvers for dashboard
- `worker/`: Async processing handlers
- `model/`: Database models and migrations
- `store/`: Data access layer
- `clickhouse/`: ClickHouse-specific queries and schema

### Frontend Structure
- `src/index.tsx`: Application entrypoint
- `src/components/`: Reusable UI components
- `src/pages/`: Route-specific components
- `src/graph/`: GraphQL queries and generated types
- `src/util/`: Utility functions and helpers

### SDK Structure
- `sdk/highlight-*/`: Language-specific client libraries
- `sdk/highlight-run/`: Core browser SDK (published as `@holdfast-io/browser`)
- Each SDK follows language-specific patterns and conventions

## What Was Removed From Upstream

The following components were stripped from the original Highlight.io codebase (see `docs/CHANGELOG-FORK.md` for details):

- **HubSpot** — CRM tracking
- **Apollo.io** — Lead enrichment / sales sequences
- **Clearbit** — Company data enrichment
- **Phonehome** — Telemetry reporting to Highlight servers
- **Stripe** — Subscription billing (stubbed to no-ops)
- **AWS Marketplace** — Usage metering
- **LaunchDarkly** — Feature flags (stubbed to return defaults)
- **highlight.io/** — Marketing website
- **Feature gates** — All defaulted to enabled

## Important Notes

- **Hot Reload**: Frontend supports hot reload; backend uses Air for live reload
- **GraphQL**: Always run codegen after schema changes
- **Environment**: Use doppler for secrets in development
- **Debugging**: Backend supports delve debugger on port 2345
- **Performance**: ClickHouse is used for high-volume analytics queries
- **Security**: CORS is configured differently for public vs private endpoints
- **License**: AGPL-3.0 — all contributions must be compatible
