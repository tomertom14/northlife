# Phase 14 Verification: Smart Ranking and "Near Me"

## Goal and prerequisites

Better feed ordering and location-aware discovery:
- A "hot now" ranking whose popularity signal is corrected for position bias.
- An exact "near me" search.
- Map marker clustering.

Builds on phase 13's interaction data and persona simulator.

## Files and components changed

- Backend, `Ranking/`:
  - `Geohash`: encode, decode, neighbours, guaranteed radius, prefix bounds.
  - `NearestEvents`: exact k-nearest-neighbour search over geohash blocks.
  - `PositionBias`:
    - `PositionBiasEstimator`: the position-based click model fitted by EM, with context-specific appeal.
    - `IsotonicRegression`: Pool-Adjacent-Violators.
    - `PropensityTable`: clipped inverse-propensity weights.
  - `PositionBiasService`: estimate, store and load.
  - `PopularityWeights`: impressions weigh 0; feed clicks weigh 3 / θ_k.
  - `HotScore` with `RankingOptions`.
  - `Exploration`: randomised top-N, and a geohash prefix filter built as expression trees.
- Backend, elsewhere:
  - `Event.Geohash` (precision 9, `COLLATE "C"`, B-tree `ix_events_geohash`), kept in step by `AppDbContext.SaveChanges`.
  - `interactions.context_key`: a 32-bit FNV-1a hash of the list (filters and sort) an interaction came from.
  - Table `position_propensities`.
  - Migration `AddRanking`: column, index and table; a PL/pgSQL `geohash_encode` to backfill and cross-check; `context_key`.
  - `PublicEventQueryService`:
    - `sort=time|hot|near`, `latitude` and `longitude` validation.
    - The hot ranking is cached for 30 seconds per filter set and location.
    - Exploration is applied to a copy of the cached ranking.
  - `GET /api/admin/analytics/position-bias`.
  - `--seed-load N`, which adds events for performance tests.
  - The simulator now models list contexts (time, day, category, hot) and exploration.
- Frontend:
  - `FilterBar` gains a sort control (by time, hot now, near me) that spans a full row on phones.
  - `GeoLocationService` asks for the location only when "near me" is chosen and rounds it to about 100 m. The location is not stored.
  - Home page:
    - The sort is part of the shareable URL; the location is not.
    - A shared "near me" link asks the recipient for their own location.
  - `buildRankedList` shows hot and near lists flat, each row with its own day and time plus the distance.
  - Tracking sends the list context with impressions and clicks.
  - Map clustering: `clusterEvents`, Google MarkerClusterer's grid algorithm.
    - Clusters show a count badge and zoom in on click.
    - Clusters are rebuilt on every zoom change.
- Tooling:
  - `scripts/e2e/phase-14-ranking.ps1`.
  - `scripts/perf/feed-load.js`: a k6 script with three scenarios and p95 thresholds of 2 s.
  - Ranking checks in CI.

## Behaviour and API

| Request | Behaviour |
| --- | --- |
| `GET /api/events?...&sort=time` | Unchanged: start time, then id. |
| `GET /api/events?...&sort=hot[&latitude&longitude]` | Ranked by the hot score. With a location, distance counts and each item has `distanceKm`. On about 10% of first pages the top 8 are shuffled. |
| `GET /api/events?...&sort=near&latitude&longitude` | The k nearest events that match the filters, nearest first, with `distanceKm`. Pages continue outward. |
| `GET /api/admin/analytics/position-bias` | Per position: smoothed and raw θ_k / θ_1, the naive click-through ratio, impressions and clicks. |
| Invalid requests | 400 for an unknown sort, `near` without a location, only one of latitude or longitude, or out-of-range values. |

## Algorithms

