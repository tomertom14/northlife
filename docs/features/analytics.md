# Analytics: first-party tracking, unique visitors, popularity and spikes

Business owners see how people engage with their events, administrators see unusual traffic, and operators get system metrics. The same data feeds ranking ([smart-ranking.md](smart-ranking.md)) and recommendations ([recommendations.md](recommendations.md)). Introduced in phase 13.

## Why first-party

- **Google Analytics and Tag Manager** would send the data to Google, lose hits to ad blockers, need a cookie banner, and need the GA4 Data API just to show numbers inside the app.
- **Prometheus** is built for system metrics. Per-event labels would create a separate time series for every event, a documented high-cardinality anti-pattern.

So business numbers are collected and stored in our own PostgreSQL, and Prometheus with Grafana watches the service itself.

## Privacy

- The browser generates a random visitor id (`crypto.randomUUID()`) and keeps it in `localStorage`. No account, name, email or IP address is stored with interactions.
- The session token is never attached to analytics calls, so a visitor id cannot be tied to an account.
- Browsers sending **Global Privacy Control** are not tracked at all.
- The footer explains what is collected and offers **"reset my history"**: the server deletes that visitor id's raw history and the browser continues under a new id. Anonymous aggregates stay.
- Raw interactions are kept for 90 days. Aggregates have no visitor ids; only HyperLogLog sketches, which cannot be reversed.

## What is tracked

| Interaction | When | Weight in engagement |
| --- | --- | --- |
| Impression | A card is at least 50% visible for one second (the IAB viewability rule), via `IntersectionObserver` | 1 |
| Detail view | The event page opens | 3 |
| Navigate | The visitor taps Waze or Google Maps | 5 |
| Share | The visitor shares the event | 4 |

Each interaction also records:
- The **surface**: feed, editors' picks, map, details, recommendations or similar events.
- The **position** in the list, counted across pages. A click keeps the position of the card that was clicked; the router state carries it to the event page.
- A hash of the **list** (filters and sort) it came from, added in phase 14.

The browser queues interactions and sends them in batches of up to 50 every four seconds. When the tab is hidden it uses `navigator.sendBeacon`, so the last ones are not lost.

## Pipeline

```
browser ──batch──▶ POST /api/analytics/events ──▶ interactions (monthly partitions)
                                                        │  every minute, window up to now − 2 min
                                                        ▼
                             event_stats_hourly · event_stats_daily (+ HyperLogLog) · event_popularity
                                                        │
                        GET /api/manage/analytics ◀─────┴────▶ GET /api/admin/analytics/anomalies
```

### Ingestion

- **Validation:** at most 50 interactions per batch and a non-empty visitor id. Unknown or unpublished events are ignored, and user agents that look like crawlers are accepted but not counted.
- **Rate limit:** `analytics` policy, 120 requests per minute per client address.
- **Deduplication:** the same visitor, event and type count **once per 30 minutes**. Impressions count per surface, so seeing a card in the picks rail and again in the feed is two exposures.
  - The check is part of the insert: `INSERT … SELECT … WHERE NOT EXISTS (a row in the last 30 minutes)`.
  - It runs under a per-visitor advisory lock (`pg_advisory_xact_lock`), so two tabs cannot both pass it at the same moment.

### Storage

- `interactions` is **range-partitioned by month** (`interactions_y2026m09`, …) with a default partition as a safety net.
- PL/pgSQL functions create partitions ahead of time and drop those whose whole month is older than 90 days.
- Dropping a partition is O(1) and leaves no bloat, where `DELETE` of millions of rows would need vacuuming.
- Indexes: `(occurred_at_utc)` for rollup windows, and `(visitor_id, event_id, type, occurred_at_utc)` for deduplication, "reset my history" and recommendations.

### Exactly-once rollups

A background worker in the web process runs every minute. Each run:
1. Takes the window `[checkpoint, database now − 2 minutes)`.
   - Interactions take their timestamp from the database clock when inserted, and inserts commit in milliseconds.
   - So every row older than the lag is already visible, and no row can later appear behind the checkpoint.
2. Aggregates the window in one pass (`RollupAggregator`, a pure function): hourly counts, daily counts per Jerusalem calendar day, visitor sets and popularity deltas.
3. Adds the deltas to the statistics rows and **moves the checkpoint in the same transaction**. A crash rolls both back, so every raw row counts exactly once.
4. Catches up after downtime in windows of at most six hours. It stops as soon as a window reaches "now − 2 minutes": looping until a window comes back empty never ends, because the clock moves on during each iteration. Prometheus exposed exactly that livelock during development.

With several instances, `pg_try_advisory_xact_lock` lets only one of them roll up at a time.

## Algorithms

