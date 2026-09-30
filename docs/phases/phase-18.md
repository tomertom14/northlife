# Phase 18 Verification: Places

## Goal and prerequisites

Public pages for permanent places (restaurants, cafés, bars, classes, attractions and local services) with weekly hours, "open now", the next opening or closing, student perks and upcoming events, as promised in the extended proposal. Owners manage their places; administrators approve them like events.

Phase 16 (deployment) is on hold for local QA and phase 17 (automatic event approval) was built in parallel, so this phase is numbered 18 and branches from the phase 17 commit, keeping the migrations in order. It builds on phase 6 (images), phase 7–8 (owner and admin lifecycle), phase 12 (suspension, audit) and phase 14 (geohash k-nearest neighbours, clustering). How it works in detail: [docs/features/places.md](../features/places.md).

## Files and components changed

- Backend, `Places/` (new):
  - `OpeningHours`: Israel wall-clock time, the "open now" rule in C# and as a query expression, the circular weekly ranges, the next change and validation.
  - `PlaceLifecycle`: submit, owner edit back to review, approve, reject with a reason, soft delete, all revision-checked.
  - `OwnerPlaceService` (with `PlaceInputValidator`), `AdminPlaceService`, `PlaceQueryService`, `DemoPlacesSeeder`.
- Backend, elsewhere:
  - `Models/Place.cs` (`Place`, `PlaceOpeningHours`, `PlaceCategory`); `Data/Configurations/PlaceConfiguration.cs`; migration `AddPlaces` (two tables, `events.place_id`).
  - `AppDbContext`: places' geohash kept in step on save.
  - `GeohashFilter.StartsWithAny<T>` for any entity with a geohash.
  - Events: optional `placeId` on owner requests (own places only), `place` on event details.
  - Images: public when used by a published place of an active owner; the orphan cleanup keeps place images.
  - `Controllers/PlacesControllers.cs`: public, owner and admin endpoints; `Program.cs` registrations and the seeder in `--seed-demo`.
  - `scripts/reset-demo-data.sql` removes demo places before their images and owners.
- Frontend:
  - `places/`: models, the API client, hour formatting and status texts.
  - Pages: `places-page` (directory), `place-details-page`, `owner-places-page`, `admin-places-page`; `manage/place-form` with the hours editor.
  - Header link "מקומות", admin tab "מקומות", "המקומות שלי" on the dashboard, the place picker in the event form, the place link on event pages, the places layer on the map (`MapRenderOptions.hrefFor` and `countLabel`), audit labels.
- Tooling and docs: `scripts/e2e/phase-18-places.ps1`, `docs/features/places.md`, this record, the local QA checklist, the README.

## Implemented behavior and API changes

| Request | Behaviour |
| --- | --- |
| `GET /api/places` | Published places of active owners; category, town, search and "open now" filters; sort by name or exact nearest first. |
| `GET /api/places/{id}` | Details, hours, open status with the next change, upcoming events at the place. |
| `GET /api/places/map` | Places inside the bounds with `isOpen`, for the map layer. |
| `/api/manage/places` | Owner CRUD; verified email required; edits return the place to review. |
| `/api/admin/places` | Review queue, approve, reject with reason, delete; audited. |
| `POST/PUT /api/manage/events` | Optional `placeId` (own places only). |
| `GET /api/events/{id}` | `place` link while the place is public. |

## Verification

Commands:

```powershell
dotnet test NorthLife.slnx
cd frontend; npm test -- --watch=false; npm run build
powershell -File scripts/local/start-stack.ps1 -Build -SeedDemo
powershell -File scripts/e2e/phase-18-places.ps1 -AdminEmail <admin> -AdminPassword <password> -AdminTotpSecret <base32>
```

