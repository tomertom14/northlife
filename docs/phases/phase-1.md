# Phase 1 Verification — Runnable Foundation

## Goal and prerequisites

Create a runnable Angular, ASP.NET Core, and PostgreSQL foundation. Phase 0 must be complete.

## Files and components changed

- Angular 22 application with separate public and management layouts
- ASP.NET Core 10 API and xUnit test project
- PostgreSQL development service in compose.yaml
- Liveness/readiness health checks and Problem Details
- Root build/test scripts and GitHub Actions CI
- SDK, package lock, environment example, and local setup documentation

## Implemented behavior

- Hebrew, RTL application shell opens without authentication.
- Public and management routes use separate layouts.
- API emits structured JSON logs and Problem Details with trace IDs.
- /health/live reports process health without a database dependency.
- /health/ready reports PostgreSQL connectivity.
- Development CORS allows http://localhost:4200.
- Production startup requires an external database connection string.

## Fixture setup and verification commands

~~~powershell
docker compose up -d postgres
dotnet restore NorthLife.slnx
dotnet build NorthLife.slnx --configuration Release
dotnet test NorthLife.slnx --configuration Release
npm --prefix frontend ci
npm --prefix frontend run build
npm --prefix frontend run test -- --watch=false
docker compose config
~~~

Manual check:

1. Run dotnet run --project backend/NorthLife.Api.
2. Run npm --prefix frontend start.
3. Open http://localhost:4200 and confirm Hebrew RTL public shell.
4. Open http://localhost:4200/manage/login and confirm management shell.
5. Stop PostgreSQL and confirm /health/live remains healthy while /health/ready returns 503.

Browser evidence: [desktop](phase-1-desktop.png) and [mobile](phase-1-mobile.png). No page errors or framework overlays were detected.

## Acceptance checklist

- [x] Fresh checkout restores successfully.
- [x] Backend build passes.
- [x] Backend health endpoint tests pass.
- [x] Frontend build passes.
- [x] Frontend component tests pass.
- [x] Docker Compose configuration validates.
- [x] Running PostgreSQL makes readiness healthy.
- [x] Stopped PostgreSQL makes readiness unhealthy.
- [x] Public and management shells work on desktop and mobile.
- [ ] CI passes on main.
