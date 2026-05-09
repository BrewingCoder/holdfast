// HOL-38 (EPIC HOL-37): soak harness entrypoint.
//
// Long-running container that emits ingest data to the local backend on a
// variable-rate schedule. Designed to run for ~24 hours without operator
// intervention. Each tick the scheduler invokes a randomly-selected scenario;
// scenarios send to the backend via OTLP/HTTP and (in HOL-39+) the public
// GraphQL endpoint for sessions/errors that don't go through OTLP.
//
// HOL-38 scope (this file): SDK boot + tick loop emitting one INFO log + one
// trivial trace per tick, just to verify the wiring end-to-end. HOL-39 swaps
// the placeholder scenarios for the full library; HOL-40 replaces the fixed
// interval with a variable-rate scheduler with spike windows.
//
// stdout protocol: every tick prints one JSON-shaped line so a `docker logs`
// tail is greppable. SIGTERM triggers a clean shutdown that flushes batched
// exporter queues before exiting.

import { NodeSDK } from '@opentelemetry/sdk-node'
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http'
import { OTLPLogExporter } from '@opentelemetry/exporter-logs-otlp-http'
import { Resource } from '@opentelemetry/resources'
import {
  ATTR_SERVICE_NAME,
  ATTR_SERVICE_VERSION,
} from '@opentelemetry/semantic-conventions'
import { BatchLogRecordProcessor, LoggerProvider } from '@opentelemetry/sdk-logs'
import { logs, SeverityNumber } from '@opentelemetry/api-logs'
import { trace } from '@opentelemetry/api'

// ── Config ──────────────────────────────────────────────────────────
const PROJECT_ID = process.env.HOLDFAST_PROJECT_ID || '2'
const OTLP_ENDPOINT = process.env.OTLP_ENDPOINT || 'http://backend:8082/otel'
const BASE_INTERVAL_MS = parseInt(
  process.env.SOAK_BASE_INTERVAL_MS || '60000',
  10,
)
const SERVICE_NAME = process.env.SOAK_SERVICE_NAME || 'holdfast-soak'
const SERVICE_VERSION = process.env.SOAK_SERVICE_VERSION || '0.0.0-soak'

// ── OTel SDK ────────────────────────────────────────────────────────
const headers = { 'x-highlight-project': PROJECT_ID }

const resource = new Resource({
  [ATTR_SERVICE_NAME]: SERVICE_NAME,
  [ATTR_SERVICE_VERSION]: SERVICE_VERSION,
})

const sdk = new NodeSDK({
  resource,
  traceExporter: new OTLPTraceExporter({
    url: `${OTLP_ENDPOINT}/v1/traces`,
    headers,
  }),
})
sdk.start()

const loggerProvider = new LoggerProvider({ resource })
loggerProvider.addLogRecordProcessor(
  new BatchLogRecordProcessor(
    new OTLPLogExporter({ url: `${OTLP_ENDPOINT}/v1/logs`, headers }),
    { scheduledDelayMillis: 1_000, maxExportBatchSize: 64 },
  ),
)
logs.setGlobalLoggerProvider(loggerProvider)

const logger = logs.getLogger(SERVICE_NAME)
const tracer = trace.getTracer(SERVICE_NAME)

// ── Tick loop ───────────────────────────────────────────────────────
let tickCount = 0
let stopping = false

function logEvent(payload) {
  // One JSON line per tick — `docker logs holdfast-soak | jq -c` works directly.
  process.stdout.write(JSON.stringify(payload) + '\n')
}

async function tick() {
  tickCount++
  const t0 = Date.now()

  // Placeholder scenarios — HOL-39 replaces these with the real library
  logger.emit({
    severityNumber: SeverityNumber.INFO,
    severityText: 'INFO',
    body: `soak tick #${tickCount}`,
    attributes: {
      tick: tickCount,
      'soak.kind': 'startup-placeholder',
    },
  })

  tracer.startActiveSpan('soak.tick', (span) => {
    span.setAttribute('tick', tickCount)
    span.end()
  })

  logEvent({
    ts: new Date().toISOString(),
    event: 'soak.tick',
    tick: tickCount,
    elapsed_ms: Date.now() - t0,
    scenarios_emitted: ['placeholder-log', 'placeholder-trace'],
  })
}

async function loop() {
  // Initial 'soak.started' event so operators have a single log line that
  // confirms the container has reached the live state. Trace name + log
  // both carry the marker so either query path finds it.
  logger.emit({
    severityNumber: SeverityNumber.INFO,
    severityText: 'INFO',
    body: 'soak.started',
    attributes: {
      'soak.kind': 'startup',
      'soak.project_id': PROJECT_ID,
      'soak.base_interval_ms': BASE_INTERVAL_MS,
    },
  })
  tracer
    .startSpan('soak.started')
    .end()
  logEvent({
    ts: new Date().toISOString(),
    event: 'soak.started',
    config: {
      project_id: PROJECT_ID,
      otlp_endpoint: OTLP_ENDPOINT,
      base_interval_ms: BASE_INTERVAL_MS,
      service_name: SERVICE_NAME,
    },
  })

  // Fixed-interval loop. HOL-40 replaces this with the variable-rate scheduler.
  while (!stopping) {
    await tick()
    await new Promise((resolve) => setTimeout(resolve, BASE_INTERVAL_MS))
  }
}

async function shutdown(signal) {
  if (stopping) return
  stopping = true
  logEvent({
    ts: new Date().toISOString(),
    event: 'soak.stopping',
    signal,
    total_ticks: tickCount,
  })
  try {
    // Flush batched exporters before exit — tests/operators expect the last
    // few ticks to land in the analytics store.
    await sdk.shutdown()
    await loggerProvider.shutdown()
  } catch (e) {
    logEvent({
      ts: new Date().toISOString(),
      event: 'soak.shutdown.error',
      error: e?.message || String(e),
    })
  }
  process.exit(0)
}

process.on('SIGTERM', () => shutdown('SIGTERM'))
process.on('SIGINT', () => shutdown('SIGINT'))

loop().catch((err) => {
  logEvent({
    ts: new Date().toISOString(),
    event: 'soak.crash',
    error: err?.message || String(err),
    stack: err?.stack,
  })
  process.exit(1)
})
