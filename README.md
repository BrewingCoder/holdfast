# HoldFast

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

Deploy a hobby instance on Linux with Docker (minimum: 8GB RAM, 4 CPUs, 64 GB disk):

```bash
git clone --recurse-submodules https://github.com/BrewingCoder/holdfast
cd holdfast/infra/docker
# Edit .env — set ADMIN_PASSWORD
./run-hobby.sh
```

The app is accessible at `https://localhost`. Log in with any email address and the password you set in `.env`.

All service endpoints are configurable via environment variables — deploy to any domain, IP, or localhost. See `infra/docker/.env` and `docs/HOLDFAST-NOTES.md` for configuration details.

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Backend | Go |
| Frontend | React / TypeScript |
| Analytics DB | ClickHouse |
| Relational DB | PostgreSQL |
| Cache | Redis |
| Message Queue | Kafka |
| Ingestion | OpenTelemetry Collector |
| Build | Vite, Turborepo, Yarn Berry |

## SDKs

Client SDKs live in `sdk/` and are published under the `@holdfast-io` npm scope. Available for:

**Browser & Frontend:** JavaScript, React, Next.js, Remix, Angular, Vue, Svelte, React Native

**Backend:** Node.js, Go, Python, Ruby, Java, .NET, PHP, Rust, Elixir

**Infrastructure:** Cloudflare Workers, Hono, NestJS, Apollo Server, Pino logger

All SDKs support configurable endpoints — point them at your HoldFast instance.

## Development

See [CONTRIBUTING.md](CONTRIBUTING.md) for full setup instructions.

```bash
# Prerequisites: Go 1.23+, Node.js 18+, Docker
cd infra/docker && docker-compose up       # Start infrastructure
cd src/backend && make migrate && make start  # Start backend
cd src/frontend && yarn dev                   # Start frontend
```

## What Was Removed

HoldFast is lighter than upstream Highlight.io. We stripped everything that served the SaaS business but had no value for self-hosted users:

- **Stripe / AWS Marketplace** — Subscription billing and usage metering
- **HubSpot / Apollo.io / Clearbit** — CRM tracking, lead enrichment, sales automation
- **LaunchDarkly** — Feature flag SDK and migration gates
- **Phonehome** — Usage telemetry reporting to Highlight's servers
- **Marketing website** — The `highlight.io` Next.js site
- **Feature gates** — All boolean flags defaulted to enabled

See [CHANGELOG-FORK.md](docs/CHANGELOG-FORK.md) for the detailed record of every change.

## Security Posture

Observability data is sensitive. Session replays capture user behavior. Error traces can contain environment variables and secrets. Logs may include PII, internal URLs, and infrastructure details. For many organizations, this data is subject to strict compliance requirements — FedRAMP, HIPAA, SOC 2, NIST 800-53.

HoldFast takes this seriously. Our security roadmap (see [ROADMAP.md](docs/ROADMAP.md) Phase 2) includes:

- **Encryption at rest** — All stored data (PostgreSQL, ClickHouse, Redis, S3, Kafka) encrypted using configurable key management (AWS KMS, GCP KMS, HashiCorp Vault, or local keys)
- **Field-level encryption** — Application-layer encryption for PII, credentials, and sensitive telemetry fields. Even database admins can't read them without the application key.
- **TLS 1.2+ everywhere** — All connections, external and inter-service, require TLS 1.2 or higher. No plaintext. Strong cipher suites only.
- **OIDC / SSO** — Bring your own identity provider. Connect to Okta, Azure AD, Google Workspace, Keycloak, or any OIDC-compliant IdP. One digital identity, no separate credentials.
- **Phishing-resistant MFA** — WebAuthn/FIDO2 hardware keys and passkeys. Configurable enforcement policies meeting NIST AAL2/AAL3.
- **RBAC & audit logging** — Role-based access, project-level isolation, scoped API keys, and an immutable audit trail of all access and administrative actions.

**The goal:** an observability platform that security teams and compliance officers can approve without caveats.

## Roadmap

See [ROADMAP.md](docs/ROADMAP.md) for the full plan. Highlights:

- **Done:** SaaS/marketing strip, feature gate unlock, domain configurability, `@holdfast-io` npm scope, AGPL-3.0 licensing
- **Next:** Security hardening (encryption at rest, TLS enforcement, OIDC auth, MFA), Go module rename, dependency updates
- **Future:** AI provider modernization (Claude/Anthropic), Helm charts, compliance documentation, ARM64 support

## Governance

HoldFast is community-driven and maintained on a best-effort basis. Contributions from both humans and AI agents are welcome — the repository is seeded with documentation to support both. See [GOVERNANCE.md](docs/GOVERNANCE.md) for details.

If the project gains sufficient traction, a HoldFast Contributor Board will be formed to guide roadmap decisions. The license will remain AGPL-3.0 permanently.

## License

**AGPL-3.0** — see [LICENSE](LICENSE) for details.

The upstream Highlight.io code was Apache 2.0 at the time of fork, which permits relicensing. All new code and modifications are AGPL-3.0. Anyone running a modified version of HoldFast as a network service must publish their source under the same license.

This is intentional. We believe observability tooling should be genuinely open — not "open source until the acquisition."

## Attribution

HoldFast builds on the work of the original Highlight.io team and contributors. The platform they built was excellent. We're keeping it alive.
