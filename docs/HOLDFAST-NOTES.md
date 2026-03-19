# HoldFast — Clean OSS Fork of Highlight.io

## Why HoldFast Exists

Highlight.io was acquired by LaunchDarkly and the standalone service was shut down on February 28, 2026. The open-source repo (Apache 2.0) remains, but active development has ceased. HoldFast is a clean, community-driven fork — all marketing, lead-gen, SaaS billing, and telemetry-home code has been stripped. What remains is pure observability.

**Based on Highlight.io at commit**: `6f3bd516c7f3527c71b250ff2376cda6db7c9c7d` (March 6, 2026)
**Original license at fork point**: Apache 2.0 (irrevocable, permits relicensing under compatible terms)
**HoldFast license**: AGPL-3.0 — all new code and modifications are AGPL-3.0. Anyone running a modified version of HoldFast as a network service must publish their source under the same license. The upstream Highlight.io code was Apache 2.0 at the time of fork, which permits this relicensing.

---

## What HoldFast Is

A full-stack, open-source observability platform:
- **Session Replay** — record and replay user sessions
- **Error Tracking** — capture, group, and analyze errors
- **Logging** — structured log ingestion and search
- **Distributed Tracing** — OTEL-native trace collection
- **Metrics** — custom metric dashboards

All in one UI, with OpenTelemetry-native ingestion. All features enabled — no tiers, no gates.

---

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Backend | Go |
| Frontend | React / TypeScript |
| Analytics DB | ClickHouse |
| Relational DB | PostgreSQL (pgvector) |
| Cache | Redis |
| Message Queue | Kafka + Zookeeper |
| Ingestion | OpenTelemetry Collector |
| Predictions | Python service |

---

## Self-Hosted Deployment

### Requirements
- Docker 25.0+ with compose plugin
- 8 GB RAM, 4 CPUs, 64 GB disk minimum
- Go 1.21+, Node.js 18+, Yarn 1+, Git 2.13+

### Quick Start
```bash
git clone --recurse-submodules <your-holdfast-repo-url>
cd holdfast/infra/docker
# Edit .env — set ADMIN_PASSWORD
./run-hobby.sh
# Access at http://localhost:3000
```

### Known Issue: Port Conflicts
Default compose binds PostgreSQL on 5432 and Redis on 6379. If you have existing services on those ports, edit `infra/docker/compose.yml` and remap:
- PostgreSQL: `0.0.0.0:5433:5432`
- Redis: `0.0.0.0:6380:6379`

Internal service discovery uses Docker Compose service names (not host ports), so remapping host ports does not break anything.

### Known Issue: Collector Build on Alpine
The `infra/docker/configure-collector.sh` script uses `[[ ]]` bash syntax but has a `#!/bin/sh` shebang. Alpine uses `ash`, which doesn't support `[[ ]]`. Fix by replacing all `[[ ]]` with POSIX `[ ]` syntax.

---

## What Was Stripped (from upstream Highlight.io)

All marketing, lead-gen, SaaS billing, and telemetry-home code has been removed. See `CHANGELOG-FORK.md` (in this directory) for full details.

| Component | What It Was | Status |
|-----------|-------------|--------|
| HubSpot | CRM tracking, contact sync | **Removed** |
| Apollo.io | Email enrichment, sales sequences | **Removed** |
| Clearbit | Company data enrichment | **Removed** |
| Phonehome | Usage telemetry to Highlight | **Removed** |
| Stripe | Subscription billing, pricing tiers | **Removed** (stubs remain) |
| AWS Marketplace | Usage metering for resale | **Removed** |
| LaunchDarkly | Feature flag SDK, migration gate | **Removed** (pass-through stubs) |
| Marketing site | Next.js site at `/highlight.io/` | **Removed** |
| Feature gates | Tier-locked features | **All defaulted to enabled** |
| Ingestion quotas | Billing-based data caps | **Removed** (`IsWithinQuota` always returns true) |

---

## Feature Gates — All Enabled

All `AllWorkspaceSettings` boolean flags now default to `true` in `/src/backend/model/model.go`:

- `AIInsights`, `AIQueryBuilder`
- `EnableBillingLimits`, `EnableGrafanaDashboard`, `EnableIngestSampling`
- `EnableJiraIntegration`, `EnableTeamsIntegration`
- `EnableProjectLevelAccess`, `EnableSessionExport`, `EnableSSO`
- `EnableUnlimitedDashboards`, `EnableUnlimitedProjects`, `EnableUnlimitedRetention`, `EnableUnlimitedSeats`
- `EnableLogTraceIngestion`

No tiers, no member limits, no billing issue banners.

---

## AI Features ("Harold")

### Current Capabilities
1. **Error Suggestions** — Sends stack trace to LLM, returns step-by-step debugging guidance
2. **Session Insights** — Analyzes session replay events, summarizes the 3 most active user activity chunks
3. **AI Query Builder** — Natural language to structured search query conversion

### Current AI Stack (OpenAI Only)
- Chat completions: `gpt-3.5-turbo` and `gpt-3.5-turbo-16k`
- Embeddings: `text-embedding-3-small` (OpenAI) + optional `gte-large` (HuggingFace)
- Config via env vars: `OPENAI_API_KEY`, `HUGGINGFACE_API_TOKEN`, `HUGGINGFACE_MODEL_URL`

### Anthropic/Claude Integration Opportunity
The AI backend is OpenAI-exclusive but cleanly isolated:

- **Backend AI code**: `/src/backend/private-graph/graph/resolver.go` (session insights), `/src/backend/private-graph/graph/schema.resolvers.go` (error suggestions)
- **Prompt templates**: `/src/backend/prompts/` directory
- **Frontend AI settings**: `HaroldAISettings.tsx`

**To add Claude support:**
1. Replace OpenAI chat completion calls with Anthropic SDK calls in the Go backend
2. Add `ANTHROPIC_API_KEY` env var alongside or replacing `OPENAI_API_KEY`
3. Allow user-configurable API keys in workspace settings (self-hosted = bring your own key)
4. For embeddings: Voyage AI (Anthropic partner) or keep HuggingFace
5. Prompts would work better with Claude — they're straightforward instruction prompts

---

## Roadmap

See **[ROADMAP.md](ROADMAP.md)** for the full roadmap including rebranding, technical debt, AI modernization, and deployment improvements.

### What's Done
- [x] Strip all marketing/lead-gen (HubSpot, Apollo.io, Clearbit, phonehome)
- [x] Remove SaaS billing (Stripe, AWS Marketplace metering)
- [x] Remove LaunchDarkly feature flags and migration gate
- [x] Remove marketing website (`highlight.io/`)
- [x] Flip all feature gates to enabled
- [x] Make all service domains env-var configurable (no hardcoded highlight.io)
- [x] Rebrand user-facing text to HoldFast
- [x] AGPL-3.0 license
- [x] Governance and contribution guidelines

---

## Last updated: 2026-03-18
