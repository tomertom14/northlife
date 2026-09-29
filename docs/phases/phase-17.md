# Phase 17 Verification: Automatic Event Approval

## Goal and prerequisites

A service that approves events for the administrators, once a day, when they meet agreed terms:
- **Approve only.** It never rejects; every event it does not approve stays in the queue with a note saying which terms failed.
- **Four terms**, all required: the owner's track record, place and time, text checks, and a duplicate check.
- **Once a day** at an administrator-set Israel time, with catch-up after downtime and a "run now" button.
- **An editable admin tab** with the mode, run time, thresholds and banned words; every change is audited.

Phase 16 (deployment) is still on hold for local QA, so this phase is numbered 17. It builds on phase 8 (moderation), phase 12 (audit log), phase 14 (haversine) and phase 15 (Hebrew text processing and TF-IDF). How it works in detail: [docs/features/auto-moderation.md](../features/auto-moderation.md).

## Files and components changed

- Backend, `Moderation/` (new):
  - `NorthRegion` and `PointInPolygon`: the northern-Israel outline and the ray-casting test.
  - `ContentScanner`: Hebrew-aware banned words; links, emails and Israeli phone numbers with non-backtracking regular expressions.
  - `DuplicateFinder`: same Israel date, within a distance, TF-IDF cosine.
  - `ModerationRules`: the pure rule evaluation that returns every failed term with its numbers.
  - `AutoModerationSchedule`: Israel dates, the run instant with daylight saving, `IsDue`, `NextRun`.
  - `AutoModerationSettingsValidator`: allowed ranges and banned-word normalisation.
  - `AutoModerationService`: the compare-and-set daily claim, the advisory lock, the run, safe approval, decisions and audit.
  - `AutoModerationWorker`: the background check every 5 minutes.
  - `AutoModerationAdminService`: the overview and settings changes with their audit entry.
- Backend, elsewhere:
  - `Models/AutoModeration.cs` and `Data/Configurations/AutoModerationConfigurations.cs`; migration `AddAutoModeration` (three tables, seeded settings row, nullable `audit_entries.actor_id`).
  - `AuditEntry.ActorId` and `AuditLog.Record` accept no actor; the audit list returns a null actor for the service.
  - `AdminEventService`: each queue item carries the latest note for its current revision (`autoReview`).
  - `AdminAutoModerationController`: `GET`, `PUT settings`, `POST run`.
  - `Program.cs`: services, options and the worker behind `AutoModeration:WorkerEnabled`.
- Frontend:
  - `admin/auto-moderation-api.ts`: the client, Hebrew sentences for every reason, queue notes, run summaries.
  - `pages/admin-auto-moderation-page`: the new tab; `manage/admin-nav.ts` and `app.routes.ts` link it.
  - `pages/admin-page`: notes on pending cards and the "אושר אוטומטית" tag.
  - `admin/admin-users-api.ts`, `admin-audit-page`, `admin-user-page`: "אישור אוטומטי" as the actor, and readable lines for runs and settings changes.
- Tooling and docs:
  - `scripts/e2e/phase-17-auto-moderation.ps1`.
  - `scripts/local/start-stack.ps1` checks every 20 seconds locally; `.env.example` documents the two worker settings.
  - `docs/features/auto-moderation.md`, this record, the local QA checklist.

## Behaviour and API

| Request | Behaviour |
| --- | --- |
| `GET /api/admin/auto-moderation` | Settings, last update, next run (null when off), last 10 runs. |
| `PUT /api/admin/auto-moderation/settings` | Validates every range (`400 invalid_settings` with field errors) and audits each changed field's old and new value. |
| `POST /api/admin/auto-moderation/run` | Runs now in the current mode; `409 run_in_progress` while another run holds the lock, `409 mode_off` when switched off. |
| `GET /api/admin/events` | Items gain `autoReview: { outcome, reasons, decidedAt }` when the latest note is about the event's current revision. |

All require `AdminWithMfa`. Business owners get 403, anonymous callers 401.

## Algorithms

| Part | Method | Cost |
| --- | --- | --- |
| Region | Even-odd ray casting (crossing-number test) over a 24-corner outline, edges half-open in latitude | O(corners) |
| Banned words | `HebrewText` normalisation; up to two prefixes stripped only when the rest is a banned word; multi-word entries in a row | O(words × entries) |
| Contact details | Linear-time regular expressions (`NonBacktracking`); emails blanked before links; digit-boundary check in code | O(text) |
| Duplicates | Same Israel date, haversine ≤ 1 km, then TF-IDF cosine ≥ 0.85 over all active events | O(candidates on the date) per event, after an O(tokens) model build |
| Daily run | Compare-and-set claim of the Israel date; catch-up by checking every 5 minutes; `Asia/Jerusalem` rules with the spring gap moved one hour later | O(1) per check |
| Overlap | PostgreSQL session advisory lock | O(1) |
| Approval | Snapshot evaluation, then `EventLifecycleService.Approve` with the evaluated revision and the revision concurrency token; approval, audit and decision in one transaction | O(1) per event |

## Verification

```powershell
dotnet test NorthLife.slnx --configuration Release           # 288 passed (103 new)
npm --prefix frontend test -- --watch=false                  # 82 passed (16 new)
npm --prefix frontend run build                              # admin-auto-moderation-page chunk 18.4 kB
powershell -File scripts/local/start-stack.ps1 -Build
powershell -File scripts/e2e/phase-17-auto-moderation.ps1 -AdminEmail <admin> -AdminPassword <password> -AdminTotpSecret <base32 secret>
```

