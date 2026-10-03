# Lighthouse evaluation

Front-end performance, accessibility, best practices and SEO of the public pages, measured with Lighthouse before and after two rounds of fixes. A Hebrew write-up of the same results is section 5.6 of the project book, which is kept outside this repository.

## Setup

- **Lighthouse 12.8.2 with Chromium 154 in a container** (`scripts/perf/lighthouse`). The container joins the app container's network namespace, so `http://localhost:10000` inside it is the app itself and the Google Maps key restricted to localhost keeps working.
  - Why a container: the first runs, in Edge on the development machine, looked better than expected. The response headers showed that the machine's antivirus web protection decompressed every response before the browser saw it (`X-Content-Encoding-Over-Network`), so compression could not be measured there.
- **Form factors:**
  - Mobile: Lighthouse defaults (simulated slow 4G, 4× CPU slowdown).
  - Desktop: the desktop configuration.
- **A fresh browser per run**: empty cache and storage, like a first visit.
- **Pages:** home, an event, the places directory, a place, the map, sign-in.

## Builds

| Build | Commit | Change |
| --- | --- | --- |
| A | `a937da0` | End of phase 18. |
| B | `7d7c48d` | Brotli and gzip for html, css, js and svg; one-year immutable caching for hashed files and `no-cache` for `index.html`; the first place images load eagerly; `main` keeps a minimum height so the footer does not jump. |
| C | this commit | Fonts self-hosted instead of Google Fonts, and preloaded from `index.html`. |

## Method

- The host's CPU speed varied during the session. The Lighthouse benchmark index ranged from 628 to 1,454.
  - TBT and LCP on the mobile profile depend strongly on CPU speed.
  - One early run was also slowed by an `npm install` running at the same time (benchmark index 285).
- **Mobile runs were therefore interleaved** (`lighthouse-interleaved.ps1`, a local wrapper around the same container):
  - Builds A and B ran as side containers on the same database, with background workers off, next to C.
  - For every page and every repetition, the three builds were measured one after the other, so host drift affects all three alike.
  - Each value is the median of 3 runs.
  - `summarize-interleaved.mjs` computes the medians.
- **Desktop runs were sequential**: 3 runs per page and build, medians. They are far less sensitive to CPU speed.

## Results

### Mobile (interleaved, medians of 3; A → B → C)

| Page | Performance | FCP (s) | LCP (s) | TBT (ms) | Worst CLS |
| --- | --- | --- | --- | --- | --- |
| Home | 51 → 69 → 86 | 3.39 → 2.05 → 1.45 | 4.56 → 3.17 → 2.92 | 1462 → 1000 → 371 | 0.029 → 0.192 → 0 |
| Event | 53 → 69 → 69 | 3.46 → 1.91 → 1.51 | 4.53 → 3.03 → 3.03 | 1258 → 1161 → 1274 | 0.029 → 0.029 → 0 |
| Places | 62 → 71 → 77 | 3.74 → 2.43 → 1.79 | 4.80 → 3.59 → 3.72 | 480 → 577 → 436 | 0.043 → 0.040 → 0 |
| Place | 48 → 81 → 82 | 3.79 → 2.28 → 1.77 | 4.98 → 3.11 → 3.42 | 600 → 406 → 355 | 0.195 → 0.054 → 0 |
| Map | 28 → 41 → 48 | 3.60 → 2.58 → 1.88 | 7.12 → 7.05 → 6.28 | 1176 → 2879 → 1404 | 0.963 → 0.108 → 0.068 |
| Sign-in | 61 → 76 → 78 | 3.74 → 2.31 → 1.85 | 4.67 → 3.37 → 3.52 | 559 → 534 → 470 | 0.015 → 0.015 → 0.015 |

### Desktop (sequential, medians of 3; A → B → C)

