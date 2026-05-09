import { SeverityNumber } from '@opentelemetry/api-logs'
import { pick, weighted, randInt, randomAttributes } from './random.mjs'

// Realistic-ish browser/OS distribution; matches what dashboard sees in real traffic
const BROWSERS = [
  [55, ['Chrome', '120.0.6099.71']],
  [20, ['Safari', '17.1']],
  [15, ['Firefox', '120.0.1']],
  [5, ['Edge', '120.0.2210.61']],
  [5, ['Opera', '105.0.4970.34']],
]

const OS_TABLE = [
  [50, ['macOS', '14.1.2']],
  [25, ['Windows', '11.0.22631']],
  [15, ['iOS', '17.1.1']],
  [5, ['Android', '14']],
  [5, ['Linux', '6.5']],
]

const COUNTRIES = [
  [40, 'US'],
  [15, 'GB'],
  [10, 'DE'],
  [10, 'CA'],
  [10, 'AU'],
  [5, 'JP'],
  [5, 'BR'],
  [5, 'IN'],
]

/**
 * Emit a "session" trace — the soak harness can't actually create sessions
 * via the public GraphQL surface (that requires an API key + full pushPayload
 * lifecycle), so we simulate the session in the trace store with a
 * `session.start` span carrying the attributes the dashboard expects on a
 * session. This still exercises the analytics-store session-data surface
 * (analytics.sessions for PG, default.sessions for CH) once the worker's
 * session-event consumer maps the OTLP span into a session row in HOL-39+.
 *
 * For now, the soak verification queries should look at traces with
 * `session.scenario=sessions` to confirm volume.
 *
 * @param {object} ctx
 * @param {import('@opentelemetry/api').Tracer} ctx.tracer
 */
export function emitSession(ctx) {
  const [browserName, browserVersion] = weighted(BROWSERS)
  const [osName, osVersion] = weighted(OS_TABLE)
  const country = weighted(COUNTRIES)
  const attrs = randomAttributes()

  const isRageClick = Math.random() < 0.04
  const hasErrors = Math.random() < 0.12
  const length = randInt(5, 600) // seconds

  ctx.tracer.startActiveSpan(
    'session.start',
    {
      attributes: {
        ...attrs,
        'session.scenario': 'sessions',
        'browser.name': browserName,
        'browser.version': browserVersion,
        'os.name': osName,
        'os.version': osVersion,
        'geo.country': country,
        'session.has_rage_clicks': isRageClick,
        'session.has_errors': hasErrors,
        'session.length_seconds': length,
        'session.identifier': `soak-user-${randInt(1, 1000)}`,
      },
    },
    (span) => span.end(),
  )

  return { scenario: 'sessions', browser: browserName, country }
}
