# Frontend Module

## Purpose

The React dashboard application for HoldFast. This is what operators and developers use to view session replays, investigate errors, search logs, analyze traces, build dashboards, and configure alerts. It communicates with the backend exclusively via GraphQL (Apollo Client).

## Architecture

- **Framework**: React 18.3.1 with TypeScript (strict mode)
- **Build Tool**: Vite 5.4 with SWC transpilation
- **State Management**: Apollo Client 3.7.17 (GraphQL cache) + React Context
- **Routing**: React Router DOM 6.16.0
- **Styling**: Tailwind CSS 3.1.8 + CSS Modules + vanilla-extract + Ant Design 4.22.8
- **Testing**: Vitest 2.1.8 with happy-dom
- **Auth**: Firebase 11.3.1 (legacy, with simple/password auth stubs for self-hosted)

## Package

`@holdfast-io/frontend` (private — not published to npm)

## Key Dependencies

| Category | Package | Notes |
|----------|---------|-------|
| GraphQL | `@apollo/client` 3.7.17 | Cache, hooks, WebSocket subscriptions |
| UI Framework | `antd` 4.22.8 | With custom Less overrides |
| CSS | `tailwindcss` 3.1.8 | Custom color tokens, Poppins font |
| Charts | `recharts` 2.12.1 | Dashboard visualizations |
| Code Editor | `@uiw/react-codemirror` | SQL editor, query builder |
| Tables | `@tanstack/react-table` 8.7.9 | Virtualized data tables |
| Session Replay | `rrweb` (workspace) | Player component for session recordings |
| Animation | `framer-motion` 6.3.15, `lottie-react` | UI animations |
| Auth | `firebase` 11.3.1 | Legacy — self-hosted uses password mode |
| Drag-Drop | `@dnd-kit/core` | Dashboard widget reordering |
| Video | `@mux/mux-player-react` | Session export playback |
| Internal | `@holdfast-io/browser`, `@holdfast-io/react`, `@holdfast-io/ui` | Self-instrumentation + design system |

83 production dependencies, 71 dev dependencies.

## Directory Structure

```
src/frontend/src/
├── index.tsx              # App entrypoint (Apollo, Auth, Router, Helmet)
├── constants.ts           # GraphQL URIs, OTLP endpoint, auth mode
├── components/            # 65 component directories, 158 component files
├── pages/                 # 29 page directories (see below)
├── routers/               # AppRouter, InternalRouter, ProjectRouter
├── graph/
│   ├── operators/         # query.gql, mutation.gql (raw GraphQL)
│   └── generated/         # 25K lines of generated hooks, schemas, operations
├── hooks/                 # 11 custom React hooks
├── util/                  # 28 utility files across 10 subdirectories
├── authentication/        # AuthContext.tsx
├── context/               # AppLoadingContext
├── stubs/                 # highlight-io.ts (removed marketing site stub)
├── style/                 # Tailwind, Ant Design overrides, vanilla-extract
├── static/                # SVGs, integration logos, quickstart content
└── @types/                # Custom type declarations
```

## Pages

| Page | Purpose |
|------|---------|
| `Player/` | Session replay viewer — the core feature. DOM replay, devtools, network, console. |
| `Sessions/` | Session search, filtering, feed |
| `ErrorsV2/` | Error monitoring — grouped errors, stack traces, AI suggestions |
| `LogsPage/` | Log search and exploration |
| `Traces/` | Distributed tracing visualization |
| `Graphing/` | Custom dashboards, metric visualization, SQL editor |
| `Alerts/` | Alert configuration and management |
| `Connect/` | SDK integration quickstart guides |
| `IntegrationsPage/` | Third-party integration setup (Slack, Jira, GitHub, etc.) |
| `Auth/` | Login, signup, workspace invitation |
| `ProjectSettings/` | Per-project configuration |
| `WorkspaceSettings/` | Workspace-level settings |
| `HaroldAISettings/` | AI feature configuration |
| `Billing/` | Stubbed — shows "all features enabled" |
| `Home/` | Dashboard landing page |

Plus 14 more for accounts, email opt-out, OAuth, onboarding, etc.

## Data Flow

```
User → React Router → Page Component → Apollo Hook (useQuery/useMutation)
                                              ↓
                                   Private GraphQL API (backend)
                                              ↓
                                   Apollo InMemoryCache
                                              ↓
                                   Component re-render
```

**Real-time**: WebSocket subscriptions via Apollo split link for live session data.

**Session Replay**: rrweb player component renders recorded DOM snapshots fetched from backend storage.

## GraphQL

The frontend consumes the backend's private GraphQL schema. Types and hooks are auto-generated:

- **Schema source**: `../backend/private-graph/graph/schema.graphqls`
- **Queries**: `src/graph/operators/query.gql`
- **Mutations**: `src/graph/operators/mutation.gql`
- **Generated**: `src/graph/generated/` — 25,023 lines of TypeScript hooks, types, and operations

Regenerate after schema changes:
```bash
cd src/frontend && yarn codegen
```

## Configuration

All configuration via Vite environment variables (prefixed `REACT_APP_`):