Results:
- Backend: **311 passed**, of them 15 new algorithm tests and 8 new endpoint tests.
  - The randomised test checked 400 weekly schedules at 25 moments each: the per-interval rule, the merged ranges and the compiled query predicate always agreed.
  - At every reported next change the open state flips.
  - Israel daylight saving: 23:30 UTC on 26 March 2026 is 01:30 local, 00:30 UTC on 27 March is 03:30; both 01:30s of 25 October 2026 read as 01:30.
- Frontend: **89 passed** (7 new); production build initial bundle 396.42 kB, the new pages load on demand (places page 15.7 kB, place page 14.6 kB, owner page 35.1 kB).
- Migration: `AddPlaces` applied to the existing local database by `--migrate`; `--seed-demo` added 13 places to the already seeded demo.
- End to end on the Docker stack (production image, PostgreSQL 17, Mailpit): **31 of 31 checks passed**, including:
  - an unverified owner gets `403 email_not_verified`; overlapping hours get 400 on `hours`;
  - a pending place is missing from the page, the list and the image endpoint; another owner's edit gets 404;
  - reject with a reason, the owner sees it, the edit returns the place to review, a stale approval gets 409, approval publishes, and both actions are audited;
  - a place open all day today is open and closes at midnight; a place open only three days ahead opens then at 09:00;
  - the SQL "open now" filter returned exactly the open test place, and across all 15 public places its count (6) equalled the number whose C# status was open;
  - "near me" from Kiryat Shmona equalled a brute-force haversine ranking of all 15 places;
  - an event cannot link to another owner's place; a linked event shows the place, and the place lists the event;
  - suspending the owner hides the place and its event; lifting it restores them; deleting removes the place from the directory.
- Smoke check on the demo data at Wednesday 08:18 Israel time: the gym (06:00–23:00), the café (07:30–20:00), the kayak centre (08:00–17:00) and the two 24/7 places were open; the rest showed their next opening that day. "Near me" from Tel Hai returned the café (0.14 km), the rooftop bar (0.22 km) and the gym (0.29 km).

Screenshots:
- `docs/screenshots/phase-18-places-d.png`, `phase-18-places-m.png`: the directory.
- `docs/screenshots/phase-18-open-now-d.png`: "פתוח עכשיו".
- `docs/screenshots/phase-18-place-d.png`, `phase-18-place-m.png`: place pages with hours, perk and events.
- `docs/screenshots/phase-18-event-place-link-d.png`: an event linking to its place.
- `docs/screenshots/phase-18-map-places-d.png`: the places layer with clusters.
- `docs/screenshots/phase-18-owner-places-d.png`, `phase-18-place-form-d.png`: "המקומות שלי" and the hours editor.
- `docs/screenshots/phase-18-admin-places-d.png`: the review queue with a pending place.

## Findings during verification

- **Updating hours clashed in the change tracker.** Deleting every hour row and adding the new ones in the same save would track two entities with the same key (place, day, opening minute). The update now syncs rows by key: kept rows are updated, missing ones removed, new ones added.
- **The image cleanup would have deleted place images.** It deleted images not referenced by any event after 24 hours; it now also counts places, including soft-deleted ones.
- **The demo reset script would have failed** on the new foreign keys from places to images and owners; it now deletes demo places first.
- **Session loss in the screenshot run.** The app keeps the session in memory, so a full reload of an owner page returns to sign-in; the screenshot script navigates inside the app instead. This matches the product's behaviour and needed no code change.
- **Windows PowerShell 5.1 read the new end-to-end script as ANSI** until it was saved with a UTF-8 byte-order mark like the other scripts.

## Acceptance checklist

- [x] Places with weekly hours, overnight and 24-hour intervals, student perk, contact details and image
- [x] "Open now" as a SQL predicate agreeing with the C# rule; next opening or closing on a circular week
- [x] Directory with filters, search and exact nearest first; map layer with clustering
- [x] Owner management with the hours editor; events linked to places both ways
- [x] Admin review with reasons, revision checks and audit; suspension hides places
- [x] Images public only for published places; cleanup keeps place images
- [x] Unit, API, frontend and end-to-end checks pass; screenshots on phone and desktop
