import { SeverityNumber } from '@opentelemetry/api-logs'
import { pick, weighted, randInt, randomAttributes } from './random.mjs'

const EVENT_NAMES = [
  [25, 'page_view'],
  [15, 'button_click'],
  [10, 'form_submit'],
  [10, 'feature_used'],
  [10, 'search_query'],
  [10, 'video_play'],
  [10, 'cart_added'],
  [5, 'cart_removed'],
  [5, 'checkout_started'],
]

/**
 * Emit a custom session event as an INFO log with the attribute schema the
 * dashboard's events surface expects. As with sessions, the soak skips the
 * full GraphQL pushPayload path for simplicity and tags via attribute
 * convention so the verification queries can find them.
 *
 * @param {object} ctx
 * @param {import('@opentelemetry/api-logs').Logger} ctx.logger
 */
export function emitEvent(ctx) {
  const eventName = weighted(EVENT_NAMES)
  const attrs = randomAttributes()

  ctx.logger.emit({
    severityNumber: SeverityNumber.INFO,
    severityText: 'INFO',
    body: `event: ${eventName}`,
    attributes: {
      ...attrs,
      'log.scenario': 'events',
      'event.name': eventName,
      'event.session_id': `soak-session-${randInt(1, 1000)}`,
      'event.user_id': `soak-user-${randInt(1, 1000)}`,
      'event.timestamp_ms': Date.now(),
    },
  })

  return { scenario: 'events', name: eventName }
}