| Algorithm | What it does | Cost |
| --- | --- | --- |
| Geohash (Niemeyer 2008) | Interleaves longitude and latitude bits in base 32; nearby points share prefixes. Stored at precision 9 (~5 m) with byte-order collation, so a prefix is a B-tree range. | O(precision) |
| Exact k nearest neighbours | Search the 3×3 block of cells around the visitor, measure candidates with haversine, and accept the k closest when all lie inside the block's guaranteed radius (one cell side, measured conservatively at the poleward edge). Otherwise drop one precision level (6 → 2) or stop at 150 km. | 9 range scans per level, O(log n + m) each |
| Position-based click model, fitted by EM | P(click) = θ_k · α_{q,e}. Appeal is per list and event (the "query" is the filter set), so audience differences between lists are not mistaken for position. | O(cells) per iteration |
| Randomised top-N exploration (Joachims et al. 2017) | On 10% of first "hot" pages the top 8 are shuffled. The same events then appear at different positions to the same audience, which identifies θ. | O(8) |
| Isotonic regression (PAVA) | Enforces that attention does not increase further down the page, pooling noisy estimates weighted by impressions. | O(n) |
| Inverse propensity weighting | A feed click at position k counts 3 · min(1/θ_k, 5) toward popularity. Impressions count 0, since exposure is not interest and would feed the hot sort into itself. | O(1) |
| Hot score | hot = 0.5·pop + 0.3·proximity + 0.15·distance + 0.05·editor, with pop = ln(1+p)/ln(1+p_max), proximity e^(−h/24) or 1 while running, and distance e^(−km/20). Without a location the weights are renormalised. | O(n log n); a 30 s cache per filter set |
| Grid clustering (MarkerClusterer) | In Web Mercator pixels at the current zoom, an event joins the first cluster whose ±60 px square around its anchor contains it. Events are visited in a fixed order, so the result is deterministic, and no split falls on an arbitrary grid line. | O(n · clusters) |

## Verification

```powershell
dotnet test NorthLife.slnx --configuration Release           # 164 passed
npm --prefix frontend test -- --watch=false                  # 64 passed
npm --prefix frontend run build                              # initial 384.31 kB
powershell -File scripts/e2e/phase-14-ranking.ps1 -AdminEmail <admin> -AdminPassword <password> -AdminTotpSecret <base32>
docker run --rm ... northlife:local --seed-load 10000
docker run --rm -i --add-host host.docker.internal:host-gateway -v "${PWD}/scripts/perf:/scripts" grafana/k6:1.3.0 run /scripts/feed-load.js
```

Unit tests (22 new backend):
- Geohash:
  - The reference vector `u4pruydqqvj` encodes correctly.
  - Decoded cells contain the point and shrink with precision.
  - The eight neighbours touch the cell.
  - Nearby points can straddle a cell edge yet stay inside the 3×3 block (Kiryat Shmona and Tel Hai at precision 4).
  - The prefix upper bound follows byte order, and invalid characters are rejected.
- Nearest events:
  - 60 random queries of k ≤ 20 over 800 points all equal brute force.
  - The maximum radius is respected, and dense areas finish at precision 6.
- Position bias:
  - The recovered attention curve is compared with the simulator's truth k^−0.6 on 2,500 simulated visitors:

    | Position | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
    | --- | --- | --- | --- | --- | --- | --- | --- |
    | Truth | 0.660 | 0.517 | 0.435 | 0.381 | 0.341 | 0.311 | 0.287 |
    | EM | 0.594 | 0.486 | 0.445 | 0.361 | 0.362 | 0.307 | 0.284 |
    | Naive | 0.609 | 0.475 | 0.384 | 0.321 | 0.314 | 0.279 | 0.278 |

  - Mean absolute error is **0.019 for EM** against 0.034 for the naive ratio.
  - A textbook two-event case gives exactly θ₂ = 0.5.
  - PAVA pools violations by weight, and smoothed propensities never increase with position.
  - Inverse weights are clipped at 5.
- Hot score:
  - A running event gets full proximity.
  - Recent engagement beats older engagement of the same size.
  - Distance counts only with a location, and scores stay within [0, 1].
  - Editors' picks get a small boost.

Frontend tests (10 new):
- Clustering:
  - The Mercator projection matches known values.
  - Every event lands in exactly one cluster at any zoom.
  - Towns merge when zoomed out and split when zoomed in.
  - Events at the same spot stay together, and the centroid lies within the bounds.
- The ranked list keeps the ranking order, marks live rows and shows distance notes.
- The home page sends `sort=hot` without a location, and a near-me link with the rounded location once it is known.

