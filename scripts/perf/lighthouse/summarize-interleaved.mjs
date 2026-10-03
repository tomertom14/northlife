// Medians of an interleaved Lighthouse comparison (one summary file per build, run and page).
// Usage: node summarize-interleaved.mjs <dir> <mobile|desktop>
import fs from 'node:fs';
import path from 'node:path';

const [dir = 'docs/evaluation/lighthouse/interleaved', formFactor = 'mobile'] = process.argv.slice(2);
const pattern = new RegExp(`^([a-z])-r(\\d+)-${formFactor}-summary-([a-z]+)\\.json$`);
const samples = {};
for (const file of fs.readdirSync(dir)) {
  const match = pattern.exec(file);
  if (!match) continue;
  const [, build, , page] = match;
  const [row] = JSON.parse(fs.readFileSync(path.join(dir, file), 'utf8'));
  ((samples[page] ??= {})[build] ??= []).push(row);
}

const median = (values) => {
  const sorted = [...values].sort((a, b) => a - b);
  const mid = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
};
const metrics = ['performance', 'fcpMs', 'lcpMs', 'tbtMs', 'cls', 'speedIndexMs', 'mainTransferKb', 'benchmarkIndex', 'accessibility', 'bestPractices', 'seo'];
const result = [];
for (const page of ['home', 'event', 'places', 'place', 'map', 'login']) {
  for (const build of Object.keys(samples[page] ?? {}).sort()) {
    const rows = samples[page][build];
    const row = { page, build, runs: rows.length };
    for (const metric of metrics) row[metric] = Number(median(rows.map((r) => r[metric])).toFixed(metric === 'cls' ? 3 : 0));
    row.clsMax = Math.max(...rows.map((r) => r.cls));
    result.push(row);
    console.log(`${page.padEnd(7)} ${build}  n=${rows.length}  P${row.performance}  FCP ${row.fcpMs}  LCP ${row.lcpMs}  TBT ${row.tbtMs}  CLS ${row.cls} (max ${row.clsMax})  SI ${row.speedIndexMs}  main ${row.mainTransferKb} KB  bi ${row.benchmarkIndex}`);
  }
}
fs.writeFileSync(path.join(dir, `..`, `interleaved-${formFactor}-summary.json`), JSON.stringify(result, null, 2));
