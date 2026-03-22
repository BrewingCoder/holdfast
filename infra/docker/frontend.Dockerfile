FROM --platform=$BUILDPLATFORM node:lts-alpine AS pruner

# turbo prune produces a minimal sub-monorepo containing only the packages
# @holdfast-io/frontend transitively depends on (~8 packages vs 50+).
# This dramatically shrinks the yarn install surface in the builder stage.
RUN npm install -g turbo@^2 --quiet

WORKDIR /app
COPY . .
RUN turbo prune @holdfast-io/frontend --docker

# ── Builder stage ─────────────────────────────────────────────────────────────
FROM --platform=$BUILDPLATFORM node:lts-alpine AS builder

RUN apk add --no-cache build-base python3

WORKDIR /app

# Yarn Berry config must be copied manually — turbo prune does not include it.
COPY .yarnrc.yml .
COPY .yarn/patches ./.yarn/patches
COPY .yarn/releases ./.yarn/releases

# Copy the pruned package manifests and lockfile, then install.
# Layer cache: only invalidated when package.json/yarn.lock changes.
COPY --from=pruner /app/out/json/ .
COPY --from=pruner /app/out/yarn.lock ./yarn.lock

RUN --mount=type=cache,target=/root/.yarn/berry/cache,sharing=locked \
    yarn install --immutable

# Copy pruned source tree and top-level configs.
COPY --from=pruner /app/out/full/ .
COPY turbo.json graphql.config.js tsconfig.json ./

# turbo prune includes rrweb package source but omits the rrweb root tsconfig.base.json
# that sub-packages extend. Copy it from the build context directly.
COPY rrweb/tsconfig.base.json ./rrweb/tsconfig.base.json

# GraphQL schemas used by codegen during the frontend build.
COPY src/backend/private-graph ./src/backend/private-graph
COPY src/backend/public-graph ./src/backend/public-graph
COPY src/backend/localhostssl ./src/backend/localhostssl

# ── Build ─────────────────────────────────────────────────────────────────────
# Bake in placeholder URLs — the runtime entrypoint replaces them from env vars.
ARG NODE_OPTIONS="--max-old-space-size=8192"
ARG REACT_APP_AUTH_MODE=firebase
ARG REACT_APP_FRONTEND_URI=https://app.highlight.io
ARG REACT_APP_PRIVATE_GRAPH_URI=https://pri.highlight.io
ARG REACT_APP_PUBLIC_GRAPH_URI=https://pub.highlight.run
ARG REACT_APP_OTLP_ENDPOINT=http://localhost:4318
ARG REACT_APP_IN_DOCKER=true

ENV REACT_APP_AUTH_MODE=$REACT_APP_AUTH_MODE \
    REACT_APP_FRONTEND_URI=$REACT_APP_FRONTEND_URI \
    REACT_APP_PRIVATE_GRAPH_URI=$REACT_APP_PRIVATE_GRAPH_URI \
    REACT_APP_PUBLIC_GRAPH_URI=$REACT_APP_PUBLIC_GRAPH_URI \
    REACT_APP_OTLP_ENDPOINT=$REACT_APP_OTLP_ENDPOINT \
    REACT_APP_IN_DOCKER=$REACT_APP_IN_DOCKER

# build:fast skips tsc — type checking is enforced in CI via the full `build` script.
RUN --mount=type=cache,target=/root/.turbo,sharing=locked \
    TURBO_CACHE_DIR=/root/.turbo yarn workspace @holdfast-io/frontend build:fast

# ── Runtime image ─────────────────────────────────────────────────────────────
FROM nginx:stable-alpine AS frontend-prod

RUN apk add --no-cache python3

LABEL org.opencontainers.image.source=https://github.com/BrewingCoder/holdfast
LABEL org.opencontainers.image.description="HoldFast Frontend Image"
LABEL org.opencontainers.image.licenses="AGPL-3.0"

COPY infra/docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY src/backend/localhostssl/server.key /etc/ssl/private/ssl-cert.key
COPY src/backend/localhostssl/server.pem /etc/ssl/certs/ssl-cert.pem
COPY infra/docker/frontend-entrypoint.py /frontend-entrypoint.py

WORKDIR /build
COPY --from=builder /app/src/frontend/build ./frontend/build

# Runtime env vars — replaced in constants.js by entrypoint.py at container start.
ENV REACT_APP_AUTH_MODE=firebase \
    REACT_APP_FRONTEND_URI=https://app.highlight.io \
    REACT_APP_PRIVATE_GRAPH_URI=https://pri.highlight.io \
    REACT_APP_PUBLIC_GRAPH_URI=https://pub.highlight.run \
    REACT_APP_OTLP_ENDPOINT=http://localhost:4318 \
    SSL=false

CMD ["python3", "/frontend-entrypoint.py"]