### HyperLogLog: unique visitors

- **Why:** unique visitors over a week are not the sum of daily uniques (the same person comes back), and raw rows are deleted after 90 days. We need a small summary that can be **merged**.
- **Implementation** (`Analytics/HyperLogLog.cs`, Flajolet et al. 2007):
  - Each visitor id is hashed to 64 bits with the SplitMix64 finalizer.
  - The first p = 14 bits choose one of m = 16,384 registers.
  - The register keeps the largest "rank", the position of the first 1-bit in the remaining 50 bits. A rank of k has probability 2^−k.
- **Estimate:** E = α_m · m² / Σ 2^−M[j].
  - Small sets use **linear counting** over empty registers, m · ln(m / V), which is close to exact.
  - The standard error is 1.04 / √m ≈ **0.81%**.
- **Merge:** the register-wise maximum. It is commutative, associative and idempotent: adding a visitor already in the sketch changes nothing.
- **Storage:** sparse (index, rank) pairs while few registers are set, as in HyperLogLog++; one byte per register above about 5,000 set registers. A one-visitor sketch takes 8 bytes and a full one 16,387.
- **Queries:** a daily sketch per event is stored in `event_stats_daily.visitor_sketch`. Unique visitors for any set of days and events is the **union** of their sketches: a person who opened two of an owner's events, or came back on another day, counts once.

### Forward-decayed popularity

- **Definition:** pop(t) = Σ wᵢ · e^(−λ(t − tᵢ)), with a 6-hour half-life (λ = ln 2 / 6 h).
- **Forward decay** (Cormode et al. 2009): for a fixed landmark L, pop(t) = e^(−λ(t−L)) · Σ wᵢ · e^(λ(tᵢ−L)). The stored sum never decays, so an interaction only **adds** to it, and ordering events by the stored sum equals ordering them by pop(t) at any time.
- **Log space:** the sum grows like e^(λt) (about 2^1460 after a year), so it is kept as a natural logarithm and combined with log-sum-exp: log(eᵃ + eᵇ) = max + ln(1 + e^(−|a−b|)).
- **Weights for ranking** (phase 14):
  - Impressions add 0, because exposure is not interest and would feed the hot sort into itself.
  - Feed clicks are corrected for position.

### EWMA spike detection

- **Series:** an hourly series of detail views, zero-filled over the last 168 complete hours.
- **Baseline and spread:** exponentially weighted average and variance with α = 2 / 25, following West (1979):
  - μ ← μ + αδ
  - σ² ← (1 − α)(σ² + αδ²)
  - where δ = x − μ
- **Score:** z = (x − μ) / √max(σ², μ, 1). The floor reflects Poisson noise, whose variance equals its mean.
- **Spike:** z ≥ 3 and at least 10 views in the hour. A steady ramp keeps z low because the average follows it; only a sudden break scores high.
- **Owners** see "taking off now" on such events.
- **Administrators** see the same spikes labelled:
  - **suspicious** when navigations and shares in that hour fall below a quarter of the event's own baseline rate, meaning many views that lead nowhere, as automated traffic would produce;
  - **surge** otherwise.

## Owner analytics page

`/manage/analytics` follows the dataviz rules: sentence-case labels, text never in series colours, a single axis and validated colours.
- **Period:** 7, 30 or 90 days. Refetching dims the previous render instead of flashing a skeleton.
- **KPI tiles:**
  - Detail views, with impressions as the note.
  - Unique visitors, marked as an estimate.
  - Navigations.
  - Click-through rate (views / impressions).
- **Line chart:** views and unique visitors per day.
  - Time runs right to left, following the Hebrew reading direction.
  - It has a legend, the latest values labelled at the line ends, a crosshair tooltip that lists both series, keyboard control (arrow keys) and a **table view** of the same numbers.
  - Today's partial day is labelled as such.
- **Funnel:** impressions → detail views → navigations, drawn in a single-hue ordinal ramp, with conversion from each previous stage.
- **Comparison table:** per event, with a sticky first column on phones.

The palette was checked with the dataviz validator: series blue and orange pass every colour-blindness check; the funnel ramp passes the ordinal checks.

## Operations: Prometheus and Grafana

- **`/metrics`** (prometheus-net) exposes:
  - HTTP request counts and duration histograms.
  - .NET meters: Npgsql connection pool and command durations, EF Core, the runtime.
  - Analytics metrics: `northlife_analytics_interactions_received_total{type}`, `…_recorded_total`, rollup runs, rollup duration histogram and rollup lag.
