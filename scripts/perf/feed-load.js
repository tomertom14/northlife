// k6 load test for the public feed: the requirements ask for results within two seconds.
// Seed extra events first (see docs/phases/phase-14.md), then run for example:
//   docker run --rm -i --add-host host.docker.internal:host-gateway -v "%cd%/scripts/perf:/scripts" \
//     grafana/k6:1.3.0 run /scripts/feed-load.js
// Environment: BASE_URL (default http://host.docker.internal:10000), VUS (default 20).
import http from 'k6/http';
import { check } from 'k6';

const base = __ENV.BASE_URL || 'http://host.docker.internal:10000';
const vus = Number(__ENV.VUS || 20);

function day(offset) {
  // Israel dates, as the feed uses them.
  const date = new Date(Date.now() + offset * 86_400_000 + 3 * 3_600_000);
  return date.toISOString().slice(0, 10);
}

const range = `period=range&from=${day(0)}&to=${day(29)}`;
const places = [
  [33.2073, 35.57],
  [32.9646, 35.496],
  [32.7922, 35.5312],
  [33.0059, 35.0943],
  [32.6996, 35.3035],
];

export const options = {
  scenarios: {
    time: { executor: 'constant-vus', vus, duration: '30s', exec: 'timeSort' },
    hot: { executor: 'constant-vus', vus, duration: '30s', exec: 'hotSort', startTime: '35s' },
    near: { executor: 'constant-vus', vus, duration: '30s', exec: 'nearSort', startTime: '70s' },
  },
  thresholds: {
    'http_req_duration{scenario:time}': ['p(95)<2000'],
    'http_req_duration{scenario:hot}': ['p(95)<2000'],
    'http_req_duration{scenario:near}': ['p(95)<2000'],
    http_req_failed: ['rate<0.01'],
  },
};

function get(path) {
  const response = http.get(`${base}${path}`);
  check(response, { 'status 200': (r) => r.status === 200, 'has items': (r) => r.json('items') !== undefined });
}

export function timeSort() {
  get(`/api/events?${range}&pageSize=12&page=${1 + Math.floor(Math.random() * 5)}`);
}

export function hotSort() {
  get(`/api/events?${range}&pageSize=12&sort=hot`);
}

export function nearSort() {
  const [latitude, longitude] = places[Math.floor(Math.random() * places.length)];
  get(`/api/events?${range}&pageSize=12&sort=near&latitude=${latitude}&longitude=${longitude}`);
}
