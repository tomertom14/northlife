# NorthLife — Initial Project Requirements

## Purpose of This Document

This document defines the **initial requirements for the NorthLife project**.

It should be used as the source material for planning the first implementation of the system.

The goal at this stage is to build the core product described below in a clean and maintainable way.

Do **not** add advanced algorithmic features, recommendation systems, computer vision, or other final-project extensions yet unless they are required by the basic architecture.

The next step after reading this document should be to produce an implementation plan.

---

# 1. Project Overview

**NorthLife** is a web platform focused on events and activities in northern Israel.

The main problem it solves is that information about local events, activities, restaurants, nightlife, workshops, and other things to do is currently scattered across many WhatsApp groups, social media pages, and local communities.

The application should provide one simple place where students and young people can immediately see:

- What is happening now
- What is happening today
- What is happening tonight
- What is happening tomorrow
- What events are available during a selected date range

The main user experience should behave like a **live local events feed**.

The public side of the system should be usable **without registration**.

Registration is intended mainly for business owners / organizers who want to publish events.

---

# 2. Main User Types

The system has three logical user types.

## 2.1 Guest / Public User

A public user does not need an account.

They can:

- Open the application
- View the daily event feed
- View highlighted events
- Filter events
- Open event details
- View events on a map
- Navigate to an event location

Public users should not be forced to register just to browse events.

---

## 2.2 Business Owner / Event Organizer

A business owner must register and log in.

They can:

- Create a business account
- Log in
- View their own events
- Create a new event
- Edit their own event
- Delete their own event
- Submit an event for approval
- View the approval status of their events

New events created by business owners should initially have a status such as:

- `Pending`
- `Published`
- `Rejected`

A business owner must only be allowed to edit events that belong to them.

---

## 2.3 Administrator

An administrator manages the content published in the system.

The administrator can:

- Log in
- View events in the system
- View events waiting for approval
- Approve events
- Reject events
- Delete events
- Create events when needed
- Perform basic moderation and access-control operations

---

# 3. Main Application Areas

The system should have two clearly separated areas.

## 3.1 Public Application

Accessible without authentication.

Main screens:

1. Home / Daily Feed
2. Map View
3. Event Details

---

## 3.2 Management Application

Requires authentication.

Main screens:

1. Login / Registration
2. Business Dashboard
3. Create Event
4. Edit Event
5. Admin Dashboard

---

# 4. Public Feed Requirements

The home page is the main experience of NorthLife.

It should immediately show useful local event information without requiring the user to select a location or create an account.

## 4.1 Header

The page should contain:

- NorthLife logo / branding
- Filters
- Business login entry point

Possible filters include:

- Category
- Price
- Location

The filtering architecture should be easy to extend later.

---

## 4.2 Editor's Picks

The upper part of the feed should contain a highlighted-events section.

Recommended UI:

- Horizontal carousel / horizontal scroll
- Large event cards
- Event image
- Event title
- Date
- Highlight indicator such as `Editor's Pick`

The exact mechanism used to choose highlighted events can initially be manual/admin-controlled.

---

## 4.3 Happening Now / Event Feed

The primary content area should contain a vertical list of events.

Each event card should include enough information for the user to decide whether to open it.

Minimum information:

- Image
- Event title
- Time
- Venue / location name
- Price

The feed should support pagination or infinite scrolling.

---

# 5. Event Details

Selecting an event should open an event details page or overlay.

It should display:

- Main image
- Event title
- Venue name
- Full date
- Start time
- End time
- Organizer name
- Event description
- Category / tags
- Price
- Navigation action

The navigation action should allow the user to open directions to the event location.

---

# 6. Map View

The public application should provide a map showing events geographically.

Initial requirements:

- Display event locations on a map
- Each event should have geographic/location information
- Selecting a map marker should allow the user to identify or open the related event
- The implementation should support Google Maps API unless the architecture plan proposes an equivalent while preserving the required behavior

---

# 7. Business Authentication

Authentication is required for business owners and administrators.

## 7.1 Registration

Business registration should collect at least:

- Full name
- Email
- Password
- Business name
- Phone number

---

## 7.2 Login

Login should use:

- Email
- Password

After successful authentication, the server should issue an authentication token.

The current specification assumes JWT-based authentication.

Passwords must never be stored as plain text.

---

# 8. Business Dashboard

After login, a business owner should have a dashboard containing their events.

For each event, display:

