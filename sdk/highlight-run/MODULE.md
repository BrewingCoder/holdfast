# Browser SDK Module

## Purpose

The core browser SDK for HoldFast. This is what users install in their web applications to capture session replays, errors, console logs, network activity, performance metrics, and OpenTelemetry traces. Everything is recorded client-side and streamed to the HoldFast backend via a web worker for non-blocking uploads.

## Package

`@holdfast-io/browser` — published to npm under the `@holdfast-io` scope.

- **Bundle size limit**: 256 KB (brotli) — enforced via `size-limit`
- **Output formats**: ESM + UMD (for `<script>` tag CDN usage)
- **Build tool**: Vite (library mode, es6 target)

## Architecture

```
User's Browser App
    ↓ H.init(projectID)
@holdfast-io/browser
    ├── RRWeb Recording → DOM snapshots, mutations, input events
    ├── 22 Listeners → errors, console, network, clicks, performance, web vitals
    ├── OpenTelemetry → traces, metrics (fetch, XHR, document load, user interaction)
    └── Web Worker → compresses & uploads to backend via GraphQL
            ↓
    HoldFast Backend (public GraphQL endpoint)
```

## Public API

The SDK exports a single `H` object (also attached to `window.H`):

| Method | Purpose |
|--------|---------|
| `H.init(projectID, options?)` | Initialize recording |
| `H.start(options?)` | Begin recording |
| `H.stop(options?)` | Stop recording |
| `H.identify(identifier, metadata)` | Set user identity |
| `H.track(event, metadata)` | Track custom event |
| `H.error(message, payload?)` | Report error message |
| `H.consume(error, opts)` | Report Error object |
| `H.snapshot(element)` | Capture canvas element |
| `H.addSessionFeedback({...})` | Collect user feedback |
| `H.getSessionURL()` | Get current replay URL (Promise) |
| `H.getSessionDetails()` | Get URL with timestamp offset |
| `H.getRecordingState()` | `'Recording'` or `'NotRecording'` |
| `H.onHighlightReady(fn)` | Execute callback when SDK ready |
| `H.startSpan(name, fn)` | Create auto-closing OTEL span |
| `H.startManualSpan(name, fn)` | Create manual-close OTEL span |
| `H.recordMetric(metric)` | Record gauge metric |
| `H.recordCount(metric)` | Record count |
| `H.recordIncr(metric)` | Increment counter |
| `H.recordHistogram(metric)` | Record histogram |

## Directory Structure

```
sdk/highlight-run/src/
├── index.tsx                    # Public API (658 lines) — H object
├── client/
│   ├── index.tsx                # Core Highlight class (1,587 lines)
│   ├── listeners/               # 22 listener files
│   │   ├── error-listener.tsx
│   │   ├── console-listener.tsx
│   │   ├── first-load-listeners.tsx
│   │   ├── network-listener/    # 6 files (fetch, XHR, WebSocket, sanitizer)
│   │   ├── click-listener/
│   │   ├── focus-listener/
│   │   ├── jank-listener/
│   │   ├── performance-listener/
│   │   ├── web-vitals-listener/
│   │   ├── path-listener.tsx
│   │   ├── page-visibility-listener.tsx
│   │   ├── viewport-resize-listener.tsx
│   │   └── segment-integration-listener.tsx
│   ├── otel/                    # OpenTelemetry integration (5 files)
│   ├── types/                   # Type definitions (10 files)
│   ├── utils/                   # Utilities (11+ files)
│   ├── workers/                 # Web Worker for async uploads (4 files)
│   ├── graph/                   # Generated GraphQL client
│   └── constants/               # Session/error constants
├── integrations/                # Amplitude, Mixpanel, Segment, LaunchDarkly
├── listeners/                   # High-level fetch/WebSocket early patching
├── browserExtension/            # Chrome extension message handler
├── environments/                # Electron-specific config
└── graph/                       # GraphQL schema and operators
```

86 TypeScript files total.

## What It Captures

### Session Replay
- DOM snapshots and mutations via RRWeb
- Input events, scroll positions, mouse movements
- Canvas recording (configurable FPS)
- Cross-origin iframe recording (opt-in)
- Privacy modes: `strict` (mask all inputs), `default` (mask sensitive), `none`

