# Phase 16 Verification: Production Deployment

## Goal and prerequisites

Run the complete product on the internet: production only, with no staging environment, at the user's request on 2026-10-03.

Prerequisites:
- Phases 11–15 and 17–18 merged into `main` (PR #2).
- The CI checks green on `main` (PR #3).
- Accounts with Render, Google Cloud (Maps key, Map ID, OAuth client) and Brevo, all owned by the team.

Production: <https://northlife.onrender.com>

## Files and components changed

- `render.yaml`: the production-only Blueprint.
  - Web service `northlife`: Docker, paid `0.5c-512mb` instance, deploys `main` after the GitHub checks pass.
  - The pre-deploy `--migrate` and the `/health/ready` health check.
  - A 5 GB disk at `/var/data` for images and the Data Protection keys.
  - PostgreSQL 17 `northlife-db` on the private network.
  - Generated JWT and metrics tokens, and prompts for the Brevo, Google and public URL settings.
- `docs/deployment.md`: the step-by-step production guide. It now also covers Brevo's IP allow list, the Google console errors and the bootstrap rules found below.
- `scripts/deploy/smoke-test.ps1`: an external check of a deployed site.
- `.github/workflows/ci.yml` and `auth-store.spec.ts` (PR #3): the first GitHub run of phases 11–18 exposed three stale CI checks and one order-dependent unit test. Details are under "Found during deployment".
- README: the production address and the phase table.

## Deployment steps performed

1. **Merge.** `feature/places` was merged into `main` (PR #2), then the CI fixes (PR #3). The checks on `main` passed: backend, frontend and the production image.
2. **Blueprint.** It was created from `main` in Render, with the `sync: false` values: Brevo key and sender `northlifeproject@gmail.com`, Maps key and Map ID, OAuth client ID, and `Email__PublicBaseUrl=https://northlife.onrender.com`.
3. **First deploy.** The migrations ran before start, and the service became ready on `/health/ready`.
4. **Google Cloud.**
   - Added `https://northlife.onrender.com/*` to the Maps key's referrers and `https://northlife.onrender.com` to the OAuth client's JavaScript origins.
   - Published the consent screen.
5. **Brevo.** Added Render's Frankfurt outbound addresses (service › Connect › Outbound) under Security › Authorized IPs.
6. **Administrator.** Created once from the Render shell with `--bootstrap-admin`, then signed in and enrolled TOTP.

## Verification commands

```powershell
powershell -File scripts/deploy/smoke-test.ps1 -BaseUrl https://northlife.onrender.com
```

```bash
B=https://northlife.onrender.com
for p in /api/admin/users /api/auth/session /api/manage/events /api/admin/auto-moderation; do curl -s -o /dev/null -w "$p %{http_code}\n" $B$p; done
curl -s -o /dev/null -w "%{http_code}\n" -X POST $B/api/auth/verify-email -H 'Content-Type: application/json' --data '{"token":"bogus"}'
```

The browser checks used Edge headless: console errors collected on `/map` and `/manage/login`, plus screenshots.

## Acceptance checklist

| Check | Result |
| --- | --- |
| CI on `main` after both merges | Pass: backend (318 unit and API tests plus the API smoke script), frontend (89 tests and the build), production image |
| Smoke test against production | Pass, 12 of 12, no warnings: liveness, readiness (database reachable), app shell, `no-cache` on `index.html`, Brotli, immutable hashed bundle, deep link, events API, places API, browser configuration (Maps key, production Map ID, client ID), `/metrics` closed (404), http → https (301) |
| Protected endpoints without a session | Pass: `401` on admin, session, owner and auto-approval endpoints; `400` on a bogus verification token |
| Map with the production key | Pass, after the referrer was added: tiles and controls render, no console errors |
| Google sign-in | Pass, after the origin was added and the consent screen published: the button renders and a sign-in completed |
| Verification email through Brevo | Pass, after Render's outbound IPs were authorized in Brevo: the email arrived from `northlifeproject@gmail.com` |
| Administrator bootstrap and TOTP | Pass: the admin was created from the Render shell, signed in and enrolled an authenticator app |
| Image and TOTP survive a redeploy (persistent disk) | [pending: upload an image, redeploy, check the image and an admin TOTP sign-in] |

## Found during deployment

- **Stale CI checks (PR #3).** Phases 11–18 had never run on GitHub. The first run failed on four tests, none of them in the product:
  - The API smoke script expected `404` from a newly registered owner, but an unverified owner is refused first with `403 email_not_verified` (since phase 11). The script now verifies that owner first.
  - `curl /metrics | grep -q …` failed under `pipefail`: grep stops at its first match and curl then exits with `23`. The script now reads the whole response first.
  - The recommendation evaluation wrote its report relative to the project folder, so the follow-up check found no file. It now uses an absolute path.
  - The AuthStore storage test saw another spec's analytics visitor id, because specs share browser storage. It now clears storage, signs in and checks that nothing was stored.
- **Google settings.** The map failed with `RefererNotAllowedMapError`, and sign-in with "The given origin is not allowed for the given client ID", until the production domain was added in Google Cloud.
- **Brevo IP blocking.** Every email failed with `401 Unauthorized` until Render's outbound IPs were authorized in Brevo. The account's settings did not allow turning the blocking off.
- **Bootstrap feedback.** A wrongly named variable (`Bootstrap__Password`) and a weak password each stopped `--bootstrap-admin`. The second case printed only `AuthValidationException`, without the reason. `docs/deployment.md` now states the variable names and the password rule.

## Notes

- The production catalogue starts empty. `--seed-demo` from the Render shell adds the demo catalogue for presentations (`docs/deployment.md`, step 5).
- Every deploy has a short downtime: a service with a disk stops the old instance before starting the new one.
- `Email__PublicBaseUrl` must match the real address for the links in emails to work. Render kept the requested name `northlife`.
