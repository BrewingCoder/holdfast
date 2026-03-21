# ── Build stage ────────────────────────────────────────────────────────
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build

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

RUN dotnet restore HoldFast.Backend.slnx --runtime linux-x64

# Copy all source and publish
COPY src/dotnet/src/ src/
ARG TARGETARCH
RUN dotnet publish src/HoldFast.Api/HoldFast.Api.csproj \
    -c Release \
    -o /app \
    --no-restore \
    --self-contained false

# ── Runtime stage ─────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

ARG REACT_APP_COMMIT_SHA
ENV REACT_APP_COMMIT_SHA=$REACT_APP_COMMIT_SHA

LABEL org.opencontainers.image.source=https://github.com/BrewingCoder/holdfast
LABEL org.opencontainers.image.description="HoldFast .NET Backend"
LABEL org.opencontainers.image.licenses="AGPL-3.0"

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

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
