# Deployment and operations

NorthLife runs in production on Render. There is no staging environment. `render.yaml` defines three resources:

| Resource | Render name | Contents |
| --- | --- | --- |
| Web service | `northlife` | The Docker image built from `Dockerfile`: the ASP.NET Core API, the Angular app it serves, and the background workers. |
| PostgreSQL 17 | `northlife-db` | All data. Reachable only from Render's private network. |
| Persistent disk | `northlife-data`, mounted at `/var/data` | Uploaded images (`/var/data/images`) and the Data Protection keys that encrypt TOTP secrets (`/var/data/keys`). |

The web service uses a paid instance type (`0.5c-512mb`), because persistent disks are not available on the free plan. A free instance is not a workable fallback: every restart would lose the uploaded images and the encryption keys, which would lock administrators out of two-factor sign-in. Check current prices at <https://render.com/pricing>.

There is one instance only: a disk attaches to a single instance. For the same reason a deploy stops the old instance before it starts the new one, so the site is down for a short while on every deploy.

## Before you start

| What | Where | Used for |
| --- | --- | --- |
| Render account with a payment method, connected to GitHub | <https://dashboard.render.com> | The Blueprint. |
| Google Maps JavaScript API key | Google Cloud console › APIs & Services › Credentials | `GoogleMaps__ApiKey` |
| Map ID (JavaScript, vector) | Google Cloud console › Google Maps Platform › Map management | `GoogleMaps__MapId`. `DEMO_MAP_ID` is for development only. |
| OAuth client ID (Web application) | Google Cloud console › APIs & Services › Credentials | `Google__ClientId`. No client secret is needed. |
| Brevo account, a verified sender and an API key | <https://app.brevo.com> | `Email__BrevoApiKey` and `Email__From` (the verified sender address). The free plan sends 300 emails a day. |
| An administrator email and a password of 10 to 128 characters, with upper and lower case letters and a digit | — | The one-time bootstrap (step 4). |

## 1. Put the code on `main`

Render deploys `main`. Push the feature branch, open a pull request into `main`, wait for the CI checks to pass, and merge.

## 2. Create the Blueprint

1. In the Render dashboard, choose **New › Blueprint**, then the repository and the `main` branch. Render reads `render.yaml` and lists the web service, the database and the disk.
2. Render asks for every value marked `sync: false`:

   | Variable | Value |
   | --- | --- |
   | `Email__PublicBaseUrl` | The site's address, for example `https://northlife.onrender.com`, with no trailing slash. If the name is taken, Render adds a suffix: correct this value once the real address is known (step 3). |
   | `Email__BrevoApiKey` | The Brevo API key. |
   | `Email__From` | The verified Brevo sender address. |
   | `GoogleMaps__ApiKey`, `GoogleMaps__MapId` | The browser key and the Map ID. |
   | `Google__ClientId` | The OAuth client ID. |

   Render generates `Authentication__JwtKey` and `Metrics__Token` itself, and takes `Database__Url` from the database.
3. Apply. The first build takes several minutes. Before the new version starts, the pre-deploy command `dotnet NorthLife.Api.dll --migrate` creates the schema. The deploy succeeds when `/health/ready` answers, which means the database is reachable.

Render prompts for `sync: false` values only when the Blueprint is created. Later changes go in the service's **Environment** tab, and saving them redeploys the service.

## 3. Point the external services at the site

Use the service's real address, shown at the top of its Render page.

- **Maps key:**
  - Application restrictions: HTTP referrers. Add `https://<service>.onrender.com/*`, and your custom domain if you add one.
  - API restrictions: Maps JavaScript API.
- **OAuth client:**
  - Authorized JavaScript origins: add `https://<service>.onrender.com`.
  - Sign-in uses Google Identity Services ID tokens, so no redirect URI is needed.
  - On the OAuth consent screen, publish the app ("In production"). Otherwise only the listed test users can sign in.
- **Brevo:** new Brevo accounts refuse API calls from unknown IP addresses.
  - Without this step every email fails, and the service log shows `Sending the "…" email failed.` with `401 (Unauthorized)`.
  - Open the service's **Connect › Outbound** tab in Render. It lists the outbound IP ranges, which are shared by every service in the region; the service can send from any address in them.
  - Add every listed address in Brevo › account menu › **Security › Authorized IPs**.
  - The change applies at once, with no redeploy.
- **`Email__PublicBaseUrl`:** correct it in the Environment tab if Render changed the name.

If a Google setting is missing, the browser console names it: `RefererNotAllowedMapError` means the Maps key's referrer list, and "The given origin is not allowed for the given client ID" means the OAuth client's JavaScript origins.

## 4. Create the administrator, once

