# HoldFast Roadmap

## Phase 1: Rebranding — Highlight.io → HoldFast

Complete the identity separation from upstream Highlight.io. The codebase still carries legacy namespaces in code-level identifiers that don't affect end users but will cause confusion as the project grows.

### 1.1 NPM Scope: `@highlight-run/*` → `@holdfast-io/*`

**Status:** DONE
**npm org:** `holdfast-io` (`https://www.npmjs.com/org/holdfast-io`)

All 17 workspace packages renamed from `@highlight-run/*` to `@holdfast-io/*`. 29 package.json files, 426+ source files, root scripts, and tsconfig references updated. `highlight.run` (core browser SDK) also renamed to `@holdfast-io/browser`. `@highlight-run/react-mentions` left as-is (external package).

NPM publish workflow is implemented and passing. Publishes in 4 dependency-ordered tiers. Dry-run mode supported. First publish to npm pending when ready to release publicly.

### 1.2 Go Module Path: `github.com/highlight-run/highlight` → `github.com/BrewingCoder/holdfast`

**Status:** DONE

All Go module paths have been renamed:
- Backend module: `github.com/highlight-run/highlight/backend` → `github.com/BrewingCoder/holdfast/src/backend`
- Go SDK module: `github.com/highlight/highlight/sdk/highlight-go` → `github.com/BrewingCoder/holdfast/sdk/highlight-go`
- `src/backend/go.mod`, `go.work`, and all import paths across every Go file updated.
- `go build ./...` passes.

### 1.3 Other SDK Package Registries

**Status:** Not started
**Priority:** Medium

| Ecosystem | Current Name | Registry | Action |
|-----------|-------------|----------|--------|
| Go | `github.com/BrewingCoder/holdfast/sdk/highlight-go` | pkg.go.dev | Module path updated — published at new path |
| Python | `highlight-io` | PyPI | Claim `holdfast` on PyPI, update `setup.py` |
| Ruby | `highlight_io` | RubyGems | Claim `holdfast`, update gemspec |
| Java | `io.highlight` | Maven Central | Update group ID (heavyweight process) |
| .NET | `Highlight.ASPCore` | NuGet | Claim new package name |
| PHP | `highlight/php-sdk` | Packagist | Update `composer.json` |
| Rust | `highlightio` | crates.io | Claim `holdfast` crate name |
| Elixir | `highlight` | Hex.pm | Check availability |

### 1.4 Container Registry

**Status:** Not started
**Priority:** Medium

Docker images are currently referenced as `ghcr.io/highlight/highlight-*`. Need to publish under HoldFast.

- Set up GitHub Container Registry under BrewingCoder (or future org)
- Update `infra/docker/compose.hobby.yml`, `infra/docker/compose.enterprise.yml`
- Update CI/CD workflows
- Update Dockerfiles with new labels

### 1.5 Domain & Contact Info

**Status:** Placeholders in place
**Priority:** Low (until project has public presence)

- Register `holdfast.dev` or chosen domain
- Set up `security@`, `support@` addresses
- Update SECURITY.md, CODE_OF_CONDUCT.md, SDK docs with real addresses

---

## Phase 2: Security Hardening

HoldFast handles sensitive telemetry — session replays, error traces, application logs, and performance data. For organizations in government, defense, healthcare, and finance, this data is often subject to strict compliance requirements. Security is not an afterthought; it's a core feature.

### 2.1 Encryption at Rest

**Status:** Not started
**Priority:** Critical

All stored data must be encrypted at rest:

- **PostgreSQL** — Enable Transparent Data Encryption (TDE) or use encrypted storage volumes. Document configuration for both managed (RDS, Cloud SQL) and self-managed Postgres.
- **ClickHouse** — Enable encrypted storage for analytics data. ClickHouse supports encrypted disks via `encrypted` disk type in `storage_policies`.
- **Redis** — Enable encryption at rest where supported (Redis Enterprise, or encrypted volumes for OSS Redis).
- **S3 / Object Storage** — Enforce SSE-S3 or SSE-KMS for session replay recordings and exported data. Support customer-managed keys (CMK).
- **Kafka** — Enable encryption at rest for message log segments via encrypted volumes or managed Kafka encryption (MSK, Confluent).

**Goal:** Zero unencrypted data at rest across all storage layers. Configurable via environment variables with secure defaults.

### 2.2 Field-Level Encryption

**Status:** Not started
**Priority:** High

Sensitive fields should be encrypted at the application layer before they reach the database, so that even database administrators cannot read them without the application key:

