# Recommendations: "For you" and "More like this"

Personal event suggestions from an anonymous browsing history, plus similar events on every event page, with an offline evaluation that tunes and scores the recommender. Introduced in phase 15.

## What a visitor sees

- **"בשבילך" (For you)** on the home page, once the visitor has opened a few events.
  - Each card says why it is there: "כי פתחתם את ״ערב ג׳אז על הגג״" ("because you opened …").
  - The rail respects the feed's current period and filters, never repeats an event the visitor already opened, and changes as soon as they open something new.
  - "Reset my history" in the footer makes it disappear again.
- **"עוד אירועים כאלה" (More like this)** under every event: its six nearest upcoming neighbours.

Nothing here needs an account. It uses the same anonymous visitor id as analytics ([analytics.md](analytics.md#privacy)).

## Pipeline

```
event text ──▶ Hebrew pipeline ──▶ TF-IDF vectors ─┐
                                                   ├─▶ blended similarity ─▶ top-20 neighbours
visitor engagement ─▶ item-item cosine ────────────┘       (event_similarities, every 15 min)
                                                                        │
visitor's recent history ─▶ decayed profile ─▶ candidate scores ─▶ + popularity prior ─▶ MMR ─▶ "For you"
```

## Text: the Hebrew pipeline

`Recommendations/HebrewText.cs`:

1. **Normalise:**
   - Remove niqqud and cantillation marks.
   - Write final letters in their regular form (ך→כ, ם→מ, ן→נ, ף→פ, ץ→צ), so inflected forms share a stem.
   - Drop geresh and gershayim ("ג׳אז" → "גאז").
   - Split on maqaf, turn other punctuation into spaces, and lower-case Latin letters.
2. **Tokenise** on anything that is not a letter or digit, and drop one-letter tokens and **stop words**: function words, plus the boilerplate of event descriptions such as "מומלץ להגיע".
3. **Strip attached prefixes** (ו, ה, ב, ל, מ, ש, כ, up to two), **but only when the remainder occurs on its own in the corpus**. A fixed rule breaks words that merely begin with those letters:
   - "והופעה" becomes "הופעה".
   - "בגליל" becomes "גליל".
   - "מוזיקה" stays intact, because "וזיקה" never occurs alone.

   This dictionary-guided light stemming needs no Hebrew lexicon.

## Content similarity: TF-IDF

`Recommendations/TfIdf.cs` (Salton and Buckley 1988):
- **Terms:** each event is a bag of weighted terms: title ×2, tags ×1.5, description ×1, plus facet tokens for its category (`#cat:Music`) and town (`#loc:צפת`).
- **Weight:** (1 + ln tf) · (ln((N + 1) / (df + 1)) + 1), sublinear TF with smoothed IDF.
  - A word in almost every description gets an IDF near 1 and barely matters.
  - A rare, specific word ("ג׳אז", "קיאקים") drives similarity.
- **Similarity:** vectors are L2-normalised, so cosine similarity is a dot product. It is computed by merging the two sorted sparse index lists, in O(|a| + |b|).

## Behavioural similarity: item-item collaborative filtering

`Recommendations/Collaborative.cs` (Sarwar et al. 2001; Linden, Smith and York 2003):
- **Vectors:** each event is a vector over visitors, whose entries are engagement weights from the last 30 days (open 3, share 4, navigate 5; impressions are ignored).
- **Similarity:** the cosine of two such vectors: "people who opened this also opened that".
- **Accumulation:** visitor by visitor, touching only the pairs a visitor actually shares, in O(Σ_u |I_u|²). Each visitor is capped at their 50 strongest events, which also blunts automated traffic.
- **Significance shrinkage:** a similarity from n shared visitors is multiplied by n / (n + β) with β = 5, so two events that happen to share one visitor do not look identical.

## Blending

```
α_ij = γ / (γ + n_ij)          sim(i, j) = α · text(i, j) + (1 − α) · behaviour(i, j)
```

- **New events have no shared visitors** (n = 0, so α = 1) and are matched on their text: content-based cold start.
- As visitors engage, behaviour takes over. γ = 25 was chosen by the evaluation below.
- The two signals live on different scales (text cosines run higher than behavioural ones), so each is divided by its largest value for the source event before blending.
  - Without this, the first version's blend was worse than either of its parts, because text alone filled every neighbour list.
- The worker keeps the top 20 neighbours of every upcoming event, and of every event engaged with in the last 30 days, using a bounded heap. They are stored in `event_similarities` every 15 minutes.

## Scoring a visitor

1. **Profile:**
   - Every event the visitor engaged with in the last 30 days, weighted by engagement and decayed with a 3-day half-life.
   - It is read straight from the raw interactions, so a new click counts at once without waiting for a rollup.
2. **Candidates:** the neighbours of the profile's events, plus the 50 most popular events. Only events that match the feed's filters, and that the visitor has not opened, are kept.
3. **Score:** (1 − μ) · personal / max personal + μ · popularity.
   - personal(j) = Σ_i profile(i) · sim(i, j).
   - The profile event contributing most becomes the explanation.
   - popularity is a **percentile rank** of decayed popularity. Decayed popularity is heavy-tailed, so on a value scale most events sit near zero.
   - μ = 0.6.
4. **New events:** an event with no popularity yet gets the 90th percentile, *optimism in the face of uncertainty* as in UCB bandits. It competes on its content instead of being buried before anyone could see it.
5. **Diversity:** re-rank with **maximal marginal relevance** (Carbonell and Goldstein 1998). Each next pick maximises λ · score − (1 − λ) · (its highest content similarity to the picks so far), with λ = 0.8, so five near-identical jazz nights do not crowd out everything else.
6. **No history:** a visitor with no history gets the popular order. The API reports `personalised: false` and the rail stays hidden.

"More like this" is simply the event's stored neighbours that are still upcoming, falling back to upcoming events of the same category.

## Offline evaluation

Real traffic is too small to evaluate a recommender on, so the evaluation uses the persona simulator. Its visitors have **known tastes**, which also yields an oracle upper bound. The full report is [docs/evaluation/recommendations.md](../evaluation/recommendations.md); regenerate it with `dotnet NorthLife.Api.dll --evaluate-recommendations`, which needs no database.

Protocol:
- 192 events and 1,500 visitors, **time split**: models are fitted on the first 24 days and must predict what each visitor opens for the first time in the last 6.
- Metrics, averaged over the 252 test users:
  - Precision@k, recall@k and NDCG@k (binary gains).
  - Hit rate.
  - Catalogue coverage.
  - Within-list diversity (mean pairwise 1 − content cosine).
- **Hyperparameters were tuned on a different seed** (validation) than the one reported (test).
- **Item cold start**: a second experiment publishes a fifth of the events only at the split.
- **Objective**: the tuning objective weighs known and new events equally, because in an events product *every* event starts cold.

Results on the test seed:

| Method | P@5 | NDCG@10 | NDCG@10, new events | Diversity |
| --- | --- | --- | --- | --- |
| Random | 0.010 | 0.043 | — | 0.966 |
| Popularity | 0.036 | 0.121 | 0.000 | 0.975 |
| Content only (α = 1) | 0.059 | 0.179 | 0.119 | 0.943 |
| Collaborative only (α = 0) | 0.063 | 0.186 | **0.000** | 0.955 |
| **Blend** | **0.063** | **0.186** | **0.117** | 0.943 |
| Blend + MMR (shipped) | 0.061 | 0.181 | 0.097 | **0.972** |
| Oracle (true relevance) | 0.076 | 0.243 | 0.237 | 0.896 |

What the numbers show:
- The blend is the only method near the best on **both** known events (77% of the oracle's NDCG) and new ones (49%).
- Behaviour alone cannot recommend a new event at all.
- Text alone is weaker once behaviour exists.
- MMR buys noticeably more variety for a small accuracy cost; the product ships it.

Limitations, stated plainly:
- The personas are simulated, so absolute numbers describe the simulator, not real people. The value is in comparing methods under the same, known behaviour.
- Pairwise scoring is quadratic in the number of upcoming events. The model covers the 3,000 soonest events, far more than a two-week northern catalogue.

## API

| Request | Response |
| --- | --- |
| `GET /api/recommendations?visitorId&limit[&period&from&to&category&locality&maxPrice]` | `{ personalised, items: [{ event, reason: "similar" or "popular", becauseOfEventId, becauseOfTitle }] }` |
| `GET /api/events/{id}/similar?limit` | Event summaries. |

Both are anonymous and rate-limited like analytics.

## Configuration (`Recommendations__*`)

| Setting | Default | Meaning |
| --- | --- | --- |
| `Neighbours` | 20 | Neighbours kept per event. |
| `ShrinkageBeta` | 5 | Significance shrinkage constant. |
| `BlendGamma` | 25 | Blend constant γ. |
| `PopularityWeight` | 0.6 | Popularity prior μ. |
| `NewEventPrior` | 0.9 | Popularity percentile for unseen events. |
| `MmrLambda` | 0.8 | MMR trade-off. |
| `ProfileHalfLifeDays` | 3 | Profile decay. |
| `RefreshMinutes` | 15 | How often the similarity model is rebuilt. |

## Tests

- **Unit:**
  - Hebrew normalisation and vocabulary-guided stemming, including the "מוזיקה" case.
  - TF-IDF properties: identity, unit length, rare words outweighing common ones, facets.
  - Collaborative cosine and co-visitor counts; blend limits.
  - Profile decay, reasons, excluding seen events, cold start, MMR, the optimistic prior, percentile ties.
  - Precision, recall and NDCG against hand-computed values.
  - An evaluation smoke run.
- **End to end** (`scripts/e2e/phase-15-recommendations.ps1`, 17 checks):
  - Model coverage.
  - Cold start.
  - Personalisation right after interactions; the list shifting from music to outdoor events after new, stronger interest (0% to 63%).
  - Filter respect.
  - "More like this".
  - Forget resetting personalisation.
  - The evaluation running inside the production image.
