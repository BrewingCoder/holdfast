// Public entrypoint for the scenario library — index.mjs imports
// `pickRandomScenario` and calls the returned function with the OTel context.

import { weighted } from './random.mjs'
import { emitLog } from './logs.mjs'
import { emitTrace } from './traces.mjs'
import { emitError } from './errors.mjs'
import { emitSession } from './sessions.mjs'
import { emitEvent } from './events.mjs'
import { emitMetric, createMetricCache } from './metrics.mjs'

// Realistic distribution: logs dominate, sessions/events trail.
// Override via SOAK_WEIGHTS env var (comma-separated key=weight pairs):
//   SOAK_WEIGHTS=logs=80,traces=10,metrics=5,errors=5
const DEFAULT_WEIGHTS = {
  logs: 40,
  traces: 20,
  metrics: 15,
  errors: 10,
  sessions: 10,
  events: 5,
}

const SCENARIO_FUNCS = {
  logs: emitLog,
  traces: emitTrace,
  metrics: emitMetric,
  errors: emitError,
  sessions: emitSession,
  events: emitEvent,
}

function parseWeights(envValue) {
  if (!envValue) return DEFAULT_WEIGHTS
  const out = { ...DEFAULT_WEIGHTS }
  for (const part of envValue.split(',')) {
    const [k, v] = part.trim().split('=')
    if (k && v && k in DEFAULT_WEIGHTS) {
      const n = parseFloat(v)
      if (Number.isFinite(n) && n >= 0) out[k] = n
    }
  }
  return out
}

const weights = parseWeights(process.env.SOAK_WEIGHTS)
const TABLE = Object.entries(weights)
  .filter(([, w]) => w > 0)
  .map(([k, w]) => [w, k])

/**
 * Pick one scenario name per the configured weights, then return its emit fn.
 */
export function pickRandomScenario() {
  const name = weighted(TABLE)
  return { name, fn: SCENARIO_FUNCS[name] }
}

export { createMetricCache, weights as scenarioWeights }
