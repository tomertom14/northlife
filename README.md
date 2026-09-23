# NorthLife

NorthLife is a Hebrew, RTL platform for discovering events and activities in northern Israel. Guests browse without an account. Business owners submit events, and administrators moderate them before publication.

## Current status

Phase 3 public API: anonymous event feed, date presets, filters, pagination, top picks, event details, schema, lifecycle, and fixtures.

## Project documents

- [Initial requirements](NorthLife_Initial_Project_Requirements.md)
- [Base implementation plan](NorthLife_Base_Plan.md)
- [Phase verification records](docs/phases/README.md)

## Prerequisites

- Node.js 24.19 or newer compatible release
- .NET SDK 10.0.401
- Docker Desktop

## Local development

Copy .env.example to .env, choose a local PostgreSQL password, and keep .env untracked. Then set the API connection string for the current terminal:

~~~powershell
$env:ConnectionStrings__Database = 'Host=localhost;Port=5432;Database=northlife;Username=northlife;Password=your-local-password'
~~~

Start PostgreSQL:

~~~powershell
docker compose up -d postgres
~~~

Apply schema and add development fixtures:

~~~powershell
npm run db:migrate
npm run db:seed
~~~

Start API:

~~~powershell
dotnet run --project backend/NorthLife.Api
~~~

Start web application in another terminal:

~~~powershell
npm --prefix frontend start
~~~

Open <http://localhost:4200>. API liveness is available at /health/live; readiness checks PostgreSQL at /health/ready.

## Verification

~~~powershell
npm run build
npm test
~~~

Implementation follows independent phases. Each phase must pass its own checks and prerequisite regression checks before the next phase starts.
