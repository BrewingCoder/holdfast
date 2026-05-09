# ── Frontend pruner ───────────────────────────────────────────────────
# HOL-20: the dedicated frontend nginx container was folded into the
# backend image. The frontend build stages below mirror what the now-removed
# frontend.Dockerfile did; the runtime stage at the bottom copies the
# resulting bundle into /app/wwwroot for Kestrel to serve.
FROM --platform=$BUILDPLATFORM node:lts-alpine AS frontend-pruner

RUN apk add --no-cache libc6-compat && npm install -g turbo@^2

WORKDIR /app
COPY . .
RUN turbo prune @holdfast-io/frontend --docker

# ── Frontend build ────────────────────────────────────────────────────
FROM --platform=$BUILDPLATFORM node:lts-alpine AS frontend-build

RUN apk update && apk add --no-cache build-base python3

WORKDIR /app

COPY .yarnrc.yml .
COPY .yarn/patches ./.yarn/patches
COPY .yarn/releases ./.yarn/releases

# Install only pruned workspace deps. rrweb/package.json is added explicitly
# because turbo prune doesn't pick it up (its devDeps are imported by
# rrweb/vite.config.default.ts which every rrweb package uses).
COPY --from=frontend-pruner /app/out/json/ .
COPY rrweb/package.json ./rrweb/package.json
# Use the full yarn.lock so --immutable works for rrweb's deps.
COPY yarn.lock ./yarn.lock

RUN --mount=type=cache,target=/root/.yarn/berry/cache,sharing=locked \
    yarn install

COPY --from=frontend-pruner /app/out/full/ .

# Root config files turbo prune omits.
COPY rrweb/tsconfig.base.json ./rrweb/tsconfig.base.json
COPY rrweb/tsconfig.json ./rrweb/tsconfig.json
COPY rrweb/vite.config.default.ts ./rrweb/vite.config.default.ts
COPY rrweb/turbo.json ./rrweb/turbo.json
COPY tsconfig.json ./tsconfig.json
COPY graphql.config.js ./graphql.config.js

# GraphQL schemas live outside the frontend workspace; needed for codegen.
COPY src/backend/localhostssl ./src/backend/localhostssl
COPY src/backend/private-graph ./src/backend/private-graph
COPY src/backend/public-graph ./src/backend/public-graph

# Bake URLs at build time. Defaults match the lean single-port deploy where
# the backend serves both API and frontend on :8082. Override via build args
# (e.g. for production deployments behind a custom domain). The
# frontend-entrypoint.py runtime substitution from the old frontend.Dockerfile
# was dropped — for self-hosted single-tenant deploys, rebuilding on URL
# changes is straightforward and avoids the runtime mutation step.
ARG NODE_OPTIONS="--max-old-space-size=8192"
ARG REACT_APP_AUTH_MODE=Password
ARG REACT_APP_FRONTEND_URI=http://localhost:8082
ARG REACT_APP_PRIVATE_GRAPH_URI=http://localhost:8082/private
ARG REACT_APP_PUBLIC_GRAPH_URI=http://localhost:8082/public
ARG REACT_APP_OTLP_ENDPOINT=http://localhost:8082/otel
ARG REACT_APP_IN_DOCKER=true

ENV REACT_APP_AUTH_MODE=$REACT_APP_AUTH_MODE
ENV REACT_APP_FRONTEND_URI=$REACT_APP_FRONTEND_URI
ENV REACT_APP_PRIVATE_GRAPH_URI=$REACT_APP_PRIVATE_GRAPH_URI
ENV REACT_APP_PUBLIC_GRAPH_URI=$REACT_APP_PUBLIC_GRAPH_URI
ENV REACT_APP_OTLP_ENDPOINT=$REACT_APP_OTLP_ENDPOINT
ENV REACT_APP_IN_DOCKER=$REACT_APP_IN_DOCKER

