# Phase 15 Verification: Recommendations ("For You")

## Goal and prerequisites

Personalised discovery with a measured evaluation:
- A "For you" rail on the home page.
- "More like this" on every event page.
- An offline evaluation that tunes and scores the recommender on simulated visitors with known tastes.

Builds on phase 13 (interactions, persona simulator) and phase 14 (list contexts, popularity).

## Files and components changed

- Backend, `Recommendations/`:
  - `HebrewText`:
    - Normalisation: niqqud, final letters, geresh and gershayim, maqaf, punctuation.
    - Stop words.
    - Prefix stripping guided by the corpus vocabulary.
  - `TfIdf`: `SparseVector` with a merge-join dot product; `TfIdfModel` weights the title ×2, tags ×1.5 and the description ×1, plus category and town facet tokens; sublinear TF, smoothed IDF, L2 normalisation.
  - `Collaborative`: item-item cosine over visitor engagement, accumulated per visitor and capped at 50 events per visitor.
  - `Recommender`:
    - `SimilarityBlender`: per-source max normalisation, significance shrinkage, blend α = γ / (γ + n) and a top-K heap.
    - `Recommender`: a decayed profile, candidate scoring, a percentile-rank popularity prior with an optimistic prior for unseen events, and MMR.
    - `RecommendationOptions`, with defaults set by the evaluation.
  - `RecommendationModelService` and `RecommendationModelCache`: rebuild the neighbours every 15 minutes in the worker and store them in `event_similarities`.
  - `RecommendationService`: "For you" within the feed's filters, and "More like this" with a same-category fallback.
  - `OfflineEvaluation` behind `--evaluate-recommendations`, which needs no database.
- Backend, elsewhere:
  - The `event_similarities` table (migration `AddRecommendations`).
  - `PublicEventQueryService.EligibleIdsAsync` and `SummariesAsync`, so recommendations use exactly the feed's filters.
  - `RecommendationsController`: `GET /api/recommendations` and `GET /api/events/{id}/similar`.
- Frontend:
  - `recommendations/recommendations-api.ts`.
  - `ForYouRail`: cards carry their reason ("כי פתחתם את ״…״"). The rail shows only for a visitor with history.
  - `SimilarEvents` on the event page.
  - Both are tracked as their own surfaces (`Recommendations`, `Similar`), with position and context.
- Docs and tooling:
  - `docs/evaluation/recommendations.md`, the generated report.
  - `scripts/e2e/phase-15-recommendations.ps1`.
  - CI checks.

## Behaviour and API

| Request | Behaviour |
| --- | --- |
| `GET /api/recommendations?visitorId&limit[&period&from&to&category&locality&maxPrice]` | Returns `{ personalised, items: [{ event, reason: similar or popular, becauseOfEventId, becauseOfTitle }] }`. Personal as soon as the visitor has opened something, since the profile is read from raw interactions with no rollup wait. Without a period it covers everything coming up. |
| `GET /api/events/{id}/similar?limit` | The event's stored neighbours that are still upcoming, falling back to the same category. |

The rail never shows events the visitor has already opened. "Reset my history" returns the visitor to a cold start.

## Algorithms

| Step | Method | Cost |
| --- | --- | --- |
| Text | Normalise, tokenise, drop stop words. Strip up to two of ו, ה, ב, ל, מ, ש, כ only if the remainder occurs on its own in the corpus: "והופעה" becomes "הופעה", while "מוזיקה" stays intact because "וזיקה" never occurs alone. | O(tokens) |
| Content | TF-IDF with sublinear TF and smoothed IDF; cosine is a dot product of unit vectors. | O(tokens) to build |
| Behaviour | Item-item cosine over engagement weights (open 3, share 4, navigate 5), then shrunk by n / (n + 5). | O(Σ_u \|I_u\|²) |
| Blend | Normalise both signals per source, then sim = α·text + (1 − α)·behaviour with α = γ / (γ + n). New events have no shared visitors, so they fall back to text; top 20 per event. | O(\|S\|·\|T\| log 20) |
| Profile | Σ weight · e^(−λΔt) with a 3-day half-life. | O(history) |
| Score | (1 − μ)·personal / max + μ·popularity percentile. An unseen event takes the 90th percentile ("optimism under uncertainty"). | O(profile·K) |
| Diversity | MMR with λ = 0.8. | O(k²·pool) |

## Offline evaluation

The full report is `docs/evaluation/recommendations.md`, reproducible with `dotnet NorthLife.Api.dll --evaluate-recommendations`.

Setup:
- 192 events and 1,500 simulated visitors, split in time: 24 days to train, 6 to test.
- 252 test users; the relevant items are events they opened for the first time in the test window.
- The hyperparameters γ, μ and the new-event prior were **tuned on validation seed 7 and reported on test seed 2026**.
- The tuning objective weighs known and new events equally, because every event starts cold.

Test results:

