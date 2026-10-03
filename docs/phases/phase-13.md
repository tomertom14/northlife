# Phase 13 Verification: Analytics

## Goal and prerequisites

Business owners see how people engage with their events, administrators see unusual traffic, and operators get system metrics. The interaction data also feeds ranking (phase 14) and recommendations (phase 15). Builds on phase 12.

Tool decision:
- Business analytics are first-party and live in PostgreSQL. Google Analytics would keep the data at Google, lose hits to ad blockers and need a cookie banner.
- Prometheus and Grafana cover operations only. Per-event labels would be a high-cardinality anti-pattern.

## Files and components changed

- Backend, `Analytics/`:
  - `HyperLogLog`: p = 14, sparse and dense encodings, merge, SplitMix64 hashing.
  - `DecayedPopularity`: forward decay in log space with a 6-hour half-life.
  - `SpikeDetector`: an EWMA mean and variance, then a z-score.
  - `RollupAggregator`: a pure, single-pass aggregation.
  - `AnalyticsIngestService`: batch insert with deduplication under a per-visitor advisory lock.
  - `AnalyticsRollupService` and `AnalyticsWorker`: exactly-once rollups and partition upkeep.
  - `OwnerAnalyticsService`: the owner report and the administrators' anomaly list.
  - `AnalyticsMetrics`: Prometheus counters, histogram and gauge.
  - `SyntheticTraffic` and `DemoSeeder`: persona simulation and `--seed-demo`.
- Backend, elsewhere:
  - `Data/DemoCatalog.cs`: the demo catalogue.
  - `Health/MetricsAccess.cs`: access control for `/metrics`.
  - Models: `EventStatsHourly`, `EventStatsDaily`, `EventPopularity`, `AnalyticsCheckpoint`.
  - Migration `AddAnalytics`:
    - A raw `interactions` table, range-partitioned by month.
    - PL/pgSQL partition functions (create, ensure ahead, drop by cutoff).
    - The four rollup tables and the checkpoint row.
  - `Controllers/AnalyticsController.cs`.
  - `prometheus-net.AspNetCore` added for `/metrics`. It also exports the .NET meters: Npgsql, EF Core and the runtime.
- Frontend:
  - `analytics/`: `AnalyticsService` (anonymous visitor id, batching, `sendBeacon`, Global Privacy Control, reset) and the `TrackImpression` directive (IntersectionObserver, at least 50% visible for 1 second).
  - Tracking is wired into the feed timetable (feed positions across pages), the picks rail, the map links and the event page (detail view with its origin, navigation, share).
  - The public footer gains a privacy notice and "reset my history".
  - The owner page `manage/analytics`:
    - `LineChart`: time runs right to left, with a crosshair tooltip, keyboard control and a table twin.
    - `FunnelChart`.
    - KPI tiles and an event comparison table.
  - The admin page gains an "unusual traffic" card.
  - The auth interceptor never attaches the session to analytics calls.
- Operations:
  - A `monitoring` compose profile: Prometheus v3.5 and Grafana 12.1, with a provisioned "NorthLife operations" dashboard.
  - `scripts/reset-demo-data.sql` (local only).
  - `scripts/e2e/phase-13-analytics.ps1` and `scripts/e2e/phase-13-browser-tracking.mjs`.
  - Analytics checks in CI.

## Behaviour and API

| Endpoint | Behaviour |
| --- | --- |
| `POST /api/analytics/events` | Anonymous batch of up to 50 interactions for one visitor id. Unknown or unpublished events and crawlers are ignored. Returns `202 { received, recorded }`. Rate limit `analytics`, 120 per minute per address. |
| `POST /api/analytics/forget` | Deletes the raw history of a visitor id. The anonymous aggregates stay. |
| `GET /api/manage/analytics?days=7\|30\|90` | Owner report: totals, a zero-filled daily series, and per-event numbers with a "taking off" flag. |
| `GET /api/admin/analytics/anomalies` | Spikes in the last complete hour, labelled `surge` or `suspicious`. |
| `GET /metrics` | Prometheus text format. Requires a bearer token when `Metrics:Token` is set; otherwise private networks only, and only when `Metrics:AllowPrivateNetwork` is on. |

