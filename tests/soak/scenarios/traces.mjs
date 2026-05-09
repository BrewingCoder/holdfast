import { SpanKind, SpanStatusCode } from '@opentelemetry/api'
import {
  pick,
  weighted,
  randInt,
  logNormal,
  randomAttributes,
} from './random.mjs'

const SHAPE_TABLE = [
  [50, 'single'],   // one root span (e.g. cron tick)
  [30, 'two-level'], // root + 3-5 children
  [15, 'tree'],      // 4-level tree, 8-15 spans total
  [5,  'tree-with-error'], // tree with a deliberate ERROR span
]

const OPERATIONS = [
  'http.request',
  'db.query',
  'cache.get',
  'cache.set',
  'queue.publish',
  'queue.consume',
  'rpc.call',
  'session.process',
]

/**
 * Emit a trace span tree of varying depth. ~10% of spans get a synthetic
 * exception event so the dashboard's error-span flag is exercised.
 *
 * @param {object} ctx
 * @param {import('@opentelemetry/api').Tracer} ctx.tracer
 */
export function emitTrace(ctx) {
  const shape = weighted(SHAPE_TABLE)
  const rootName = pick(OPERATIONS)
  const baseAttrs = randomAttributes()
  baseAttrs['trace.scenario'] = 'traces'
  baseAttrs['trace.shape'] = shape

  ctx.tracer.startActiveSpan(
    rootName,
    {
      kind: SpanKind.SERVER,
      attributes: baseAttrs,
    },
    (root) => {
      try {
        // Simulate latency
        busyWait(logNormal(2_000_000)) // ~2ms median, log-normal tail

        if (shape === 'single') return

        const children = shape === 'two-level' ? randInt(3, 5) : randInt(2, 4)
        for (let i = 0; i < children; i++) {
          emitChild(ctx.tracer, /* depth */ shape === 'tree' || shape === 'tree-with-error' ? 1 : 0, shape === 'tree-with-error' && i === 0)
        }

        if (shape === 'tree-with-error') {
          // Make sure at least one branch has an error
          root.setStatus({ code: SpanStatusCode.ERROR, message: 'synthetic-error' })
          root.recordException(new Error('synthetic soak error'))
        }
      } finally {
        root.end()
      }
    },
  )

  return { scenario: 'traces', shape }
}

function emitChild(tracer, depth, forceError) {
  const name = pick(OPERATIONS)
  const isError = forceError || Math.random() < 0.07
  tracer.startActiveSpan(name, { kind: SpanKind.INTERNAL }, (span) => {
    try {
      busyWait(logNormal(500_000))
      // 60% chance of recursing one more level
      if (depth < 3 && Math.random() < 0.6) {
        const grandkids = randInt(1, 3)
        for (let i = 0; i < grandkids; i++) emitChild(tracer, depth + 1, false)
      }
      if (isError) {
        span.setStatus({ code: SpanStatusCode.ERROR, message: 'synthetic child error' })
        span.recordException(new Error(`error in ${name}`))
      }
    } finally {
      span.end()
    }
  })
}

function busyWait(nanoseconds) {
  // Tight loop so the trace duration is non-zero — `process.hrtime.bigint`
  // gives us nanosecond precision; OTel coerces span durations from
  // performance.now() so we just need elapsed real time.
  const start = process.hrtime.bigint()
  const target = start + BigInt(Math.floor(nanoseconds))
  while (process.hrtime.bigint() < target) { /* busy */ }
}