- Event title
- Relevant date information
- Current moderation status

Supported statuses should include:

- `Pending`
- `Published`
- `Rejected`

The dashboard should provide a clear action for creating a new event.

Optional initial statistic:

- Number of active events

---

# 9. Create / Edit Event

The event form should work on both desktop and mobile.

Required event information:

- Title
- Category
- Location
- Start date/time
- End date/time
- Price
- Image

The system should also support information required by the event details page, such as:

- Description
- Organizer
- Venue name
- Tags or event attributes

When a business owner creates an event:

1. Validate the submitted data.
2. Save the event.
3. Set its moderation state to `Pending`.
4. Do not expose it in the public feed until it is approved.

The implementation plan should define the expected behavior when an already-published event is edited.

---

# 10. Admin Dashboard

The administrator dashboard should provide basic event moderation.

The administrator should be able to:

- View all relevant events
- View pending events
- Approve an event
- Reject an event
- Delete an event
- Create an event

The UI should clearly show event status.

Optional initial statistic:

- Number of active events

---

# 11. Event Lifecycle

The basic event lifecycle should be designed around moderation.

Example flow:

```text
Business Owner Creates Event
        |
        v
     Pending
      /   \
     /     \
Approve   Reject
   |         |
   v         v
Published  Rejected
```

Only approved/published events should normally appear in the public application.

Expired events should not behave like active current events.

---

# 12. Core Data Model

The original specification defines two main entities:

- Users
- Events

The implementation plan may normalize the database further if needed, but should keep the model simple.

## 12.1 User

At minimum, a user should support:

- ID
- Full name
- Email
- Password hash
- Phone
- Business name
- Role

Expected roles:

- `BusinessOwner`
- `Admin`

Public visitors do not require a user record.

---

## 12.2 Event

An event should support at least:

- ID
- Title
- Description
- Category
- Venue name
- Location
- Geographic coordinates if required by the map
- Start date/time
- End date/time
- Price
- Image
- Organizer / owner
- Approval status
- Highlight / Editor's Pick state if needed
- Created timestamp
- Updated timestamp

The exact schema should be proposed during planning.

---

# 13. Required Public API Behavior

The system should expose a REST API for public event retrieval.

Initial endpoints from the project specification include:

```http
GET /api/events/today
GET /api/events/top-picks
GET /api/events/{id}
GET /api/events/tomorrow
GET /api/events/from-to
```

Expected behavior:

### `GET /api/events/today`

Returns events relevant to today.

### `GET /api/events/top-picks`

Returns highlighted events.

### `GET /api/events/{id}`

Returns full information about one event.

### `GET /api/events/tomorrow`

Returns events relevant to tomorrow.

### `GET /api/events/from-to`

Returns events within a provided date range.

The planner may improve naming or consolidate endpoints if there is a clear architectural reason, but should preserve all required behaviors.

---

# 14. Authentication API

Required authentication behavior:

```http
POST /api/auth/register
POST /api/auth/login
```

### Register

Expected input:

- FullName
- Email
- Password
- BusinessName
- Phone

### Login

Expected input:

- Email
- Password

Expected result:

- Authenticated session/token
- User role information required by the client

---

# 15. Management API

Initial management endpoints include:

```http
GET    /api/manage/my-events
POST   /api/manage/events
PUT    /api/manage/events/{id}
DELETE /api/manage/events/{id}

GET    /api/admin/pending
POST   /api/admin/approve/{id}
```

Required authorization rules:

- `my-events` — BusinessOwner
- create event — BusinessOwner
- edit event — owning BusinessOwner
- delete event — owning BusinessOwner or Admin
- view pending events — Admin
- approve/reject event — Admin

The planning phase should explicitly define authorization rules for every management endpoint.

---

# 16. UX Edge Cases

The implementation must account for at least the following cases.

## No Events

If no relevant events exist, show a clear and friendly empty state.

## Event Removed / Rejected / Expired

If the user opens an event that is unavailable:

- Do not crash
- Show a valid not-found/unavailable state
- Provide a way back to the feed

## Server / Network Error

The client should display a clear temporary error state.

## Unauthorized Management Access

Users without the required permissions should not gain access to protected actions or screens.

Authorization must be enforced by the backend, not only hidden in the frontend.

## Invalid Event Form

Validation errors should:

- Be visible
- Explain what is invalid
- Allow the user to correct the form

---

# 17. Non-Functional Requirements