- **PII fields** — user emails, IP addresses, user identifiers, custom user attributes
- **Session metadata** — URLs visited, form inputs, network request/response bodies
- **Error payloads** — stack traces may contain environment variables, secrets, or internal paths
- **API keys and tokens** — workspace API keys, OAuth tokens, integration credentials

**Approach:**
- Implement envelope encryption with a configurable KMS provider (AWS KMS, GCP KMS, HashiCorp Vault, or local key file)
- AES-256-GCM for field encryption
- Key rotation support without re-encrypting all data
- Configurable per-workspace: operators choose which fields to encrypt
- Search over encrypted fields via blind index or deterministic encryption where needed

### 2.3 TLS 1.2+ Everywhere

**Status:** Not started
**Priority:** Critical

All network communication — external and internal — must use TLS 1.2 or higher. TLS 1.0 and 1.1 must be explicitly rejected.

- **Ingress** — Frontend, public GraphQL, private GraphQL, OTLP collector endpoints: enforce TLS 1.2+ with strong cipher suites
- **Inter-service** — Backend to PostgreSQL, ClickHouse, Redis, Kafka: enforce TLS for all internal connections
- **SDK to collector** — SDKs must default to HTTPS and validate certificates. Provide clear documentation for self-signed cert deployment.
- **Certificate management** — Document and support cert-manager (Kubernetes), Let's Encrypt (Docker), and manual certificate configuration
- **Cipher suite policy** — Disable weak ciphers (RC4, 3DES, export ciphers). Default to ECDHE key exchange with AES-GCM.

**Goal:** No plaintext connections between any HoldFast components or between clients and the platform. Configurable via env vars (`TLS_MIN_VERSION`, `TLS_CERT_PATH`, `TLS_KEY_PATH`, per-service connection strings).

### 2.4 Authentication: OIDC / SSO Integration

**Status:** Not started (Firebase auth is legacy dead code)
**Priority:** High

Replace the legacy Firebase authentication with a flexible, standards-based auth system:

- **OIDC (OpenID Connect)** — First-class support for bringing your own identity provider. Connect HoldFast to your organization's Okta, Azure AD, Google Workspace, Keycloak, or any OIDC-compliant IdP.
- **SAML 2.0** — Support for enterprise SSO via SAML where OIDC is not available.
- **Local password auth** — Retain as fallback for small deployments without an IdP. Enforce bcrypt/argon2 hashing, configurable password policy.
- **Single digital identity** — Users authenticate once via their organization's IdP. No separate HoldFast credentials to manage, rotate, or leak.

**Current state:** The backend has an `OAUTH_PROVIDER_URL` env var and basic OAuth2 flow support. Firebase code is present but non-functional for self-hosted. The hobby deployment uses password auth. This needs to be cleaned up and expanded into a proper multi-provider auth system.

### 2.5 Phishing-Resistant MFA

**Status:** Not started
**Priority:** High

Support modern, phishing-resistant multi-factor authentication:

- **WebAuthn / FIDO2** — Hardware security keys (YubiKey, etc.) and platform authenticators (Windows Hello, Touch ID, passkeys). This is the gold standard for phishing resistance.
- **TOTP** — Time-based one-time passwords (Google Authenticator, Authy) as a fallback for environments where hardware keys aren't feasible.
- **MFA enforcement** — Workspace administrators can require MFA for all users. Configurable policy: `optional`, `required`, `required-phishing-resistant`.
- **IdP-delegated MFA** — When using OIDC/SAML, MFA can be enforced at the IdP level rather than in HoldFast. HoldFast should respect and surface the `amr` (authentication methods reference) claim.

**Goal:** Operators can enforce that all access to their observability data requires phishing-resistant authentication, meeting NIST AAL2/AAL3 requirements.

### 2.6 Authorization & Access Control

**Status:** Partial (project-level access exists but needs hardening)
**Priority:** Medium

- **RBAC** — Role-based access control: admin, member, viewer roles with configurable permissions
- **Project-level isolation** — Users see only the projects they're assigned to (flag exists: `EnableProjectLevelAccess`)
- **API key scoping** — Ingestion keys vs. read keys vs. admin keys with distinct permissions
- **Audit logging** — Log all authentication events, permission changes, data access, and administrative actions to an immutable audit trail
- **Session timeout** — Configurable session duration and idle timeout

### 2.7 Network Security Defaults

**Status:** Not started
**Priority:** Medium

- **CORS hardening** — Configurable allowed origins, default to same-origin only
- **Rate limiting** — Configurable rate limits on all API endpoints to prevent abuse
- **Content Security Policy** — Strict CSP headers on the frontend
- **HSTS** — HTTP Strict Transport Security headers enabled by default
- **Container security** — Non-root container execution, read-only filesystems where possible, minimal base images

---

