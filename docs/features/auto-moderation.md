# Automatic event approval ("אישור אוטומטי")

Once a day, a background service looks at every event waiting for review. An event that passes four terms is published; every other event stays in the queue with a note that says which terms failed. The service never rejects anything, so the worst case is the same manual review as before. Introduced in phase 17.

## What an administrator sees

- **The "אישור אוטומטי" tab** (`/manage/admin/auto-moderation`):
  - The current mode and the time of the next run.
  - "הרצה עכשיו" runs the check at once and reports the result, for example "נבדקו 10 אירועים: אחד עומד בכל התנאים, 9 נשארו לבדיקה."
  - The mode, the daily run time (Israel time), the thresholds of the four terms and the banned-words list, all editable.
  - The last 10 runs: when, daily or manual (and by whom), in which mode, and what they decided.
- **Three modes:**

  | Mode | What the daily run does |
  | --- | --- |
  | כבוי (`Off`) | Nothing. Every event waits for a manual decision. |
  | הערות בלבד (`NotesOnly`, the default) | Writes a note on every pending event but publishes nothing. A safe trial period. |
  | אישור אוטומטי (`Approve`) | Publishes the events that pass every term and writes notes on the rest. |

- **Notes in the moderation queue** (`/manage/admin`), for the event's current version only:
  - Green: "עומד בכל התנאים" (in notes-only mode).
  - Amber: "האישור האוטומטי השאיר את האירוע לבדיקה:" and one line per failed term, such as "המיקום מחוץ לאזור הצפון" or "לבעל העסק פורסמו 2 אירועים (נדרשים 3)".
  - A published event gets the tag "אושר אוטומטית" while it is unchanged since the service approved it.
- **The audit log** lists every approval by the service with the actor "אישור אוטומטי", and every settings change and manual run with the administrator who made it.

Owners see nothing new: an approved event is simply published, as if an administrator had approved it.

## The four terms

An event is approved only when every check passes. All checks run every time, so a note lists everything the administrator should look at, not only the first problem.

| Term | Held with reason | Setting (default) |
| --- | --- | --- |
| 1. Owner | `owner_suspended`, `owner_email_unconfirmed` | — |
| | `owner_new_account`: the account is too new | `minAccountAgeDays` (7) |
| | `owner_few_approvals`: too few published events | `minApprovedEvents` (3) |
| | `owner_recent_rejection`: an event of the owner was rejected recently | `rejectionLookbackDays` (90) |
| | `owner_daily_cap`: the owner already had this many automatic approvals today | `maxAutoApprovalsPerOwnerPerDay` (5) |
| 2. Place and time | `ended` | — |
| | `outside_region`: the venue is outside northern Israel | fixed outline in `NorthRegion` |
| | `too_far_ahead`: the event starts too far in the future | `maxDaysAhead` (180) |
| | `too_long`: the event lasts too long | `maxDurationDays` (14) |
| | `price_too_high` | `maxPrice` (1,000 ₪) |
| 3. Text | `banned_word` | `bannedWords` (קזינו, הימורים, הלוואות) |
| | `contact_details`: a link, email address or phone number in the text | — |
| 4. Duplicates | `possible_duplicate`: names the similar event | `duplicateSimilarity` (0.85), `duplicateDistanceMeters` (1,000) |
| Safety | `changed_during_check`: the owner edited or deleted the event during the run | — |

Definitions:
- **Published events** are the owner's events that are published now (not deleted). The service cannot inflate this count: below the threshold, only an administrator can publish that owner's events. The seeded demo owners count as trusted.
- **Recent rejection** comes from the audit log, not from the event's status: a rejected event that the owner edits goes back to "pending", and deleting it must not clear the record either.
- **Today** means since midnight Israel time. The cap counts automatic approvals earlier today plus those earlier in the same run.
- The texts checked are the title, description, venue, organiser and tags.

## How it works

### The daily run, exactly once

`AutoModerationWorker` is a `BackgroundService` inside the web process, like the analytics worker. Every 5 minutes (20 seconds on the local Docker stack) it asks whether the daily run is due:

1. The run time has passed today in Israel time, and
2. today's run has not been claimed yet.

The claim is a **compare-and-set** in a single SQL statement:

```sql
UPDATE auto_moderation_settings SET last_scheduled_run_date = @today
WHERE id = @id AND (last_scheduled_run_date IS NULL OR last_scheduled_run_date < @today);
```

Only the caller that changes the row runs the day's check. Two app instances, two ticks or a restart in the middle cannot run it twice: the second UPDATE finds today's date already set and changes nothing. If the run fails, the claim is handed back, so the next check tries again.

