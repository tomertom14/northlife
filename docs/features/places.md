# Places: business and venue pages

Permanent places (restaurants, cafés, bars, studios, attractions and local services) get their own public pages next to the events. The extended proposal promised them ("מסעדות, חוגים, מקומות בילוי ושירותים מקומיים", a business profile and extended business details); they were added in phase 18.

## What a visitor sees

- **"מקומות" in the header** opens the directory `/places`:
  - category chips, a town field, free-text search, "פתוח עכשיו" and a sort by name or "קרוב אליי";
  - each card shows the type, the town, an open or closed line with the next change ("סגור עכשיו · נפתח ב-20:00") and a student-perk badge;
  - the filters live in the URL, so a filtered list can be shared.
- **A place page** `/places/:id`:
  - the open status, the weekly hours with today in bold, the student perk ("הטבה לסטודנטים"), the description;
  - Waze, Google Maps, phone, website and Instagram buttons;
  - the upcoming events that happen at the place.
- **The map** has a second layer, "מקומות", clustered like the events.
- **An event page** links to its place ("לדף המקום") when the owner linked the event to one of their places.

## What owners and administrators see

- **Owners**: "המקומות שלי" (`/manage/places`) lists their places with status and rejection reason. The form has a weekly hours editor (up to three intervals a day, a shortcut that copies Sunday to Monday–Thursday) and the same image upload and map picker as events. In the event form, "אחד המקומות שלכם" fills the venue, town, address and pin and links the event to the place.
- **Administrators**: a "מקומות" tab (`/manage/admin/places`) with the pending queue first come, first served, full details, approve, reject with a reason the owner sees, and delete. Every action goes to the audit log (`place.approved`, `place.rejected`, `place.deleted`).
- **The same rules as events**: an owner must have a verified email; a new or edited place waits for approval; every change checks the revision the caller saw (a stale approval gets 409); places of a suspended owner disappear from every public view, including their images.

## Opening hours

### Representation

`place_opening_hours` holds one row per interval: day of week (0 = Sunday), opening minute and closing minute from midnight, Israel local time.

- A closing minute **at or before** the opening minute means the interval ends on the next day: 20:00–02:00 is a late bar.
- **00:00–00:00** means open for the whole day; seven such rows mean open around the clock.
- A day without rows is closed.

Wall-clock minutes make daylight saving time a non-issue: the instant is converted to Israel local time first (`OpeningHours.ToLocal`), and the hours are compared on the wall clock. On the night the clocks go back, 01:30 happens twice and both count as 01:30.

### "Open now"

At local time (d, t), a place is open when

- an interval of day d has opens ≤ t and either t < closes or closes ≤ opens (it runs past midnight), or
- an interval of day d − 1 runs past midnight (closes ≤ opens) and t < closes.

The rule exists three times, and tests hold them together:

| Form | Where | Used for |
| --- | --- | --- |
| `OpeningHours.IsOpen` | C#, per interval, O(k) | unit tests, the map layer |
| `OpeningHours.OpenAt` | the same rule as an expression tree; EF Core translates it to an `EXISTS` subquery | the "פתוח עכשיו" filter, which therefore composes with every other filter, paging and the geohash nearest-neighbour search |
| `OpeningHours.Status` | merged weekly ranges | the next opening or closing |

A randomised test draws 400 weekly schedules (split days, overnight intervals, 24-hour days) and checks all three at 25 random moments each; another checks that the state flips exactly at the reported next change. End to end, the SQL filter's count equals the number of places whose C# status is open.

### Next opening or closing

Each interval becomes a range on a circular week of 10,080 minutes: `start = day · 1440 + opens`, `length = closes − opens`, or `1440 − opens + closes` when it runs past midnight. A range that passes the end of Saturday is split and continues on Sunday morning. Sorting and sweeping merges overlapping or touching ranges (Monday 20:00–02:00 and Tuesday 01:00–04:00 become one range), in O(k log k).

- Inside a range: the place is open and closes at the range's end. A range that ends at the week boundary continues into the one that starts at minute 0.
- Otherwise: it opens at the next range start, or at the first start of next week.
- One range covering the whole week: always open, no next change.

The API returns the change as a day of week, a minute and "days ahead"; the browser writes "נסגר ב-23:00", "נסגר בחצות", "נפתח מחר ב-08:00" or "נפתח ביום שלישי ב-10:00".