RUN --mount=type=cache,target=/root/.turbo,sharing=locked \
    TURBO_CACHE_DIR=/root/.turbo npx turbo run build:fast --filter=@holdfast-io/frontend...

# ── Backend build ─────────────────────────────────────────────────────
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build

WORKDIR /src

# Copy project files first for layer caching
COPY src/dotnet/HoldFast.Backend.slnx .
COPY src/dotnet/src/HoldFast.Api/HoldFast.Api.csproj src/HoldFast.Api/
COPY src/dotnet/src/HoldFast.Data/HoldFast.Data.csproj src/HoldFast.Data/
COPY src/dotnet/src/HoldFast.Data.ClickHouse/HoldFast.Data.ClickHouse.csproj src/HoldFast.Data.ClickHouse/
COPY src/dotnet/src/HoldFast.Domain/HoldFast.Domain.csproj src/HoldFast.Domain/
COPY src/dotnet/src/HoldFast.GraphQL.Private/HoldFast.GraphQL.Private.csproj src/HoldFast.GraphQL.Private/
COPY src/dotnet/src/HoldFast.GraphQL.Public/HoldFast.GraphQL.Public.csproj src/HoldFast.GraphQL.Public/
COPY src/dotnet/src/HoldFast.Integrations/HoldFast.Integrations.csproj src/HoldFast.Integrations/
COPY src/dotnet/src/HoldFast.Shared/HoldFast.Shared.csproj src/HoldFast.Shared/
COPY src/dotnet/src/HoldFast.Storage/HoldFast.Storage.csproj src/HoldFast.Storage/
COPY src/dotnet/src/HoldFast.Worker/HoldFast.Worker.csproj src/HoldFast.Worker/

RUN dotnet restore src/HoldFast.Api/HoldFast.Api.csproj

# Copy all source and publish
COPY src/dotnet/src/ src/
RUN dotnet publish src/HoldFast.Api/HoldFast.Api.csproj \
    -c Release \
    -o /app \
    --no-restore

# ── Runtime stage ─────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

ARG REACT_APP_COMMIT_SHA
ENV REACT_APP_COMMIT_SHA=$REACT_APP_COMMIT_SHA

LABEL org.opencontainers.image.source=https://github.com/BrewingCoder/holdfast
LABEL org.opencontainers.image.description="HoldFast .NET Backend (with frontend bundle)"
LABEL org.opencontainers.image.licenses="AGPL-3.0"

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=backend-build /app .

# Frontend SPA bundle — served by Kestrel via UseStaticFiles + MapFallbackToFile.
COPY --from=frontend-build /app/src/frontend/build /app/wwwroot

# ClickHouse migration files — applied at startup by ClickHouseMigrationService.
# Disable via ClickHouse__Migrations__Disabled=true when the schema is managed
# externally (e.g. golang-migrate run by a Helm pre-job).
COPY src/backend/clickhouse/migrations /app/clickhouse-migrations

# Postgres analytics migration files (HOL-26) — applied at startup by
# PostgresMigrationService when the Postgres analytics backend is enabled.
# Disable via PostgresAnalytics__Migrations__Disabled=true.
COPY src/dotnet/src/HoldFast.Data.Postgres/Migrations /app/postgres-analytics-migrations

# Default port — matches Go backend convention
ENV ASPNETCORE_URLS=http://+:8082
EXPOSE 8082

# Storage volume — matches Go backend's /highlight-data mount point
VOLUME /highlight-data
ENV Storage__Type=filesystem
ENV Storage__FilesystemRoot=/highlight-data

# Runtime mode — default all-in-one, override with HOLDFAST_RUNTIME env var
ENV HOLDFAST_RUNTIME=all

HEALTHCHECK --interval=15s --timeout=5s --retries=3 \
    CMD curl -f http://localhost:8082/health || exit 1

ENTRYPOINT ["dotnet", "HoldFast.Api.dll"]
