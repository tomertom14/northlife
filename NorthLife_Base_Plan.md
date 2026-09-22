# NorthLife Base Implementation Plan

## Project baseline

NorthLife is a Hebrew, right-to-left web platform for discovering events and activities in northern Israel. Public browsing requires no account. Business owners register to manage events, and administrators moderate them before publication.

The initial repository contains only `NorthLife_Initial_Project_Requirements.md`. All application code, tests, infrastructure, and deployment configuration must be created.

### Technology choices

- Angular 22 single-page application
- ASP.NET Core 10 REST API
- Entity Framework Core 10 with PostgreSQL 17
- Google Maps for maps and navigation
- Render as the initial hosting target
- xUnit for backend tests, Angular component tests for frontend behavior, and Playwright for browser flows

Angular 22 replaces the requirements document's Angular 19 because Angular 19 is no longer supported. Keep the remaining preferred architecture.

### Repository structure

```text
/
  NorthLife_Initial_Project_Requirements.md
  NorthLife_Base_Plan.md
  README.md
  frontend/                 Angular application and component tests
  backend/
    NorthLife.Api/
      Controllers/
      Contracts/
      Services/
      Data/
      Models/
      Integrations/
    NorthLife.Tests/
  tests/e2e/                Playwright browser tests
  docs/phases/              Phase instructions and verification records
  infra/                    Containers and deployment configuration
  .github/workflows/        Build and test automation
```

Use one backend application with clear controller, service, persistence, authentication, and integration boundaries. Do not introduce microservices or generic repositories over Entity Framework Core.

## Core product decisions

### Data model

Use UUID primary keys and UTC timestamps.

**User**

- ID
- Full name
- Unique normalized email
- Password hash
- Phone number
- Business name
- Role: `BusinessOwner` or `Admin`
- Created timestamp

**Event**

- ID and owner ID
- Title and description
- Category
- Venue name, locality, and address
- Latitude and longitude
- Start and end timestamps
- Price in ILS
- Image ID
- Organizer name
- Tags
- Status: `Pending`, `Published`, or `Rejected`
- Highlight flag
- Rejection reason
- Created and updated timestamps
- Integer revision for concurrency checks

**EventImage**

- ID and uploader ID
- Private storage key
- Content type and byte size
- Created timestamp

Use PostgreSQL `timestamptz` for event times, `numeric(10,2)` for prices, and a text array for tags. Add indexes for email, owner, moderation status and time, end time, and highlighted published events. Database constraints must reject negative prices and end times that do not follow start times.

Initial categories are music, nightlife, food, workshops, outdoors, culture, sports, and other.

### Authentication and authorization

- Registration always creates a `BusinessOwner`; never accept a role from the client.
- Hash passwords with ASP.NET Core Identity password hashing.
- Login issues a signed JWT containing user ID and role. Token lifetime is 60 minutes.
- Keep the token in client memory. Version 1 has no refresh token; reloading requires login again.
- Backend authorization is authoritative. Angular route guards only improve navigation.
- Provision the first administrator through an explicit bootstrap command using environment credentials.
- Rate-limit registration and login and return generic login failures.
- Never expose password hashes, contact details, or internal image storage keys through public APIs.

### Event lifecycle

```text
Owner creates event
        |
        v
     Pending
      /    \
 Approve  Reject
    |       |
    v       v
Published Rejected
```

- Owner creation submits directly as `Pending`; version 1 has no draft state.
- Admin approval changes `Pending` to `Published`.
- Admin rejection requires a reason and changes `Pending` to `Rejected`.
- Any owner edit returns the event to `Pending`, clears rejection information, and removes its highlight.
- Editing a published event immediately removes it from public results until reapproval. Warn the owner before saving.
- Admin-created events publish immediately.
- Only admins can highlight published, unexpired events.
- Deletion removes an event from all results.
- Updates and moderation require the expected revision. Stale operations return HTTP 409.
- Expiration is derived from end time, not stored as another moderation status.

