// Audits the public pages several times on one form factor and writes the median of every metric.
// Usage (inside the container): node audit.mjs <mobile|desktop> <runs> <tag>
import fs from 'node:fs';
import lighthouse from 'lighthouse';
import desktopConfig from 'lighthouse/core/config/desktop-config.js';
import * as chromeLauncher from 'chrome-launcher';

const [formFactor = 'mobile', runsText = '3', tag = 'run'] = process.argv.slice(2);
const runs = Math.max(1, Number(runsText));
const base = process.env.BASE_URL ?? 'http://localhost:10000';
const out = '/out';

const places = await (await fetch(`${base}/api/places?pageSize=50`)).json();
const place = places.items.find((item) => item.name.includes('גליל לייב')) ?? places.items[0];
const details = await (await fetch(`${base}/api/places/${place.id}`)).json();
const eventId = details.upcomingEvents[0]?.id;

// PAGES=home,event limits a run to some pages, for example to repeat one that a busy host disturbed.
const only = (process.env.PAGES ?? '').split(',').map((name) => name.trim()).filter(Boolean);
const pages = [
  ['home', '/'],
  ...(eventId ? [['event', `/events/${eventId}`]] : []),
  ['places', '/places'],
  ['place', `/places/${place.id}`],
  ['map', '/map'],
  ['login', '/manage/login'],
].filter(([name]) => only.length === 0 || only.includes(name));

const median = (values) => {
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.floor(sorted.length / 2)];
};

const summary = [];
for (const [name, path] of pages) {
  const samples = [];
  for (let run = 0; run < runs; run++) {
    // A fresh browser per run: empty cache and storage, like a first visit.
    const chrome = await chromeLauncher.launch({
      chromeFlags: ['--headless=new', '--no-sandbox', '--disable-gpu', '--disable-dev-shm-usage'],
    });
    try {
      const { lhr, report } = await lighthouse(
        `${base}${path}`,
        { port: chrome.port, logLevel: 'error', output: ['json', 'html'], onlyCategories: ['performance', 'accessibility', 'best-practices', 'seo'] },
        formFactor === 'desktop' ? desktopConfig : undefined,
      );
      if (run === 0) fs.writeFileSync(`${out}/${tag}-${formFactor}-${name}.html`, report[1]);
      const score = (key) => Math.round((lhr.categories[key]?.score ?? 0) * 100);
      const metric = (key) => lhr.audits[key]?.numericValue ?? 0;
      const main = lhr.audits['network-requests'].details.items.find((item) => /\/main-[A-Za-z0-9_]+\.js$/.test(item.url));
      samples.push({
        performance: score('performance'),
        accessibility: score('accessibility'),
        bestPractices: score('best-practices'),
        seo: score('seo'),
        fcpMs: metric('first-contentful-paint'),
        lcpMs: metric('largest-contentful-paint'),
        tbtMs: metric('total-blocking-time'),
        cls: metric('cumulative-layout-shift'),
        speedIndexMs: metric('speed-index'),
        mainTransferKb: (main?.transferSize ?? 0) / 1024,
        benchmarkIndex: lhr.environment.benchmarkIndex,
      });
    } finally {
      await chrome.kill();
    }
  }

  const row = { page: name, formFactor, runs };
  for (const key of Object.keys(samples[0])) row[key] = Number(median(samples.map((sample) => sample[key])).toFixed(key === 'cls' ? 3 : 0));
  summary.push(row);
  console.log(`${formFactor} ${name}: P${row.performance} A${row.accessibility} BP${row.bestPractices} SEO${row.seo} | FCP ${row.fcpMs} LCP ${row.lcpMs} TBT ${row.tbtMs} CLS ${row.cls} | main.js ${row.mainTransferKb} KB`);
}

// A partial run writes its own summary, so it never overwrites the full one.
const suffix = only.length ? `-${only.join('-')}` : '';
fs.writeFileSync(`${out}/${tag}-${formFactor}-summary${suffix}.json`, JSON.stringify(summary, null, 2));
