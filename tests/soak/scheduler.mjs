// HOL-40: variable-rate scheduler with spike windows.
//
// Replaces the fixed-interval tick loop from HOL-38/39. Two layers of
// randomness:
//
// 1. Per-tick burst: each tick emits 1 / 2-3 / 5-10 / 20-50 events with
//    weighted probability. This produces bursty traffic at all times,
//    even outside the macro-spike windows, so the analytics store has to
//    handle inserts in clumps rather than nicely spaced singles.
//
// 2. Macro-spike windows: every ~30 minutes (poisson-ish via random gap)
//    a 5-15 minute window opens where:
//    - the base interval drops to SOAK_SPIKE_INTERVAL_MS (default 5s)
//    - the per-tick burst distribution shifts toward the high end
//    Mirrors a real production traffic burst (cron alignment, deploy push,
//    feature launch, etc.). Operators can disable spikes via
//    SOAK_DISABLE_SPIKES=1.
//
// Telemetry: every 5 minutes the scheduler emits a "soak.summary" line
// reporting the last window's totals + spike state. Operators tail
// `docker logs` and grep for `summary` to confirm the harness is
// generating the expected mix.

import { weighted, randInt } from './scenarios/random.mjs'

// Burst distribution. Each tick rolls one of these.
const BURST_TABLE_NORMAL = [
  [50, () => 1],
  [30, () => randInt(2, 3)],
  [15, () => randInt(5, 10)],
  [5,  () => randInt(20, 50)],
]
// During a macro-spike window we shift the distribution toward the high end.
const BURST_TABLE_SPIKE = [
  [10, () => 1],
  [25, () => randInt(2, 3)],
  [35, () => randInt(5, 10)],
  [30, () => randInt(20, 50)],
]

/**
 * @typedef {object} SchedulerConfig
 * @property {number} baseIntervalMs       - tick interval outside spikes (default SOAK_BASE_INTERVAL_MS)
 * @property {number} spikeIntervalMs      - tick interval during spikes (default 5_000)
 * @property {number} spikeMinDurationMs   - minimum spike length (default 5 min)
 * @property {number} spikeMaxDurationMs   - maximum spike length (default 15 min)
 * @property {number} spikeMinGapMs        - minimum quiet window between spikes (default 25 min)
 * @property {number} spikeMaxGapMs        - maximum quiet window between spikes (default 45 min)
 * @property {number} summaryIntervalMs    - how often to emit summary lines (default 5 min)
 * @property {boolean} disableSpikes       - if true, never enter spike mode
 * @property {(payload: object) => void} logEvent - stdout protocol writer
 */

/**
 * @typedef {object} TickContext - per-tick state passed to the emit callback
 * @property {boolean} inSpike      - whether the scheduler is currently in a macro-spike window
 * @property {number}  burstCount   - number of events to emit this tick
 */

/**
 * Run the scheduler. `emit(ctx)` is called once per scheduled tick; it should
 * synchronously emit `ctx.burstCount` events. The scheduler handles all the
 * timing, spike logic, and summary reporting.
 *
 * Returns a promise that resolves when `requestStop()` causes the loop to exit.
 *
 * @param {SchedulerConfig} config
 * @param {(ctx: TickContext) => Promise<{name: string}[]>} emit  - returns array of {name} for each event emitted
 * @returns {{ run: () => Promise<void>, requestStop: () => void }}
 */
export function createScheduler(config, emit) {
  const cfg = {
    baseIntervalMs: 60_000,
    spikeIntervalMs: 5_000,
    spikeMinDurationMs: 5 * 60_000,
    spikeMaxDurationMs: 15 * 60_000,
    spikeMinGapMs: 25 * 60_000,
    spikeMaxGapMs: 45 * 60_000,
    summaryIntervalMs: 5 * 60_000,
    disableSpikes: false,
    logEvent: () => {},
    ...config,
  }

  let stopping = false

  // Spike state — null means we're outside a window. Otherwise holds the
  // wall-clock end time for the current spike.
  let spikeEndsAt = null
  let nextSpikeStartsAt = cfg.disableSpikes
    ? Number.POSITIVE_INFINITY
    : Date.now() + randInt(cfg.spikeMinGapMs, cfg.spikeMaxGapMs)

  // Counters for the 5-min summary. Reset after each summary tick.
  const window = {
    startedAt: Date.now(),
    ticks: 0,
    events: 0,
    spikeTicks: 0,
    perScenario: Object.create(null),
  }

  function shouldEnterSpike() {
    return !cfg.disableSpikes && spikeEndsAt === null && Date.now() >= nextSpikeStartsAt
  }

  function shouldExitSpike() {
    return spikeEndsAt !== null && Date.now() >= spikeEndsAt
  }

  function enterSpike() {
    const duration = randInt(cfg.spikeMinDurationMs, cfg.spikeMaxDurationMs)
    spikeEndsAt = Date.now() + duration
    cfg.logEvent({
      ts: new Date().toISOString(),
      event: 'soak.spike.start',
      duration_ms: duration,
      ends_at: new Date(spikeEndsAt).toISOString(),
    })
  }

  function exitSpike() {
    spikeEndsAt = null
    nextSpikeStartsAt = Date.now() + randInt(cfg.spikeMinGapMs, cfg.spikeMaxGapMs)
    cfg.logEvent({
      ts: new Date().toISOString(),
      event: 'soak.spike.end',
      next_spike_at: new Date(nextSpikeStartsAt).toISOString(),
    })
  }

  function emitSummary() {
    const elapsed = Date.now() - window.startedAt
    cfg.logEvent({
      ts: new Date().toISOString(),
      event: 'soak.summary',
      window_ms: elapsed,
      ticks: window.ticks,
      events: window.events,
      spike_ticks: window.spikeTicks,
      per_scenario: { ...window.perScenario },
      currently_in_spike: spikeEndsAt !== null,
    })
    // Reset window
    window.startedAt = Date.now()
    window.ticks = 0
    window.events = 0
    window.spikeTicks = 0
    for (const k of Object.keys(window.perScenario)) delete window.perScenario[k]
  }

  async function run() {
    let lastSummaryAt = Date.now()

    while (!stopping) {
      // Spike state transitions
      if (shouldEnterSpike()) enterSpike()
      else if (shouldExitSpike()) exitSpike()

      const inSpike = spikeEndsAt !== null
      const burstFn = weighted(inSpike ? BURST_TABLE_SPIKE : BURST_TABLE_NORMAL)
      const burstCount = burstFn()

      const results = await emit({ inSpike, burstCount })

      // Counters for summary
      window.ticks++
      window.events += burstCount
      if (inSpike) window.spikeTicks++
      for (const r of results || []) {
        const name = r?.name || 'unknown'
        window.perScenario[name] = (window.perScenario[name] || 0) + 1
      }

      if (Date.now() - lastSummaryAt >= cfg.summaryIntervalMs) {
        emitSummary()
        lastSummaryAt = Date.now()
      }

      const interval = inSpike ? cfg.spikeIntervalMs : cfg.baseIntervalMs
      await new Promise((resolve) => setTimeout(resolve, interval))
    }

    // One final summary on shutdown so operators see the last window's stats.
    if (window.ticks > 0) emitSummary()
  }

  return { run, requestStop: () => { stopping = true } }
}