## Phase 3: Technical Debt & Cleanup

### 3.1 Fix Alpine Collector Shebang
Replace `[[ ]]` with POSIX `[ ]` in `infra/docker/configure-collector.sh`. Five-minute fix.

### 3.2 Run Codegen & Dependency Cleanup
- `cd src/backend && go mod tidy` — remove clearbit-go, stripe-go, unused AWS SDK modules
- `make private-gen` — regenerate backend GraphQL after schema changes
- `yarn install` — update lockfile after LD SDK removal
- `yarn codegen` — regenerate frontend GraphQL types
- `cd src/frontend && yarn types:check` — verify no TypeScript errors

### 3.3 Remove `enterprise/` Directory
Evaluate what's in `enterprise/` — if it's proprietary-licensed code from Highlight Inc., it should be removed from an AGPL-3.0 project. If it contains useful self-hosted features, consider relicensing or rewriting.

### 3.4 Remove `docs-content/` and `blog-content/`
These are Highlight.io's documentation and blog content. Already deleted. Confirm no orphan references remain.

### 3.5 Dependency Updates & Security Patches
Audit and update outdated dependencies. Prioritize known CVEs.

### 3.6 Remove Firebase Auth Dead Code
The Firebase authentication integration is non-functional for self-hosted. Clean out Firebase SDK references, config objects, and auth flows. Replace with the new OIDC/local auth system from Phase 2.

---

## Phase 4: AI Provider Modernization

### 4.1 Replace OpenAI with Anthropic Claude
The AI features ("Harold") currently use GPT-3.5-turbo exclusively. Replace with Claude or make provider-configurable.

**Key files:**
- `/src/backend/private-graph/graph/resolver.go` — session insights
- `/src/backend/private-graph/graph/schema.resolvers.go` — error suggestions
- `/src/backend/openai_client/` — OpenAI client wrapper
- `/src/backend/prompts/` — prompt templates
- `/packages/ai/` — AI insights Lambda

**Approach:**
- Add `ANTHROPIC_API_KEY` env var
- Create provider abstraction (OpenAI, Anthropic, or user-configurable)
- Upgrade from GPT-3.5 to modern models
- Add user-facing API key configuration in workspace settings (bring your own key)

### 4.2 Embeddings Provider
Currently uses OpenAI `text-embedding-3-small` + optional HuggingFace `gte-large`. Consider:
- Voyage AI (Anthropic partner)
- Keep HuggingFace (already self-hostable)
- Make configurable

---

## Phase 5: Deployment & Operations

### 5.1 Helm Charts
Create official Helm charts for Kubernetes deployment with security defaults baked in (TLS, network policies, pod security standards).

### 5.2 Simplify Container Count
Evaluate whether the 7+ service architecture can be consolidated for smaller deployments.

### 5.3 ARM64 Support
Ensure all containers build and run on ARM64 (important for self-hosted on Apple Silicon, Graviton, etc.).

### 5.4 Backup & Restore
Document and script backup/restore for PostgreSQL, ClickHouse, and Redis state. Include encrypted backup support.

### 5.5 Compliance Documentation
Provide deployment guides mapped to common compliance frameworks:
- **FedRAMP** — control mapping for federal deployments
- **HIPAA** — PHI handling guidance for healthcare
- **SOC 2** — control evidence for audit readiness
- **NIST 800-53** — security control alignment

These are not certifications (HoldFast is a tool, not a service) but guidance for operators deploying HoldFast within their compliance boundary.

---

## Phase 6: Module Documentation

Document every module in the codebase, starting from the deepest layer (storage, data models, core libraries) and working upward through the stack. Documentation serves two audiences: human contributors reading a wiki, and agentic AI contributors that need structured context to work effectively.

### Approach

Work bottom-up through the dependency graph:

1. **Storage layer** — ClickHouse schema, PostgreSQL models (GORM), Redis cache patterns, Kafka topics and message formats, S3/object storage
2. **Data access** — `store/` package, `clickhouse/` query layer, `model/` structs and migrations
3. **Core libraries** — `parser/`, `queryparser/`, `errorgroups/`, `stacktraces/`, `embeddings/`, `otel/` extraction
4. **GraphQL APIs** — `public-graph/` (ingestion) and `private-graph/` (dashboard) schemas, resolvers, middleware
5. **Workers** — Kafka consumer handlers, scheduled tasks, async processing pipeline
6. **Alert system** — `alerts/`, `alerts/v2/`, integration destinations (Slack, Discord, Teams, webhooks, issue trackers)
7. **Frontend** — React component tree, page routing, Apollo Client state, search/filter UI
8. **SDKs** — Per-SDK architecture, public API surface, configuration options, data flow to collector