### Dates, filters, and pagination

Use `Asia/Jerusalem` for date selection and display. Exchange ISO 8601 timestamps with offsets and store UTC.

- Now: start time is at or before the current time and end time is after it.
- Today: event overlaps the current local calendar day and has not ended.
- Tonight: event overlaps 18:00 today through 06:00 tomorrow and has not ended.
- Tomorrow: event overlaps tomorrow's local calendar day.
- Date range: event overlaps the inclusive selected local dates and has not ended.
- Reject nonexistent or ambiguous local event times with a correction message.
- Public queries always exclude unpublished, deleted, and expired events.
- Default feed shows today's events across all northern localities without requesting device location.
- Combine category, locality, and maximum-price filters with AND. Price zero means free.
- Sort by start time and then ID.
- Default page size is 20; maximum page size is 100.
- Top picks return at most 10 matching events.
- Store public filters in URL query parameters.

### API surface

Use camelCase JSON and publish an OpenAPI contract. Collection responses contain `items`, `page`, `pageSize`, and `totalCount`.

| Endpoint | Access and behavior |
| --- | --- |
| `POST /api/auth/register` | Anonymous; validate profile and create owner |
| `POST /api/auth/login` | Anonymous; return token, expiry, and role |
| `GET /api/events` | Anonymous; canonical feed query with time preset and filters |
| `GET /api/events/today` | Anonymous; today's events |
| `GET /api/events/tomorrow` | Anonymous; tomorrow's events |
| `GET /api/events/from-to` | Anonymous; inclusive local date range |
| `GET /api/events/top-picks` | Anonymous; highlighted events using shared filters |
| `GET /api/events/{id}` | Anonymous; available published event details |
| `GET /api/manage/my-events` | Business owner; caller's paginated events |
| `GET /api/manage/events/{id}` | Owning business owner; editable event details |
| `POST /api/manage/events` | Business owner; create pending event |
| `PUT /api/manage/events/{id}` | Owning business owner; update and resubmit |
| `DELETE /api/manage/events/{id}` | Owning business owner or administrator |
| `POST /api/manage/images` | Business owner or administrator; validated image upload |
| `GET /api/images/{id}` | Anonymous for public events; otherwise uploader or administrator |
| `GET /api/admin/events` | Administrator; all events with status filter |
| `GET /api/admin/events/{id}` | Administrator; moderation details |
| `GET /api/admin/pending` | Administrator; pending queue |
| `POST /api/admin/events` | Administrator; create published event |
| `POST /api/admin/approve/{id}` | Administrator; approve expected revision |
| `POST /api/admin/reject/{id}` | Administrator; reject expected revision with reason |
| `PUT /api/admin/events/{id}/highlight` | Administrator; set highlight flag |

Derive owner and moderation fields server-side. Use HTTP 400 for validation, 401 for missing or invalid authentication, 403 for role denial, 404 for unavailable or other-owner resources, and 409 for conflicts. Return Problem Details with a stable error code, field errors, and trace ID.

### Images, locations, and validation

- Accept JPEG, PNG, and WebP images up to 5 MB.
- Verify signatures, decode files, reject excessive dimensions, strip metadata, and resize the longest edge to 1600 pixels.
- Generate storage keys server-side and never trust uploaded filenames.
- Store files outside the web root. Use a local volume in development and persistent storage in production.
- Serve files through authorization-aware API routes so pending images remain private.
- Remove unreferenced uploads after 24 hours.
- Require title, description, category, organizer, venue, locality, address, valid coordinates, start/end times, image, and nonnegative price.
- Limit title to 150 characters, description to 5,000, tags to 10, and each tag to 30 characters.
- Require a future end time when creating, editing, or approving an event.
- Treat descriptions as plain text.
- Enforce validation on the server and mirror relevant feedback in the client.

## Implementation phases

Each phase depends only on listed earlier phases. Each phase must run and pass its tests without code from later phases. Record verification in `docs/phases/` before starting the next phase.