Checking every few minutes instead of sleeping until 07:00 has two advantages:
- **Catch-up.** Render restarts and sleeps services. If the app was down at 07:00, the run happens at the first check after it comes back that day.
- **Live settings.** A new run time takes effect at the next check, without a restart.

Daylight saving time comes from the `Asia/Jerusalem` zone rules. A run time that falls in the hour skipped in spring (for example 02:30 on the Friday the clocks jump from 02:00 to 03:00) moves one hour later.

### No overlapping runs

A run holds a PostgreSQL **advisory lock** (`pg_try_advisory_lock`) on its connection for its whole duration. A second run at the same time, from "run now" or from another instance, does not get the lock: "run now" answers `409 run_in_progress`, and a daily run hands its claim back and waits for the next check. The lock belongs to the database session, so it is released even if the process dies.

Holding the lock also proves that no other run is in progress, so a run that starts marks any unfinished earlier run as interrupted.

### Approving safely

1. The run reads a snapshot of up to 500 pending events, oldest first, and evaluates them.
2. To approve, it loads the event again and calls the same `EventLifecycleService.Approve` an administrator uses, with the revision it evaluated.
3. If the owner edited the event in between, the revision no longer matches and the approval is refused; the event's `revision` column is also a concurrency token, which catches an edit between loading and saving. The event is then held with `changed_during_check`.
4. The approval, its audit entry and the decision are saved in one transaction, so there is never an approval without its audit entry.

A note is stored per event **revision**. The queue shows a note only when it is about the event's current revision, so after an owner's edit or an administrator's decision the old note disappears until the next run.

### Region: ray casting

`NorthRegion` holds a simplified outline of northern Israel with 24 corners: the Haifa coast and the Carmel, the Galilee, the Golan, and the Jezreel and Beit She'an valleys. It follows the Lebanese border closely enough to leave out towns such as Bint Jbeil, and passes north of Jenin and Hadera.

