import { SeverityNumber } from '@opentelemetry/api-logs'
import { pick, weighted, randomAttributes } from './random.mjs'

// Severity distribution: realistic skew toward INFO/DEBUG with occasional WARN/ERROR.
const SEVERITY_TABLE = [
  [50, { number: SeverityNumber.INFO,  text: 'INFO' }],
  [25, { number: SeverityNumber.DEBUG, text: 'DEBUG' }],
  [12, { number: SeverityNumber.WARN,  text: 'WARN' }],
  [10, { number: SeverityNumber.ERROR, text: 'ERROR' }],
  [3,  { number: SeverityNumber.FATAL, text: 'FATAL' }],
]

const BODIES = {
  INFO: [
    'request handled successfully',
    'user signed in',
    'cache hit',
    'background job completed',
    'feature flag enabled for cohort',
    'metric flushed',
  ],
  DEBUG: [
    'cache miss; fetching from upstream',
    'rate limiter check passed',
    'feature lookup',
    'query plan chosen',
    'middleware completed',
  ],
  WARN: [
    'request rate approaching threshold',
    'slow database query',
    'fallback path used',
    'config value missing; using default',
    'deprecated endpoint hit',
  ],
  ERROR: [
    'database query failed',
    'upstream service unavailable',
    'authentication failed',
    'validation error in request body',
    'payment provider rejected charge',
  ],
  FATAL: [
    'process out of memory',
    'unable to bind to required port',
    'critical dependency unreachable on startup',
  ],
}

/**
 * Emit a log row with random severity, body, and attributes.
 *
 * @param {object} ctx
 * @param {import('@opentelemetry/api-logs').Logger} ctx.logger
 * @returns {{ scenario: 'logs', severity: string }}
 */
export function emitLog(ctx) {
  const sev = weighted(SEVERITY_TABLE)
  const body = pick(BODIES[sev.text])
  const attrs = randomAttributes()
  attrs['log.scenario'] = 'logs'

  ctx.logger.emit({
    severityNumber: sev.number,
    severityText: sev.text,
    body,
    attributes: attrs,
  })

  return { scenario: 'logs', severity: sev.text }
}