### Documentation format

Each module gets a `MODULE.md` file in its directory containing:

- **Purpose** — what this module does, in one paragraph
- **Dependencies** — what it imports, what imports it
- **Key types/interfaces** — the public API surface with brief descriptions
- **Data flow** — how data enters and leaves this module
- **Configuration** — environment variables and config options
- **Gotchas** — non-obvious behavior, known issues, historical context
- **Testing** — how to test this module, what fixtures exist

These files serve as context anchors for both human readers and AI agents. An agent dropping into `store/MODULE.md` should have enough context to make changes without reading every file in the package.

### Status
**Not started.** This is a significant effort but pays compound interest — every module documented makes future contributions faster for both humans and agents.

---

## Phase 7: Test Coverage Investment

The inherited test suite is thin — 72 frontend tests and 98 backend tests for a 200K+ line codebase. Most backend tests require a full infrastructure stack (Postgres, ClickHouse, Redis, Kafka) to run. This is tech debt we can't see until something breaks in production.

Same bottom-up approach as module documentation — start at the foundation and work up. Document and test together: you can't write good tests for a module you don't understand, and you can't document a module properly without testing its edge cases.

### Approach

Follow the same dependency order as Phase 6. For each module:

1. **Audit** — what tests exist, what's covered, what's not
2. **Unit tests first** — pure logic that doesn't need infrastructure (parsers, validators, transformers, serializers)
3. **Integration tests second** — tests that need a database, using Docker Compose test fixtures
4. **Contract tests for APIs** — GraphQL schema compliance, SDK-to-backend contract validation
5. **Ratchet, don't mandate** — set coverage thresholds at current levels, only allow them to go up. No "achieve 80% by Friday" mandates.

### Priority order (highest risk, lowest coverage)

| Module | Current Coverage | Risk | Notes |
|--------|-----------------|------|-------|
| `parser/`, `queryparser/` | 87-100% | Low | Already well-tested. Maintain. |
| `public-graph/` resolvers | ~0% (needs infra) | **Critical** | Data ingestion path. Bugs here = data loss. |
| `private-graph/` resolvers | ~0% (needs infra) | **High** | Dashboard API. Bugs here = broken UI. |
| `worker/` handlers | ~0% (needs infra) | **High** | Async processing. Silent failures. |
| `clickhouse/` queries | ~0% (needs infra) | **High** | Analytics queries. Wrong results = misleading dashboards. |
| `store/` data access | ~0% (needs infra) | **High** | Core CRUD. Every feature depends on this. |
| `model/` | ~0% (needs infra) | Medium | GORM models. Migrations are the real risk. |
| `alerts/` | ~3% | Medium | Alert delivery. False negatives = missed incidents. |
| `errorgroups/` | ~14% | Medium | Error grouping logic. Bad groups = noise. |
| `stacktraces/` | ~0% (needs AWS) | Medium | Stack trace enhancement. Needs S3 mock. |
| Frontend components | ~5% | Medium | UI logic. Search, filters, graphing. |
| SDKs | ~10% | Medium | Client-facing. Bugs here = broken customer apps. |

### Infrastructure for testing

The biggest blocker is that most backend tests need Postgres + ClickHouse + Redis + Kafka. To make testing practical:

- [ ] Create a `docker-compose.test.yml` with lightweight test instances
- [ ] Add a `make test-with-infra` target that spins up containers, runs tests, tears down
- [ ] Set up test database seeding scripts
- [ ] Add ClickHouse test fixtures
- [ ] Mock S3 with MinIO for stack trace tests
- [ ] CI workflow that runs integration tests against Docker services

### Coverage tooling

- **Backend**: `go test -coverprofile` (already in Makefile)
- **Frontend**: Vitest with v8 coverage provider (configured)
- **CI**: Coverage artifacts uploaded, thresholds enforced via quality gates
- [ ] Add coverage badge to README (via Codecov, SonarQube, or custom shield)

### Status
**Not started.** Pairs naturally with Phase 6 (documentation) — do both per-module as you go.

---

## Action Items — Claim These Now

These are first-come-first-served and should be grabbed regardless of timeline:

- [x] **npmjs.com** — `holdfast-io` org created
- [x] **GitHub** — repo created at `BrewingCoder/holdfast`
- [x] **GitHub Actions** — CI (backend, frontend, SDK, security) + NPM publish workflows
- [x] **Self-hosted runner** — dedicated Ubuntu 24.04 VM
- [ ] **PyPI** — register `holdfast` package name
- [ ] **crates.io** — register `holdfast` crate name
- [ ] **Domain** — register `holdfast.dev` or preferred domain
