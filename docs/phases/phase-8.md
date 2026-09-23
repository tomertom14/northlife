# Phase 8 Verification — Administrator Moderation

## Goal

Complete the moderated publishing workflow with an administrator-only review queue and revision-safe actions.

## Implemented behavior

- Administrators filter up to 200 active events by status and search title, locality, or business.
- New administrator events publish immediately; administrators can edit any active event.
- Approval, rejection, highlight changes, edits, and deletion require the current revision.
- Approval publishes pending owner content. Rejection requires a reason, keeps the event private, and exposes the reason to its owner.
- Highlights are limited to current published events.
- Business owners receive 403 on all `/api/admin/events` operations.
- The Hebrew admin dashboard provides direct creation, image upload, editing, filtering, approval, rejection, highlights, and deletion.
- General administrator login redirects to the admin dashboard.

## Automated verification

```powershell
dotnet test NorthLife.slnx --configuration Release
cd frontend
npm run build
npm test -- --watch=false
```

Lifecycle tests cover administrator edit revision behavior. Frontend tests cover queue filters and revision-bearing approval. CI exercises the complete API workflow against PostgreSQL.

## End-to-end verification

- Owner access to the admin API returned 403.
- Admin status/search filtering found the pending submission.
- Approval changed `Pending:1` to `Published:2` and made the event public.
- A stale rejection returned 409.
- Highlighting produced `isHighlighted=true` at revision 3.
- Rejection kept a second event private and exposed its reason through the owner API.
- Direct administrator creation produced `Published:1`; administrator deletion succeeded.
- The browser queue approved a pending fixture and removed it from the pending filter without console errors.
- The admin page had zero automated WCAG A/AA violations.

Screenshot:

- `docs/screenshots/phase-8-admin.png`

## Acceptance

- [x] Admin can filter all active events
- [x] Approval publishes an owner event
- [x] Rejection remains private and gives the owner a reason
- [x] Admin can directly create, edit, highlight, and delete
- [x] Business owners cannot call admin operations
- [x] Stale moderation actions return HTTP 409
- [x] Admin browser flow works without console or WCAG A/AA errors
- [x] Backend and frontend regression tests pass
- [x] Main-branch CI moderation smoke test passes ([run 35874070713](https://github.com/tomertom14/northlife/actions/runs/35874070713))
