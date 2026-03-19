# BrewingCoder Fork — Changelog

## 2026-03-18: Strip Marketing, Lead-Gen, and SaaS Billing

Removed all components that served Highlight's SaaS business but have no value for self-hosted deployments. **1,056 files changed — ~82,800 lines deleted.**

---

### Removed: HubSpot CRM Integration

**What it was:** CRM tracking — synced workspace/admin data to HubSpot, tracked session views, merged duplicate contacts ("doppelgänger" detection).

**Deleted:**
- `/src/backend/hubspot/` — `hubspot.go`, `merge.go`

**Modified:**
- `/src/backend/env/environment.go` — removed `HubspotApiKey`, `HubspotCookieString`, `HubspotCsrfToken`, `HubspotOAuthToken` from `Configuration`
- `/src/backend/model/model.go` — removed `HubspotCompanyID` from `Workspace`, `HubspotContactID` from `Admin`
- `/src/backend/redis/utils.go` — removed `CacheKeyHubspotCompanies`, `SetHubspotCompanies`, `GetHubspotCompanies`
- `/src/backend/kafka-queue/types.go` — replaced 5 deprecated HubSpot `PayloadType` constants with blank `_` identifiers (preserves iota numbering)
- `/src/backend/private-graph/graph/schema.resolvers.go` — cleaned "hubspot" from log messages
- `/src/backend/worker/worker.go` — cleaned "hubspot" from log messages
- `/src/backend/public-graph/graph/resolver.go` — removed HubSpot mention from comment
- `/src/frontend/src/util/analytics.ts` — removed `window._hsq` tracking
- `/src/frontend/src/@types/window.d.ts` — removed `_hsq` type

**Env vars removed:** `HUBSPOT_API_KEY`, `HUBSPOT_COOKIE_STRING`, `HUBSPOT_CSRF_TOKEN`, `HUBSPOT_OAUTH_TOKEN`

---

### Removed: Apollo.io Lead Enrichment

**What it was:** Contact enrichment on admin signup — looked up email via Apollo.io API, created contacts in Apollo, added them to sales email sequences.

**Deleted:**
- `/src/backend/apolloio/` — `apolloio.go`

**Modified:**
- `/src/backend/env/environment.go` — removed `ApolloIoAPIKey`, `ApolloIoSenderID`
- `/src/backend/private-graph/graph/schema.resolvers.go` — removed `apolloio.Enrich()`, `apolloio.CreateContact()`, `apolloio.AddToSequence()` from `EmailSignup` resolver; resolver now just saves the email
- `/src/backend/model/model.go` — removed `ApolloData`, `ApolloDataShortened` from `EmailSignup` struct

**Env vars removed:** `APOLLO_IO_API_KEY`, `APOLLO_IO_SENDER_ID`

---

### Removed: Clearbit Company Enrichment

**What it was:** Company data enrichment from email/IP — used for lead scoring. Gated behind Premium/Startup/Enterprise tiers.

**Deleted:**
- `/src/frontend/src/pages/IntegrationsPage/components/ClearbitIntegration/` — 3 files
- `/src/frontend/src/clearbit.js` — tracking script
- `/src/frontend/src/static/integrations/clearbit.svg` — logo

**Modified:**
- `/src/backend/pricing/pricing.go` — removed `MustUpgradeForClearbit()`
- `/src/backend/env/environment.go` — removed `ClearbitApiKey`
- `/src/backend/main.go` — removed clearbit client initialization
- `/src/backend/private-graph/graph/resolver.go` — removed `ClearbitClient` field and import
- `/src/backend/private-graph/graph/schema.resolvers.go` — stubbed `ModifyClearbitIntegration` (returns true), stubbed `EnhancedUserDetails` (returns nil)
- `/src/frontend/index.html` — removed clearbit script tag
- `/src/frontend/src/pages/IntegrationsPage/Integrations.tsx` — removed Clearbit from integrations list
- `/src/frontend/src/pages/IntegrationsPage/IntegrationsPage.tsx` — removed Clearbit hooks/state
- `/src/frontend/src/util/billing/billing.ts` — removed `mustUpgradeForClearbit()`
- `/src/frontend/src/pages/Player/MetadataBox/MetadataBox.tsx` — removed Clearbit tooltip mention

**Env vars removed:** `CLEARBIT_API_KEY`

**Note:** `ClearbitEnabled` bool left on `Workspace` struct (inert DB column — removing requires migration).