| Method | P@5 | NDCG@10 | Hit@10 | NDCG@10, new events | Diversity |
| --- | --- | --- | --- | --- | --- |
| Random | 0.010 | 0.043 | 0.095 | — | 0.966 |
| Popularity | 0.036 | 0.121 | 0.302 | 0.000 | 0.975 |
| Content only | 0.059 | 0.179 | 0.401 | 0.119 | 0.943 |
| Collaborative only | 0.063 | 0.186 | 0.440 | **0.000** | 0.955 |
| **Blend (γ 25, μ 0.6)** | **0.063** | **0.186** | 0.409 | **0.117** | 0.943 |
| Blend + MMR (λ 0.8), shipped | 0.061 | 0.181 | 0.389 | 0.097 | **0.972** |
| Oracle (true relevance) | 0.076 | 0.243 | 0.484 | 0.237 | 0.896 |

Reading the results:
- The blend is the only method near the best on both known events (77% of the oracle's NDCG) and new events (49%).
- Behaviour alone cannot recommend a new event at all.
- Text alone is weaker once behaviour exists.
- MMR raises within-list diversity from 0.943 to 0.972 at a small accuracy cost; the product ships it because a rail of near-duplicates is a poor rail.

## Verification

```powershell
dotnet test NorthLife.slnx --configuration Release           # 184 passed
npm --prefix frontend test -- --watch=false                  # 66 passed
npm --prefix frontend run build                              # initial 392.13 kB
powershell -File scripts/e2e/phase-15-recommendations.ps1
docker run --rm northlife:local --evaluate-recommendations /tmp/evaluation.md
```

Unit tests (20 new backend):
- Hebrew normalisation cases: niqqud, final letters, gershayim, maqaf, punctuation, Latin case.
- Stop words are dropped.
- Vocabulary-guided stemming, including the "מוזיקה" non-split and the minimum stem length.
- TF-IDF:
  - Identical texts score 1 and vectors have unit length.
  - A rare shared word outweighs a common one.
  - Category and town act as features, and unrelated events score 0.
- Collaborative cosine and co-visitor counts; blend limits (no shared visitors gives text only; many gives behaviour).
- Profile weights and half-life decay.
- Recommendation behaviour:
  - Picks come with reasons and skip what the visitor has seen.
  - A visitor with no history gets the popular order.
  - MMR swaps a near-duplicate for variety.
  - The optimistic prior lets a new event compete.
  - Percentile ranks share their ties.
- Metrics:
  - Precision, recall and NDCG match hand-computed values.
  - An evaluation smoke run ranks random below the blend below the oracle, with collaborative-only at 0 on new events.

Frontend tests (2 new): the "For you" rail shows only a personalised list with its reason text, and requests carry the period and the visitor id.

End-to-end on the Docker stack with the demo seed: **17 of 17 checks passed**.
- Model: neighbours are stored for all 80 upcoming events, at most 20 each, and no event is its own neighbour.
- Cold start: a new visitor gets 8 popular picks marked `popular`.
- Personalisation:
  - After opening 3 music events the list is personal at once, 63% music or nightlife, with reasons naming the opened events and without the opened events.
  - After opening and navigating to 4 outdoor events, outdoor and sport picks rise from **0% to 63%**.
  - With `period=tomorrow`, every pick is in tomorrow's feed.
- More like this:
  - Six neighbours, all music or nightlife, never the event itself.
  - An unknown event returns an empty list.
- Forget: "reset my history" returns 204 and the visitor is a cold start again.
- Offline evaluation: it runs in the production image with no database, and the blend's P@5 of 0.063 beats random's 0.010.

Fresh database: the production image migrated an empty database cleanly. `event_similarities` has its primary key and the two indexes, and EF reported no pending model changes.

Found and fixed during verification:
- **Scale mismatch.** The first blend scored below both of its parts (NDCG 0.082). Text cosines run higher than behavioural ones, so text filled every neighbour list. Each signal is now max-normalised per source before blending.
- **Uninformative median.** With a 6-hour half-life, decayed popularity at the split is heavy-tailed; the median was about 0, so a "median prior" for new events did nothing. Popularity is now a percentile rank.
- **Warm-only tuning.** Tuning for warm accuracy alone chose μ = 0.8, which buried every new event (cold-start recall 0). The tuner now weighs known and new events equally, and new events get an optimistic prior.
- **Harness assumption.** The first end-to-end filter check assumed "tomorrow" means start date. The feed counts events that overlap tomorrow, so the check now compares with the feed itself.

Screenshots:
- `docs/screenshots/phase-15-for-you-d.png` and `docs/screenshots/phase-15-for-you-m.png`
- `docs/screenshots/phase-15-more-like-this-d.png`

## Acceptance

- [x] Hebrew text pipeline (normalisation, stop words, corpus-guided prefix stripping)
- [x] TF-IDF content similarity and item-item collaborative filtering, blended by the amount of shared evidence
- [x] Decayed visitor profile, popularity prior with optimistic cold start, MMR diversification, explained picks
- [x] "For you" rail and "More like this", tracked as their own surfaces
- [x] Offline evaluation with a time split, baselines, precision, recall, NDCG, coverage and diversity, tuned on a separate seed, including item cold start
- [x] Regression suites pass
