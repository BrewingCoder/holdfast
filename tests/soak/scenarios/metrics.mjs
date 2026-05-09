import { pick, weighted, randInt, logNormal, randomAttributes } from './random.mjs'

// Metric kinds: counter (always-increasing), gauge (point-in-time), histogram (latency-shaped).
const METRIC_KINDS = [
  [40, 'counter'],
  [30, 'gauge'],
  [30, 'histogram'],
]

const COUNTER_NAMES = [
  'request_count', 'error_count', 'cache_hit_count',
  'queue_processed_count', 'auth_success_count',
]
const GAUGE_NAMES = [
  'cpu_percent', 'memory_used_bytes', 'queue_depth',
  'active_connections', 'pool_idle_count',
]
const HISTOGRAM_NAMES = [
  'request_duration_ms', 'db_query_duration_ms',
  'queue_processing_ms', 'render_duration_ms',
]

/**
 * Emit one metric data point. Routes through the OTel meter provider that
 * index.mjs sets up — counters and histograms record observations; gauges
 * use a synchronous gauge instrument.
 *
 * @param {object} ctx
 * @param {import('@opentelemetry/api').Meter} ctx.meter
 */
export function emitMetric(ctx) {
  const kind = weighted(METRIC_KINDS)
  const attrs = randomAttributes()
  attrs['metric.scenario'] = 'metrics'

  switch (kind) {
    case 'counter': {
      const name = pick(COUNTER_NAMES)
      const c = ctx.metricCache.counters.get(name) ?? ctx.meter.createCounter(name)
      ctx.metricCache.counters.set(name, c)
      c.add(randInt(1, 25), attrs)
      return { scenario: 'metrics', kind, name }
    }
    case 'gauge': {
      const name = pick(GAUGE_NAMES)
      // Gauge: use observable gauge with a fresh callback each time
      // (simpler than the synchronous-gauge API which isn't stable in 0.55)
      const g = ctx.metricCache.gauges.get(name) ?? ctx.meter.createObservableGauge(name)
      if (!ctx.metricCache.gauges.has(name)) {
        const samples = []
        g.addCallback((result) => {
          while (samples.length > 0) {
            const s = samples.shift()
            result.observe(s.value, s.attrs)
          }
        })
        ctx.metricCache.gauges.set(name, { instrument: g, samples })
      }
      const cached = ctx.metricCache.gauges.get(name)
      const value = name === 'cpu_percent' ? logNormal(35, 0.4)
                  : name === 'memory_used_bytes' ? logNormal(500_000_000, 0.3)
                  : randInt(0, 200)
      cached.samples.push({ value, attrs })
      return { scenario: 'metrics', kind, name }
    }
    case 'histogram': {
      const name = pick(HISTOGRAM_NAMES)
      const h = ctx.metricCache.histograms.get(name) ?? ctx.meter.createHistogram(name)
      ctx.metricCache.histograms.set(name, h)
      // Latency-shaped distribution: median ~50ms, log-normal tail
      const median = name === 'render_duration_ms' ? 16
                  : name === 'db_query_duration_ms' ? 5
                  : 50
      h.record(logNormal(median, 0.7), attrs)
      return { scenario: 'metrics', kind, name }
    }
  }
}

/**
 * Set up the metric cache structure that emitMetric closes over. Called once
 * at startup by index.mjs.
 */
export function createMetricCache() {
  return {
    counters: new Map(),
    // gauges store both the instrument and a samples queue the observable
    // callback drains on every collection cycle
    gauges: new Map(),
    histograms: new Map(),
  }
}
