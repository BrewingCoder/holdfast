# HoldFast Roadmap

## Phase 1: Rebranding — Highlight.io → HoldFast

Complete the identity separation from upstream Highlight.io. The codebase still carries legacy namespaces in code-level identifiers that don't affect end users but will cause confusion as the project grows.

### 1.1 NPM Scope: `@highlight-run/*` → `@holdfast-io/*`

**Status:** DONE
**npm org:** `holdfast-io` (`https://www.npmjs.com/org/holdfast-io`)

All 17 workspace packages renamed from `@highlight-run/*` to `@holdfast-io/*`. 29 package.json files, 426+ source files, root scripts, and tsconfig references updated. `highlight.run` (core browser SDK) kept unchanged for now. `@highlight-run/react-mentions` left as-is (external package).

**Remaining:** Packages not yet published to npm. First publish needed when ready to release.

### 1.2 Go Module Path: `github.com/highlight-run/highlight` → new path

**Status:** Not started
**Priority:** High
**Prerequisite:** Create the HoldFast GitHub repo

Every Go file in `/backend` imports from `github.com/highlight-run/highlight/backend/...`. This is the Go module path and must match the actual repository URL for `go get` to work.

**Work involved:**
- Update `backend/go.mod` module declaration to match new repo URL
- Find-replace all import paths across every Go file (~hundreds of files, mechanical)
- Update `go.work` workspace file
- Verify `go build ./...` still passes

**Note:** Massive but simple find-replace. Best done as a single atomic commit.

### 1.3 Other SDK Package Registries

**Status:** Not started
**Priority:** Medium

| Ecosystem | Current Name | Registry | Action |
|-----------|-------------|----------|--------|
| Go | `github.com/highlight/highlight/sdk/highlight-go` | pkg.go.dev | Moves automatically with repo rename |
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
- Update `docker/compose.hobby.yml`, `docker/compose.enterprise.yml`
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
Replace `[[ ]]` with POSIX `[ ]` in `docker/configure-collector.sh`. Five-minute fix.

### 3.2 Run Codegen & Dependency Cleanup
- `cd backend && go mod tidy` — remove clearbit-go, stripe-go, unused AWS SDK modules
- `make private-gen` — regenerate backend GraphQL after schema changes
- `yarn install` — update lockfile after LD SDK removal
- `yarn codegen` — regenerate frontend GraphQL types
- `cd frontend && yarn types:check` — verify no TypeScript errors

### 3.3 Remove `enterprise/` Directory
Evaluate what's in `/enterprise/` — if it's proprietary-licensed code from Highlight Inc., it should be removed from an AGPL-3.0 project. If it contains useful self-hosted features, consider relicensing or rewriting.

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
- `/backend/private-graph/graph/resolver.go` — session insights
- `/backend/private-graph/graph/schema.resolvers.go` — error suggestions
- `/backend/openai_client/` — OpenAI client wrapper
- `/backend/prompts/` — prompt templates
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

## Action Items — Claim These Now

These are first-come-first-served and should be grabbed regardless of timeline:

- [x] **npmjs.com** — `holdfast-io` org created
- [ ] **GitHub** — create repo under BrewingCoder
- [ ] **PyPI** — register `holdfast` package name
- [ ] **crates.io** — register `holdfast` crate name
- [ ] **Domain** — register `holdfast.dev` or preferred domain
