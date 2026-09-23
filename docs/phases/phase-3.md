# Phase 3 Verification — Public Event API

## Goal

Expose published event discovery through anonymous REST endpoints. Phase 2 is the prerequisite.

## Implemented behavior

- Date routes: today, tomorrow, custom date range, and highlighted top picks.
- Optional category, locality, and maximum-price filters combine with AND semantics.
- Stable pagination defaults to 20 items and caps pages at 100 items.
- Results sort by start time and event ID.
- Pending, rejected, deleted, and expired events stay hidden.
- Date boundaries use the `Asia/Jerusalem` time zone.
- Public responses omit owner contact details and storage keys.
- Invalid queries and unavailable events return stable Problem Details responses.
- Partial database indexes cover category and locality feed queries.

## Verification

Run:

```powershell
dotnet test NorthLife.slnx --configuration Release
dotnet tool run dotnet-ef migrations has-pending-model-changes --project backend/NorthLife.Api --startup-project backend/NorthLife.Api
cd frontend
npm test -- --watch=false
```

The CI database smoke test applies migrations, seeds twice to prove idempotence, starts the API, and checks the public feed, top picks, published details, and exclusion of a pending event.

## Acceptance

- [x] Jerusalem date-window unit tests, including daylight-saving and winter dates
- [x] Invalid query and unavailable-event controller tests
- [x] Local PostgreSQL filters, order, pagination, and detail checks
- [x] Pending and expired events excluded
- [x] EF model matches migrations
- [x] Backend and frontend regression suites pass
- [ ] Main-branch CI public API smoke test passes
