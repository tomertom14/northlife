# Phase 5 Verification — Business Authentication

## Goal

Secure management routes and establish business-owner and administrator identities. Phase 2 is the data prerequisite; the public Phase 4 experience remains anonymous.

## Implemented behavior

- Business registration always creates the `BusinessOwner` role; the request contract has no role field.
- Login returns a signed JWT containing user ID, profile, and role with a fixed 60-minute lifetime and zero validation clock skew.
- Passwords use ASP.NET Core Identity hashing and are never returned.
- Login failures are generic; duplicate emails return a stable conflict code.
- Registration and login are rate-limited per client IP.
- Protected responses distinguish invalid authentication (401) from forbidden roles (403).
- Angular keeps the JWT and profile in memory only, adds the bearer token through an interceptor, and guards management routes.
- Reloading the application clears the session and redirects to login, as required for version 1.
- Logout calls the protected endpoint and clears client state even if the network call fails.
- The first administrator is provisioned only by `--bootstrap-admin` with environment credentials. The command refuses to promote an existing business account.

## Automated verification

```powershell
dotnet test NorthLife.slnx --configuration Release
cd frontend
npm run build
npm test -- --watch=false
```

Backend tests cover password policy, hashing, identity/role claims, the 60-minute lifetime, expired-token rejection, and the registration contract. Frontend tests cover memory-only storage, anonymous login requests, bearer attachment, and logout clearing.

## End-to-end verification

Using a disposable migrated PostgreSQL database:

- Anonymous navigation to `/manage/dashboard` redirected to login with a return URL.
- Registration created a business owner and opened the protected dashboard.
- A full reload cleared the in-memory token and returned to login.
- Login restored the protected session; logout returned to login.
- A duplicate email returned 409.
- A registration payload containing `role: Admin` still created `BusinessOwner`.
- The bootstrap command created an administrator that logged in with the `Admin` role.
- An invalid bearer token returned 401.
- The database password value differed from the submitted password.
- The public event feed remained available without authentication.
- Axe WCAG 2 A/AA audit on the mobile login page reported zero violations.

Screenshots:

- `docs/screenshots/phase-5-dashboard.png`
- `docs/screenshots/phase-5-login-mobile.png`

## Acceptance

- [x] Registration and login work
- [x] Duplicate emails fail
- [x] Invalid and expired tokens fail
- [x] Role escalation fails
- [x] Passwords are hashed
- [x] Logout clears the browser session
- [x] Protected route guard and bearer interceptor work
- [x] Admin bootstrap works without public role assignment
- [x] Public browsing remains anonymous
- [x] Backend and frontend tests pass
- [x] Mobile auth accessibility audit has zero violations
- [x] Main-branch CI authentication smoke test passes: https://github.com/tomertom14/northlife/actions/runs/35824497818
