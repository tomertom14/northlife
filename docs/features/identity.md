# Identity: verified accounts, Google sign-in and two-factor authentication

This feature controls who can publish on NorthLife and how they sign in. It covers:
- Email verification.
- Password reset.
- Google sign-in.
- TOTP two-factor authentication, which is mandatory for administrators.
- Revocable sessions.

Introduced in phase 11; session revocation and the second-factor lockout were added in phase 12.

## What a user sees

- **Registering** creates a business-owner account and sends a Hebrew verification email.
  - Until the address is confirmed, the owner can sign in and look around.
  - Submitting an event fails with `403 email_not_verified`.
  - The dashboard shows a banner with a "send again" button.
- **Forgot password** always answers the same way, whether or not the address has an account, so the form cannot be used to discover who is registered.
  - The email link opens a form for a new password.
  - Using it signs out every other session.
- **Google sign-in** works for new and existing users.
  - A new user completes a short business profile (name, business, phone).
  - An existing account is linked only when Google says the email address is verified.
- **Two-factor authentication** (the "Account security" page):
  - The user scans a QR code into any authenticator app (Google Authenticator, Microsoft Authenticator, 1Password and so on).
  - They confirm with a six-digit code and receive ten single-use backup codes.
  - After that, sign-in asks for a code after the password.
  - Owners may switch it off with a valid code. Administrators cannot, and the admin area refuses sessions that did not pass the second step.

## How it works

### Tokens in emails

Verification and reset links carry a random token:
- 32 bytes from a cryptographic random generator, Base64url-encoded.
- The database stores only its SHA-256 hash (`user_tokens.token_hash`), so a database leak does not expose working links.
- Each token has a purpose and an expiry: verification lasts 24 hours, reset 30 minutes.

Redeeming a token is one conditional statement:

```sql
UPDATE user_tokens SET used_at_utc = now()
WHERE token_hash = @hash AND purpose = @purpose AND used_at_utc IS NULL AND expires_at_utc > now()
```

- It succeeds only if exactly one row changed. Two simultaneous clicks cannot both succeed, so there is no race between checking and using.
- The reset form checks the password rule **before** spending the token, so a weak password does not burn the link.

### TOTP (RFC 6238), implemented from the specification

- **Secret:** 20 random bytes, shown to the user in Base32 (RFC 4648) inside an `otpauth://totp/NorthLife:{email}?secret=…&issuer=NorthLife` URI and a QR code.
- **Code at time t:** the HOTP value (RFC 4226) of the counter ⌊t / 30 s⌋:
  1. HMAC-SHA1(secret, counter as 8 big-endian bytes).
  2. Dynamic truncation: take 31 bits at the offset given by the last nibble.
  3. The result modulo 10^6, zero-padded to six digits.
- **Clock drift:** codes from the previous and next 30-second steps are accepted as well (±1 step).
- **Replay guard:** the accepted step number is stored (`totp_last_used_step`), and no step at or before it is accepted again. A code seen over someone's shoulder is useless once used.
- **Storage:** secrets are encrypted at rest with ASP.NET Data Protection.
  - The keys live in `DataProtection:KeysPath`, a persistent disk in production.
  - Without persistent keys, a restart would make every enrolled secret unreadable.
- **Backup codes:** ten 10-character codes, stored hashed and redeemed with the same conditional update as email tokens.
- **Lockout:** after 5 wrong codes within 15 minutes the account's second factor locks (`429 mfa_locked`).
  - The per-address rate limiter alone would let an attacker with the password spread guesses over many addresses.
  - With three valid codes out of 10^6 per check, one 15-minute window gives an attacker at most 5 × 3 / 10^6 = 1.5 × 10^-5 chance.

### Two-step sign-in

1. The password step (`POST /api/auth/login`) checks the password and the suspension flag.
   - Without 2FA it returns a session.
   - With 2FA it returns `mfa_required` and a five-minute **MFA ticket**: a Data Protection-encrypted, purpose-bound blob holding the user id, not a session.
2. `POST /api/auth/mfa` takes the ticket and a code (TOTP or backup) and returns the session.
   - The session JWT then carries `northlife:mfa=true`.
   - The `AdminWithMfa` policy on every `/api/admin/*` endpoint requires the Admin role **and** that claim.
   - A missing claim gets `403 mfa_required`, which the frontend turns into a redirect to the security page.