Unit tests (103 new backend):
- Region: 18 northern places inside (every demo locality at its extreme coordinates, Haifa, Nahariya, Majdal Shams), 10 places outside (Tel Aviv, Netanya, Hadera, Jerusalem, Eilat, Jenin, Irbid, Bint Jbeil, Beirut, Amman), and a concave "U" polygon.
- Text: banned words with an attached prefix, with niqqud, in Latin capitals and as a phrase; ordinary text untouched; 12 kinds of contact details found; prices, years, a date, times, "18+", Hebrew abbreviations, decimals and a long catalogue number ignored; an email is not reported again as a link.
- Duplicates: a copy 200 m away on the same day is found (similarity 1.0), while the same text on another day or 5 km away, and a different event at the same place, are not; light rewording stays above 0.85.
- Rules: a clean event from a trusted owner passes; account age 6.9/7 days, published events 2/3, rejection 89/91 days ago, the daily cap counting today's and this run's approvals, 180/181 days ahead, 14 days / 14 days and 1 minute, 1,000 / 1,000.01 ₪, ended events, suspended and unverified owners; all failures are reported together and in order.
- Schedule: not due before 07:00, due after, once a day, caught up in the afternoon, the Israel date turns at local midnight, the run instant on both sides of both daylight-saving changes of 2026, 02:30 on 27 March moves to 03:30.
- Settings: defaults valid, banned words trimmed and deduplicated, six out-of-range fields reported, 201 banned words refused.
- Endpoints: 403 for a business owner and for an administrator without two-factor sign-in, 401 when anonymous, on all three routes.

End-to-end on the Docker stack: **34 of 34 checks passed**.
- Access control and settings validation.
- A new owner gets three events approved by the administrator. Their next clean event:
  - in notes-only mode is marked `WouldApprove` and stays pending;
  - in approve mode is published and public, audited with no actor and `automatic: true`.
- Manual runs and settings changes are audited with the administrator as the actor.
- Held with the right reason: a phone number in the description, a venue in Tel Aviv, a copy of the published event (the note names the original), and an event from a second new owner.
- With a daily cap of 2 and one automatic approval already that day, the next event is published and the one after it is held with `owner_daily_cap`.
- Three simultaneous "run now" requests: one ran, two got `409` (statuses 200, 409, 409).
- With the run time set 2 minutes in the past and the day's claim reset, the worker ran the daily check by itself; after `docker restart northlife-app` the day still had exactly one daily run, and the next run moved to tomorrow.
- Switched off, "run now" answers `409 mode_off`.
- The original settings are restored and the test events deleted.

First start after deploying: the worker found no run for the day and caught up at once: "Automatic approval run … (Scheduled, NotesOnly): 7 checked, 0 approved, 0 would approve, 7 held". The 7 were old test events that had already ended.

Browser check (Edge, headless) with three sample pending events:
- The queue showed 10 notes: 9 held with their reasons, 1 "עומד בכל התנאים".
- "Run now" reported "נבדקו 10 אירועים: אחד עומד בכל התנאים, 9 נשארו לבדיקה." and added the run to the list.
- The audit log listed the service's approvals under "אישור אוטומטי".
- axe-core, WCAG 2.1 A and AA: **0 violations** on the queue, the tab, and the tab at phone width. No horizontal overflow at 1280 px or 390 px.
- The only console errors came from Google's sign-in script, loaded on the login page, failing to fetch `accounts.google.com/gsi/style` in the headless browser. They are unrelated to this phase.

Found and fixed during verification:
- **Rollback.** The generated `Down` migration made `actor_id` required again with an all-zero default, which the foreign key to `users` would reject once the service had written entries. `Down` now deletes the service's entries first.
- **Index name.** EF generated `IX_auto_moderation_runs_triggered_by_id`, unlike the lower-case names everywhere else; the configuration now names it.
- **Script encoding.** Windows PowerShell 5.1 read the new end-to-end script, saved without a byte-order mark, as ANSI and broke on the Hebrew text. Saved as UTF-8 with a BOM, like the other scripts.
- **Wording.** Run summaries said "1 עומדים"; singular forms now read "אחד עומד", "אחד אושר", "אחד נשאר".
- **Browser session.** A full page reload in the browser check lost the in-memory session; the check navigates inside the app instead.

Screenshots:
- `docs/screenshots/phase-17-auto-moderation-d.png` and `docs/screenshots/phase-17-auto-moderation-m.png`: the tab.
- `docs/screenshots/phase-17-run-now-d.png`: the result of "run now".
- `docs/screenshots/phase-17-queue-notes-d.png` and `docs/screenshots/phase-17-queue-notes-m.png`: notes in the queue.
- `docs/screenshots/phase-17-audit-d.png`: service approvals in the audit log.

## Acceptance

- [x] The service approves only; every other event stays pending with its reasons
- [x] Four terms (owner record, place and time, text, duplicates), each with boundary tests
- [x] Exactly one daily run per Israel day, caught up after downtime and kept across a restart
- [x] Runs cannot overlap; an owner's edit during a run cannot be approved unseen
- [x] Admin tab with modes, run time, thresholds, banned words, "run now" and recent runs; changes audited
- [x] Queue notes and audit entries in Hebrew; no WCAG A/AA violations
- [x] Regression suites pass
