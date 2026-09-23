# Phase 4 Verification — Public Feed and Event Details

## Goal

Deliver useful anonymous browsing on desktop and mobile. Phase 3 is the prerequisite.

## Implemented behavior

- Anonymous feed with now, today, tonight, tomorrow, and custom-range views.
- Category, locality, and maximum-price filters persist in shareable query-string URLs.
- Highlighted top picks and responsive event cards use local fixture artwork.
- Event details include description, schedule, venue, address, price, organizer, tags, and a Google Maps navigation link.
- Loading, empty, unavailable, server-error, and retry states are explicit.
- Details return to the prior filtered feed through browser history.
- Hebrew RTL layouts adapt from three columns to a single mobile column.

## Automated verification

Run:

```powershell
cd frontend
npm run build
npm test -- --watch=false
cd ..
dotnet test NorthLife.slnx --configuration Release
```

The API client tests verify filter serialization, pagination behavior, and encoded detail URLs.

## Browser verification

Tested against a migrated and seeded local PostgreSQL database:

- Desktop feed loaded real public API fixtures.
- Music plus locality filtering produced a shareable URL and one matching event.
- Event details opened and exposed the external navigation link.
- Back navigation restored the filtered feed.
- A 390 × 844 viewport preserved all interactive controls and RTL flow.
- Tab navigation reached an interactive control.
- Forced API failure showed the retry action; removing the failure and retrying restored results.
- Axe WCAG 2 A/AA audit reported zero violations. One ARIA incomplete finding was corrected by assigning the time selector a group role; remaining incomplete contrast checks were caused by gradient backgrounds and hidden decorative arrows.

Screenshots:

- `docs/screenshots/phase-4-desktop.png`
- `docs/screenshots/phase-4-details-desktop.png`
- `docs/screenshots/phase-4-mobile.png`

## Acceptance

- [x] Guest browsing and combined filters
- [x] Shareable filter URLs
- [x] Complete public event details and map navigation
- [x] Loading, empty, unavailable, and retry states
- [x] Desktop and mobile RTL verification
- [x] Keyboard focus verification
- [x] Network-failure recovery
- [x] Zero automated accessibility violations
- [x] Frontend production build and tests
- [x] Backend regression tests
- [ ] Main-branch CI passes
