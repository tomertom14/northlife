# Smart ranking: "hot now", position-bias correction, "near me" and map clustering

The feed can be ordered by time (the default), by what is **hot now**, or by what is **near me**, and the map clusters markers by zoom level. Introduced in phase 14.

## What a visitor sees

- A sort control above the feed: "לפי שעה" (by time), "הכי חם עכשיו" (hot now) and "קרוב אליי" (near me).
  - The choice is part of the URL, so a shared link keeps it.
  - The visitor's location is never put in the URL.
- **Hot** and **near me** lists are shown flat: they are rankings, not a timetable. Each row carries its own day and time, and near-me rows show the distance ("350 מ׳ ממך", "3.2 ק״מ ממך").
- **Near me** asks for the browser's location only when chosen.
  - The coordinates are rounded to about 100 m before they leave the browser, kept for the visit only, and never stored on the server.
  - A shared near-me link asks the recipient for their own location.
- On the **map**, nearby events merge into numbered circles; clicking one zooms in until it splits. Events at exactly the same place open as a list.

## How "hot now" works

### Hot score

```
hot = 0.50 · popularity + 0.30 · proximity + 0.15 · distance + 0.05 · editor
```

| Term | Definition |
| --- | --- |
| popularity | ln(1 + pop) / ln(1 + pop_max). pop is the forward-decayed popularity at this moment ([analytics.md](analytics.md#forward-decayed-popularity)) and pop_max the largest among the candidates. |
| proximity | 1 while the event is running, otherwise e^(−hours until start / 24). |
| distance | e^(−km / 20), only when the visitor shared a location. |
| editor | 1 for editors' picks. |

- Without a location the distance weight is dropped and the others are rescaled to sum to 1.
- All weights and scales are configuration (`Ranking__*`).
- Scoring n candidates is O(n) and sorting O(n log n).
- The ranking for a filter set (and rounded location) is cached for 30 seconds. With 10,000 events the p95 dropped from 1.26 s to 0.30 s.

### Position bias and why it matters

An event at the top of the feed gets more clicks partly *because* it is at the top. If popularity counted raw clicks, whatever happened to be first would look popular, rank first under "hot", get more clicks, and so on.

Two measures prevent this:
- **Impressions add no popularity.** Being shown is not interest.
- **A feed click is divided by the attention its position gets**: weight 3 · min(1 / θ_k, 5). This is inverse propensity weighting (IPW); capping the multiplier 1 / θ_k at 5 bounds the variance a rarely seen position can add.

The worker re-estimates θ_k every six hours from the last 30 days of traffic.

#### The click model

The position-based model (Craswell et al. 2008; Chuklin, Markov and de Rijke 2015):

```
P(click | event e at position k in list q) = θ_k · α_{q,e}
```

- θ_k is the chance the visitor examines position k.
- α_{q,e} is the chance they open e once they notice it in list q.

Both are hidden, so they are fitted by **expectation–maximisation** over the logged (list, event, position) impression and click counts:

```
θ_k      ← Σ [c + (n − c) · θ_k(1 − α) / (1 − θ_k α)] / Σ n
α_{q,e}  ← Σ [c + (n − c) · α(1 − θ_k) / (1 − θ_k α)] / Σ n
```

Two design choices make the estimate trustworthy:

1. **Appeal per list, not per event.** Different lists have different audiences: fans read a category-filtered list, so the same event draws more clicks there regardless of position.
   - With one α per event, EM blamed that on position. On simulated data its error was *worse* than the naive click-through ratio (0.092 against 0.037).
   - Treating the list (its filters and sort, hashed as `interactions.context_key`) like the query in search click models fixes this.
2. **Randomised top-N exploration.** Within one list, the ranking is mostly the same every time, so θ cannot be separated from α.
   - On 10% of first "hot" pages, the top 8 events are shuffled (Joachims, Swaminathan and Schnabel 2017).
   - The same events then appear at different positions to the same audience, which identifies θ.
   - The cost is a slightly less sorted top of the page, now and then.

The estimates are finally smoothed by **isotonic regression** (the Pool-Adjacent-Violators algorithm, weighted by impressions), because attention cannot grow further down the page. The raw EM values are kept next to them for transparency.

#### Validation against ground truth

The persona simulator draws clicks with a known attention curve k^−0.6, so the estimator can be checked. On 2,500 simulated visitors:

| Position | 2 | 3 | 4 | 5 | 6 | 7 | 8 | Mean abs. error |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Truth | 0.660 | 0.517 | 0.435 | 0.381 | 0.341 | 0.311 | 0.287 | — |
| EM | 0.594 | 0.486 | 0.445 | 0.361 | 0.362 | 0.307 | 0.284 | **0.019** |
| Naive CTR ratio | 0.609 | 0.475 | 0.384 | 0.321 | 0.314 | 0.279 | 0.278 | 0.034 |

End to end, a feed click at position 8 raised an event's log-popularity by exactly ln(1/θ₈) more than a click at position 1.

## How "near me" works

### Geohash

A geohash (Niemeyer 2008) halves the world alternately by longitude and latitude and writes the bits five at a time in base 32. Nearby points share long prefixes.
- Every event stores a 9-character geohash (about 5 m) in a column with byte-order collation (`COLLATE "C"`) and a B-tree index. "All events in cell `sv9b`" is then a range scan: `geohash >= 'sv9b' AND geohash < 'sv9c'`.
- The application sets the geohash on every save.
- A PL/pgSQL implementation of the same algorithm backfilled existing rows. It is used to cross-check the C# one: they agree on every event.
- Cell sizes around the Galilee: precision 6 ≈ 1.0 × 0.6 km, 5 ≈ 4 × 5 km, 4 ≈ 33 × 20 km, 3 ≈ 131 × 156 km.

### Exact k nearest neighbours

A point near a cell edge can be closer to events in the next cell than to most of its own, so a single cell is never enough. Kiryat Shmona and Tel Hai, 3 km apart, fall in different precision-4 cells. The search:

1. Take the visitor's precision-6 cell **and its eight neighbours**.
2. Fetch the candidates in those nine cells (nine B-tree range scans, with the feed's other filters applied) and measure each exactly with the **haversine** formula.
3. Any event outside the 3×3 block is at least one cell side away, the *guaranteed radius*, computed conservatively at the block's poleward edge.
   - If at least k candidates lie within that radius, the k closest are the exact answer.
4. Otherwise drop one precision level (a larger block) and repeat, up to precision 2 or a 150 km radius.

Each level costs O(9 · log n + m). A randomised test compared 60 queries over 800 points with a brute-force ranking and they matched every time; so did the end-to-end check against all events in the database. Page 2 of near-me continues outward: k = page × page size, and the earlier rows are skipped.

## Map clustering

Grid-based clustering as in Google's MarkerClusterer:
- Events are projected to Web Mercator screen pixels at the current zoom: x = (lon + 180)/360 · 256 · 2^z, y from the Mercator formula.
- An event joins the first cluster whose ±60 px square around its anchor contains it; otherwise it starts a new cluster.
- Events are visited in a fixed order (north to south, west to east, then id), so the result is deterministic.
- Unlike fixed grid cells, two towns a few pixels apart are never split just because a cell edge runs between them. The first implementation used fixed cells and had exactly that artifact.
- Clusters show a count badge, sit at the centroid of their members and zoom to their bounding box on click.
- They are rebuilt on every zoom change, in O(n · clusters).

## API

| Request | Notes |
| --- | --- |
| `GET /api/events?…&sort=time\|hot\|near&latitude&longitude` | Items carry `distanceKm` when a location is given. Returns 400 for `near` without a location, only one of latitude or longitude, out-of-range values or an unknown sort. |
| `GET /api/admin/analytics/position-bias` | Per position: smoothed and raw θ_k / θ_1, the naive ratio, impressions and clicks. |

## Configuration (`Ranking__*`)

| Setting | Default | Meaning |
| --- | --- | --- |
| `PopularityWeight` | 0.5 | Hot-score weight of popularity. |
| `ProximityWeight` | 0.3 | Hot-score weight of start-time proximity. |
| `DistanceWeight` | 0.15 | Hot-score weight of distance. |
| `EditorWeight` | 0.05 | Hot-score weight of editors' picks. |
| `ProximityHours` | 24 | Start-time scale of the proximity term. |
| `DistanceKm` | 20 | Distance scale of the distance term. |
| `ExplorationRate` | 0.1 | Share of first hot pages shuffled. |
| `ExplorationDepth` | 8 | How many top positions are shuffled. |
| `NearMaxRadiusKm` | 150 | Near me looks no further than this. |

## Performance

k6 (`scripts/perf/feed-load.js`), 20 concurrent users per sort, 10,079 upcoming events, a 14-day window:

| Sort | p95 |
| --- | --- |
| By time | 189–340 ms |
| Near me | 426–539 ms |
| Hot now, with the 30 s cache | 300 ms |

All are well under the requirements' two seconds, with no failed requests.

## Tests

- **Unit:**
  - Geohash: the reference vector `u4pruydqqvj`, containment, neighbours, the edge case, byte-order bounds.
  - Nearest neighbours against brute force.
  - EM against the simulator's truth and a textbook case.
  - Isotonic regression, clipped inverse weights.
  - Hot-score properties.
  - Clustering invariants and the Mercator projection.
  - The ranked-list builder, and home-page sort handling.
- **End to end** (`scripts/e2e/phase-14-ranking.ps1`, 19 checks):
  - Geohash agreement and index use.
  - Near me against brute force, paging, validation.
  - Hot pages and the observed exploration rate.
  - The propensity model.
  - Context hashing, IPW credit, impressions adding no popularity.