Counting rules:
- **Weights:** impression 1, detail view 3, navigation 5, share 4.
- **Deduplication:** the same visitor, event and type count once per 30 minutes. Impressions count per surface, so the picks rail and the feed are separate exposures.
- **Exactly once:** each worker run takes the window from the checkpoint to (database now − 120 s). It aggregates the window in one pass, applies additive deltas and moves the checkpoint in the same transaction.
- **Unique visitors:** a union of daily HyperLogLog sketches across days and events, never a sum.

## Algorithms

| Algorithm | What it does | Cost |
| --- | --- | --- |
| HyperLogLog, p = 14 | Unique visitors per event per day, mergeable. The standard error is 1.04 / √16384 ≈ 0.81%. Linear counting covers small sets. Sparse encoding stores a 1-visitor sketch in 8 bytes, against 16,387 bytes dense. | O(1) per add, O(m) per estimate or merge |
| Forward-decayed popularity | pop(t) = Σ w·e^(−λ(t − tᵢ)), with λ = ln 2 / 6 h. It is stored as log Σ w·e^(λ(tᵢ − L)), so a new interaction only adds (log-sum-exp) and ordering by the stored value equals ordering by pop(t) at any time. | O(1) per interaction |
| EWMA spike detector | Uses α = 2/25, μ ← μ + αδ and σ² ← (1 − α)(σ² + αδ²), then z = (x − μ) / √max(σ², μ, 1). A spike needs z ≥ 3 and at least 10 views. The "suspicious" label applies when navigations and shares fall below a quarter of the event's baseline rate. | O(n) per series |
| Monthly range partitions | Retention drops whole partitions older than 90 days. That is O(1) and leaves no table bloat. | — |
| Persona traffic simulator | Uses Dirichlet(0.4) category tastes, a home town, visit rate and price sensitivity, plus a hidden appeal per event. Opening a card has probability attention(k) × relevance, where attention(k) = k^−0.6. Scroll depth is 0.92. | — |

## Verification

```powershell
dotnet test NorthLife.slnx --configuration Release           # 142 passed
npm --prefix frontend test -- --watch=false                  # 54 passed
npm --prefix frontend run build                              # initial 379.46 kB
powershell -File scripts/e2e/phase-13-analytics.ps1 -AdminEmail <admin> -AdminPassword <password> -AdminTotpSecret <base32>
node scripts/e2e/phase-13-browser-tracking.mjs               # PLAYWRIGHT_CORE_PATH=<playwright-core>
docker compose --profile monitoring up -d                    # Prometheus :9090, Grafana :3000
```

Unit tests (32 new):
- HyperLogLog:
  - Estimates stay within 1% for 10, 1,000 and 10,000 items, and within 2.5% for 100,000 and 1,000,000.
  - Across 30 sketches of 100,000 items the RMS error falls between 0.4% and 1.3%, against a theoretical 0.81%.
  - Merge is idempotent and commutative, and a union counts overlaps once.
  - Small sketches serialize sparsely and large ones densely; corrupt data is rejected.
- Decayed popularity:
  - The half-life halves a score, and forward decay equals the directly decayed sum to within 10⁻⁹.
  - Ordering is invariant over time, and there is no overflow five years after the landmark.
- Spike detector:
  - Noise is not flagged, while a jump from about 3 to 30 is.
  - A steady ramp is not flagged, and counts under 10 never qualify.
- Rollup and calendar:
  - The rollup buckets by UTC hour and by Jerusalem day; 21:30 UTC in September is the next local day.
  - Popularity deltas sum correctly, and hourly series are zero-filled.
- Simulator:
  - The same seed reproduces the data, the deduplication rule holds, and CTR falls with position.
- `/metrics` access:
  - Private ranges are recognized, including IPv4-mapped and ULA addresses, and the bearer token is enforced.

