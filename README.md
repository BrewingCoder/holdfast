# HoldFast

[![License: AGPL-3.0](https://img.shields.io/badge/License-AGPL--3.0-blue.svg)](LICENSE)

### Backend
![Quality Gate](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-backend&metric=alert_status&token=sqb_22a4eb22b3984d19c31629b856e828b01f2255e8)
![Coverage](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-backend&metric=coverage&token=sqb_22a4eb22b3984d19c31629b856e828b01f2255e8)
![Bugs](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-backend&metric=bugs&token=sqb_22a4eb22b3984d19c31629b856e828b01f2255e8)
![Vulnerabilities](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-backend&metric=vulnerabilities&token=sqb_22a4eb22b3984d19c31629b856e828b01f2255e8)
![Lines of Code](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-backend&metric=ncloc&token=sqb_22a4eb22b3984d19c31629b856e828b01f2255e8)

### Frontend
![Quality Gate](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-frontend&metric=alert_status&token=sqb_0ead45158289886244c6621ff8829cdf10bf118e)
![Coverage](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-frontend&metric=coverage&token=sqb_0ead45158289886244c6621ff8829cdf10bf118e)
![Bugs](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-frontend&metric=bugs&token=sqb_0ead45158289886244c6621ff8829cdf10bf118e)
![Vulnerabilities](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-frontend&metric=vulnerabilities&token=sqb_0ead45158289886244c6621ff8829cdf10bf118e)
![Lines of Code](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-frontend&metric=ncloc&token=sqb_0ead45158289886244c6621ff8829cdf10bf118e)

### Browser SDK
![Quality Gate](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-browser&metric=alert_status&token=sqb_1989da71d95593c6654cb88ec596ada796d9821c)
![Coverage](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-browser&metric=coverage&token=sqb_1989da71d95593c6654cb88ec596ada796d9821c)
![Bugs](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-browser&metric=bugs&token=sqb_1989da71d95593c6654cb88ec596ada796d9821c)
![Vulnerabilities](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-browser&metric=vulnerabilities&token=sqb_1989da71d95593c6654cb88ec596ada796d9821c)

### Node SDK
![Quality Gate](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-node&metric=alert_status&token=sqb_f550bba4def8eed38f0a0b6b5c6db4392bf0386e)
![Bugs](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-node&metric=bugs&token=sqb_f550bba4def8eed38f0a0b6b5c6db4392bf0386e)
![Vulnerabilities](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-node&metric=vulnerabilities&token=sqb_f550bba4def8eed38f0a0b6b5c6db4392bf0386e)

### SDK Integrations
![Quality Gate](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-sdk-integrations&metric=alert_status&token=sqb_962fda2b9f5419934283429d8691b498a9973272)
![Bugs](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-sdk-integrations&metric=bugs&token=sqb_962fda2b9f5419934283429d8691b498a9973272)
![Vulnerabilities](https://sonar.brewingcoder.com/api/project_badges/measure?project=holdfast-sdk-integrations&metric=vulnerabilities&token=sqb_962fda2b9f5419934283429d8691b498a9973272)

---

**Full-stack observability that stays on your network.**

HoldFast is an open-source, self-hosted observability platform — session replay, error monitoring, logging, and distributed tracing in a single UI. It runs entirely on your infrastructure. Your data never leaves your network.

---

## Why HoldFast Exists

Highlight.io was one of the best open-source observability platforms available. Then it was acquired by LaunchDarkly and shut down on February 28, 2026. The SaaS went dark, and the codebase went dormant.

We didn't want to lose it.

HoldFast is a clean fork of Highlight.io, taken at the final commit before shutdown. We stripped out the SaaS billing, the marketing integrations, the telemetry-home code, and the feature gates. What's left is the pure observability platform — every feature enabled, no tiers, no paywalls, no phone-home.

**This is not a startup. There is no SaaS model. There is no plan to monetize you.** HoldFast is a community-maintained project built to preserve something valuable and keep it available for teams that need it.

## Why Self-Hosted Matters

Not everyone can push telemetry to the cloud. For many organizations — especially in government, defense, healthcare, and finance — sending session replays, error traces, and application logs to a third-party service is a non-starter. That data can contain secrets, PII, classified artifacts, and internal architecture details that are required to stay within the organization's boundary.

The SaaS observability model doesn't work for these teams. The self-hosted model does.

HoldFast is built for operators who need full-stack observability but can't — or won't — trust a vendor with their telemetry. Everything runs on your hardware, behind your firewall, under your control. No data leaves your network. No vendor has access. No subscription to cancel.

## What It Does

- **Session Replay** — DOM-based high-fidelity replay of browser sessions, powered by a fork of [rrweb](https://github.com/rrweb-io/rrweb)
- **Error Monitoring** — Capture, group, and analyze errors with customizable alerting. AI-assisted error suggestions. GitHub-enhanced stack traces.
- **Logging** — Structured log ingestion and search with powerful filtering. OpenTelemetry-native.
- **Distributed Tracing** — Track operations across your stack with trace visualization and flame graphs.
- **Metrics & Dashboards** — Custom dashboards with flexible graphing, drilldown, and SQL editor support.
- **Alerts** — Route alerts to Slack, Discord, Microsoft Teams, email, webhooks, and issue trackers (Jira, Linear, GitHub, GitLab, ClickUp, Height).

All features are enabled by default. No tiers. No feature gates. No billing.

## Quick Start

Deploy a hobby instance on Linux with Docker. The default ClickHouse-backed stack runs comfortably on **2 CPUs / 2 GiB RAM**; the Postgres-only stack runs on **1 CPU / 1 GiB RAM**.

```bash
git clone https://github.com/BrewingCoder/holdfast
cd holdfast/infra/docker
# Edit .env — set ADMIN_PASSWORD
./run-hobby.sh
```

The app is accessible at `http://localhost:8082`. Log in with any email address and the password you set in `.env`.

All service endpoints are configurable via environment variables — deploy to any domain, IP, or localhost. See `infra/docker/.env` and `docs/HOLDFAST-NOTES.md` for configuration details.

### Choose your analytics backend

A single environment variable picks where session/log/trace/metric/error data lands:

```bash
COMPOSE_PROFILES=clickhouse  STORAGE_ANALYTICS=ClickHouse  # default — recommended for >100k events/day
COMPOSE_PROFILES=postgres    STORAGE_ANALYTICS=Postgres    # PG-only — drops the ClickHouse container entirely
```

In Postgres-only mode the deployment is **two containers** (backend + Postgres). At hobby scale the PG backend handles the same workload with a fraction of the resource footprint and zero columnar-database operations cost.

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Backend | **.NET 10** with HotChocolate GraphQL |
| Frontend | React / TypeScript (Vite, Turborepo, Yarn Berry) |
| Analytics DB | ClickHouse 24.3 *(optional — opt-in via compose profile)* |
| Relational DB | TimescaleDB-HA (Postgres 16 + Timescale + pgvector) |
| Message bus | In-process `Channel<T>` *(Kafka removed)* |
| Ingestion | OTLP/HTTP receivers hosted by the backend *(OTel collector removed)* |
| Frontend serving | Same backend image — no separate nginx container |

## SDKs

Client SDKs live in `sdk/` and are published under the `@holdfast-io` npm scope. Available for:

**Browser & Frontend:** JavaScript, React, Next.js, Remix, Angular, Vue, Svelte, React Native

**Backend:** Node.js, Go, Python, Ruby, Java, .NET, PHP, Rust, Elixir

**Infrastructure:** Cloudflare Workers, Hono, NestJS, Apollo Server, Pino logger

All SDKs support configurable endpoints — point them at your HoldFast instance.

## Development

See [CONTRIBUTING.md](CONTRIBUTING.md) for full setup instructions.

```bash
# Prerequisites: .NET 10 SDK, Node.js 22+, Docker

# Bring up the data plane only (Postgres + optional ClickHouse)
cd infra/docker && docker compose up -d postgres clickhouse

# Run the backend on the host with hot reload
cd src/dotnet && dotnet watch --project src/HoldFast.Api

# Run the frontend on the host with hot reload
cd src/frontend && yarn dev
```

The legacy Go backend tree under `src/backend/` is preserved for reference (the fork-source schema files still drive frontend codegen) but is no longer the runtime — the .NET solution under `src/dotnet/` is what ships.

## Why the Architecture Changed

Upstream Highlight.io was a SaaS-shaped product. The infrastructure assumptions that come with that — fleets of replicated services, dedicated message brokers, per-domain microservices — make sense when you're running a multi-tenant cloud. They make almost no sense when a single team is running a single instance of an observability platform on their own hardware.

So we rebuilt the parts that hurt the most.

### Smaller footprint by design

The original hobby stack was **9 containers** consuming **~12 GiB of RAM at warm idle** with no traffic. Most of that was infrastructure overhead for services we didn't actually need at single-tenant scale.

The current ClickHouse-backed hobby stack is **3 containers** at **~700 MiB warm idle** (backend + Postgres + ClickHouse). The Postgres-only mode cuts that to **2 containers at ~400 MiB warm idle**.

| | Upstream Highlight | HoldFast (CH mode) | HoldFast (PG mode) |
|---|---|---|---|
| Containers | 9 | 3 | **2** |
| Warm idle RAM | ~12 GiB | ~700 MiB | **~400 MiB** |
| Hobby host minimum | 8 GiB / 4 CPU | 2 GiB / 2 CPU | **1 GiB / 1 CPU** |

What got dropped: the standalone OTel collector (the backend hosts OTLP receivers directly), the predictions service (Python ML container that nobody self-hosting actually used), Redis (the in-memory cache layer was load-bearing for a cloud at scale; at hobby scale a `MemoryCache` is faster and free), Zookeeper, Kafka, and the dedicated nginx-frontend container (the backend's Kestrel serves the SPA bundle from `wwwroot`). Each removal is documented in HOL-18 through HOL-23 on the issue tracker.

### Why we ditched Go

The Go backend was the worst offender for resource pressure in self-hosted deployments. It wasn't a Go problem in the abstract — it was a *running this Go service on a constrained box* problem:

- **Memory growth that never plateaued.** The Go runtime's GC strategy keeps a generous heap reservation proportional to peak allocation. Ingest bursts (which observability workloads constantly do — error storms, deploy spikes, replay-heavy sessions) walked the heap up and the GC never gave it back. Pods got OOM-killed under cgroup limits that wouldn't have bothered a tighter runtime. Operators learned to over-provision RAM by 3-4x just to keep restarts at bay.
- **Goroutine deadlocks under contention.** The original kitchen-sink `IClickHouseService` interface fanned out work across many goroutines that shared mutexes for connection pooling, Kafka producer batching, and resolver state. Under load — particularly during retention sweeps or the autoresolve worker firing — we observed lock contention degenerating into outright deadlocks that wedged the whole pod until a restart. Reproducing them was painful; fixing them more so.
- **Cgroup behavior.** The Go runtime predates modern container-aware tuning. `GOGC` and `GOMEMLIMIT` help, but the defaults guess wrong about cgroup memory limits often enough that operators needed bespoke tuning per deployment. Self-hosted users don't want to be Go runtime experts.
- **Deployment surface.** A single Go binary is famously easy to ship — but that binary still had `cgo` dependencies for some integrations, and the Helm/compose configurations still needed to wire all the supporting services. Simplicity at the binary level didn't translate into operational simplicity.

The .NET 10 rewrite (issue-55) replaces every public/private GraphQL resolver, every worker, every ingest path. We kept the same external API contracts — same OTLP endpoints, same GraphQL shape, same SDK protocol — so client SDKs and the React frontend didn't need to change. The migration shipped with **3,153 unit tests** and a continuous-soak harness validating end-to-end ingest paths. Memory profile is dramatically tighter: backend pods run comfortably with **128 MiB requests / 256 MiB limits** in Kubernetes; the host process behaves predictably under cgroup pressure; and the analytics writer dispatches by metric kind rather than fighting a single shared lock.

### Kafka → in-process bus

For a single-tenant deployment, Kafka was overkill. It existed because Highlight ran a multi-tenant cloud where Kafka was the seam between ingest and worker pods. HoldFast doesn't have that seam — ingest and workers run in the same process. Kafka was replaced (HOL-23) by a `Channel<T>`-backed in-process message bus that preserves the same `produce → consume` contract for the worker code and JSON-round-trips the same message types. That alone dropped two containers (Kafka + Zookeeper) and several hundred MiB of resident memory.

Multi-node deployments that *do* need a real broker can still plug one in — the bus is behind an interface — but the default deployment is now a single process talking to a single database.

## What Was Removed

HoldFast is lighter than upstream Highlight.io. We stripped everything that served the SaaS business but had no value for self-hosted users:

- **Stripe / AWS Marketplace** — Subscription billing and usage metering
- **HubSpot / Apollo.io / Clearbit** — CRM tracking, lead enrichment, sales automation
- **LaunchDarkly** — Feature flag SDK and migration gates
- **Phonehome** — Usage telemetry reporting to Highlight's servers
- **Marketing website** — The `highlight.io` Next.js site
- **Feature gates** — All boolean flags defaulted to enabled
- **Calendly / "Book a call" CTAs** — SaaS sales flow
- **Discord help/community links** — HoldFast has no Discord
- **Harold AI** — Highlight's paid-tier AI assistant; UI surfaces removed pending a clean self-hosted AI integration

See [CHANGELOG-FORK.md](docs/CHANGELOG-FORK.md) for the detailed record of every change.

## Security Posture

Observability data is sensitive. Session replays capture user behavior. Error traces can contain environment variables and secrets. Logs may include PII, internal URLs, and infrastructure details. For many organizations, this data is subject to strict compliance requirements — FedRAMP, HIPAA, SOC 2, NIST 800-53.

HoldFast takes this seriously. Our security roadmap (see [ROADMAP.md](docs/ROADMAP.md) Phase 2) includes:

- **Encryption at rest** — All stored data (PostgreSQL, ClickHouse, object storage) encrypted using configurable key management (AWS KMS, GCP KMS, HashiCorp Vault, or local keys)
- **Field-level encryption** — Application-layer encryption for PII, credentials, and sensitive telemetry fields. Even database admins can't read them without the application key.
- **TLS 1.2+ everywhere** — All connections, external and inter-service, require TLS 1.2 or higher. No plaintext. Strong cipher suites only.
- **OIDC / SSO** — Bring your own identity provider. Connect to Okta, Azure AD, Google Workspace, Keycloak, or any OIDC-compliant IdP. One digital identity, no separate credentials.
- **Phishing-resistant MFA** — WebAuthn/FIDO2 hardware keys and passkeys. Configurable enforcement policies meeting NIST AAL2/AAL3.
- **RBAC & audit logging** — Role-based access, project-level isolation, scoped API keys, and an immutable audit trail of all access and administrative actions.

**The goal:** an observability platform that security teams and compliance officers can approve without caveats.

## Roadmap

See [ROADMAP.md](docs/ROADMAP.md) for the full plan. Highlights:

- **Done:** SaaS/marketing strip, feature gate unlock, domain configurability, `@holdfast-io` npm scope, browser SDK rename, AGPL-3.0 licensing, **.NET 10 backend rewrite (issue-55)**, **LEAN stack (9 → 3 containers, ~12 GiB → ~700 MiB warm idle)**, **Kafka → in-process bus**, **Postgres-only analytics mode (CH optional)**
- **Next:** Security hardening (encryption at rest, TLS enforcement, OIDC auth, MFA), 24h soak validation, frontend Highlight-branding cleanup pass
- **Future:** Helm charts, compliance documentation, ARM64 support, native AI integrations (Claude/Anthropic) for self-hosted operators

## Governance

HoldFast is community-driven and maintained on a best-effort basis. Contributions from both humans and AI agents are welcome — the repository is seeded with documentation to support both. See [GOVERNANCE.md](docs/GOVERNANCE.md) for details.

If the project gains sufficient traction, a HoldFast Contributor Board will be formed to guide roadmap decisions. The license will remain AGPL-3.0 permanently.

## License

**AGPL-3.0** — see [LICENSE](LICENSE) for details.

The upstream Highlight.io code was Apache 2.0 at the time of fork, which permits relicensing. All new code and modifications are AGPL-3.0. Anyone running a modified version of HoldFast as a network service must publish their source under the same license.

This is intentional. We believe observability tooling should be genuinely open — not "open source until the acquisition."

## Attribution

HoldFast builds on the work of the original Highlight.io team and contributors. The platform they built was excellent. We're keeping it alive.
