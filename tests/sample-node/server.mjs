// HOL-5: Node sample app for the backend-ingest E2E test.
//
// Wires up an OpenTelemetry SDK that pushes traces + logs to the HoldFast
// backend's OTLP receiver (the same endpoint the @holdfast-io/node SDK uses
// internally — using the OTel SDK directly avoids the workspace-install dance
// while exercising the same ingest path).
//
// Endpoints:
//   GET /health        → 200 "ok"
//   GET /test/log      → emits a log row with severity=INFO
//   GET /test/trace    → handled span auto-emitted by HTTP instrumentation
//   GET /test/error    → emits a log row with severity=ERROR + stack trace
//
// Configured via env:
//   HOLDFAST_PROJECT_ID  — analytics project id (default 2 = DevSeed dev)
//   OTLP_ENDPOINT        — backend OTLP base, default http://localhost:8082/otel
//   PORT                 — express port, default a random free port (printed
//                          on stdout as "READY <port>")
//
// stdout protocol: prints "READY <port>" once Express is bound. The C# E2E
// test parses that line to know when to start hitting endpoints.

import { NodeSDK } from '@opentelemetry/sdk-node'
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http'
import { OTLPLogExporter } from '@opentelemetry/exporter-logs-otlp-http'
import { Resource } from '@opentelemetry/resources'
import { ATTR_SERVICE_NAME, ATTR_SERVICE_VERSION } from '@opentelemetry/semantic-conventions'
import { HttpInstrumentation } from '@opentelemetry/instrumentation-http'
import { BatchLogRecordProcessor, LoggerProvider } from '@opentelemetry/sdk-logs'
import { logs, SeverityNumber } from '@opentelemetry/api-logs'
import { trace } from '@opentelemetry/api'
import express from 'express'
import { createServer } from 'node:http'

const PROJECT_ID = process.env.HOLDFAST_PROJECT_ID || '2'
const OTLP_ENDPOINT = process.env.OTLP_ENDPOINT || 'http://localhost:8082/otel'
const PORT = parseInt(process.env.PORT || '0', 10)

const headers = { 'x-highlight-project': PROJECT_ID }

const resource = new Resource({
  [ATTR_SERVICE_NAME]: 'holdfast-sample-node',
  [ATTR_SERVICE_VERSION]: '0.0.0-test',
})

// ── Trace SDK ────────────────────────────────────────────────────────
// Auto-instrument incoming HTTP requests; spans flush via OTLP/HTTP to the
// backend's /otel/v1/traces. Batch settings tuned for tests — short delay so
// the assertion phase doesn't have to wait minutes.
const traceExporter = new OTLPTraceExporter({
  url: `${OTLP_ENDPOINT}/v1/traces`,
  headers,
})

const sdk = new NodeSDK({
  resource,
  traceExporter,
  instrumentations: [new HttpInstrumentation()],
})
sdk.start()

// ── Log SDK ──────────────────────────────────────────────────────────
// OpenTelemetry's logs API is separate from the trace SDK. Wire a
// LoggerProvider with a batched OTLP/HTTP exporter pointed at /otel/v1/logs.
const loggerProvider = new LoggerProvider({ resource })
loggerProvider.addLogRecordProcessor(
  new BatchLogRecordProcessor(
    new OTLPLogExporter({
      url: `${OTLP_ENDPOINT}/v1/logs`,
      headers,
    }),
    {
      // Test-friendly batch settings — flush quickly.
      scheduledDelayMillis: 1_000,
      maxExportBatchSize: 64,
    },
  ),
)
logs.setGlobalLoggerProvider(loggerProvider)
const logger = logs.getLogger('holdfast-sample-node')

// ── Express endpoints ────────────────────────────────────────────────
const app = express()

app.get('/health', (_req, res) => res.status(200).send('ok'))

app.get('/test/log', (_req, res) => {
  const tag = `holdfast-sample-log-${Date.now()}`
  logger.emit({
    severityNumber: SeverityNumber.INFO,
    severityText: 'INFO',
    body: `Sample log emitted from /test/log (${tag})`,
    attributes: { tag, scenario: 'happy-path' },
  })
  res.json({ ok: true, tag })
})

app.get('/test/trace', (_req, res) => {
  // HTTP instrumentation already creates a server span for this request; add a
  // child span with a known name so the C# test can assert it appears.
  const tracer = trace.getTracer('holdfast-sample-node')
  const tag = `holdfast-sample-trace-${Date.now()}`
  tracer.startActiveSpan('sample.work', (span) => {
    span.setAttribute('tag', tag)
    // Simulate work
    const start = process.hrtime.bigint()
    while (process.hrtime.bigint() - start < 5_000_000n) { /* busy 5ms */ }
    span.end()
    res.json({ ok: true, tag })
  })
})

app.get('/test/error', (_req, res) => {
  const tag = `holdfast-sample-error-${Date.now()}`
  try {
    throw new Error(`synthetic error from /test/error (${tag})`)
  } catch (e) {
    logger.emit({
      severityNumber: SeverityNumber.ERROR,
      severityText: 'ERROR',
      body: e.message,
      attributes: { tag, stack: e.stack || '', scenario: 'error' },
    })
    res.json({ ok: true, tag })
  }
})

// ── Bind + ready signal ──────────────────────────────────────────────
const server = createServer(app)
server.listen(PORT, '127.0.0.1', () => {
  const addr = server.address()
  const port = typeof addr === 'object' && addr ? addr.port : PORT
  // The C# test parses this exact line — keep the format stable.
  process.stdout.write(`READY ${port}\n`)
})

// Clean shutdown on SIGTERM so the parent process can stop us deterministically.
async function shutdown() {
  try {
    await sdk.shutdown()
    await loggerProvider.shutdown()
  } finally {
    server.close(() => process.exit(0))
  }
}
process.on('SIGTERM', shutdown)
process.on('SIGINT', shutdown)