---

### Removed: Phonehome Telemetry

**What it was:** Sent usage metrics (workspace counts, session counts, admin details) back to Highlight's own OpenTelemetry endpoint for internal analytics.

**Deleted:**
- `/src/backend/phonehome/` — `phonehome.go`

**Modified:**
- `/src/backend/main.go` — removed `phonehome.Start(ctx)` call
- `/src/backend/public-graph/graph/resolver.go` — removed `phonehome.ReportUsageMetrics` call
- `/src/backend/private-graph/graph/schema.resolvers.go` — removed all `phonehome.ReportUsageMetrics` and `phonehome.ReportAdminAboutYouDetails` calls (4+ sites), removed `PhoneHomeContactAllowed` assignment
- `/src/backend/worker/worker.go` — removed entire phonehome reporting section from `RefreshMaterializedViews`
- `/src/backend/private-graph/graph/schema.graphqls` — removed `phone_home_contact_allowed` from `AdminAboutYouDetails` and `AdminAndWorkspaceDetails` input types
- `/src/backend/model/model.go` — removed `PhoneHomeContactAllowed` from `Admin` struct
- `/src/backend/private-graph/graph/model/models_gen.go` — removed field from generated types
- `/src/backend/private-graph/graph/generated/generated.go` — removed from schema/unmarshal
- `/src/backend/projectpath/config.go` — removed `PhoneHomeDeploymentID` from `Config`

---

### Removed: Stripe Billing & SaaS Pricing

**What it was:** Full subscription management — Stripe customer creation, plan selection, graduated pricing, usage-based billing, overage detection, billing issue banners, promo codes.

**Strategy:** Kept `/src/backend/pricing/` package (meter query functions are used by non-billing code) but gutted all Stripe API calls and billing logic.

**Deleted:**
- `/src/backend/lambda-functions/metering/` — AWS Marketplace metering Lambda

**Modified:**
- `/src/backend/env/environment.go` — removed `PricingBasicPriceID`, `PricingEnterprisePriceID`, `PricingStartupPriceID`, `StripeApiKey`, `StripeErrorsProductID`, `StripeSessionsProductID`, `StripeWebhookSecret`
- `/src/backend/pricing/pricing.go` — gutted. Kept: price tables, meter query functions, `RetentionMultiplier`, `IncludedAmount`. Removed: ALL Stripe API calls, AWS Marketplace functions, overage reporting, billing issue detection (~1000+ lines)
- `/src/backend/pricing/billing.go` — replaced entirely with minimal `Client` struct and `NewNoopClient()`
- `/src/backend/public-graph/graph/resolver.go` — **`IsWithinQuota` now always returns `(true, 0)`** — this is the critical change making all data ingestion unlimited
- `/src/backend/private-graph/graph/resolver.go` — removed `PricingClient`, `AWSMPClient` fields; stubbed `StripeWebhook`, `AWSMPCallback`, `updateBillingDetails` as no-ops
- `/src/backend/private-graph/graph/schema.resolvers.go` — stubbed: `CreateOrUpdateStripeSubscription` (returns ""), `HandleAWSMarketplace` (returns true), `UpdateBillingDetails` (returns true), `SubscriptionDetails` (returns enterprise defaults), `CustomerPortalURL` (returns ""); removed Stripe customer creation from `CreateWorkspace`
- `/src/backend/main.go` — removed pricing/Stripe/AWS MP client initialization
- `/src/backend/worker/worker.go` — `ReportStripeUsage` is now a no-op; removed periodic reporting goroutine
- `/src/backend/migrations/cmd/add-stripe-prices/main.go` — gutted to no-op

**Env vars removed:** `STRIPE_API_KEY`, `STRIPE_WEBHOOK_SECRET`, `STRIPE_ERRORS_PRODUCT_ID`, `STRIPE_SESSIONS_PRODUCT_ID`, `BASIC_PLAN_PRICE_ID`, `ENTERPRISE_PLAN_PRICE_ID`, `STARTUP_PLAN_PRICE_ID`

---

### Removed: AWS Marketplace Metering

**What it was:** Usage metering for reselling Highlight through AWS Marketplace.

**Deleted:**
- `/src/backend/lambda-functions/metering/` — entire Lambda function

**Modified:**
- Covered above in Stripe/Billing section (client removed from resolver, main.go, etc.)

---

### Removed: LaunchDarkly Feature Flags

