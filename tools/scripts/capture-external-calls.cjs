const { chromium } = require('playwright');

const LOCAL_PATTERNS = [
  'va-holdfast-dev.home.local',
  '192.168.1.143',
  'localhost',
  '127.0.0.1',
];

function isExternal(url) {
  return !LOCAL_PATTERNS.some(p => url.includes(p));
}

(async () => {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext();
  const page = await context.newPage();

  const requests = new Map(); // url -> { method, type, count }

  page.on('request', req => {
    const url = req.url();
    if (!isExternal(url)) return;
    const key = `${req.method()} ${url}`;
    if (requests.has(key)) {
      requests.get(key).count++;
    } else {
      requests.set(key, { method: req.method(), url, type: req.resourceType(), count: 1 });
    }
  });

  page.on('console', msg => {
    if (msg.type() === 'error') {
      console.error('[console.error]', msg.text());
    }
  });

  console.log('\n=== Loading login page ===');
  await page.goto('http://va-holdfast-dev.home.local:3000', { waitUntil: 'load', timeout: 30000 });
  await page.waitForTimeout(6000);

  console.log('\n=== Logging in ===');
  try {
    await page.fill('input[type="email"], input[name="email"]', 'dev@holdfast.local');
    await page.fill('input[type="password"], input[name="password"]', 'Oyster44');
    await page.click('button[type="submit"], button:has-text("Sign in"), button:has-text("Log in")');
    await page.waitForURL('**', { timeout: 20000 }).catch(() => {});
    await page.waitForTimeout(8000);
  } catch (e) {
    console.error('Login step error:', e.message);
  }

  console.log('\n=== Post-login idle ===');
  await page.waitForTimeout(5000);

  await browser.close();

  console.log('\n\n=== EXTERNAL REQUESTS CAPTURED ===\n');
  const sorted = [...requests.values()].sort((a, b) => {
    const domainA = new URL(a.url).hostname;
    const domainB = new URL(b.url).hostname;
    return domainA.localeCompare(domainB);
  });

  for (const r of sorted) {
    console.log(`[${r.type.padEnd(10)}] ${r.method} ${r.url}  (x${r.count})`);
  }

  console.log(`\nTotal unique external requests: ${requests.size}`);
})();
