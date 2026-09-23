# NorthLife

NorthLife is a Hebrew, RTL platform for discovering events and activities in northern Israel. Guests browse without an account. Business owners submit events, and administrators moderate them before publication.

## Current status

Phase 5 authentication: business registration/login, memory-only JWT sessions, protected management routes, and administrator bootstrap.

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
$env:Authentication__JwtKey = 'choose-a-random-secret-with-at-least-32-bytes'
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

## Administrator bootstrap

Set the one-time administrator credentials in the current terminal, then run the explicit bootstrap command:

~~~powershell
$env:BootstrapAdmin__Email = 'admin@example.com'
$env:BootstrapAdmin__Password = 'choose-a-strong-password'
$env:BootstrapAdmin__FullName = 'NorthLife Admin'
$env:BootstrapAdmin__Phone = '0500000000'
$env:BootstrapAdmin__BusinessName = 'NorthLife'
dotnet run --project backend/NorthLife.Api -- --bootstrap-admin
~~~

The command is repeatable for the same administrator email and refuses to promote an existing business-owner account.

## Verification

~~~powershell
npm run build
npm test
~~~

Implementation follows independent phases. Each phase must pass its own checks and prerequisite regression checks before the next phase starts.