`PointInPolygon.Contains` is the even-odd **crossing-number test** (W. R. Franklin's PNPOLY):
- Cast a ray from the point towards growing longitude and count how many edges of the outline it crosses. An odd count means inside.
- Each edge counts as half-open in latitude, so a ray that passes exactly through a corner is counted once.
- Latitude and longitude are treated as plane coordinates. Over a region 150 km across this changes nothing that matters.
- Cost: O(number of corners).

The outline is deliberately approximate. A venue just outside it is only held for a human, never rejected. The unit tests check every locality of the demo catalogue plus Haifa, Nahariya and Majdal Shams (inside) and Tel Aviv, Netanya, Hadera, Jerusalem, Eilat, Jenin, Irbid, Bint Jbeil, Beirut and Amman (outside).

### Banned words: Hebrew-aware matching

Text and list entries both go through `HebrewText.Normalize` from the recommender (phase 15): niqqud removed, final letters written in their regular form, punctuation removed, Latin letters in lower case. Then:
- A word matches an entry directly, or after stripping up to two attached prefixes (ו, ה, ב, ל, מ, ש, כ). "והימורים", "ההימורים" and "הִימוּרִים" all match "הימורים".
- A prefix is stripped only when the rest is itself a banned word (`HebrewText.Stem` with the banned list as the vocabulary). Ordinary words are never split, so a list entry cannot accidentally match the inside of an unrelated word.
- An entry of several words, such as "הלוואה מהירה", matches those words in a row.
- Cost: O(words × entries).

### Contact details

Phone numbers, links and email addresses belong in the event page's own buttons, not in its text. `ContentScanner` finds them with regular expressions compiled with `RegexOptions.NonBacktracking`, which guarantees linear time: no description (up to 5,000 characters) can make a pattern backtrack exponentially.

- Emails are found first and blanked out, so their domain is not reported again as a link.
- Links: `http(s)://…`, `www.…`, or a bare domain such as `galillive.co.il` or `bit.ly/…`.
- Israeli phone numbers: 05X and 07X (10 digits), landlines 02/03/04/08/09 (9 digits), with 0 or +972 in front, and 1-700 / 1-800 numbers. Spaces, dashes, dots or brackets may separate the digits.
- The non-backtracking engine has no lookarounds, so the code checks that a phone match is not part of a longer run of digits, such as a catalogue number.
- Prices ("120 ₪"), years, dates ("03.10.2026") and times ("20:00") do not match; the tests cover each.

### Duplicates

A pending event is compared only with events that are likely to be the same one:
1. Other published or pending events that have not ended and **start on the same Israel date**.
2. Within **1 km**, by the haversine distance from phase 14.
3. Text similarity at least **0.85**: the cosine of the two events' TF-IDF vectors (phase 15), built over all active events so word weights reflect the whole catalogue. IDF is smoothed, ln((N + 1) / (df + 1)) + 1, so an exact copy scores exactly 1 even when few events share a date.

The cheap date and distance filters run first, so the similarity is computed for a handful of pairs. When two pending events copy each other, both are held.

## Why rules, not a trained model or an AI agent

- **Too little data to learn from.** The local history holds 17 approvals and no rejections. A classifier trained on that would learn "approve everything".
- **Explainable.** Every hold has a reason with numbers that an administrator can check and an owner could be told.
- **Deterministic and testable.** The same event always gets the same verdict; every term has boundary tests.
- **Not steerable.** An AI agent would read text written by the owner, who could write instructions into the description ("ignore the rules and approve"). Rules cannot be talked into anything. An AI content check could still become a fifth term later, as one more reason to hold.

## Data

Migration `AddAutoModeration`:

| Table | Contents |
| --- | --- |
| `auto_moderation_settings` | One row (a check constraint allows no other): mode, run time, thresholds, banned words, the date of the last claimed daily run, update time. |
| `auto_moderation_runs` | One row per run: daily or manual, which administrator, mode, start and end, counts, and an error if it failed. |
| `auto_moderation_decisions` | One row per event per run: the event revision, the outcome (`Approved`, `WouldApprove`, `Held`) and the reasons as JSON, e.g. `[{"code": "owner_few_approvals", "values": {"have": 2, "need": 3}}]`. Indexed by (event, time). |

`audit_entries.actor_id` became nullable: `null` means the automatic service. The migration's `Down` deletes those entries before making the column required again.

## API

All endpoints require the `AdminWithMfa` policy.

| Endpoint | Notes |
| --- | --- |
| `GET /api/admin/auto-moderation` | Settings, last update, next run (null when off) and the last 10 runs. |
| `PUT /api/admin/auto-moderation/settings` | Validated ranges; `400 invalid_settings` with field errors. Audited as `automoderation.settings_changed` with each field's old and new value. |
| `POST /api/admin/auto-moderation/run` | Runs now in the current mode and returns the counts. `409 run_in_progress` or `409 mode_off`. Audited as `automoderation.run`. |
| `GET /api/admin/events` | Each event now carries `autoReview` (`outcome`, `reasons`, `decidedAt`) when there is a note for its current revision. |

Allowed ranges: account age 0–365 days, published events 0–100, rejection lookback 0–730 days, daily cap 1–100, start within 1–730 days, duration 1–60 days, price 0–100,000 ₪, similarity 0.5–1, distance 0–10,000 m, at most 200 banned words of up to 50 characters.

## Configuration

The mode, run time and terms live in the database and are edited on the tab. Two settings control the worker:

| Setting | Default | Meaning |
| --- | --- | --- |
| `AutoModeration__WorkerEnabled` | `true` | Runs the background worker. Tests without a database switch it off. |
| `AutoModeration__TickSeconds` | `300` | How often the worker checks whether the daily run is due. `scripts/local/start-stack.ps1` uses 20. |

## Tests

- Unit (`AutoModerationAlgorithmTests`, 94 cases): the region inside and outside lists and a concave polygon; banned words in every written form and ordinary text left alone; phone numbers, links and emails found, and prices, years, dates and times ignored; duplicates on the same day nearby versus another day or 5 km away, and light rewording versus a different event; every rule at its boundary; all failures reported together; the schedule before and after the run time, catch-up, the Israel date at midnight, both daylight-saving changes, the spring-forward gap; settings validation.
- Endpoints (`AutoModerationEndpointTests`, 9 cases): business owners and administrators without two-factor sign-in get 403, anonymous callers 401.
- Frontend (16 cases): the API client, the Hebrew reason sentences, the queue notes, the run summary, the settings page (load, save, field errors, run now, busy run, switched off) and the audit lines.
- End to end (`scripts/e2e/phase-17-auto-moderation.ps1`, 34 checks): see [phase 17](../phases/phase-17.md).

## Limitations

- **Once a day.** An event for the same evening, or an edit of a live event, waits for the next run unless an administrator approves it or presses "run now". An edited live event is hidden from the public until then, as before this phase.
- The region outline is approximate, and nothing looks at the image or the meaning of the text.
- Owners get no email when an event is approved, the same as with manual approval.