### Error Tracking
- Uncaught exceptions (`window.onerror`)
- Unhandled promise rejections
- Custom error reporting via `H.error()` / `H.consume()`
- Stack trace parsing with source map support

### Network Activity
- Fetch API interception
- XMLHttpRequest interception
- WebSocket event tracking
- Performance resource timing
- Header/body redaction (configurable PII sanitization)

### Console Logs
- Captures `console.log`, `console.error`, `console.warn`, `console.info`, `console.debug`
- Configurable method list

### Performance
- Web Vitals (LCP, FID, CLS, TTFB, INP)
- Long task monitoring (jank detection, >50ms frames)
- PerformanceObserver metrics

### User Interaction
- Click events with coordinates and target selectors
- Focus/blur events
- Viewport resize
- URL/route changes

### OpenTelemetry
- Full browser tracing via `@opentelemetry/sdk-trace-web`
- Auto-instrumentation: Fetch, XHR, Document Load, User Interaction
- Metrics via `@opentelemetry/sdk-metrics`
- Custom span API (`H.startSpan`, `H.startManualSpan`)
- CORS propagation for configured `tracingOrigins`

## Dependencies

19 production dependencies:

| Category | Packages |
|----------|----------|
| **Session Replay** | `rrweb`, `@rrweb/rrweb-plugin-sequential-id-record` (workspace) |
| **OpenTelemetry** | `@opentelemetry/api`, `sdk-trace-web`, `sdk-metrics`, `exporter-trace-otlp-http`, `exporter-metrics-otlp-http`, `instrumentation-*` (4 packages), `resources`, `semantic-conventions` |
| **GraphQL** | `graphql`, `graphql-request`, `graphql-tag` |
| **Utilities** | `error-stack-parser`, `fflate` (compression), `js-cookie`, `json-stringify-safe`, `stacktrace-js`, `web-vitals`, `zone.js` |

## Configuration

Key init options passed to `H.init(projectID, options)`:

| Option | Type | Default | Purpose |
|--------|------|---------|---------|
| `backendUrl` | string | `http://localhost:8082/public` | Backend GraphQL endpoint |
| `otlpEndpoint` | string | `http://localhost:4318` | OTEL collector endpoint |
| `environment` | string | — | `development`, `production`, `staging` |
| `privacySetting` | string | `'default'` | `'strict'`, `'default'`, `'none'` |
| `networkRecording` | boolean/object | `true` | Header/body capture + redaction |
| `tracingOrigins` | boolean/RegExp[] | `true` | CORS propagation scope |
| `enableCanvasRecording` | boolean | `false` | Record canvas elements |
| `disableSessionRecording` | boolean | `false` | Disable RRWeb recording |
| `disableConsoleRecording` | boolean | `false` | Disable console capture |
| `reportConsoleErrors` | boolean | `false` | Treat console.error as errors |

## Web Worker

All data uploads happen in a background Web Worker (`src/client/workers/highlight-client-worker.ts`, 407 lines):

- Receives events from main thread via `postMessage`
- Compresses with gzip (fflate)
- Uploads via GraphQL mutations to the public endpoint
- Retries failed uploads (max 3 attempts, exponential backoff)
- Handles session unload (sends final batch)
- Validates and truncates property values (max length enforcement)

## Integrations

Third-party SDK integrations in `src/integrations/`:

| Integration | What it does |
|-------------|-------------|
| **Amplitude** | Syncs `H.track()` and `H.identify()` to Amplitude SDK |
| **Mixpanel** | Relays events and user properties to Mixpanel |
| **Segment** | Middleware for Segment event pipeline |
| **LaunchDarkly** | Tracks feature flag changes via `H.track()` |

## Testing

4 test files:

| File | What it tests |
|------|---------------|
| `src/__tests__/index.test.tsx` | Public H API — init, error, track, identify, session URLs |
| `src/client/__tests__/index.test.tsx` | Highlight class — identify, properties, event dispatch |
| `src/client/otel/index.test.ts` | OTEL initialization and exporter behavior |
| `src/client/listeners/network-listener/utils/utils.test.ts` | URL blocklist, header/body sanitization, performance timing |