### Phase 0 — Planning files and public repository

**Dependencies:** None.

**Goal:** Establish a reviewable source of truth and public GitHub repository.

**Expected changes:** Root documentation, `.gitignore`, Git repository, and GitHub repository.

**Required behavior:**

- Save this plan and retain the initial requirements.
- Add a README and phase-document template.
- Initialize Git with `main`.
- Create public `tomertom14/northlife`, connect `origin`, and push the documentation commit.
- Exclude secrets, local settings, uploads, dependencies, and build artifacts.
- Do not select a license without a separate licensing decision.
- If the remote exists during execution, inspect it first and never overwrite or force-push.

**Verification:** Public repository opens, documents render, local and remote commit IDs match, and the working tree is clean.

### Phase 1 — Runnable application foundation

**Dependencies:** Phase 0.

**Goal:** Produce a runnable, tested frontend/API/database skeleton.

**Expected changes:** Angular shell, ASP.NET Core host, Docker Compose, CI workflow, test projects, and environment examples.

**Required behavior:**

- Create the Hebrew RTL shell with public and management layouts.
- Connect the API to local PostgreSQL.
- Add liveness/readiness endpoints, Problem Details, structured logging, and environment examples.
- Add build and test scripts and CI for frontend and backend.
- Pin SDK and package versions with lockfiles.

**Verification:** A fresh checkout starts successfully; frontend loads; readiness detects an unavailable database; builds and initial smoke tests pass.

### Phase 2 — Persistence, lifecycle rules, and fixtures

**Dependencies:** Phase 1.

**Goal:** Implement the domain model and moderation rules independently of user interfaces.

**Expected changes:** Entity Framework entities and mappings, migrations, lifecycle service, clock abstraction, and fixtures.

**Required behavior:**

- Implement the agreed schema and lifecycle transitions.
- Add repeatable development fixtures with two owners, one admin, and events across statuses and time boundaries.
- Include free, paid, highlighted, expired, and overnight events.
- Use fixed time in tests and keep seed data environment-specific.

**Verification:** Empty PostgreSQL migrates; database constraints reject invalid data; lifecycle and timezone tests pass; reseeding creates no duplicates.

### Phase 3 — Public event API

**Dependencies:** Phase 2.

**Goal:** Expose all public event data without authentication.

**Expected changes:** Public controller, query service, response contracts, and API integration tests.

**Required behavior:** Implement all public queries, presets, filters, pagination, detail retrieval, and safe response projection.

**Verification:** API tests prove filtering, ordering, pagination, midnight and daylight-saving behavior, plus complete exclusion of pending, rejected, deleted, and expired events.

### Phase 4 — Public feed and event details

**Dependencies:** Phase 3.

**Goal:** Deliver useful anonymous browsing on desktop and mobile.

**Expected changes:** Feed, cards, filters, top-picks carousel, event details, and public API client.

**Required behavior:**

- Browse and filter without login.
- Persist filters in shareable URLs.
- Show all required event details and navigation link.
- Provide loading, empty, unavailable, and retry states.
- Use fixture images; image upload and maps are not dependencies.

**Verification:** A guest filters events, opens details, and returns to the feed on mobile and desktop. Keyboard navigation and RTL layout work. Network failures show retry controls.

### Phase 5 — Business authentication

**Dependencies:** Phase 2.

**Goal:** Secure management routes and establish business/admin identities.

**Expected changes:** Authentication endpoints and service, registration/login pages, token interceptor, route guards, and admin bootstrap command.

**Required behavior:** Implement registration, login, logout, hashing, JWT validation, role handling, and protected placeholder routes while keeping public routes anonymous.

**Verification:** Registration and login work; duplicate emails fail; invalid and expired tokens fail; role escalation fails; passwords are hashed; public browsing remains available.

### Phase 6 — Image upload and retrieval

**Dependencies:** Phase 5.