### Google sign-in

- The browser gets an ID token from Google Identity Services.
- The API validates it with `GoogleJsonWebSignature.ValidateAsync`: signature, issuer, expiry and audience equal to our client ID.
- A pure decision function (`GoogleAccountResolver.Decide`) covers every case:

| Linked Google account? | Email already registered? | Google says verified? | Outcome |
| --- | --- | --- | --- |
| yes | any | any | Sign in |
| no | yes | yes | Link, then sign in |
| no | yes | no | Refuse: stops takeover of an account with someone else's unverified address |
| no | no | any | Ask for the business profile |

- New users get a 15-minute **signup ticket** (another purpose-bound Data Protection blob). It cannot be used as an MFA ticket, because the purposes differ.
- Google users have no password.

### Revocable sessions

Sessions are stateless JWTs (60 minutes), which normally cannot be revoked. To make them revocable:
- Every account has a random **security stamp**, and every JWT carries it (`northlife:stamp`).
- After the signature check, `JwtBearerEvents.OnTokenValidated` compares the token's stamp with the account's. It also rejects suspended accounts.
- The lookup is cached for 30 seconds per user.
- The stamp is rotated on suspension, unsuspension, role change, password reset and enabling or disabling 2FA.
- The instance that made the change evicts its cache entry immediately, so the old token fails at once. Other instances notice within 30 seconds.
- The stamp comparison uses a fixed-time equality check.

## API

| Endpoint | Notes |
| --- | --- |
| `POST /api/auth/register` | Creates an unverified owner and emails the link. Rate limit `auth`. |
| `POST /api/auth/verify-email` | `{ token }`, single use. |
| `POST /api/auth/resend-verification` | Signed in. Rate limit `email`, 5 per 15 minutes. |
| `POST /api/auth/forgot-password` | Always 204. |
| `POST /api/auth/reset-password` | `{ token, password }`. Rotates the security stamp. |
| `POST /api/auth/login` | Returns `authenticated` or `mfa_required`. |
| `POST /api/auth/mfa` | `{ mfaToken, code }`. |
| `POST /api/auth/google` and `/api/auth/google/complete` | The ID-token flow and profile completion. |
| `GET /api/auth/session` | The current user with `emailConfirmed`, `totpEnabled` and `mfaVerified`. |
| `GET /api/auth/security`, `POST /api/auth/security/totp/setup`, `/enable`, `/disable` | TOTP management. |

## Configuration

| Setting | Purpose |
| --- | --- |
| `Email__Provider` | `Smtp` (Mailpit locally), `Brevo` in production, or `Log`. |
| `Email__Smtp__Host`, `Email__Smtp__Port`, `Email__BrevoApiKey`, `Email__From`, `Email__PublicBaseUrl` | Mail delivery and link base URL. |
| `Google__ClientId` | Google sign-in is disabled when empty. |
| `DataProtection__KeysPath` | Must be persistent in production (`/var/data/keys` on Render). |
| `RateLimiting__AuthPermitsPerMinute` (10), `RateLimiting__EmailPermitsPer15Minutes` (5) | Rate limits. |

## Tests

- Unit (`IdentityAlgorithmTests`):
  - All six RFC 6238 SHA-1 test vectors.
  - Drift of ±1 step is accepted and ±2 rejected; replay is rejected; malformed codes are rejected.
  - RFC 4648 Base32 vectors.
  - Token hashing, and backup-code normalisation (case, hyphens, spaces).
  - Every Google linking branch.
  - Ticket tampering and purpose separation.
  - Throttle lockout, per-account counting and reset.
- API (`IdentityEndpointTests`, `SessionRevocationTests`): validation, gating, and revoked stamps returning 401.
- End to end (`scripts/e2e/phase-11-identity.ps1`, 26 checks):
  - Registration, blocked submission, email in Mailpit, single-use verification.
  - The reset flow.
  - TOTP enrollment with a code computed independently in PowerShell, and the replay rejection.
  - A backup code that works once.
  - The admin blocked until enrolled.
- Phase 12's script checks that a password reset, enabling TOTP and a role change each revoke earlier tokens, and that five wrong codes lock the second factor.
- Google sign-in end to end needs a real OAuth client ID. It is listed in the deployment checklist.
