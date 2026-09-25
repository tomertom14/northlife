// Real-browser check of Phase 13 tracking: impressions from the IntersectionObserver, a feed click
// that carries its position into the event page, a navigation click, the batched upload, and
// "reset my history". Reads the stored rows back from PostgreSQL through `docker exec`.
//
// Usage: node scripts/e2e/phase-13-browser-tracking.mjs
// Environment:
//   BASE                  site URL (default http://localhost:10000)
//   PLAYWRIGHT_CORE_PATH  path to the playwright-core package (default: resolve "playwright-core")
//   BROWSER_PATH          Chromium-based browser (default: Microsoft Edge on Windows)
//   POSTGRES_CONTAINER    default northlife-postgres-1
import { execFileSync } from 'node:child_process';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const { chromium } = require(process.env.PLAYWRIGHT_CORE_PATH ?? 'playwright-core');
const base = process.env.BASE ?? 'http://localhost:10000';
const browserPath = process.env.BROWSER_PATH ?? 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const container = process.env.POSTGRES_CONTAINER ?? 'northlife-postgres-1';
let failures = 0;

function check(name, condition) {
  console.log(`${condition ? 'PASS' : 'FAIL'}  ${name}`);
  if (!condition) failures++;
}

function sql(query) {
  return execFileSync('docker', ['exec', '-i', container, 'psql', '-U', 'northlife', '-d', 'northlife', '-At', '-F', ','], {
    input: query,
    encoding: 'utf8',
  }).trim();
}

const rowsFor = (visitor) =>
  sql(`SELECT type, source, coalesce(position::text, '') FROM interactions WHERE visitor_id = '${visitor}' ORDER BY occurred_at_utc, type;`)
    .split('\n')
    .filter(Boolean)
    .map((line) => {
      const [type, source, position] = line.split(',');
      return { type: Number(type), source: Number(source), position: position ? Number(position) : null };
    });

const browser = await chromium.launch({ executablePath: browserPath, headless: true });
const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, locale: 'he-IL', timezoneId: 'Asia/Jerusalem' });
// Navigation links open Waze or Google Maps in a new tab; keep the test offline.
await context.route(/waze\.com|google\.com\/maps/, (route) => route.abort());
const page = await context.newPage();

console.log('\n== Impressions from the feed');
await page.goto(`${base}/?period=tomorrow`, { waitUntil: 'networkidle' });
const cards = page.locator('a.row');
const cardCount = await cards.count();
check(`the feed shows events for tomorrow (${cardCount})`, cardCount >= 2);
// Cards need to stay half visible for a second; then the batch leaves after about four seconds.
await page.waitForTimeout(1500);
await page.waitForTimeout(4500);
const visitor = await page.evaluate(() => localStorage.getItem('northlife.visitor'));
check('the browser keeps an anonymous visitor id', /^[0-9a-f-]{36}$/.test(visitor ?? ''));
let rows = rowsFor(visitor);
const impressions = rows.filter((row) => row.type === 1);
check(`visible cards are recorded as feed impressions with positions (${impressions.length})`, impressions.length >= 2 && impressions.every((row) => row.source === 1 && row.position >= 1));

console.log('\n== A click carries its feed position to the event page');
const target = cards.nth(1);
await target.click();
await page.waitForLoadState('networkidle');
check('the event page opened', page.url().includes('/events/'));
const navigate = page.getByText('ניווט עם Waze', { exact: true });
await navigate.click();
await page.waitForTimeout(5000);
rows = rowsFor(visitor);
const detail = rows.find((row) => row.type === 2);
check('the detail view is attributed to the feed at position 2', detail?.source === 1 && detail?.position === 2);
check('the navigation click is recorded from the details page', rows.some((row) => row.type === 3 && row.source === 4));

console.log('\n== Reset my history');
await page.getByRole('button', { name: 'איפוס ההיסטוריה שלי' }).click();
await page.getByText('ההיסטוריה נמחקה', { exact: false }).waitFor();
const renewed = await page.evaluate(() => localStorage.getItem('northlife.visitor'));
check('the browser continues under a new visitor id', renewed && renewed !== visitor);
check('the old history is deleted on the server', rowsFor(visitor).length === 0);

await browser.close();
console.log(`\n${failures ? `${failures} check(s) failed` : 'All checks passed'}`);
process.exit(failures);
