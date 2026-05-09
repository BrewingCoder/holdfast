# HOL-38: soak harness container.
#
# Single-stage Alpine + Node — the entire harness is JS/Node, no compile step.
# Layered so npm install caches when only index.mjs / scenarios change.

FROM node:lts-alpine

WORKDIR /app

# Install deps in a separate layer for cache reuse
COPY package.json ./
RUN npm install --omit=dev

# Source last so changes don't bust the deps layer
COPY index.mjs ./
COPY scenarios ./scenarios/
# scheduler.mjs lands in HOL-40; ignore-if-missing via a glob trick
COPY scheduler*.mjs ./

# Defaults; override via compose env
ENV SOAK_BASE_INTERVAL_MS=60000 \
    HOLDFAST_PROJECT_ID=2 \
    OTLP_ENDPOINT=http://backend:8082/otel \
    SOAK_SERVICE_NAME=holdfast-soak \
    SOAK_SERVICE_VERSION=0.0.0-soak

# No EXPOSE — soak only makes outbound calls.
# No HEALTHCHECK — `docker logs holdfast-soak` is the operator-facing signal.

ENTRYPOINT ["node", "index.mjs"]