Open the service's **Shell** tab in Render. The shell starts in `/app` with the service's environment:

```sh
BootstrapAdmin__Email='admin@example.com' \
BootstrapAdmin__Password='a-long-unique-password' \
BootstrapAdmin__FullName='Site Admin' \
dotnet NorthLife.Api.dll --bootstrap-admin
history -c
```

The command only works if:
- every variable starts with `BootstrapAdmin__` (two underscores);
- the password has 10 to 128 characters, with upper and lower case letters and a digit.

A missing variable stops it with "BootstrapAdmin:Password is required". A weak password stops it with an `AuthValidationException`.

Then sign in at `/manage/login`:
- The admin area asks you to enrol an authenticator app (TOTP) before it opens.
- Keep the ten backup codes somewhere safe.
- To add more administrators later, use the Users page.

## 5. Optional: demo content

For a presentation, an empty site shows little. This command adds the demo catalogue: six demo businesses, 64 upcoming events in northern towns, 13 places and 30 days of simulated visits.

```sh
Demo__OwnerPassword='another-long-password' dotnet NorthLife.Api.dll --seed-demo
history -c
```

- The demo businesses sign in as `owner1@demo.northlife.local` … `owner6@demo.northlife.local`.
- To hide all of it later, suspend those six owners on the admin Users page. Their events, places and images leave every public page at once, and lifting the suspension brings them back.

The demo events cover only the two weeks after the seed; after that the feed empties. Running `--seed-demo` again does nothing, because the demo owners exist. Before a review or a presentation, refresh the demo from the Shell tab:

```sh
dotnet NorthLife.Api.dll --refresh-demo
```

- It moves the 64 demo events to the coming two weeks and replaces their traffic with a new simulated month. The feed, the "hot now" order and the owners' charts look current again.
- Accounts, passwords, places and images stay. Events the demo owners added themselves keep their dates.
- Real visits recorded on the 64 demo events are replaced too.
- It is safe while the site is running, and it can be run again at any time. The log line `Demo refreshed: …` gives the new date range.
- Run it in the morning of the day you need it. The day's events that are already over move to the next day, so an evening run leaves few events on today's feed.

## 6. Verify

From your machine:

```powershell
powershell -File scripts/deploy/smoke-test.ps1 -BaseUrl https://<service>.onrender.com
```

It checks:
- health and readiness;
- the app shell and deep links;
- caching and compression;
- the events and places APIs;
- the browser configuration (it warns about a missing key or a development Map ID);
- that `/metrics` is closed;
- the redirect from http to https.

Then by hand:

1. **Register.** Register a business account. The verification email arrives (Brevo) and its link opens the site and verifies the account.
2. **Google.** Sign in with Google.
3. **Events.** Create an event with an image as that owner, and approve it as the administrator. It appears in the feed, on the map and on its page.
4. **After a redeploy.** Choose **Manual Deploy › Deploy latest commit**, then check that:
   - the image is still there (disk);
   - the administrator can still sign in with the authenticator app (the encryption keys are on the disk).
5. **Phone.** The map loads with the production Map ID, on a phone too.

## Day to day

- **Deploys:** every merge into `main` deploys automatically once the CI checks pass (`autoDeployTrigger: checksPass`).
- **Logs:** the service's Logs tab. Requests are logged with method, path, status and duration. Query strings, bodies, passwords and tokens are never logged.
- **Metrics:** `GET /metrics` with `Authorization: Bearer <Metrics__Token>`. The token is in the Environment tab. The Prometheus and Grafana setup in `compose.yaml` is for local use.
- **Background work:** the analytics rollup, the recommendation model and the daily automatic approval run inside the web service. Each takes a PostgreSQL advisory lock.
- **Automatic approval:** it starts in "notes only" mode. Switch it on in the admin "אישור אוטומטי" tab when you trust its notes.

## Backups, restore and rollback

- **Database:** see the database's Backups / Recovery tab for the plan's backups. Take a manual export before risky changes.
- **Disk:** Render snapshots persistent disks daily. Restore from the disk's page.
- **Restore together:** restore the database and the disk to the same point in time. A database without its images shows broken pictures, and images without their rows are orphans that the nightly cleanup removes.
- **Rollback:** on the service's Events tab, choose an earlier successful deploy and **Rollback**. Every migration so far is additive, so an earlier version runs against the newer schema. Do not run a down migration in production without a backup.

## Running the production image locally

`scripts/local/start-stack.ps1 -Build` builds the same image and runs it on <http://localhost:10000>, with PostgreSQL and Mailpit in Docker. See `docs/local-qa.md`. The smoke test also runs against it:

```powershell
powershell -File scripts/deploy/smoke-test.ps1 -BaseUrl http://localhost:10000
```