**What it was:** Feature flag SDK integrated right before Highlight's shutdown. Included a migration gate that blocked users whose workspaces hadn't been migrated to LaunchDarkly.

**Modified (Frontend):**
- `/src/frontend/src/components/LaunchDarkly/LaunchDarklyProvider.tsx` — replaced with pass-through provider (no LD SDK, noops for context setters)
- `/src/frontend/src/components/LaunchDarkly/useFeatureFlag.ts` — replaced with stub returning `flags[flag].defaultValue`
- `/src/frontend/src/components/LaunchDarkly/useFeatureFlag.test.ts` — updated tests
- `/src/frontend/src/util/analytics.ts` — removed `LDClient`, `setLDClient`, `ldClient.track()`
- `/src/frontend/src/index.tsx` — removed `MigrationBlockedPage`, `isMigrationBlockedError`, LD migration UI
- `/src/frontend/src/pages/Auth/SignUp.tsx` — removed "Creating new workspaces is disabled" callout
- `/src/frontend/src/pages/Auth/JoinWorkspace.tsx` — removed LD migration callouts
- `/src/frontend/src/routers/AppRouter/AppRouter.tsx` — removed `/demo` redirect to launchdarkly.com
- `/src/frontend/src/env.d.ts` — removed `REACT_APP_LD_CLIENT_ID`
- `/src/frontend/package.json` — removed `launchdarkly-react-client-sdk` dependency
- `/src/frontend/src/graph/operators/query.gql` — removed `migration_allowlist` from `GetSystemConfiguration`
- `/src/frontend/src/graph/generated/` — removed `migration_allowlist` from generated types

**Modified (Backend):**
- `/src/backend/private-graph/graph/middleware.go` — removed `migrationBlockedResponse`, `isUserInAllowedWorkspace`, `writeMigrationBlockedError`, migration checks in `PrivateMiddleware` and `WebsocketInitializationFunction`
- `/src/backend/model/model.go` — removed `MigrationAllowlist` from `SystemConfiguration`
- `/src/backend/private-graph/graph/schema.graphqls` — removed `migration_allowlist` from `SystemConfiguration` type
- `/src/backend/private-graph/graph/schema.resolvers.go` — removed `MigrationAllowlist` resolver
- `/src/backend/private-graph/graph/generated/generated.go` — removed all generated migration_allowlist references

---

### Removed: highlight.io Marketing Website

**What it was:** Next.js marketing site — landing pages, blog, Calendly scheduling, Mux video. Not the app dashboard.

**Deleted:**
- `/highlight.io/` — entire directory (~1000 files)

**Modified:**
- `/package.json` — removed `highlight.io` from workspaces, removed `dev:highlight.io` script
- `/turbo.json` — removed `highlight.io#build` and `highlight.io#lint` task configs
- `/.changeset/config.json` — removed `highlight.io` from ignore list

---

### Changed: All Feature Gates Default to Enabled

**`/src/backend/model/model.go` — `AllWorkspaceSettings` struct:**

All feature flag defaults changed from `false` to `true`:
- `AIInsights`, `AIQueryBuilder`
- `EnableBillingLimits`, `EnableGrafanaDashboard`, `EnableIngestSampling`
- `EnableJiraIntegration`, `EnableTeamsIntegration`
- `EnableProjectLevelAccess`, `EnableSessionExport`, `EnableSSO`
- `EnableUnlimitedDashboards`, `EnableUnlimitedProjects`, `EnableUnlimitedRetention`, `EnableUnlimitedSeats`
- `EnableLogTraceIngestion`

`CanShowBillingIssueBanner` default changed to `false`.

**Effect:** All new workspaces get every feature enabled. Existing workspaces need a DB update or re-run of `EnableAllWorkspaceSettings`.

---

### Pending Cleanup

These steps should be run before committing but haven't been executed yet:

- [ ] `cd backend && go mod tidy` — remove unused Go deps (clearbit-go, stripe-go, AWS marketplace SDK)
- [ ] `make private-gen` — regenerate backend GraphQL after schema changes
- [ ] `yarn install` — update lockfile after removing LD SDK
- [ ] `yarn codegen` — regenerate frontend GraphQL types
- [ ] `cd frontend && yarn types:check` — verify no TypeScript errors
- [ ] `cd backend && go build ./...` — final compilation check (currently passes)

### Total Env Vars Removed

