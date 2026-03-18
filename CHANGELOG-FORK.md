# BrewingCoder Fork — Changelog

## 2026-03-18: Strip Marketing, Lead-Gen, and SaaS Billing

Removed all components that served Highlight's SaaS business but have no value for self-hosted deployments. **1,056 files changed — ~82,800 lines deleted.**

---

### Removed: HubSpot CRM Integration

**What it was:** CRM tracking — synced workspace/admin data to HubSpot, tracked session views, merged duplicate contacts ("doppelgänger" detection).

**Deleted:**
- `/backend/hubspot/` — `hubspot.go`, `merge.go`

**Modified:**
- `/backend/env/environment.go` — removed `HubspotApiKey`, `HubspotCookieString`, `HubspotCsrfToken`, `HubspotOAuthToken` from `Configuration`
- `/backend/model/model.go` — removed `HubspotCompanyID` from `Workspace`, `HubspotContactID` from `Admin`
- `/backend/redis/utils.go` — removed `CacheKeyHubspotCompanies`, `SetHubspotCompanies`, `GetHubspotCompanies`
- `/backend/kafka-queue/types.go` — replaced 5 deprecated HubSpot `PayloadType` constants with blank `_` identifiers (preserves iota numbering)
- `/backend/private-graph/graph/schema.resolvers.go` — cleaned "hubspot" from log messages
- `/backend/worker/worker.go` — cleaned "hubspot" from log messages
- `/backend/public-graph/graph/resolver.go` — removed HubSpot mention from comment
- `/frontend/src/util/analytics.ts` — removed `window._hsq` tracking
- `/frontend/src/@types/window.d.ts` — removed `_hsq` type

**Env vars removed:** `HUBSPOT_API_KEY`, `HUBSPOT_COOKIE_STRING`, `HUBSPOT_CSRF_TOKEN`, `HUBSPOT_OAUTH_TOKEN`

---

### Removed: Apollo.io Lead Enrichment

**What it was:** Contact enrichment on admin signup — looked up email via Apollo.io API, created contacts in Apollo, added them to sales email sequences.

**Deleted:**
- `/backend/apolloio/` — `apolloio.go`

**Modified:**
- `/backend/env/environment.go` — removed `ApolloIoAPIKey`, `ApolloIoSenderID`
- `/backend/private-graph/graph/schema.resolvers.go` — removed `apolloio.Enrich()`, `apolloio.CreateContact()`, `apolloio.AddToSequence()` from `EmailSignup` resolver; resolver now just saves the email
- `/backend/model/model.go` — removed `ApolloData`, `ApolloDataShortened` from `EmailSignup` struct

**Env vars removed:** `APOLLO_IO_API_KEY`, `APOLLO_IO_SENDER_ID`

---

### Removed: Clearbit Company Enrichment

**What it was:** Company data enrichment from email/IP — used for lead scoring. Gated behind Premium/Startup/Enterprise tiers.

**Deleted:**
- `/frontend/src/pages/IntegrationsPage/components/ClearbitIntegration/` — 3 files
- `/frontend/src/clearbit.js` — tracking script
- `/frontend/src/static/integrations/clearbit.svg` — logo

**Modified:**
- `/backend/pricing/pricing.go` — removed `MustUpgradeForClearbit()`
- `/backend/env/environment.go` — removed `ClearbitApiKey`
- `/backend/main.go` — removed clearbit client initialization
- `/backend/private-graph/graph/resolver.go` — removed `ClearbitClient` field and import
- `/backend/private-graph/graph/schema.resolvers.go` — stubbed `ModifyClearbitIntegration` (returns true), stubbed `EnhancedUserDetails` (returns nil)
- `/frontend/index.html` — removed clearbit script tag
- `/frontend/src/pages/IntegrationsPage/Integrations.tsx` — removed Clearbit from integrations list
- `/frontend/src/pages/IntegrationsPage/IntegrationsPage.tsx` — removed Clearbit hooks/state
- `/frontend/src/util/billing/billing.ts` — removed `mustUpgradeForClearbit()`
- `/frontend/src/pages/Player/MetadataBox/MetadataBox.tsx` — removed Clearbit tooltip mention

**Env vars removed:** `CLEARBIT_API_KEY`

**Note:** `ClearbitEnabled` bool left on `Workspace` struct (inert DB column — removing requires migration).

---

### Removed: Phonehome Telemetry

**What it was:** Sent usage metrics (workspace counts, session counts, admin details) back to Highlight's own OpenTelemetry endpoint for internal analytics.

**Deleted:**
- `/backend/phonehome/` — `phonehome.go`

**Modified:**
- `/backend/main.go` — removed `phonehome.Start(ctx)` call
- `/backend/public-graph/graph/resolver.go` — removed `phonehome.ReportUsageMetrics` call
- `/backend/private-graph/graph/schema.resolvers.go` — removed all `phonehome.ReportUsageMetrics` and `phonehome.ReportAdminAboutYouDetails` calls (4+ sites), removed `PhoneHomeContactAllowed` assignment
- `/backend/worker/worker.go` — removed entire phonehome reporting section from `RefreshMaterializedViews`
- `/backend/private-graph/graph/schema.graphqls` — removed `phone_home_contact_allowed` from `AdminAboutYouDetails` and `AdminAndWorkspaceDetails` input types
- `/backend/model/model.go` — removed `PhoneHomeContactAllowed` from `Admin` struct
- `/backend/private-graph/graph/model/models_gen.go` — removed field from generated types
- `/backend/private-graph/graph/generated/generated.go` — removed from schema/unmarshal
- `/backend/projectpath/config.go` — removed `PhoneHomeDeploymentID` from `Config`

