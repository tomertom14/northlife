# NorthLife

NorthLife is a Hebrew, RTL platform for discovering events and activities in northern Israel. Guests browse without an account. Business owners submit events, and administrators moderate them before publication.

## Current status

NorthLife runs in production at <https://northlife.onrender.com> (Render). Phases 11–18 of the final-project roadmap are complete; each phase has a verification record in [docs/phases](docs/phases/README.md).

| Phase | What it adds |
| --- | --- |
| 11 | Verified accounts, password reset, Google sign-in, TOTP two-factor authentication |
| 12 | Admin user management, revocable sessions, audit log |
| 13 | First-party analytics: HyperLogLog, decayed popularity, spike detection; Prometheus and Grafana |
| 14 | "Hot now" ranking with position-bias correction, geohash "near me", map clustering |
| 15 | Recommendations: Hebrew TF-IDF, item-item CF, adaptive blend, MMR, offline evaluation |
| 16 | Production deployment on Render: one web service, managed PostgreSQL, a persistent disk, Brevo email; deploys from `main` after CI passes |
| 17 | Automatic event approval: a daily rules check (owner record, ray-casting region test, Hebrew text checks, duplicates), exactly-once scheduling |
| 18 | Places: business and venue pages with weekly hours, "open now" in SQL (overnight and 24-hour hours), next opening or closing, student perks, geohash "near me", map layer, moderation |

Phase 16 (deployment) follows local QA; see [docs/local-qa.md](docs/local-qa.md).

## Project documents

- [Initial requirements](NorthLife_Initial_Project_Requirements.md)
- [Base implementation plan](NorthLife_Base_Plan.md)
- [Phase verification records](docs/phases/README.md)
- How the features work:
  - [Identity](docs/features/identity.md)
  - [Admin users](docs/features/admin-users.md)
  - [Analytics](docs/features/analytics.md)
  - [Smart ranking](docs/features/smart-ranking.md)
  - [Recommendations](docs/features/recommendations.md)
  - [Automatic event approval](docs/features/auto-moderation.md)
  - [Places](docs/features/places.md)
- [Recommendation offline evaluation](docs/evaluation/recommendations.md)
- [Lighthouse evaluation](docs/evaluation/lighthouse.md)
- [Local QA guide](docs/local-qa.md)
- [Deployment and operations](docs/deployment.md)

## Prerequisites

- Node.js 24.19 or newer compatible release
- .NET SDK 10.0.401
- Docker Desktop

## Local development

Copy .env.example to .env, choose a local PostgreSQL password, and keep .env untracked. Then set the API connection string for the current terminal:

~~~powershell
$env:ConnectionStrings__Database = 'Host=localhost;Port=5432;Database=northlife;Username=northlife;Password=your-local-password'
$env:Authentication__JwtKey = 'choose-a-random-secret-with-at-least-32-bytes'
$env:ImageStorage__RootPath = 'C:\northlife-data\images'
$env:GoogleMaps__ApiKey = 'a-browser-key-restricted-to-your-hostnames'
$env:GoogleMaps__MapId = 'your-google-map-id'
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

## Whole stack in Docker

The quickest way to see everything is the production image with PostgreSQL and Mailpit:

~~~powershell
powershell -File scripts/local/start-stack.ps1 -Build -BootstrapAdmin -SeedDemo -Monitoring
~~~

Other commands of the API, all run against the configured database unless noted:

| Command | What it does |
| --- | --- |
| `--migrate` | Applies database migrations. |
| `--seed-demo` | Adds 6 demo businesses, 64 events and 30 days of simulated traffic, once. |
| `--seed-load N` | Adds N extra events for load tests. |
| `--evaluate-recommendations [file]` | Runs the offline recommender evaluation. Needs no database. |

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

## Owner event management

Business owners sign in at `/manage/login`. The dashboard uploads an event image, creates events in `Pending` status, lists moderation status and rejection reasons, and supports revision-protected edits and deletion. Editing a published event immediately returns it to `Pending` review.

Administrators use `/manage/admin` to filter the full event queue, approve or reject owner submissions, publish events directly, edit, highlight, and delete. Every moderation action uses the current event revision.

## Image storage

Set `ImageStorage__RootPath` to a persistent directory outside the web root. Uploaded files are private and must be served through `/api/images/{id}`. Remove abandoned uploads older than 24 hours with:

~~~powershell
dotnet run --project backend/NorthLife.Api -- --cleanup-images
~~~

## Verification

~~~powershell
npm run build
npm test
~~~

Implementation follows independent phases. Each phase must pass its own checks and prerequisite regression checks before the next phase starts.
