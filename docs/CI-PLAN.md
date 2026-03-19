# HoldFast CI/CD Plan

## NPM Publish Order

Packages must be published bottom-up — leaf dependencies first, then packages that depend on them.

### Tier 1 — Leaf Packages (no internal deps)

| Package | Path | Notes |
|---------|------|-------|
| `highlight.run` | `sdk/highlight-run/` | Core browser SDK |
| `@holdfast-io/cloudflare` | `sdk/highlight-cloudflare/` | Cloudflare Workers SDK |
| `@holdfast-io/sourcemap-uploader` | `packages/sourcemap-uploader/` | CLI tool for uploading sourcemaps |

### Tier 2 — Depends on Tier 1

| Package | Path | Internal Deps |
|---------|------|---------------|
| `@holdfast-io/node` | `sdk/highlight-node/` | `highlight.run` |
| `@holdfast-io/react` | `sdk/highlight-react/` | `highlight.run` |

### Tier 3 — Depends on Tier 2

| Package | Path | Internal Deps |
|---------|------|---------------|
| `@holdfast-io/apollo` | `sdk/highlight-apollo/` | `@holdfast-io/node` |
| `@holdfast-io/hono` | `sdk/highlight-hono/` | `@holdfast-io/node` |
| `@holdfast-io/nest` | `sdk/highlight-nest/` | `@holdfast-io/node` |
| `@holdfast-io/pino` | `sdk/pino/` | `@holdfast-io/node` |
| `@holdfast-io/remix` | `sdk/highlight-remix/` | `@holdfast-io/node`, `@holdfast-io/react`, `highlight.run` |

### Tier 4 — Depends on Tier 3

| Package | Path | Internal Deps |
|---------|------|---------------|
| `@holdfast-io/next` | `sdk/highlight-next/` | `@holdfast-io/cloudflare`, `@holdfast-io/node`, `@holdfast-io/react`, `@holdfast-io/sourcemap-uploader`, `highlight.run` |
| `@holdfast-io/chrome` | `sdk/highlight-chrome/` | `@holdfast-io/react`, `@holdfast-io/ui` (private), `highlight.run` |

### Private Packages (not published to npm)

| Package | Path | Notes |
|---------|------|-------|
| `@holdfast-io/ui` | `packages/ui/` | Design system components |
| `@holdfast-io/ai` | `packages/ai/` | AI insights Lambda |
| `@holdfast-io/emails` | `packages/react-email-templates/` | Email templates |
| `@holdfast-io/component-preview` | `packages/component-preview/` | Storybook-style previewer |
| `@holdfast-io/grafana-datasource` | `sdk/highlightinc-highlight-datasource/` | Grafana plugin |
| `@holdfast-io/frontend` | `src/frontend/` | React dashboard app |
| `mock-otel-server` | `packages/mock-otel-server/` | Test utility |

---

## SonarQube Projects

| SQ Project Key | Scope | Path(s) |
|----------------|-------|---------|
| `holdfast-backend` | Go backend — GraphQL APIs, workers, storage, alerts | `/src/backend/` |
| `holdfast-frontend` | React dashboard application | `/src/frontend/` |
| `holdfast-sdk-core` | Core browser SDK (most complex JS package) | `sdk/highlight-run/` |
| `holdfast-sdk-node` | Node.js server SDK | `sdk/highlight-node/` |
| `holdfast-sdk-integrations` | Framework integration SDKs (thin wrappers) | `sdk/highlight-next/`, `sdk/highlight-react/`, `sdk/highlight-remix/`, `sdk/highlight-cloudflare/`, `sdk/highlight-apollo/`, `sdk/highlight-hono/`, `sdk/highlight-nest/`, `sdk/pino/` |
| `holdfast-infra` | Docker, deployment configs, collector | `/infra/docker/`, `/infra/deploy/` |

---

## GitHub Actions Workflows

All workflows run on a self-hosted runner (dedicated Ubuntu 24.04 VM).

### CI Workflows (run on PR + push to main)

| Workflow | File | Triggers | What It Does |
|----------|------|----------|-------------|
| Backend CI | `ci-backend.yml` | `src/backend/**`, `sdk/highlight-go/**` | Go format, golangci-lint, build, unit tests + coverage |
| Frontend CI | `ci-frontend.yml` | `src/frontend/**`, `packages/ui/**` | Yarn install, build deps, TS check, lint, Vitest + coverage |
| SDK CI | `ci-sdk.yml` | `sdk/**`, `packages/sourcemap-uploader/**` | Build all SDKs in tier order, run tests |
| Security | `security.yml` | All pushes + weekly schedule | CodeQL (Go + JS/TS), npm audit, govulncheck, TruffleHog |

### Release Workflows (manual dispatch)

| Workflow | File | Inputs | What It Does |
|----------|------|--------|-------------|
| NPM Publish | `publish-npm.yml` | tier (1-4/all), dry-run, version | Build all SDKs, publish in dependency order. Requires `NPM_TOKEN` secret. |

### Planned (not yet implemented)

| Workflow | Purpose |
|----------|---------|
| `sonarqube.yml` | SonarQube analysis for all 6 projects |
| `publish-docker.yml` | Build and push Docker images to GHCR |

### Quality Gates

- All PRs must pass: build, lint, tests
- SonarQube quality gate (pending setup)
- Coverage thresholds enforced (TBD — start with what exists, ratchet up)
- No new security vulnerabilities (CodeQL + `npm audit` + `go vuln`)

### Secrets Required

| Secret | Purpose |
|--------|---------|
| `NPM_TOKEN` | npm automation token for `@holdfast-io` org publishing |
| `SONAR_TOKEN` | SonarQube analysis token (pending) |
| `SONAR_HOST_URL` | SonarQube server URL (pending) |
