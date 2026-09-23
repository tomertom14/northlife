# Phase 10 Verification — Production Packaging and Operations

## Goal

Run the complete product as one production artifact and provide a reviewable Render deployment with operational procedures.

## Implemented behavior

- A multi-stage Docker build compiles Angular and ASP.NET Core, then serves both from one non-root runtime on port 10000.
- Angular routes fall back to `index.html`; `/api`, health, and private image routes remain server endpoints.
- `--migrate` is an explicit, repeatable database release command.
- JSON request logs record method, path, status, elapsed time, and trace scope without query strings, bodies, passwords, or authorization headers.
- Forwarded headers support Render TLS termination.
- The runtime includes Npgsql's required GSS library discovered during real container execution.
- `render.yaml` defines Frankfurt web/database resources, CI-gated deploys, readiness checks, pre-deploy migrations, private PostgreSQL, generated JWT secret, external Google settings, and a persistent image disk.
- The deployment runbook covers staging-first promotion, admin bootstrap, monitoring, database/image backup and paired restore, and rollback.

## Verification

```powershell
dotnet test NorthLife.slnx --configuration Release
npm --prefix frontend run build
npm --prefix frontend test -- --watch=false
docker build -t northlife:verification .
```

Executed against disposable PostgreSQL and a named Docker volume:

- Explicit container migration and idempotent seed commands succeeded.
- `/health/ready`, `/`, `/map`, and `/api/events/today` returned 200 from port 10000.
- Root and deep Angular routes returned the same production `app-root` artifact.
- A processed image had the same SHA-256 hash before and after recreating the app container with its persistent volume.
- JSON logs contained request path, status, elapsed milliseconds, and trace scope.
- A business owner uploaded an image and created `Pending:1`; an administrator approved `Published:2`; the public details endpoint returned the event.
- The production browser feed rendered from the same origin with no console or WCAG A/AA violations.

Screenshot:

- `docs/screenshots/phase-10-production.png`

## Acceptance

- [x] Angular and API run from one production origin
- [x] Database migrations are an explicit release step
- [x] PostgreSQL, JWT, Google Maps, and image paths are externally configured
- [x] Persistent images survive application restart
- [x] Request timing/errors are logged without credentials or tokens
- [x] Admin bootstrap, backup/restore, image backup, monitoring, and rollback are documented
- [x] Both primary product journeys pass in the production container
- [x] Backend and frontend regression tests pass
- [ ] Main-branch CI production-image job passes
- [ ] Render staging deployment passes
- [ ] Production deployment is promoted from verified staging