- **Access:** not public. It requires `Metrics:Token` as a bearer token; locally it can instead be opened to loopback and private networks with `Metrics:AllowPrivateNetwork`.
- **`docker compose --profile monitoring up -d`** starts Prometheus (:9090) and Grafana (:3000) with a provisioned "NorthLife operations" dashboard: request rate, p95 latency, error share, rollup lag, latency percentiles, database operation p95, connections, interactions by type, rollup runs.

## API

| Endpoint | Notes |
| --- | --- |
| `POST /api/analytics/events` | `{ visitorId, interactions: [{ eventId, type, source, position, context }] }` → `202 { received, recorded }` |
| `POST /api/analytics/forget` | `{ visitorId }` → 204 |
| `GET /api/manage/analytics?days=7\|30\|90` | Business owner. Totals, daily series and per-event rows with the trend flag. |
| `GET /api/admin/analytics/anomalies` | Administrators with 2FA. |
| `GET /metrics` | Prometheus text format. |

## Configuration

| Setting | Default | Meaning |
| --- | --- | --- |
| `Analytics__WorkerEnabled` | true | Run the background worker. |
| `Analytics__RollupIntervalSeconds` | 60 | How often the rollup runs. |
| `Analytics__IngestLagSeconds` | 120 | Rows younger than this wait for the next run. |
| `Analytics__RawRetentionDays` | 90 | Raw interaction retention. |
| `RateLimiting__AnalyticsPermitsPerMinute` | 120 | Ingestion rate limit per client address. |
| `Metrics__Token` | (none) | Bearer token for `/metrics`. |
| `Metrics__AllowPrivateNetwork` | false | Local Docker only. |
| `Demo__OwnerPassword` | (none) | Password for the demo owners that `--seed-demo` creates. `--refresh-demo` does not change passwords. |

## Demo data and the persona simulator

`--seed-demo` creates:
- Six businesses and 64 upcoming events in 14 northern towns, with generated cover images.
- **30 days of simulated traffic** from about 1,500 synthetic visitors.

The traffic is loaded with binary `COPY` below the rollup checkpoint and rolled up with the same code the worker uses, so it is never counted twice.

The catalogue only covers the two weeks after the seed. `--refresh-demo` brings it back:
- It puts the 64 catalogue events on the schedule a seed run today would give them (`DemoCatalog.Schedule`). The fixed random seed keeps every venue, price and start hour, so only the dates move.
- It deletes the traffic and statistics of those events (raw rows, hourly and daily rows, popularity), real visits included, and writes a new simulated month in the same way as the seed.
- Both the seed and the refresh hold the rollup worker's advisory lock while they write, so they can run against a live site.
- Accounts, places, images and place links stay. Events are matched by owner and title: events the demo owners added themselves, and renamed catalogue events, keep their dates and visits.
- It ends by rebuilding the recommendation model, so similar events are current at once.

The simulator (`Analytics/SyntheticTraffic.cs`):
- Each visitor has category tastes drawn from a sparse Dirichlet(0.4) distribution, a home town, a visit rate and a price sensitivity.
- Each event has a hidden appeal.
- On a visit the visitor sees a list (by time, one day, a favourite category, or "hot now" with occasional randomised top-8 shuffles).
- They scroll with depth 0.92, pay attention to position k in proportion to k^−0.6, and open a card with probability attention × relevance.
- The 30-minute deduplication rule is applied, so the output looks exactly like recorded traffic.
- The same simulator is the ground truth for the position-bias and recommendation evaluations.

## Tests

- **Unit:**
  - HyperLogLog:
    - Estimates within 1% below 10⁴ items and within 2.5% at 10⁵ and 10⁶.
    - The observed RMS error over 30 sketches matches 0.81%.
    - Union, idempotence, sparse and dense encodings, and corrupt-input rejection.
  - Decay: the half-life halves a score, forward decay equals the direct sum to within 10⁻⁹, ordering is invariant, and there is no overflow.
  - Spike detector: noise, a jump, a ramp, tiny counts.
  - Rollup: hourly and Jerusalem-day bucketing.
  - Simulator: determinism, deduplication and the position effect.
  - Demo schedule: reproducible, unique titles, every event still ahead and within two weeks (also across daylight saving time changes), every category on today's feed, and only the dates depend on the day of the seed.
  - `/metrics` access control.
- **CI:** after `--seed-demo`, the demo is aged by three weeks and its old traffic marked. `--refresh-demo` must bring all 64 events back within the coming two weeks, remove the marked rows, and leave raw rows and daily totals equal.
- **End to end:**
  - `phase-13-analytics.ps1` (31 checks): ingestion rules, 300 synthetic visitors, rollup, HyperLogLog union semantics, isolation, forget, seeded spikes, retention, metrics.
  - `phase-13-browser-tracking.mjs` (8 checks, real Edge): viewability impressions, click position carried to the event page, navigation, reset.
