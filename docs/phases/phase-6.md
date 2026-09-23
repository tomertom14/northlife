# Phase 6 Verification — Image Upload and Retrieval

## Goal

Provide secure event image handling before owner event forms depend on it. Phase 5 authentication is the prerequisite.

## Implemented behavior

- Authenticated business owners and administrators upload JPEG, PNG, or WebP files up to 5 MB.
- The server ignores filenames, verifies the declared type against decoded content, rejects excessive dimensions, normalizes orientation, strips metadata, resizes the longest edge to 1600 pixels, and stores WebP output.
- Generated private storage keys use UUIDv7 identifiers and never appear in API responses.
- Atomic file writes live outside the web root through an `IImageStorage` adapter.
- Anonymous retrieval succeeds only when the image belongs to a currently published, unexpired event.
- Uploaders and administrators may retrieve private images; other owners receive 404.
- The reusable ownership policy is exposed to Phase 7 event attachment logic.
- `--cleanup-images` deletes database rows and files for uploads older than 24 hours with no event reference.
- The protected Angular dashboard includes a validated upload component with preview, progress, error, and success states.

## Automated verification

```powershell
dotnet test NorthLife.slnx --configuration Release
cd frontend
npm run build
npm test -- --watch=false
```

Tests cover processing to WebP, 1600-pixel resizing, metadata removal, content-type spoofing, invalid signatures, the 5 MB limit, path traversal, storage persistence across adapter restart, ownership rules, and multipart client requests.

## End-to-end verification

Using a disposable migrated PostgreSQL database and private temporary volume:

- A PNG upload was stored as a 1264×625 WebP.
- Anonymous access to the unreferenced image returned 404.
- The uploader received 200; a second owner received 404.
- A PNG declared as JPEG returned 400.
- Missing and invalid bearer tokens returned 401.
- The stored image remained readable after an API process restart.
- Two unreferenced images aged to 25 hours were removed by `--cleanup-images`; referenced seed rows remained.
- The Angular upload component successfully selected, previewed, processed, and recorded an image without browser errors.

Screenshot:

- `docs/screenshots/phase-6-upload.png`

## Acceptance

- [x] Valid images survive restart
- [x] Oversized files fail
- [x] Spoofed content types fail
- [x] Metadata is stripped and longest edge is capped
- [x] Storage keys are generated server-side and remain private
- [x] Another owner cannot manage or retrieve the upload
- [x] Unpublished images cannot be read anonymously
- [x] Unreferenced uploads older than 24 hours are cleaned
- [x] Upload component works through the authenticated browser flow
- [x] Backend and frontend regression tests pass
- [x] Main-branch CI image smoke test passes ([run 35826048839](https://github.com/tomertom14/northlife/actions/runs/35826048839))
