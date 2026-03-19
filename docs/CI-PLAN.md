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

## GitHub Actions Workflows (Planned)

### CI Workflows (run on PR + push to main)

| Workflow | Purpose | Runner |
|----------|---------|--------|
| `backend.yml` | Go build, lint (golangci-lint), format check, unit tests, coverage | `ubuntu-latest` |
| `frontend.yml` | Yarn install, TypeScript check, Vitest, coverage | `ubuntu-latest` |
| `sdk.yml` | Build all SDKs, run SDK tests, coverage | `ubuntu-latest` |
| `sonarqube.yml` | SonarQube analysis for all 6 projects | `ubuntu-latest` |
| `security.yml` | CodeQL + dependency audit + secret scanning | `ubuntu-latest` |

### Release Workflows (manual or on tag)

| Workflow | Purpose | Runner |
|----------|---------|--------|
| `publish-npm.yml` | Publish SDKs to npm in tier order | `ubuntu-latest` |
| `publish-docker.yml` | Build and push Docker images to GHCR | `ubuntu-latest` |

### Quality Gates

- All PRs must pass: build, lint, tests, SonarQube quality gate
- Coverage thresholds enforced (TBD — start with what exists, ratchet up)
- No new security vulnerabilities (CodeQL + `npm audit` + `go vuln`)