End-to-end on the Docker stack: **31 of 31 checks passed** (`phase-13-analytics.ps1`).
- Ingestion:
  - A missing visitor id or an oversized batch gets 400.
  - Duplicates inside one batch count once (3 of 4).
  - A repeat within 30 minutes records 0.
  - Unknown and pending events are ignored, and so are crawlers.
  - Rows land in `interactions_y2026m09`.
- Volume and rollup:
  - 300 synthetic visitors recorded exactly 700 interactions.
  - The worker rolled them up within the lag plus one interval.
  - Totals came to 401 impressions, 301 views and 1 navigation, and CTR equals 301 / 401.
- HyperLogLog accuracy:
  - Unique visitors were estimated as 301 for event A and 100 for event B, matching the true values.
  - The owner total is 301: a union, not the sum 401.
- Report correctness and access:
  - The daily series has 7 zero-filled days.
  - Another owner sees zeros, and an anonymous caller gets 401.
  - Popularity ranks the more engaging event first.
- Forget:
  - It returns 204 and the forgotten visitor counts again.
  - Other visitors stay deduplicated.
- Seeded hourly history:
  - z = 17.9 was labelled `surge` and z = 20.4 `suspicious`.
  - An owner gets 403 on the anomaly list, and both events are flagged as taking off.
- Operations:
  - Retention dropped a 2020 partition.
  - `/metrics` is served to the private network with no event ids in any label.

Real browser, Edge headless: **8 of 8 checks passed** (`phase-13-browser-tracking.mjs`).
- Cards that stayed visible for a second were recorded as feed impressions with positions.
- A click on the second card produced a detail view attributed to Feed, position 2.
- The Waze click was recorded as a navigation.
- "Reset my history" switched the browser to a new visitor id and deleted the old rows on the server.

A second run estimated 97 unique visitors for a true 100. An independent Python implementation of the same hash reproduced the registers exactly: 99 of the visitors fell into 96 registers, so three pairs collided. Over 3,000 simulated sets of 100 random ids the hash averaged 0.31 collisions, against 0.30 in theory, and three or more collisions occurred in 0.37% of sets. The run was a legitimate rare outcome of a probabilistic sketch, not a defect. The end-to-end tolerance for 100 visitors is now ±4, which a correct sketch exceeds with probability of about 2 × 10⁻⁵.

Found and fixed during verification:
- **Rollup livelock.** The worker's first catch-up loop looped "until the window is empty". The database clock moves during each iteration, so a sliver was always left and the loop never ended. The Prometheus histogram showed 168 windows within seconds of start-up. The loop now stops after the first window that is not capped.
- **Cross-surface deduplication.** It hid feed impressions of cards already seen in the picks rail and inflated feed CTR at those positions. Impressions are now deduplicated per surface.

Demo data: `--seed-demo` created 6 owners, 64 events and 30,497 simulated interactions from 712 visitors over 30 days. It loads them with binary `COPY` into the monthly partitions, below the rollup checkpoint, so the worker never counts them twice. Feed CTR by position in the simulated data is confounded by event appeal (position 2 above position 1), which is the reason phase 14 needs a proper propensity estimator.

Screenshots:
- `docs/screenshots/phase-13-owner-analytics-d.png` and `docs/screenshots/phase-13-owner-analytics-m.png`
- `docs/screenshots/phase-13-chart-tooltip-d.png`
- `docs/screenshots/phase-13-admin-anomalies-d.png`
- `docs/screenshots/phase-13-privacy-footer-m.png`

## Acceptance

- [x] First-party, anonymous tracking with batching, deduplication, bot filtering, Global Privacy Control and "reset my history"
- [x] Monthly partitioned raw table with O(1) retention
- [x] Exactly-once hourly and daily rollups
- [x] Self-implemented HyperLogLog for unique visitors, accurate within its error bounds, with union semantics across days and events
- [x] Forward-decayed popularity with a 6-hour half-life
- [x] EWMA z-score spike detection: "taking off" for owners, surge or suspicious for administrators
- [x] Owner analytics page following the dataviz rules: KPI tiles, line chart with crosshair tooltip and table twin, funnel, comparison table
- [x] Prometheus metrics and a provisioned Grafana dashboard, not publicly exposed
- [x] Regression suites pass
