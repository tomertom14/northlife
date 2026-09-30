# Local QA before deployment

This guide runs the complete product on your machine and walks through every feature added in phases 11–15, 17 and 18. Deployment (phase 16) waits until this checklist passes.

## 1. Start the stack

Prerequisites: Docker Desktop and PowerShell. The .NET 10 SDK and Node 24 are only needed to run the automated tests.

1. Copy `.env.example` to `.env` and set at least:
   - `POSTGRES_PASSWORD`
   - `Authentication__JwtKey`: 32 or more random characters.
   - the five `BootstrapAdmin__*` values.
   - `Demo__OwnerPassword`: the password for the demo business accounts.
   - Optional (see [section 4](#4-google-services-locally)):
     - `GoogleMaps__ApiKey` and `GoogleMaps__MapId`, for the map page. Without them the map says it is unavailable.
     - `Google__ClientId`, for Google sign-in. Without it the Google button is hidden.
2. Start everything:

   ```powershell
   powershell -File scripts/local/start-stack.ps1 -Build -BootstrapAdmin -SeedDemo -Monitoring
   ```

   Later runs need no flags. After pulling new code, add `-Build`.

| What | Where |
| --- | --- |
| The site | <http://localhost:10000> |
| Emails the app sends (verification, reset) | <http://localhost:8025> (Mailpit) |
| Operations dashboard | <http://localhost:3000> (Grafana, anonymous viewer) |
| Raw metrics | <http://localhost:10000/metrics> |

### Accounts

| Account | Sign-in | Notes |
| --- | --- | --- |
| Administrator | `BootstrapAdmin__Email` / `BootstrapAdmin__Password` | The first sign-in requires enrolling an authenticator app (TOTP). Keep the backup codes. |
| Demo businesses | `owner1@demo.northlife.local` … `owner6@demo.northlife.local` / `Demo__OwnerPassword` | Each has a month of simulated analytics. owner1 (גליל לייב) is the richest. |
| New owner | Register at `/manage/register` | The verification email lands in Mailpit. |

### Starting clean

`scripts/reset-demo-data.sql` removes the demo catalogue and **all** analytics data, and keeps real accounts and events:

```powershell
Get-Content scripts/reset-demo-data.sql | docker exec -i northlife-postgres-1 psql -U northlife -d northlife
powershell -File scripts/local/start-stack.ps1 -SeedDemo
```

To start from a completely empty database, `docker compose down` then `docker volume rm northlife_northlife-postgres`, and run the start script with `-BootstrapAdmin -SeedDemo`.

> **Warning:** this permanently deletes every account and event in your local database.

The automated end-to-end scripts create test events with names like "…לבדיקת…". Reset afterwards if you want a clean demo.

## 2. Feature checklist

Tick each item as you go. Expected results are in bold.

### Identity (phase 11)

- [ ] Register a new owner.
  - **The dashboard shows a yellow "verify your email" banner.**
  - Creating an event fails with a message about verification.
- [ ] Open Mailpit, click the verification link.
  - **The banner disappears; creating an event now works.**
  - Clicking the same link again says it was already used.
- [ ] "שכחתי סיסמה" with an unknown email.
  - **Same confirmation as for a real one (no account discovery).**
- [ ] Reset the password from the Mailpit link.
  - **The old password stops working; the new one works.**
- [ ] Enable 2FA under "אבטחת החשבון": scan the QR code, enter the code.
  - **Ten backup codes appear.**
  - Signing out and in now asks for a code; a backup code works once only.
- [ ] Enter a wrong code five times.
  - **"Too many wrong codes, try again in 15 minutes."**
- [ ] Sign in as the administrator on a fresh database.
  - **The admin area redirects to the security page until TOTP is enrolled.**

### Admin user management (phase 12)

- [ ] `/manage/admin` → "משתמשים": search, filter by role and status, "show more".
- [ ] Open a demo owner and suspend them with a reason.
  - **In another browser signed in as that owner, the next action sends them to the login page.**
  - Signing in says the account is suspended.
  - Their events disappear from the public feed and event pages.
- [ ] Lift the suspension. **Everything comes back.**
- [ ] Try to suspend yourself or change your own role. **Refused with an explanation.**
- [ ] Promote an owner to admin.
  - **They must enroll 2FA before the admin area opens.**
  - Demote them again.
- [ ] "יומן פעולות": **every action above is listed with who, what and why; nothing can be edited.**

### Analytics (phase 13)

- [ ] Browse the home page for a minute and open two events. Sign in as the owner of one of them.
  - **Within about a minute, "צפיות ונתונים" shows your views.**
- [ ] The owner page (sign in as owner1):
  - KPI tiles.
  - A line chart: hover for the tooltip, or use arrow keys after focusing it. Time runs right to left.
  - The table view under the chart.
  - The funnel.
  - The event comparison table.
  - The 7/30/90-day switch.
- [ ] Footer "איפוס ההיסטוריה שלי".
  - **The confirmation appears; the "For you" rail (below) disappears.**
- [ ] Admin moderation page: the "תנועה חריגה" card.
  - The demo seed creates one surge and one suspicious burst **in the hour before seeding**. The card shows them only during the following hour, since the detector looks at the last complete hour; re-seed to see it again.
- [ ] Grafana "NorthLife operations": request rate and latency move while you browse; rollup lag stays around 30–50 s.

### Smart ranking and near me (phase 14)

- [ ] The sort control: "הכי חם עכשיו".
  - **A flat list; each row has its own day and time.**
  - Reloading a few times occasionally reorders the top (the 10% exploration).
- [ ] "קרוב אליי": allow location.
  - **Nearest first with distances ("… ק״מ ממך").**
  - Deny location instead: **a message, and the list stays by time.**
- [ ] Copy a near-me URL into a private window. **It asks that browser for its own location.**
- [ ] On a phone-sized window, **the sort control spans its own row and is easy to tap.**
- [ ] Map (needs a Google Maps key and Map ID): zoom out.
  - **Numbered clusters; clicking one zooms in until it splits.**

### Recommendations (phase 15)

- [ ] In a fresh browser, **no "בשבילך" rail yet.**
- [ ] Open two or three music events, go back home.
  - **"בשבילך" appears, mostly music and nightlife, each card saying "כי פתחתם את ״…״".**
  - The events you opened are not in it.
- [ ] Open and navigate (Waze) to a few outdoor events. **The rail shifts towards outdoor events.**
- [ ] Switch the feed to "מחר". **The rail only shows events from tomorrow's feed.**
- [ ] Any event page. **"עוד אירועים כאלה" lists similar events (a jazz night brings other music first).**

### Automatic event approval (phase 17)

The service starts in "הערות בלבד" (notes only): it writes notes but publishes nothing until you switch it on.

- [ ] `/manage/admin` → "אישור אוטומטי".
  - **The current mode, the next run time (07:00 Israel time by default) and the recent runs.**
  - If the stack started after today's run time, the list already shows one daily run: the service catches up a missed run at once.
- [ ] As owner1 (a trusted owner), create an event in Safed for next week with a clean description. Back on the tab, press "הרצה עכשיו".
  - **"נבדקו … : אחד עומד בכל התנאים …".**
  - In "תור בדיקה" the event has a green note "עומד בכל התנאים" and is still pending.
- [ ] As owner1, create an event with a phone number in the description, or with its pin in Tel Aviv. Run again.
  - **An amber note lists the reason: "בטקסט יש מספר טלפון" or "המיקום מחוץ לאזור הצפון".**
- [ ] Register a new owner and submit a clean event. Run again.
  - **Held: the account is new and has no published events.**
- [ ] Switch to "אישור אוטומטי", save, and run.
  - **The clean owner1 event is published and tagged "אושר אוטומטית"; the held ones stay pending.**
  - "יומן פעולות" shows the approval under "אישור אוטומטי", and your settings change and run under your name.
- [ ] Add a word to the banned list (for example "טעימות"), save, submit an event with "והטעימות" in its title, and run. **Held for the banned word, despite the attached prefix.**
- [ ] Enter an invalid value, such as a similarity of 0.2, and save. **The field is marked with its allowed range.**
- [ ] Switch back to "הערות בלבד" or "כבוי" when you are done, and delete the test events.

### Places (phase 18)

`-SeedDemo` adds 13 demo places, also to a database seeded before places existed.

- [ ] Header → "מקומות".
  - **Cards with the type, town, an open or closed line ("סגור עכשיו · נפתח ב-20:00") and a student-perk badge.**
  - "פתוח עכשיו" keeps only open places; the two 24/7 places are always among them.
  - "קרוב אליי" asks for your location and shows distances.
  - The filters are in the address bar, so a filtered list can be shared.
- [ ] Open "גליל לייב – בר הופעות".
  - **Weekly hours with today in bold, late nights such as 20:00–03:00, the student perk, contact buttons and two upcoming events.**
  - One of its events links back ("לדף המקום").
- [ ] Map → "מקומות". **Clusters of places; clicking a marker opens the place.**
- [ ] As owner1 → "המקומות שלי" → "מקום חדש".
  - Enter an overlapping second interval on one day. **"יש טווחי שעות חופפים".**
  - Save a valid place. **It is pending and not public.**
  - In a new event, "אחד המקומות שלכם" fills the venue, town, address and pin.
- [ ] As the administrator → "מקומות".
  - Reject with a reason. **The owner sees the reason; editing sends the place back to review.**
  - Approve. **The place appears in the directory; the audit log lists both actions.**
- [ ] Suspend owner1 in "משתמשים". **Their places disappear too.** Lift the suspension.

## 3. Automated suites

```powershell
npm test                                   # backend (311) and frontend (89) unit tests
powershell -File scripts/e2e/phase-11-identity.ps1 -AdminEmail <admin> -AdminPassword <password>
powershell -File scripts/e2e/phase-12-admin-users.ps1 -AdminEmail <admin> -AdminPassword <password> -AdminTotpSecret <base32 secret>
powershell -File scripts/e2e/phase-13-analytics.ps1 -AdminEmail <admin> -AdminPassword <password> -AdminTotpSecret <base32 secret>
powershell -File scripts/e2e/phase-14-ranking.ps1 -AdminEmail <admin> -AdminPassword <password> -AdminTotpSecret <base32 secret>
powershell -File scripts/e2e/phase-15-recommendations.ps1
docker run --rm northlife:local --evaluate-recommendations /tmp/evaluation.md
powershell -File scripts/e2e/phase-17-auto-moderation.ps1 -AdminEmail <admin> -AdminPassword <password> -AdminTotpSecret <base32 secret>
powershell -File scripts/e2e/phase-18-places.ps1 -AdminEmail <admin> -AdminPassword <password> -AdminTotpSecret <base32 secret>
```

Notes on the scripts:
- The TOTP secret is the Base32 string shown at enrollment ("enter this key manually"). Save it in your password manager when you enroll the local admin.
- Phase 13 expects the fast rollup settings the start script uses, and a rate limit high enough for 300 synthetic visitors. Add `-e RateLimiting__AnalyticsPermitsPerMinute=5000` to the app container for that run, or accept a few rate-limit failures.
- The real-browser tracking check is `node scripts/e2e/phase-13-browser-tracking.mjs`. It needs `playwright-core` (`PLAYWRIGHT_CORE_PATH`) and Edge or Chrome.
- Phase 17 switches the service to "approve" for a few checks, which would publish any other upcoming pending event that passes the terms. It therefore skips those checks when other upcoming events wait for review, unless you add `-AllowApprovingOtherEvents`. It restarts the app container once, restores your settings and deletes its test events.

Load test (optional; see [docs/phases/phase-14.md](phases/phase-14.md)): add 10,000 events with `--seed-load 10000`, run `scripts/perf/feed-load.js` with the `grafana/k6` image, then reset the demo data.

## 4. Google services locally

The map and Google sign-in work on `http://localhost:10000` with your own Google Cloud values. Put them in `.env` (never in `.env.example`, which is committed) and restart with `scripts/local/start-stack.ps1`; the browser reads them from `/api/config/public`, so no rebuild is needed.

| Setting | How to create it |
| --- | --- |
| `GoogleMaps__ApiKey` | Enable the **Maps JavaScript API** in a project with billing, create an API key, restrict it to the website `http://localhost:10000/*` (add the deployed address later) and to the Maps JavaScript API. |
| `GoogleMaps__MapId` | `DEMO_MAP_ID` (Google's test ID) is enough locally. For deployment, create a JavaScript map ID in Map Management. |
| `Google__ClientId` | Google Auth Platform: consent screen with audience **External**, your Gmail under **Test users**, then a **Web application** client whose authorised JavaScript origins are **both** `http://localhost` and `http://localhost:10000`. No redirect URI. The client secret is not used anywhere. |

Google requires `Referrer-Policy: no-referrer-when-downgrade` when sign-in is tested on plain `http://localhost`; the app sends it only for `localhost` and `127.0.0.1`.

## 5. What cannot be checked locally

| Item | Needs | Where it is handled |
| --- | --- | --- |
| Real email delivery | A Brevo API key and verified sender (locally Mailpit catches everything) | Phase 16 checklist |

## 6. Evidence

| Evidence | Where |
| --- | --- |
| Phase records with test output, findings and screenshots | [docs/phases](phases/) |
| How each feature works (algorithms, API, configuration) | [docs/features](features/) |
| Recommendation evaluation report | [docs/evaluation/recommendations.md](evaluation/recommendations.md) |
