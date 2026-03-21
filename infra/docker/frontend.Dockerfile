FROM --platform=$BUILDPLATFORM node:lts-alpine AS frontend-build

RUN apk update && apk add --no-cache build-base python3

WORKDIR /highlight

# Copy workspace config files
COPY .yarnrc.yml package.json yarn.lock turbo.json tsconfig.json graphql.config.js ./
COPY .yarn/patches ./.yarn/patches
COPY .yarn/releases ./.yarn/releases

# Copy package.json files for all workspace members that exist.
# tests/e2e/* is a workspace glob — copy the whole dir to satisfy it.
COPY tests/e2e ./tests/e2e
COPY src/frontend/package.json ./src/frontend/package.json
COPY packages/render/package.json ./packages/render/package.json
COPY packages/sourcemap-uploader/package.json ./packages/sourcemap-uploader/package.json
COPY tools/scripts/package.json ./tools/scripts/package.json
COPY sdk/highlight-apollo/package.json ./sdk/highlight-apollo/package.json
COPY sdk/highlight-chrome/package.json ./sdk/highlight-chrome/package.json
COPY sdk/highlight-cloudflare/package.json ./sdk/highlight-cloudflare/package.json
COPY sdk/highlight-hono/package.json ./sdk/highlight-hono/package.json
COPY sdk/highlight-nest/package.json ./sdk/highlight-nest/package.json
COPY sdk/highlight-next/package.json ./sdk/highlight-next/package.json
COPY sdk/highlight-node/package.json ./sdk/highlight-node/package.json
COPY sdk/highlight-react/package.json ./sdk/highlight-react/package.json
COPY sdk/highlight-remix/package.json ./sdk/highlight-remix/package.json
COPY sdk/highlight-run/package.json ./sdk/highlight-run/package.json
COPY sdk/highlightinc-highlight-datasource/package.json ./sdk/highlightinc-highlight-datasource/package.json
COPY sdk/pino/package.json ./sdk/pino/package.json

RUN yarn install --immutable

# Copy source files (tests/e2e already copied above for yarn install)
COPY src/backend/localhostssl ./src/backend/localhostssl
COPY src/backend/private-graph ./src/backend/private-graph
COPY src/backend/public-graph ./src/backend/public-graph
COPY src/frontend ./src/frontend
COPY packages ./packages
COPY rrweb ./rrweb
COPY tools/scripts ./tools/scripts
COPY sdk ./sdk

# Bake in the same placeholder URLs the entrypoint knows to replace at runtime.
# REACT_APP_AUTH_MODE=firebase matches the entrypoint's replacement target.
# All other URLs match upstream defaults the entrypoint expects to find.
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

RUN yarn build:frontend

# ── Runtime image ──────────────────────────────────────────────────────────
FROM nginx:stable-alpine AS frontend-prod

RUN apk update && apk add --no-cache python3

LABEL org.opencontainers.image.source=https://github.com/holdfast-io/holdfast
LABEL org.opencontainers.image.description="HoldFast Frontend Image"
LABEL org.opencontainers.image.licenses="AGPL-3.0"

COPY infra/docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY src/backend/localhostssl/server.key /etc/ssl/private/ssl-cert.key
COPY src/backend/localhostssl/server.pem /etc/ssl/certs/ssl-cert.pem
COPY infra/docker/frontend-entrypoint.py /frontend-entrypoint.py

WORKDIR /build
COPY --from=frontend-build /highlight/src/frontend/build ./frontend/build

# Runtime env vars — replaced in constants.js by entrypoint.py at startup
ENV REACT_APP_AUTH_MODE=firebase
ENV REACT_APP_FRONTEND_URI=https://app.highlight.io
ENV REACT_APP_PRIVATE_GRAPH_URI=https://pri.highlight.io
ENV REACT_APP_PUBLIC_GRAPH_URI=https://pub.highlight.run
ENV REACT_APP_OTLP_ENDPOINT=http://localhost:4318
ENV SSL=false

CMD ["python3", "/frontend-entrypoint.py"]