| Variable | Purpose | Default |
|----------|---------|---------|
| `REACT_APP_PRIVATE_GRAPH_URI` | Private GraphQL endpoint | `https://localhost:8082/private` |
| `REACT_APP_PUBLIC_GRAPH_URI` | Public GraphQL endpoint | `http://localhost:8082/public` |
| `REACT_APP_FRONTEND_URI` | Frontend URL (for links) | `https://localhost` |
| `REACT_APP_AUTH_MODE` | Auth mode: `firebase`, `password`, `simple`, `oauth` | `password` |
| `REACT_APP_OTLP_ENDPOINT` | OTLP collector for self-instrumentation | `http://localhost:4318` |
| `REACT_APP_IN_DOCKER` | Docker deployment flag | — |
| `REACT_APP_DISABLE_ANALYTICS` | Disable Rudder analytics | — |

33 environment variables defined in `src/env.d.ts`.

## Authentication

The auth system supports multiple modes via `REACT_APP_AUTH_MODE`:

- **`password`** — Default for self-hosted. Simple email/password with bcrypt. No external dependencies.
- **`firebase`** — Legacy from Highlight.io. Requires Firebase project config. Supports Google/GitHub OAuth.
- **`simple`** — Minimal auth for development.
- **`oauth`** — Custom OIDC provider (planned for Phase 2 security hardening).

Firebase code is still present but non-functional for self-hosted deployments. Cleanup is on the roadmap (Phase 3.6).

## Testing

### Current State

```bash
cd src/frontend && yarn test          # Run tests
cd src/frontend && yarn test:coverage # With v8 coverage
```

12 test files, 72 passing tests. All utility/logic tests — no component render tests.

| Test File | Tests | What It Covers |
|-----------|-------|----------------|
| `components/Search/SearchForm/utils.test.ts` | 15 | Search query parsing |
| `pages/Billing/utils/utils.test.ts` | 12 | Billing calculations (stubbed) |
| `pages/Traces/utils.test.ts` | 10 | Trace formatting |
| `pages/Alerts/utils/AlertsUtils.test.ts` | 8 | Alert threshold logic |
| `pages/Player/MetadataBox/utils/utils.test.ts` | 5 | Metadata formatting |
| `components/Search/utils.test.ts` | 4 | Search utilities |
| `components/LaunchDarkly/useFeatureFlag.test.ts` | 3 | Feature flag stub returns defaults |
| `components/JsonViewer/utils.test.ts` | 2 | JSON formatting |
| Others | 13 | Auth, string utils, player utils |

### SonarQube Analysis (2026-03-19)

| Metric | Value | Notes |
|--------|-------|-------|
| **Coverage** | 0.0% | 12 test files for 65 components and 29 pages — needs serious investment |
| **Lines of Code** | 124,720 | Largest module in the platform. Includes generated GraphQL (25K lines excluded from analysis). |
| **Bugs** | 50 | Needs triage — highest bug count across all projects |
| **Vulnerabilities** | 0 | Clean |
| **Code Smells** | 1,444 | Expected for a frontend this size with no prior static analysis |
| **Duplication** | 18.6% | High — likely repeated patterns across pages/components |
| **Security Hotspots** | 57 | Needs review — likely auth, cookie, and API handling patterns |
| **Reliability Rating** | D | Driven by bug count |
| **Security Rating** | A | |
| **Maintainability Rating** | A | |

Priority areas for improvement:
1. **Duplication** (18.6%) — identify repeated component patterns and extract shared abstractions
2. **Security hotspots** (57) — triage and resolve, especially around auth and API calls
3. **Bugs** (50) — triage by severity, fix critical path issues first
4. **Coverage** — start with utility functions and hooks, then component render tests

## Gotchas

- **Generated code is huge** — `src/graph/generated/` is 25K lines. Exclude from analysis. Regenerate with `yarn codegen` after backend schema changes.
- **Ant Design + Tailwind coexistence** — Tailwind preflight is disabled (`corePlugins: { preflight: false }`) to avoid conflicts with Ant Design's global styles. Be careful with CSS specificity.
- **Firebase is dead code for self-hosted** — The Firebase SDK (11.3.1) is imported but unused when `AUTH_MODE=password`. It adds ~500KB to the bundle. Removing it is planned.
- **Session replay player** — The Player page is the most complex page. It imports rrweb, manages a custom state machine (`PlayerState.ts`), handles WebSocket subscriptions, and renders DevTools panels. Tread carefully.
- **IndexedDB caching** — Apollo links include an IndexedDB wrapper (`util/db.ts`, 9K lines) for offline GraphQL caching. This is complex and fragile.
- **LESS compilation** — Ant Design overrides use Less (`src/style/AntDesign/antd.overrides.less`). Vite handles this via the `less` package but it's a legacy pattern.
- **Build memory** — Production build requires `--max-old-space-size=32768` (32GB). It's in the build script. CI runners need sufficient memory.
- **Path aliases** — TypeScript paths (`@components/`, `@pages/`, `@util/`, etc.) are defined in `tsconfig.json` and resolved by `vite-tsconfig-paths`. Don't use relative imports for cross-directory references.
