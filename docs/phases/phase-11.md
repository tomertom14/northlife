# Phase 11 Verification — Identity

## Goal and prerequisites

Verified accounts, password recovery, Google sign-in and TOTP two-factor authentication, with two-factor mandatory for administrators. Builds on phases 5 (authentication) and 10 (packaging).

## Files and components changed

- Backend:
  - `Identity/`: `Totp` (RFC 6238), `Base32` (RFC 4648), `SecureTokens`, `UserTokenService`, `TotpService`, `IdentityTickets`, `GoogleSignIn`.
  - `Email/`: `IEmailSender` with Log, SMTP and Brevo senders; Hebrew `AccountEmails`.
  - `Authentication/AuthService.cs` gains two-step sign-in and Google; new `AccountService`.
  - `Controllers/AuthController.cs` and the new `SecurityController`.
  - Models `UserToken`, `ExternalLogin` and `RecoveryCode`.
  - Migration `AddIdentityVerificationAndTotp`.
  - `Program.cs`: Data Protection, the `AdminWithMfa` policy, and the `email` rate limit.
- Frontend:
  - `auth/` store, API, guards, `GoogleButton` and `sign-in-navigation`.
  - Pages: two-step login, `account-pages` (verify, forgot, reset, complete profile) and `security-page`.
  - A dashboard verification banner.
- Infrastructure: Mailpit in `compose.yaml` and CI, new variables in `.env.example` and `render.yaml`, `DataProtection__KeysPath` in the `Dockerfile`, and the `scripts/e2e/phase-11-identity.ps1` end-to-end script.

## Behaviour and API

| Endpoint | Behaviour |
| --- | --- |
| `POST /api/auth/register` | Creates an unverified owner and emails a 24-hour verification link. |
| `POST /api/auth/verify-email` | Redeems a single-use link with one conditional `UPDATE`. |
| `POST /api/auth/resend-verification` | Authorized, rate-limited to 5 per 15 minutes. |
| `POST /api/auth/forgot-password` | Always 204; emails a 30-minute reset link when the account exists. |
| `POST /api/auth/reset-password` | Enforces the password rule before spending the token; also confirms the email. |
| `POST /api/auth/login` | Returns `authenticated`, or `mfa_required` with a 5-minute ticket. |
| `POST /api/auth/mfa` | Accepts a TOTP code (each 30-second step once) or a single-use backup code. |
| `POST /api/auth/google` (+ `/complete`) | Verifies the Google ID token; signs in, links on a verified email, or asks for the business profile. |
| `GET/POST /api/auth/security/...` | TOTP status, setup (QR), enable (10 backup codes) and disable (owners only). |

Other behaviour:
- Owners with unverified emails get `403 email_not_verified` when they create or edit events.
- `/api/admin/*` requires the Admin role and the `northlife:mfa=true` claim; otherwise it returns `403 mfa_required`.
- Existing accounts were marked verified by the migration.

## Verification

```powershell
dotnet test NorthLife.slnx --configuration Release           # 82 passed
npm --prefix frontend test -- --watch=false                  # 40 passed
npm --prefix frontend run build                              # initial 373.53 kB
docker compose up -d postgres mailpit
powershell -File scripts/e2e/phase-11-identity.ps1 -AdminEmail <admin> -AdminPassword <password>
```

Unit tests:
- The TOTP implementation reproduces all six RFC 6238 SHA-1 test vectors.
- The ±1 step drift window is accepted and a ±2 step offset is rejected.
- Replay of a used step is rejected.
- Base32 matches the RFC 4648 vectors.
- Every Google linking branch is covered.
- A tampered ticket, or a signup ticket used as an MFA ticket, is rejected.

End-to-end on the Docker stack (production image, PostgreSQL 17, Mailpit): **26 of 26 checks passed**.
- Registration, then a blocked submission, then the email in Mailpit, then verification, then a single-use link check, then a successful submission.
- Forgot password, then the reset email, then the weak-password rejection, then the reset, then the old password rejected and the new one accepted.
- TOTP enrollment with a code computed independently of the API, the two-step login, the replay rejection, and a backup code that works once.
- An admin is blocked until enrollment, then gets 200, and cannot disable 2FA.

Google sign-in is covered by unit tests and API gating. It needs a real OAuth client ID for end-to-end verification (phase 16).

Screenshots:
- `docs/screenshots/phase-11-mfa-step-m.png`
- `docs/screenshots/phase-11-security-setup-m.png`
- `docs/screenshots/phase-11-dashboard-unverified-d.png`
- `docs/screenshots/phase-11-verify-invalid-m.png`

## Acceptance

- [x] Email verification gates event submission, with single-use, expiring, hashed tokens
- [x] Forgot and reset password without account enumeration
- [x] TOTP (RFC 6238) with a drift window, a replay guard, and hashed backup codes
- [x] Administrators must pass TOTP, enforced by the API policy and the frontend guard
- [x] Google sign-in with verified-email linking and profile completion
- [x] Regression suites pass (backend and frontend)
- [ ] Google sign-in verified with a production OAuth client ID (phase 16)