---

### Removed: Stripe Billing & SaaS Pricing

**What it was:** Full subscription management — Stripe customer creation, plan selection, graduated pricing, usage-based billing, overage detection, billing issue banners, promo codes.

**Strategy:** Kept `/backend/pricing/` package (meter query functions are used by non-billing code) but gutted all Stripe API calls and billing logic.

**Deleted:**
- `/backend/lambda-functions/metering/` — AWS Marketplace metering Lambda

**Modified:**
- `/backend/env/environment.go` — removed `PricingBasicPriceID`, `PricingEnterprisePriceID`, `PricingStartupPriceID`, `StripeApiKey`, `StripeErrorsProductID`, `StripeSessionsProductID`, `StripeWebhookSecret`
- `/backend/pricing/pricing.go` — gutted. Kept: price tables, meter query functions, `RetentionMultiplier`, `IncludedAmount`. Removed: ALL Stripe API calls, AWS Marketplace functions, overage reporting, billing issue detection (~1000+ lines)
- `/backend/pricing/billing.go` — replaced entirely with minimal `Client` struct and `NewNoopClient()`
- `/backend/public-graph/graph/resolver.go` — **`IsWithinQuota` now always returns `(true, 0)`** — this is the critical change making all data ingestion unlimited
- `/backend/private-graph/graph/resolver.go` — removed `PricingClient`, `AWSMPClient` fields; stubbed `StripeWebhook`, `AWSMPCallback`, `updateBillingDetails` as no-ops
- `/backend/private-graph/graph/schema.resolvers.go` — stubbed: `CreateOrUpdateStripeSubscription` (returns ""), `HandleAWSMarketplace` (returns true), `UpdateBillingDetails` (returns true), `SubscriptionDetails` (returns enterprise defaults), `CustomerPortalURL` (returns ""); removed Stripe customer creation from `CreateWorkspace`
- `/backend/main.go` — removed pricing/Stripe/AWS MP client initialization
- `/backend/worker/worker.go` — `ReportStripeUsage` is now a no-op; removed periodic reporting goroutine
- `/backend/migrations/cmd/add-stripe-prices/main.go` — gutted to no-op

**Env vars removed:** `STRIPE_API_KEY`, `STRIPE_WEBHOOK_SECRET`, `STRIPE_ERRORS_PRODUCT_ID`, `STRIPE_SESSIONS_PRODUCT_ID`, `BASIC_PLAN_PRICE_ID`, `ENTERPRISE_PLAN_PRICE_ID`, `STARTUP_PLAN_PRICE_ID`

---

### Removed: AWS Marketplace Metering

**What it was:** Usage metering for reselling Highlight through AWS Marketplace.

**Deleted:**
- `/backend/lambda-functions/metering/` — entire Lambda function

**Modified:**
- Covered above in Stripe/Billing section (client removed from resolver, main.go, etc.)

---

### Removed: LaunchDarkly Feature Flags

**What it was:** Feature flag SDK integrated right before Highlight's shutdown. Included a migration gate that blocked users whose workspaces hadn't been migrated to LaunchDarkly.

**Modified (Frontend):**
- `/frontend/src/components/LaunchDarkly/LaunchDarklyProvider.tsx` — replaced with pass-through provider (no LD SDK, noops for context setters)
- `/frontend/src/components/LaunchDarkly/useFeatureFlag.ts` — replaced with stub returning `flags[flag].defaultValue`
- `/frontend/src/components/LaunchDarkly/useFeatureFlag.test.ts` — updated tests
- `/frontend/src/util/analytics.ts` — removed `LDClient`, `setLDClient`, `ldClient.track()`
- `/frontend/src/index.tsx` — removed `MigrationBlockedPage`, `isMigrationBlockedError`, LD migration UI
- `/frontend/src/pages/Auth/SignUp.tsx` — removed "Creating new workspaces is disabled" callout
- `/frontend/src/pages/Auth/JoinWorkspace.tsx` — removed LD migration callouts
- `/frontend/src/routers/AppRouter/AppRouter.tsx` — removed `/demo` redirect to launchdarkly.com
- `/frontend/src/env.d.ts` — removed `REACT_APP_LD_CLIENT_ID`
- `/frontend/package.json` — removed `launchdarkly-react-client-sdk` dependency
- `/frontend/src/graph/operators/query.gql` — removed `migration_allowlist` from `GetSystemConfiguration`
- `/frontend/src/graph/generated/` — removed `migration_allowlist` from generated types

**Modified (Backend):**
- `/backend/private-graph/graph/middleware.go` — removed `migrationBlockedResponse`, `isUserInAllowedWorkspace`, `writeMigrationBlockedError`, migration checks in `PrivateMiddleware` and `WebsocketInitializationFunction`
- `/backend/model/model.go` — removed `MigrationAllowlist` from `SystemConfiguration`
- `/backend/private-graph/graph/schema.graphqls` — removed `migration_allowlist` from `SystemConfiguration` type
- `/backend/private-graph/graph/schema.resolvers.go` — removed `MigrationAllowlist` resolver
- `/backend/private-graph/graph/generated/generated.go` — removed all generated migration_allowlist references

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

**`/backend/model/model.go` — `AllWorkspaceSettings` struct:**

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
