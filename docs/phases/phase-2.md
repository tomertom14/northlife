# Phase 2 Verification — Persistence and Event Lifecycle

## Goal and prerequisites

Implement the PostgreSQL domain model, moderation lifecycle, migration, and repeatable fixtures. Phase 1 is complete.

## Files and components changed

- User, event, image, role, category, and status domain types
- Entity Framework Core mappings with lowercase snake_case identifiers
- Initial PostgreSQL migration and repository-local EF tool manifest
- Event lifecycle service with revision conflict checks
- Explicit fixture-seeding command
- Lifecycle tests and database verification procedure
- PostgreSQL-backed CI migration and seed checks

## Implemented behavior

- Application creates time-ordered UUIDv7 IDs for new records.
- PostgreSQL stores UTC timestamptz values and exact numeric prices/coordinates.
- Database constraints enforce roles, statuses, categories, prices, coordinates, revisions, and time ranges.
- Every foreign key has a supporting index.
- Composite and partial indexes support public, owner, expiry, and highlighted-event queries.
- Owner submissions are pending; owner edits return published or rejected events to pending.
- Approval, rejection, highlighting, deletion, expiry, and stale revisions follow the base plan.
- Seeding creates two owners, one admin, three images, and six event fixtures.
- Seed users receive undisclosed random passwords; fixtures cannot be used as known credentials.
- Running the seed command again creates no duplicates.

## Verification commands

Set ConnectionStrings__Database for the current terminal before database commands.

~~~powershell
docker compose up -d postgres
npm run db:migrate
npm run db:seed
npm run db:seed
dotnet build NorthLife.slnx --configuration Release
dotnet test NorthLife.slnx --configuration Release
~~~

Database checks used during verification:

~~~sql
select count(*) from users;
select count(*) from event_images;
select count(*) from events;
~~~

Expected counts remain 3, 3, and 6 after two seed runs. Transactional checks must prove that negative prices, invalid time ranges, and unknown statuses raise check violations.

## Acceptance checklist

- [x] Empty PostgreSQL database accepts the initial migration.
- [x] Schema uses lowercase identifiers and indexed foreign keys.
- [x] Price, time-range, and status constraints reject invalid data.
- [x] First seed creates 3 users, 3 images, and 6 events.
- [x] Second seed leaves fixture counts unchanged.
- [x] Lifecycle and revision tests pass.
- [x] UTC and overnight fixtures persist through Npgsql.
- [x] Backend build passes with zero warnings.
- [ ] CI migration and double-seed checks pass on main.