End-to-end on the Docker stack with the demo seed: **19 of 19 checks passed**.
- Geohash:
  - The C# and PL/pgSQL encodings agree for every event.
  - A prefix query uses `ix_events_geohash`, per `EXPLAIN` with sequential scans disabled.
- Near me:
  - The 12 nearest equal a brute-force haversine ranking of all 75 events.
  - Page 2 continues outward without repeating page 1.
  - The three invalid-request cases get 400.
- Hot:
  - Pages come back full, with distances only when a location is given.
  - Exploration shuffled 14 of 100 first pages, against a configured 10%.
- Position bias:
  - The model covers 12 positions, starts at 1 and never increases.
  - Positions 2 to 4 are within 0.032 of the truth on the demo month.
  - A business owner gets 403.
- Context and weighting:
  - The list context is stored as its FNV-1a hash.
  - A feed click at position 8 raised popularity by exactly ln(1/θ₈) = 1.560 more than a click at position 1.
  - An impression alone added no popularity.

Demo month, fitted by the worker on 77,340 simulated interactions:

| Position | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Smoothed | 1.000 | 0.596 | 0.487 | 0.434 | 0.291 | 0.218 | 0.210 | 0.210 |
| Truth | 1.000 | 0.660 | 0.517 | 0.435 | 0.381 | 0.341 | 0.311 | 0.287 |

Beyond position 4 the demo estimates run low. Only the top 8 are ever randomised and the demo sample is small, so deeper positions rest on fewer comparisons within the same list.

Performance (k6, 20 virtual users per scenario, 10,079 upcoming events, 14-day range):

| Scenario | p95 before cache | p95 with 30 s hot cache | Target |
| --- | --- | --- | --- |
| By time | 189 ms | 340 ms (cold start) | < 2 s |
| Hot now | 1.26 s | 300 ms | < 2 s |
| Near me | 426 ms | 539 ms | < 2 s |

There were no failed requests across 8,690 and 8,143 requests. The load events were removed afterwards and the demo was re-seeded.

Fresh database: the production image migrated an empty database cleanly. `position_propensities.raw_propensity` exists, `events.geohash` uses the `C` collation, `interactions.context_key` exists, and EF reported no pending model changes.

Regression: the phase 13 end-to-end suite passes after the ingestion changes.

Found and fixed during verification:
- **Audience confounding.** The first estimator fitted one appeal per event. Category lists are read by fans, so the same event draws more clicks there regardless of position. EM scored worse than the naive ratio (error 0.092 against 0.037). Two changes fixed it: appeal per (list, event), as click models do per (query, document), and randomised top-N exploration on the hot feed. Error fell to 0.019.
- **Mobile sort control.** On phones the new control sat in one cell of the two-column grid, under the locality field, which intercepted taps. It now spans the row.
- **Grid edges.** Fixed grid cells split two towns 3 km apart whenever a cell edge ran between them. Clustering now uses MarkerClusterer's anchored squares.
- **Test-harness bug.** In the end-to-end script, PowerShell resolved `[Math]::Min(1, x)` to the integer overload, which made every brute-force distance 0. The kNN was correct; the harness now uses `1.0`.

Screenshots:
- `docs/screenshots/phase-14-hot-d.png`
- `docs/screenshots/phase-14-near-m.png` and `docs/screenshots/phase-14-near-d.png`
- `docs/screenshots/phase-14-sort-control-m.png`

Map clustering is covered by unit tests. Seeing the clusters needs a Google Maps key and Map ID (phase 16).

## Acceptance

- [x] Hot score with configurable weights and inverse-propensity-weighted popularity
- [x] Position bias estimated by EM with context-specific appeal and randomised top-N exploration, then smoothed by isotonic regression and validated against simulated ground truth
- [x] Geohash column, backfill and index; exact k-nearest "near me" matching brute force
- [x] Sort options in the filter bar, shareable in the URL, with the location asked for only when needed
- [x] Grid clustering of map markers with count badges and click-to-zoom (visual check pending a Maps key)
- [x] Feed p95 under 2 s for all sorts with 10,000 events
- [x] Regression suites pass
- [ ] Map clusters verified in a browser with a Google Maps key (phase 16)