### Validation

The server and the form apply the same checks: minutes within 00:00–23:59, no zero-length interval except 00:00–00:00, at most three intervals a day, and no overlap between intervals of the same day (sorted by opening time, each must start at or after the previous one ends, counting past midnight).

## "Near me" for places

The Phase 14 exact k-nearest-neighbour search (`NearestEvents.FindAsync<T>`) is reused unchanged: places have their own geohash column (precision 9, byte-order collation, B-tree index), kept in step on every save by `AppDbContext`, and `GeohashFilter.StartsWithAny<T>` builds the nine range scans for any entity. Because "open now" is part of the query, "open now, nearest first" is exact too. The end-to-end check compares the result with a brute-force haversine ranking of every public place.

## Data model

| Table | Columns |
| --- | --- |
| `places` | id, owner_id, name, category (`Food`, `Cafe`, `Nightlife`, `Classes`, `Sports`, `Culture`, `Outdoors`, `Services`), description, locality, address, latitude, longitude, geohash, phone, website, instagram, student_perk, image_id, status, rejection_reason, created/updated/deleted times, revision |
| `place_opening_hours` | place_id, day_of_week, opens_minute, closes_minute; primary key (place_id, day_of_week, opens_minute) |
| `events.place_id` | optional link to one of the owner's places |

Indexes cover the directory filters (status with category or locality, then name), the geohash, the owner's list and the image. Updating a place's hours syncs the rows by their key (kept, updated, removed, added) rather than deleting and re-adding, which would clash in the change tracker.

Images are shared with events: the public image rule also accepts an image of a published place of an active owner, and the nightly orphan cleanup keeps images referenced by places, including soft-deleted ones.

## API

| Request | Notes |
| --- | --- |
| `GET /api/places?category&locality&openNow&q&sort=name\|near&latitude&longitude&page&pageSize` | Paged summaries with open status and, with a location, `distanceKm`. 400 `invalid_place_query` for a bad sort, `near` without a location, only one coordinate, `pageSize` over 50 or a search over 100 characters. |
| `GET /api/places/{id}` | Details, weekly hours, open status and up to 10 upcoming events. 404 `place_unavailable` when not public. |
| `GET /api/places/map?north&south&east&west&category&openNow` | Up to 500 places with coordinates and `isOpen`. |
| `GET/POST /api/manage/places`, `GET/PUT/DELETE /api/manage/places/{id}` | Business owners; create and update need a verified email; 400 `invalid_place` with field errors, 409 `revision_conflict`. |
| `GET /api/admin/places?status&search`, `POST /api/admin/places/{id}/approve\|reject`, `DELETE /api/admin/places/{id}` | Administrators with TOTP. |
| `POST/PUT /api/manage/events` | Optional `placeId`, only one of the owner's own places (400 `placeId` otherwise). |
| `GET /api/events/{id}` | `place: { id, name }` while the place is public. |

## Demo data

`--seed-demo` adds 13 published places for the six demo businesses when they have none, so it also upgrades a database seeded before places existed. They include a café and a bar next to the Tel-Hai campus, a gym and a youth centre with student perks, a restaurant that opens on Saturday night after Shabbat, bars open until 03:00, a studio with split hours and two places open around the clock. Two upcoming events of each owner move to the matching place.

## Tests

- **Unit** (`PlaceAlgorithmTests`, 15): same-day boundaries, a late bar, Saturday night into Sunday, 24-hour days, always open, next change open and closed, merging, the randomised agreement of the three forms, the flip at the next change, Israel daylight saving time, hour validation and input validation.
- **API** (`PlaceEndpointTests`, 8): invalid directory queries, owner-only and admin-with-TOTP endpoints, an invalid place rejected before anything is saved.
- **Frontend** (7): status texts, weekly rows, time parsing, and the form's hour checks.
- **End to end** (`scripts/e2e/phase-18-places.ps1`, 31 checks): verification gate, validation, pending not public (page, list, image), ownership, reject with reason, edit back to review, stale approval 409, approve, audit, public page and image, open-now today and next opening in three days, SQL and C# agreement across all places, combined filters, near me against brute force, map layer, event links both ways, suspension hiding and restoring, cleanup.