**Goal:** Provide secure event image handling before event forms depend on it.

**Expected changes:** Storage adapter, image endpoints, upload component, and cleanup command.

**Required behavior:** Validate and process uploads, store them privately, enforce ownership, and clean unreferenced images after 24 hours.

**Verification:** Valid images survive restart; oversized and spoofed files fail; another owner cannot attach an image; unpublished images cannot be read anonymously.

### Phase 7 — Owner event management

**Dependencies:** Phases 2, 5, and 6.

**Goal:** Allow a business owner to manage only their own events.

**Expected changes:** Owner endpoints, dashboard, event form, and create/edit/delete flows.

**Required behavior:**

- Implement owner-scoped list and CRUD operations.
- Show moderation status and rejection reason.
- Collect coordinates with numeric inputs until map selection exists.
- Resubmit all owner edits as pending.

**Verification:** Owner creates pending events, edits/resubmits, and deletes. Other-owner access fails. Published edits disappear publicly. Stale updates conflict. Mobile validation works.

### Phase 8 — Administrator moderation

**Dependencies:** Phase 7.

**Goal:** Complete the moderated publishing workflow.

**Expected changes:** Admin endpoints, pending queue, review screen, admin event form, and highlight controls.

**Required behavior:** Implement all-event filtering, approval, rejection, deletion, direct creation, and highlights. Require revision checks for every moderation action.

**Verification:** Admin approval publishes an owner event. Rejection remains private and gives the owner a reason. Owners cannot call admin operations. Stale reviews fail with a conflict.

### Phase 9 — Google Maps integration

**Dependencies:** Phases 4 and 7.

**Goal:** Add geographic browsing and visual location selection without making maps a hard dependency.

**Expected changes:** Map page, map adapter, location selector, and bounded map query.

**Required behavior:**

- Add `GET /api/events/map` with bounding-box filters and a limit of 200 markers.
- Apply the same visibility and filter rules as the feed.
- Return a truncation indicator and ask users to zoom when the limit is exceeded.
- Group colocated events in one marker popup.
- Keep address entry, numeric coordinates, and navigation links usable if Google Maps fails.

**Verification:** Markers match eligible events; markers open details; selected coordinates persist; denied geolocation and map failures have usable fallbacks. Automated tests use a fake adapter; one manual check uses Google Maps.

### Phase 10 — Deployment and complete-product verification

**Dependencies:** Phases 4, 8, and 9.

**Goal:** Run the full product in a production-like environment and prove both primary user journeys.

**Expected changes:** Production container, Render configuration, deployment guide, monitoring configuration, and end-to-end suite.

**Required behavior:**

- Serve the Angular build and API from one origin.
- Configure PostgreSQL, persistent image storage, and secrets externally.
- Apply migrations through an explicit release step.
- Document admin bootstrap, backup/restore, image backup, and rollback.
- Log request timing and errors without passwords or tokens.
- Deploy staging before production.

**Verification:**

- Business owner registers, logs in, creates an event, and sees `Pending`.
- Admin reviews and approves it.
- Guest finds it in the public feed, opens details, and navigates to its location.
- Guest browsing and filtering work without login.
- Data and images survive restart.
- Deep links, HTTPS, expired sessions, authorization, and error states work.
- With 10,000 seeded events, the first feed targets under two seconds on a documented 10 Mbps connection with 100 ms latency.
- Record timings, cold-start behavior, and release blockers.

## Phase completion rules

Every phase document must contain:

1. Goal and prerequisites.
2. Files and components expected to change.
3. Required behavior and API changes.
4. Fixture setup and exact verification commands.
5. Acceptance checklist and recorded results.

A phase is complete only when its own checks and prerequisite regression checks pass. Keep commits scoped to one phase. Every schema change needs a migration and fresh-database verification.

Initial scope excludes recommendations, AI, social features, public user accounts, chat, advanced analytics, native applications, and microservices. Optional dashboard statistics remain deferred.
