// Small RNG helpers shared across scenario modules.

/**
 * Pick a random element from an array.
 */
export function pick(arr) {
  return arr[Math.floor(Math.random() * arr.length)]
}

/**
 * Weighted random: pass an array of [weight, value] pairs. Returns one value
 * proportional to its weight.
 */
export function weighted(pairs) {
  const total = pairs.reduce((sum, [w]) => sum + w, 0)
  let r = Math.random() * total
  for (const [w, v] of pairs) {
    r -= w
    if (r <= 0) return v
  }
  return pairs[pairs.length - 1][1]
}

/**
 * Random integer in [min, max] inclusive.
 */
export function randInt(min, max) {
  return min + Math.floor(Math.random() * (max - min + 1))
}

/**
 * Random float in [min, max).
 */
export function randFloat(min, max) {
  return min + Math.random() * (max - min)
}

/**
 * Box-Muller log-normal-ish skew for latency-shaped distributions.
 * Returns a positive number with median ~`median` and right-tail skew.
 */
export function logNormal(median, sigma = 0.6) {
  const u = 1 - Math.random()
  const v = 1 - Math.random()
  const z = Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * v)
  return median * Math.exp(sigma * z)
}

/**
 * Realistic-looking attribute dimensions for tagging logs/traces/metrics.
 * These are intentionally bounded (low cardinality) so the soak doesn't
 * blow up the analytics catalog tables.
 */
export const dimensions = {
  service: ['api', 'web', 'worker', 'auth-service', 'billing-service'],
  environment: ['prod', 'staging', 'dev'],
  region: ['us-east-1', 'us-west-2', 'eu-west-1', 'ap-southeast-2'],
  feature: [
    'checkout', 'login', 'signup', 'profile', 'search',
    'dashboard', 'api', 'reports', 'admin', 'health',
  ],
  http_method: ['GET', 'POST', 'PUT', 'DELETE', 'PATCH'],
  http_route: [
    '/api/users', '/api/orders', '/api/products', '/api/auth/login',
    '/api/billing/charge', '/api/reports', '/api/health',
  ],
}

/**
 * Build a random attribute bag of size 3-8 keys drawn from the dimension catalog.
 */
export function randomAttributes() {
  const keys = ['service', 'environment', 'region', 'feature']
  const optional = ['http_method', 'http_route']
  const optCount = randInt(0, optional.length)
  const chosen = [...keys, ...optional.slice(0, optCount)]
  const out = {}
  for (const k of chosen) out[k.replace('_', '.')] = pick(dimensions[k])
  return out
}
