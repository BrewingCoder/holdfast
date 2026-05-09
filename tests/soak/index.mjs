// HOL-37 EPIC: soak harness entrypoint.
//
// Long-running container that emits ingest data to the local backend on a
// variable-rate schedule. Designed to run for ~24 hours without operator
// intervention. Each tick the scheduler picks a random scenario from the
// HOL-39 scenario library; scenarios send to the backend via OTLP/HTTP.
//
// HOL-38: SDK boot + fixed-interval tick loop with placeholder scenarios.
// HOL-39 (this file): wires the real scenario library and adds the OTel
// metrics SDK so metrics scenarios have somewhere to write.
// HOL-40: replaces the fixed interval with a variable-rate scheduler.
//
// stdout protocol: every tick prints one JSON-shaped line so a `docker logs`
// tail is greppable. SIGTERM triggers a clean shutdown that flushes batched
// exporter queues before exiting.

import { NodeSDK } from '@opentelemetry/sdk-node'
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http'
import { OTLPLogExporter } from '@opentelemetry/exporter-logs-otlp-http'
import { OTLPMetricExporter } from '@opentelemetry/exporter-metrics-otlp-http'
import { Resource } from '@opentelemetry/resources'
import {
  ATTR_SERVICE_NAME,
  ATTR_SERVICE_VERSION,
} from '@opentelemetry/semantic-conventions'
import { BatchLogRecordProcessor, LoggerProvider } from '@opentelemetry/sdk-logs'
import {
  MeterProvider,
  PeriodicExportingMetricReader,
} from '@opentelemetry/sdk-metrics'
import { logs } from '@opentelemetry/api-logs'
import { metrics, trace } from '@opentelemetry/api'

import {
  pickRandomScenario,
  createMetricCache,
  scenarioWeights,
} from './scenarios/index.mjs'
import { createScheduler } from './scheduler.mjs'

// ── Config ──────────────────────────────────────────────────────────
const PROJECT_ID = process.env.HOLDFAST_PROJECT_ID || '2'
const OTLP_ENDPOINT = process.env.OTLP_ENDPOINT || 'http://backend:8082/otel'
const BASE_INTERVAL_MS = parseInt(process.env.SOAK_BASE_INTERVAL_MS || '60000', 10)
// HOL-40 spike-mode tuning. Defaults: 5s ticks during a 5-15min spike,
// occurring every 25-45min outside spike windows.
const SPIKE_INTERVAL_MS = parseInt(process.env.SOAK_SPIKE_INTERVAL_MS || '5000', 10)
const SPIKE_MIN_DURATION_MS = parseInt(process.env.SOAK_SPIKE_MIN_DURATION_MS || `${5 * 60_000}`, 10)
const SPIKE_MAX_DURATION_MS = parseInt(process.env.SOAK_SPIKE_MAX_DURATION_MS || `${15 * 60_000}`, 10)
const SPIKE_MIN_GAP_MS = parseInt(process.env.SOAK_SPIKE_MIN_GAP_MS || `${25 * 60_000}`, 10)
const SPIKE_MAX_GAP_MS = parseInt(process.env.SOAK_SPIKE_MAX_GAP_MS || `${45 * 60_000}`, 10)
const SUMMARY_INTERVAL_MS = parseInt(process.env.SOAK_SUMMARY_INTERVAL_MS || `${5 * 60_000}`, 10)
const DISABLE_SPIKES = process.env.SOAK_DISABLE_SPIKES === '1'
const SERVICE_NAME = process.env.SOAK_SERVICE_NAME || 'holdfast-soak'
const SERVICE_VERSION = process.env.SOAK_SERVICE_VERSION || '0.0.0-soak'
const METRIC_EXPORT_MS = parseInt(process.env.SOAK_METRIC_EXPORT_MS || '15000', 10)

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

