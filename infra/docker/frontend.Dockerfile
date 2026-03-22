FROM --platform=$BUILDPLATFORM node:lts-alpine AS pruner

RUN apk add --no-cache libc6-compat && npm install -g turbo@^2

WORKDIR /app
COPY . .
RUN turbo prune @holdfast-io/frontend --docker

# ── Dependency installation layer ────────────────────────────────────────────
# Uses the pruned package manifest (12 packages vs 87 in the full monorepo)
# to drastically reduce yarn install time.
FROM --platform=$BUILDPLATFORM node:lts-alpine AS frontend-build

RUN apk update && apk add --no-cache build-base python3

WORKDIR /app

COPY .yarnrc.yml .
COPY .yarn/patches ./.yarn/patches
COPY .yarn/releases ./.yarn/releases

# Install only pruned workspace deps
COPY --from=pruner /app/out/json/ .
COPY --from=pruner /app/out/yarn.lock ./yarn.lock

RUN --mount=type=cache,target=/root/.yarn/berry/cache,sharing=locked \
    yarn install --immutable

# ── Source copy ───────────────────────────────────────────────────────────────
COPY --from=pruner /app/out/full/ .

# turbo prune omits the rrweb root tsconfigs that each rrweb/packages/*/tsconfig.json
# extends with "../../tsconfig.base.json". Copy them explicitly from the build context.
COPY rrweb/tsconfig.base.json ./rrweb/tsconfig.base.json
COPY rrweb/tsconfig.json ./rrweb/tsconfig.json

# GraphQL schemas are outside the frontend workspace; copy them for codegen/typegen
COPY src/backend/localhostssl ./src/backend/localhostssl
COPY src/backend/private-graph ./src/backend/private-graph
COPY src/backend/public-graph ./src/backend/public-graph

# ── Build ────────────────────────────────────────────────────────────────────
# Bake in the same placeholder URLs the entrypoint knows to replace at runtime.
# REACT_APP_AUTH_MODE=firebase matches the entrypoint's replacement target.
# All other URLs match the upstream defaults the entrypoint expects to find.
ARG NODE_OPTIONS="--max-old-space-size=8192"
ARG REACT_APP_AUTH_MODE=firebase
ARG REACT_APP_FRONTEND_URI=https://app.highlight.io
ARG REACT_APP_PRIVATE_GRAPH_URI=https://pri.highlight.io
ARG REACT_APP_PUBLIC_GRAPH_URI=https://pub.highlight.run
ARG REACT_APP_OTLP_ENDPOINT=http://localhost:4318
ARG REACT_APP_IN_DOCKER=true

ENV REACT_APP_AUTH_MODE=$REACT_APP_AUTH_MODE
ENV REACT_APP_FRONTEND_URI=$REACT_APP_FRONTEND_URI
ENV REACT_APP_PRIVATE_GRAPH_URI=$REACT_APP_PRIVATE_GRAPH_URI
ENV REACT_APP_PUBLIC_GRAPH_URI=$REACT_APP_PUBLIC_GRAPH_URI
ENV REACT_APP_OTLP_ENDPOINT=$REACT_APP_OTLP_ENDPOINT
ENV REACT_APP_IN_DOCKER=$REACT_APP_IN_DOCKER

# build:fast skips tsc for the frontend; workspace deps still build normally
# via turbo's ^build dependency. Type checking is enforced in CI via `build`.
RUN --mount=type=cache,target=/root/.turbo,sharing=locked \
    TURBO_CACHE_DIR=/root/.turbo npx turbo run build:fast --filter=@holdfast-io/frontend...

# ── Runtime image ─────────────────────────────────────────────────────────────
FROM nginx:stable-alpine AS frontend-prod

RUN apk update && apk add --no-cache python3

LABEL org.opencontainers.image.source=https://github.com/BrewingCoder/holdfast
LABEL org.opencontainers.image.description="HoldFast Frontend Image"
LABEL org.opencontainers.image.licenses="AGPL-3.0"

COPY infra/docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY src/backend/localhostssl/server.key /etc/ssl/private/ssl-cert.key
COPY src/backend/localhostssl/server.pem /etc/ssl/certs/ssl-cert.pem
COPY infra/docker/frontend-entrypoint.py /frontend-entrypoint.py

WORKDIR /build
COPY --from=frontend-build /app/src/frontend/build ./frontend/build

# Runtime env vars — replaced in constants.js by entrypoint.py at startup
ENV REACT_APP_AUTH_MODE=firebase
ENV REACT_APP_FRONTEND_URI=https://app.highlight.io
ENV REACT_APP_PRIVATE_GRAPH_URI=https://pri.highlight.io
ENV REACT_APP_PUBLIC_GRAPH_URI=https://pub.highlight.run
ENV REACT_APP_OTLP_ENDPOINT=http://localhost:4318
ENV SSL=false

CMD ["python3", "/frontend-entrypoint.py"]