| Page | Performance | FCP (s) | LCP (s) | CLS |
| --- | --- | --- | --- | --- |
| Home | 78 → 79 → 100 | 0.70 → 0.55 → 0.37 | 0.91 → 0.69 → 0.63 | 0.46 → 0.47 → 0 |
| Event | 99 → 100 → 100 | 0.70 → 0.48 → 0.36 | 0.89 → 0.62 → 0.63 | 0 → 0 → 0 |
| Places | 97 → 100 → 99 | 0.92 → 0.60 → 0.47 | 1.11 → 0.72 → 0.86 | 0.014 → 0.015 → 0.011 |
| Place | 99 → 99 → 100 | 0.78 → 0.61 → 0.44 | 0.95 → 0.82 → 0.74 | 0.017 → 0.017 → 0.011 |
| Map | 75 → 95 → 96 | 0.80 → 0.57 → 0.45 | 0.98 → 0.72 → 0.76 | 0.59 → 0.11 → 0.10 |
| Sign-in | 94 → 99 → 99 | 0.85 → 0.58 → 0.49 | 1.11 → 0.79 → 0.86 | 0.005 → 0.005 → 0.003 |

Accessibility, best practices and SEO scored 100 on every page, in every build and form factor.

## Findings

**Compression (A → B).**
- `main.js` fell from 384 KB to 116 KB on the wire (−70%).
- On mobile, where the network is the bottleneck, FCP fell by about 35–45% on every page, and LCP by about 1.5 s everywhere except the map.
- Caching does not change a first visit, which is what Lighthouse measures, but it removes the download on later visits.

**Fonts (B → C).**
- **The cause.** IBM Plex Sans Hebrew and Karantina came from Google Fonts. The browser requests a font only when text using it is rendered, and the app renders only after its JavaScript has run. So the fonts arrived after the first paint and swapped in, changing text widths and moving the layout.
  - Lighthouse attributed the shifts to "Web font loaded" on the `html`, `section.feed` and footer nodes.
  - The shift depends on timing: the same page scored CLS 0 in one run and 0.2 in the next, and the map reached 0.96.
- **The fix.**
  - The Hebrew and Latin subsets of the weights in use (12 files, about 140 KB) are served from `/fonts` with content-hashed names. The existing static caching makes them immutable for a year.
  - `index.html` preloads all twelve. The Latin subsets are needed too, because they hold the digits, spaces and punctuation of Hebrew text.
  - The fonts then arrive with the app bundle, and text is painted once in its final font.
- **The result.**
  - CLS is 0 on four of the six pages on mobile, and on every page that had a problem on desktop.
  - The desktop home page went from 79 to 100.
  - FCP fell by roughly another 25%, because no connection to the Google Fonts servers is needed before the first paint.
- **The cost.** The preloaded fonts compete with images for bandwidth. On pages whose largest element is an image, LCP rose by up to 0.3 s against B. This is accepted: in the performance score, CLS weighs 25% against FCP's 10%.

**What is left.**
- **The map page.** Most of its main-thread work and its LCP element (a static map tile) come from Google Maps.
  - A static preview, with the interactive map loaded on demand, would help.
  - The remaining 0.068 CLS comes from the list below the map.
- **TBT on mobile.** It is mostly Angular bootstrapping under the 4× CPU slowdown. Server-side rendering or further code splitting would be needed to go further.

## Reproduce

```powershell
docker build -t northlife-lighthouse scripts/perf/lighthouse
powershell -File scripts/perf/lighthouse/run.ps1 -Runs 3 -Tag current        # mobile and desktop against the running stack
# one page only, e.g. to repeat a run that a busy host disturbed:
docker run --rm --network container:northlife-app -e PAGES=event -v "${PWD}/docs/evaluation/lighthouse:/out" northlife-lighthouse mobile 3 current
node scripts/perf/lighthouse/summarize-interleaved.mjs docs/evaluation/lighthouse/interleaved mobile
```

The JSON summaries are kept in `docs/evaluation/lighthouse/`. The HTML reports are regenerated by the commands above and are not committed.