## 17.1 Performance

The main feed should target a load time below approximately 2 seconds under reasonable network conditions.

---

## 17.2 Scalability

Event retrieval must not require loading the entire events table into the client.

Use:

- Pagination
- Infinite scroll
- Or another bounded query strategy

---

## 17.3 Responsive Design

The application must support:

- Desktop
- Mobile

The public feed and event creation flow should both be usable on mobile.

---

## 17.4 Security

At minimum:

- Passwords must be hashed securely
- Protected endpoints require authentication
- Authorization must be role-based
- A business owner cannot modify another owner's events
- Admin-only operations must be enforced server-side
- User input must be validated
- Secrets must not be committed to Git
- Database access should use the ORM / parameterized queries

---

# 18. Initial Technology Direction

The existing project specification currently proposes the following stack.

## Frontend

- Angular 19
- Single Page Application

## Backend

- C#
- ASP.NET Core
- REST API

## Database

- PostgreSQL

## ORM

- Entity Framework Core

## Maps

- Google Maps API

## Hosting

- Render.com was proposed as an initial hosting option
- Hosting is not considered permanently fixed yet

## Version Control

- Git
- GitHub

Codex should treat these technologies as the preferred initial stack unless the existing repository already establishes something different.

Do not replace the stack unnecessarily.

---

# 19. Architectural Requirements

The system should follow a standard client-server architecture.

```text
Angular Client
      |
      | HTTP / REST
      v
ASP.NET Core API
      |
      | Entity Framework Core
      v
PostgreSQL
```

External services such as maps and image storage may be added around this architecture.

The architecture should maintain clear separation between:

- UI
- API/controllers
- Business logic
- Authentication/authorization
- Persistence
- External integrations

Avoid unnecessary complexity or microservices for the initial implementation.

---

# 20. Initial Scope

The first implementation should focus on completing the basic end-to-end product.

A successful initial version should allow this complete flow:

```text
Business owner registers
        ->
Business owner logs in
        ->
Business owner creates an event
        ->
Event becomes Pending
        ->
Admin reviews the event
        ->
Admin approves the event
        ->
Event appears in the public feed
        ->
Guest opens the event
        ->
Guest views its details/location
```

A second important public flow is:

```text
Guest opens NorthLife
        ->
Sees today's events without login
        ->
Filters / browses events
        ->
Opens an event
        ->
Views details
```

---

# 21. Out of Scope for the Initial Implementation

Do not include the following unless they already exist in the repository or are required as infrastructure for future work:

- Personalized recommendation algorithms
- User accounts for normal public visitors
- Social network functionality
- Chat between users
- Likes/follows
- Complex reputation systems
- AI event recommendations
- Computer vision
- Automatic image moderation
- Advanced ranking algorithms
- Complex analytics
- Native mobile applications
- Microservices
- Premature optimization

These may be introduced later as project extensions.

---

# 22. Planning Requirements for Codex

Before writing implementation code, inspect the repository and produce a detailed plan.

The plan should:

1. Describe the current repository structure.
2. Identify what already exists.
3. Compare the existing implementation against this requirements document.
4. Identify missing features.
5. Identify incorrect or incomplete behavior.
6. Propose the final project structure.
7. Define the database schema.
8. Define backend modules/services/controllers.
9. Define frontend pages/components/services.
10. Define authentication and authorization behavior.
11. Define the event moderation lifecycle.
12. Define image handling/storage.
13. Define map/location handling.
14. Define API contracts.
15. Define validation rules.
16. Define error handling.
17. Define testing strategy.
18. Define seed/development data if useful.
19. Define deployment/environment configuration.
20. Split the implementation into clear phases.

Each phase should:

- Have one clear goal
- List the files/components expected to change
- List the required behavior
- Include tests/verification
- Be small enough to review before starting the next phase

---

# 23. Important Planning Rules

When planning:

- Do not redesign requirements without a reason.
- Do not over-engineer the system.
- Prefer simple, explicit architecture.
- Reuse existing repository code where reasonable.
- Do not introduce unnecessary dependencies.
- Keep public browsing independent of authentication.
- Keep authorization enforced in the backend.
- Preserve the separation between public functionality and management functionality.
- Build the initial product first.
- Leave advanced final-project algorithmic additions for a later phase/document.

The output of the planning step should be a proposed implementation plan only.

Do not begin modifying the code until the plan has been reviewed and approved.