// HOL-39: dedicated MeterProvider for the metrics scenario. Periodic export
// at SOAK_METRIC_EXPORT_MS (default 15s) so the dashboard sees fresh
// counters/gauges without flooding the wire on every tick.
const meterProvider = new MeterProvider({
  resource,
  readers: [
    new PeriodicExportingMetricReader({
      exporter: new OTLPMetricExporter({
        url: `${OTLP_ENDPOINT}/v1/metrics`,
        headers,
      }),
      exportIntervalMillis: METRIC_EXPORT_MS,
    }),
  ],
})
metrics.setGlobalMeterProvider(meterProvider)

const ctx = {
  logger: logs.getLogger(SERVICE_NAME),
  tracer: trace.getTracer(SERVICE_NAME),
  meter: metrics.getMeter(SERVICE_NAME),
  metricCache: createMetricCache(),
}

// ── Scheduler-driven tick loop ──────────────────────────────────────
let tickCount = 0
let totalEvents = 0

function logEvent(payload) {
  process.stdout.write(JSON.stringify(payload) + '\n')
}

/**
 * Per-tick emit callback the scheduler invokes. Emits `burstCount` events
 * each from a freshly-picked random scenario, returns the per-event names
 * so the scheduler's summary tracker can roll them up.
 */
async function emitBurst({ inSpike, burstCount }) {
  tickCount++
  const t0 = Date.now()
  const results = []
  for (let i = 0; i < burstCount; i++) {
    let scenarioName = null
    try {
      const { name, fn } = pickRandomScenario()
      scenarioName = name
      const result = fn(ctx)
      results.push({ name: scenarioName, result })
    } catch (e) {
      logEvent({
        ts: new Date().toISOString(),
        event: 'soak.scenario.error',
        tick: tickCount,
        scenario: scenarioName,
        error: e?.message || String(e),
      })
    }
  }
  totalEvents += results.length
  // One concise log per tick — the scheduler summary covers the per-scenario rollup.
  logEvent({
    ts: new Date().toISOString(),
    event: 'soak.tick',
    tick: tickCount,
    burst: burstCount,
    in_spike: inSpike,
    elapsed_ms: Date.now() - t0,
    scenarios: results.map((r) => r.name),
  })
  return results
}

const scheduler = createScheduler(
  {
    baseIntervalMs: BASE_INTERVAL_MS,
    spikeIntervalMs: SPIKE_INTERVAL_MS,
    spikeMinDurationMs: SPIKE_MIN_DURATION_MS,
    spikeMaxDurationMs: SPIKE_MAX_DURATION_MS,
    spikeMinGapMs: SPIKE_MIN_GAP_MS,
    spikeMaxGapMs: SPIKE_MAX_GAP_MS,
    summaryIntervalMs: SUMMARY_INTERVAL_MS,
    disableSpikes: DISABLE_SPIKES,
    logEvent,
  },
  emitBurst,
)

async function loop() {
  logEvent({
    ts: new Date().toISOString(),
    event: 'soak.started',
    config: {
      project_id: PROJECT_ID,
      otlp_endpoint: OTLP_ENDPOINT,
      base_interval_ms: BASE_INTERVAL_MS,
      spike_interval_ms: SPIKE_INTERVAL_MS,
      spike_min_duration_ms: SPIKE_MIN_DURATION_MS,
      spike_max_duration_ms: SPIKE_MAX_DURATION_MS,
      spike_min_gap_ms: SPIKE_MIN_GAP_MS,
      spike_max_gap_ms: SPIKE_MAX_GAP_MS,
      summary_interval_ms: SUMMARY_INTERVAL_MS,
      disable_spikes: DISABLE_SPIKES,
      service_name: SERVICE_NAME,
      metric_export_ms: METRIC_EXPORT_MS,
      weights: scenarioWeights,
    },
  })

  // Mark startup in the analytics store so a single query verifies the
  // harness reached live state.
  ctx.tracer.startSpan('soak.started').end()

  await scheduler.run()
}

let shuttingDown = false
async function shutdown(signal) {
  if (shuttingDown) return
  shuttingDown = true
  scheduler.requestStop()
  logEvent({
    ts: new Date().toISOString(),
    event: 'soak.stopping',
    signal,
    total_ticks: tickCount,
    total_events: totalEvents,
  })
  try {
    await sdk.shutdown()
    await loggerProvider.shutdown()
    await meterProvider.shutdown()
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
