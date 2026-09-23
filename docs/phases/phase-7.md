# Phase 7 Verification — Owner Event Management

## Goal

Give authenticated business owners a complete, owner-scoped event workflow that always returns new or edited content to moderation.

## Implemented behavior

- Business owners list, read, create, update, and delete only their own events under `/api/manage/events`.
- Required text, category, coordinates, time range, price, image ownership, tags, and revision values are validated server-side.
- New events start as `Pending`; edits clear rejection details and return published or rejected events to `Pending`.
- Responses expose moderation status, rejection reason, update time, and revision.
- Updates and deletes use optimistic revisions and return stable HTTP 409 conflicts for stale clients.
- Cross-owner event and image access returns 404 without disclosing ownership.
- The responsive Hebrew dashboard combines image upload, event creation/editing, moderation state, and two-step deletion.

## Automated verification

```powershell
dotnet test NorthLife.slnx --configuration Release
cd frontend
npm run build
npm test -- --watch=false
```

Unit tests cover input normalization and invalid coordinates, time ranges, tags, and revisions. Frontend tests cover create payloads and revision-protected deletion. The CI smoke test exercises authenticated creation, owner listing, cross-owner denial, stale update conflict, and deletion against PostgreSQL.

## End-to-end verification

Using a disposable migrated PostgreSQL database:

- An owner uploaded an image and created a pending event.
- A second owner received 404 when reading the event or attaching the first owner's image.
- A stale update and stale delete returned 409.
- Editing a published event changed it to `Pending`, incremented its revision, and removed it from the public API immediately.
- The current revision deleted the event.
- The mobile-width dashboard created an event and displayed it in the management list without browser errors.

Screenshot:

- `docs/screenshots/phase-7-owner-mobile.png`

## Acceptance

- [x] Owner create, list, edit, and delete work
- [x] Required fields, numeric coordinates, times, image ownership, and revisions are validated
- [x] New and edited events enter `Pending`
- [x] Moderation state and rejection reason are visible to the owner
- [x] Cross-owner reads, writes, and image attachment fail
- [x] Published edits disappear from the public feed immediately
- [x] Stale writes return HTTP 409
- [x] Mobile dashboard flow works without browser errors
- [x] Backend and frontend regression tests pass
- [x] Main-branch CI owner-management smoke test passes ([run 35827392528](https://github.com/tomertom14/northlife/actions/runs/35827392528))
