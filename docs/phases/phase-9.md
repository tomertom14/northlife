# Phase 9 Verification — Maps and Location Selection

## Goal

Add geographic browsing and coordinate selection while keeping the product usable without Google Maps or geolocation permission.

## Implemented behavior

- `GET /api/events/map` applies the public visibility, time, category, locality, and price rules inside validated latitude/longitude bounds.
- Responses contain at most 200 markers and report truncation so users can zoom and search again.
- The public map groups events sharing coordinates and links every marker event to details.
- Moving the Google map records new bounds; “search this area” performs a new bounded query.
- Location permission can center a bounded search, while denial leaves the map and list usable.
- Every map result has a Google Maps navigation link independent of the embedded map.
- Owner and admin forms retain numeric coordinates and add click-to-select maps plus geolocation.
- Missing keys, load errors, and denied location show usable list/manual-coordinate fallbacks.
- Google integration uses the official dynamic loader and Advanced Markers with externally supplied API key and map ID.

Primary references:

- [Google Maps JavaScript API loading](https://developers.google.com/maps/documentation/javascript/load-maps-js-api)
- [Advanced Markers migration](https://developers.google.com/maps/documentation/javascript/advanced-markers/migration)
- [Official JS API loader](https://github.com/googlemaps/js-api-loader)

## Automated verification

```powershell
dotnet test NorthLife.slnx --configuration Release
cd frontend
npm run build
npm test -- --watch=false
```

Backend tests reject invalid bounds. A fake map adapter test proves colocated-event grouping without loading Google. CI verifies eligible markers, bounds validation, and public runtime configuration against PostgreSQL.

## End-to-end verification

- Pending, rejected, deleted, and expired events were excluded by the shared public query rules.
- The default northern-Israel bounds returned current published seed events.
- Invalid/reversed bounds returned structured HTTP 400.
- With no Google key, the map page displayed its fallback notice, complete event list, details links, and navigation links without browser errors.
- Owner/admin numeric inputs remain functional when the location picker cannot load.

Screenshot:

- `docs/screenshots/phase-9-map-fallback.png`

## Acceptance

- [x] Markers use the same eligibility/filter rules as the feed
- [x] Queries are bounded to 200 markers and report truncation
- [x] Colocated events share a marker popup
- [x] Marker events open event details
- [x] Selected coordinates persist in owner/admin forms
- [x] Denied geolocation and map load failures have usable fallbacks
- [x] Automated tests use a fake map adapter
- [x] Backend and frontend regression tests pass
- [ ] Manual Google Maps check with a restricted API key and map ID
- [ ] Main-branch CI map smoke test passes