Coverage is low — most tests are integration-level. Unit tests for individual listeners and the web worker are needed.

### SonarQube Analysis (2026-03-19)

| Metric | Value | Notes |
|--------|-------|-------|
| **Coverage** | 0.0% | 4 test files — needs unit tests for listeners and web worker |
| **Lines of Code** | 8,769 | Moderate size for a browser SDK |
| **Bugs** | 7 | Needs triage |
| **Vulnerabilities** | 3 | **Needs immediate attention** — likely in network interception or data handling |
| **Code Smells** | 365 | High relative to LOC — inherited patterns |
| **Duplication** | 3.9% | Acceptable |
| **Security Hotspots** | 4 | Review needed — likely around data capture and transmission |
| **Reliability Rating** | D | Driven by bug count |
| **Security Rating** | D | Driven by 3 vulnerabilities — priority fix |
| **Maintainability Rating** | A | |

#### Vulnerability Details (3 — all CRITICAL, rule S2819)

All in `src/client/index.tsx` — `postMessage` cross-origin security:

| Line | Issue | Fix |
|------|-------|-----|
| 888 | `postMessage` without target origin | Specify explicit origin instead of `'*'` |
| 910 | `postMessage` without target origin | Specify explicit origin instead of `'*'` |
| 920 | `addEventListener('message')` without origin check | Validate `event.origin` before processing |

These are web worker communication calls. Since the worker is same-origin, the fix is straightforward: replace `'*'` with `window.location.origin` and add origin validation on the listener.

#### Security Hotspot Breakdown (4 total)

| Priority | Count | Category | Details |
|----------|-------|----------|---------|
| **P1 — Fix soon** | 2 | ReDoS-vulnerable regex | `listeners/network-listener/utils/utils.ts:23`, `utils/dom/index.ts:405` — backtracking regex patterns |
| **P2 — Low risk** | 1 | `Math.random()` | `listeners/network-listener/utils/utils.ts:305` — non-crypto context (request ID generation) |
| **P2 — Low risk** | 1 | `Math.random()` | `utils/secure-id.ts:21` — despite the filename, used for non-security session IDs; rename or switch to `crypto.getRandomValues()` |

Priority areas for improvement:
1. **P0 — Vulnerabilities** (3) — fix `postMessage` origin validation in `src/client/index.tsx`
2. **P1 — ReDoS regex** (2) — rewrite backtracking patterns in network listener and DOM utils
3. **`secure-id.ts`** — rename file or switch to `crypto.getRandomValues()` to match the implied security contract
4. **Coverage** — unit tests for individual listeners, web worker, and OTEL integration
5. **Bugs** (7) — triage by impact on recording accuracy

## Gotchas

- **UMD build** — The `build:umd` step copies `dist/index.umd.cjs` to `dist/index.umd.js` for CDN compatibility (`unpkg`, `jsdelivr`). Don't remove this.
- **First-load listeners** — `first-load-listeners.tsx` patches `window.fetch` and `console.*` BEFORE the main SDK loads. This is intentional — it captures early errors and network requests that happen before `H.init()`.
- **Web Worker serialization** — The worker communicates via structured clone. Complex objects (Error, DOM nodes) must be serialized before posting. Property values are truncated to max length.
- **Privacy modes** — `strict` mode masks ALL text inputs and text content. `default` masks inputs with `type=password`. `none` records everything. This is user-configurable and must be respected.
- **256KB bundle limit** — Enforced via `size-limit`. Adding new dependencies requires checking that the limit isn't exceeded. Run `yarn enforce-size` to verify.
- **RRWeb plugin dependency** — Requires `@rrweb/rrweb-plugin-sequential-id-record` which must be built before this SDK can build. The CI workflow handles this via the rrweb build step.
- **`window.H` global** — The SDK attaches itself to `window.H` for script-tag usage. This means only one instance per page.
- **Zone.js** — Used for async context tracking in OTEL instrumentation. Can conflict with Angular's Zone.js if both are loaded.
