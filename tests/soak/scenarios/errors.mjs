import { SeverityNumber } from '@opentelemetry/api-logs'
import { pick, weighted, randomAttributes } from './random.mjs'

// 20 stable error "groups" (Type + first stack frame) so the dedup behavior
// in error_groups gets exercised across the full soak run.
const ERROR_GROUPS = [
  ['TypeError', 'cannot read property of undefined', 'at processOrder (/app/orders.js:42:12)'],
  ['TypeError', 'is not a function', 'at validateInput (/app/validate.js:18:8)'],
  ['TypeError', 'expected string', 'at parseConfig (/app/config.js:55:22)'],
  ['ReferenceError', 'unknown identifier', 'at runHook (/app/hooks.js:91:5)'],
  ['ReferenceError', 'before initialization', 'at Bootstrap (/app/boot.js:12:3)'],
  ['NetworkError', 'connection reset', 'at fetchUpstream (/app/net.js:33:9)'],
  ['NetworkError', 'timeout exceeded', 'at apiCall (/app/api.js:48:14)'],
  ['NetworkError', 'DNS lookup failed', 'at resolveHost (/app/dns.js:7:1)'],
  ['RangeError', 'maximum call stack', 'at recurse (/app/recurse.js:3:10)'],
  ['ValidationError', 'email not valid', 'at signup (/app/auth.js:67:18)'],
  ['ValidationError', 'password too short', 'at signup (/app/auth.js:71:18)'],
  ['ValidationError', 'amount must be positive', 'at chargeCard (/app/billing.js:88:24)'],
  ['DatabaseError', 'unique constraint violation', 'at insertUser (/app/db/users.js:120:7)'],
  ['DatabaseError', 'connection pool exhausted', 'at acquire (/app/db/pool.js:33:5)'],
  ['DatabaseError', 'query timeout', 'at runQuery (/app/db/query.js:200:14)'],
  ['AuthError', 'token expired', 'at verify (/app/auth/jwt.js:12:6)'],
  ['AuthError', 'invalid signature', 'at verify (/app/auth/jwt.js:25:6)'],
  ['NotFoundError', 'user not found', 'at lookupUser (/app/users.js:44:8)'],
  ['NotFoundError', 'order does not exist', 'at fetchOrder (/app/orders.js:78:8)'],
  ['NotFoundError', 'product unavailable', 'at fetchProduct (/app/products.js:31:8)'],
]

const RECURRENCE_TABLE = [
  [50, 1],   // 50% of error events are a single occurrence
  [30, 2],   // 30% are 2 in quick succession
  [15, 5],   // 15% are 5 (a "burst")
  [5, 20],   // 5% are 20 (an "outage" window)
]

/**
 * Emit a backend-style error as an ERROR-severity log with the attribute
 * bag the backend's worker can dedup on. We use the OTLP log path (rather
 * than the public GraphQL pushBackendPayload mutation) to keep the soak
 * harness API-key-free; this still exercises the analytics-store error
 * surface end-to-end since the worker grouping is downstream of log
 * ingestion in the Postgres-backend path.
 *
 * @param {object} ctx
 * @param {import('@opentelemetry/api-logs').Logger} ctx.logger
 */
export function emitError(ctx) {
  const [type, message, frame] = pick(ERROR_GROUPS)
  const recurrence = weighted(RECURRENCE_TABLE)
  const groupKey = `${type}:${frame}` // mimic the backend's grouping signature
  const baseAttrs = randomAttributes()

  for (let i = 0; i < recurrence; i++) {
    ctx.logger.emit({
      severityNumber: SeverityNumber.ERROR,
      severityText: 'ERROR',
      body: `${type}: ${message}`,
      attributes: {
        ...baseAttrs,
        'log.scenario': 'errors',
        'error.type': type,
        'error.message': message,
        'error.stack_top': frame,
        'error.group_key': groupKey,
        'error.occurrence': i + 1,
      },
    })
  }
  return { scenario: 'errors', type, recurrence }
}