| Variable | Component |
|----------|-----------|
| `HUBSPOT_API_KEY` | HubSpot |
| `HUBSPOT_COOKIE_STRING` | HubSpot |
| `HUBSPOT_CSRF_TOKEN` | HubSpot |
| `HUBSPOT_OAUTH_TOKEN` | HubSpot |
| `APOLLO_IO_API_KEY` | Apollo.io |
| `APOLLO_IO_SENDER_ID` | Apollo.io |
| `CLEARBIT_API_KEY` | Clearbit |
| `STRIPE_API_KEY` | Stripe |
| `STRIPE_WEBHOOK_SECRET` | Stripe |
| `STRIPE_ERRORS_PRODUCT_ID` | Stripe |
| `STRIPE_SESSIONS_PRODUCT_ID` | Stripe |
| `BASIC_PLAN_PRICE_ID` | Stripe |
| `ENTERPRISE_PLAN_PRICE_ID` | Stripe |
| `STARTUP_PLAN_PRICE_ID` | Stripe |
| `REACT_APP_LD_CLIENT_ID` | LaunchDarkly |

---

## 2026-03-19: Go Module Path Rename

Renamed all Go module paths from the upstream `github.com/highlight-run/highlight` namespace to the HoldFast repository path.

**Changes:**
- `src/backend/go.mod` — module declaration changed from `github.com/highlight-run/highlight/backend` to `github.com/BrewingCoder/holdfast/src/backend`
- `sdk/highlight-go/go.mod` — module declaration changed from `github.com/highlight/highlight/sdk/highlight-go` to `github.com/BrewingCoder/holdfast/sdk/highlight-go`
- `go.work` — workspace updated to reflect new module paths
- All Go import statements across `src/backend/` updated via find-replace (hundreds of files, mechanical change)
- `go build ./...` passes after rename

---

## 2026-03-19: Browser SDK Renamed — `highlight.run` → `@holdfast-io/browser`

The core browser SDK (`sdk/highlight-run/`) was previously published as `highlight.run` — the upstream Highlight.io package name. It is now published as `@holdfast-io/browser` under the HoldFast npm org.

**Changes:**
- `sdk/highlight-run/package.json` — `name` changed from `highlight.run` to `@holdfast-io/browser`
- All internal package.json `dependencies` referencing `highlight.run` updated to `@holdfast-io/browser`
- All internal source imports updated accordingly

---

## 2026-03-19: NPM Publish Workflow

Added `publish-npm.yml` GitHub Actions workflow for publishing SDK packages to npm.

**Details:**
- Manual dispatch with inputs: `tier` (1–4 or all), `dry-run`, `version`
- Publishes packages in dependency order across 4 tiers (leaf packages first)
- Uses `node -e` for version setting to avoid reliance on `npm version` in workspace context
- Requires `NPM_TOKEN` secret configured in repository settings
- Runs on self-hosted runner (`runs-on: [self-hosted, holdfast]`)
- Builds verified passing for all 4 tiers

---

## 2026-03-19: Self-Hosted GitHub Actions Runner

All CI/CD workflows (`ci-backend.yml`, `ci-frontend.yml`, `ci-sdk.yml`, `security.yml`, `publish-npm.yml`) migrated to a dedicated self-hosted runner.

**Configuration:**
- `runs-on: [self-hosted, holdfast]` on all workflow jobs
- Runner OS: Ubuntu 24.04 VM
- Eliminates GitHub-hosted runner minute consumption and provides consistent build environment

---

## 2026-03-19: Repo Reorganization

The repository directory structure was reorganized from the upstream flat layout to a cleaner monorepo structure:

| Old Path | New Path |
|----------|----------|
| `backend/` | `src/backend/` |
| `frontend/` | `src/frontend/` |
| `docker/` | `infra/docker/` |
| `e2e/` | `tests/e2e/` |
| `cypress/` | `tests/cypress/` |
| `scripts/` | `tools/scripts/` |
| (root) | `docs/` for all markdown documentation |

---

## 2026-03-19: rrweb — Submodule Removed

The `rrweb/` directory was previously tracked as a git submodule pointing to a forked rrweb repository. It is now included as regular files directly in the monorepo.

**Why:** Submodule checkout requirements (`--recurse-submodules`) complicated fresh clones and CI setup. Inlining the files removes the dependency on a separate repository and simplifies the clone process.

**Effect:** `git clone https://github.com/BrewingCoder/holdfast` is sufficient — no `--recurse-submodules` flag needed.
