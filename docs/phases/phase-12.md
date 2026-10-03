# Phase 12 Verification: Admin User Management

## Goal and prerequisites

Administrators can find, inspect, suspend and re-role accounts safely. Every action is audited, and a suspension or role change ends the user's live sessions at once, even though sessions are stateless JWTs. Builds on phase 11 (identity), which made TOTP mandatory for administrators.

## Files and components changed

- Backend:
  - `Models/AppUser.cs`: `SecurityStamp`, `SuspendedAtUtc` and `SuspensionReason`.
  - The new `Models/AuditEntry.cs`, an append-only table with `jsonb` details.
  - Migration `AddUserAdministration`: adds the columns, backfills a random stamp for existing users, and creates `audit_entries` plus the `(created_at, id)` keyset indexes.
  - `Authentication/SessionValidator.cs` and `JwtBearerEvents.OnTokenValidated` in `Program.cs`: token revocation through the security stamp.
  - `AuthTokenService` writes the `northlife:stamp` claim.
  - `Services/AdminUserService.cs`:
    - Guard rules in `AdminUserRules`.
    - Keyset listing, detail, suspend, unsuspend and role change.
    - `SERIALIZABLE` transactions.
  - `Services/KeysetCursor.cs` and `Services/AuditLog.cs`.
  - `Controllers/AdminUsersController.cs`, under the `AdminWithMfa` policy.
  - `AdminEventService` now audits create, update, approve, reject, highlight and delete.
  - `PublicEventQueryService` and `ImagesController` hide the events and images of suspended owners.
  - Sign-in (`AuthService`) refuses suspended accounts. Password reset and TOTP enable or disable rotate the stamp.
  - `Identity/SecondFactorThrottle.cs`: locks the second factor after 5 wrong codes per account in 15 minutes (`429 mfa_locked`).
- Frontend:
  - `admin/admin-users-api.ts` holds the API client, the audit labels and `describeAudit`.
  - `manage/admin-nav.ts` adds the section tabs: queue, users, audit log.
  - New pages:
    - `admin-users-page`: search, role and status filters, and "show more" through the keyset cursor.
    - `admin-user-page`: account facts, role change, suspension with a reason, the user's events and account history.
    - `admin-audit-page`.
  - The login and security pages explain the second-factor lockout.
- Tooling:
  - `scripts/e2e/e2e-common.ps1` holds the shared end-to-end helpers.
  - `scripts/e2e/phase-12-admin-users.ps1` is the new end-to-end script.
  - The CI workflow gains suspension and audit checks.

## Behaviour and API

| Endpoint | Behaviour |
| --- | --- |
| `GET /api/admin/users?search=&role=&status=&cursor=&pageSize=` | Newest first, up to 50 per page. `status` is `active`, `suspended` or `unverified`. `nextCursor` continues after the last row. |
| `GET /api/admin/users/{id}` | Account facts, Google link, up to 50 events, event counts, and the 20 newest audit entries where the user is the target or the actor. |
| `POST /api/admin/users/{id}/suspend` `{ reason }` | A reason of 1 to 500 characters is required. Rotates the stamp, so live tokens fail at once. |
| `POST /api/admin/users/{id}/unsuspend` | Lifts the suspension and rotates the stamp again. |
| `POST /api/admin/users/{id}/role` `{ role }` | Changes the role and rotates the stamp. |
| `GET /api/admin/audit?cursor=` | Read-only audit log, newest first, with keyset pagination. |

Guard codes:

| Code | Status | Meaning |
| --- | --- | --- |
| `cannot_suspend_self` | 400 | The admin tried to suspend their own account. |
| `cannot_change_own_role` | 400 | The admin tried to change their own role. |
| `reason_required` | 400 | The suspension reason is empty or longer than 500 characters. |
| `already_suspended` | 400 | The account is already suspended. |
| `last_admin` | 409 | The change would leave no active administrator. |
| `concurrent_change` | 409 | PostgreSQL aborted the transaction with a serialization failure (SQLSTATE 40001). |

How revocation works:
- Every JWT carries the account's security stamp.
- After the signature check, `OnTokenValidated` compares that stamp with the database value, which is cached for 30 seconds per user. It also rejects suspended accounts.
- A change on the same instance evicts the cache entry at once. Other instances notice within 30 seconds.

Why SERIALIZABLE:
- The "at least one active admin" rule reads the set of admins, then writes a different row.
- Two admins who demote each other at the same moment each see the other still active. That is write skew, which neither row locks nor READ COMMITTED prevent.
- PostgreSQL's serializable snapshot isolation detects the read-write cycle and aborts one of the two transactions.

## Verification

```powershell
dotnet test NorthLife.slnx --configuration Release           # 96 passed
npm --prefix frontend test -- --watch=false                  # 47 passed
npm --prefix frontend run build                              # initial 373.62 kB
powershell -File scripts/e2e/phase-12-admin-users.ps1 -AdminEmail <admin> -AdminPassword <password> -AdminTotpSecret <base32>
powershell -File scripts/e2e/phase-11-identity.ps1 -AdminEmail <admin> -AdminPassword <password>   # regression
```

Unit tests:
- Guard rules: self-suspension, a second suspension, last admin, own role, and a no-op role change.
- The keyset cursor round-trips and rejects malformed input.
- Revoked or rotated stamps get 401 through the API factory.
- The second-factor throttle locks after 5 failures, keeps a separate count per account, and resets on success.
- The frontend API client sends only the filters that are set. `describeAudit` covers every action type.

End-to-end on the Docker stack (production image, PostgreSQL 17, Mailpit): **44 of 44 checks passed**.
- Access control:
  - An owner gets 403 on `/api/admin/users`.
  - An anonymous caller gets 401.
- Search and pagination:
  - Search by email finds exactly one account.
  - Pages of size 1 match a page of size 2.
  - A full walk visits every user once, newest first.
  - A malformed cursor falls back to the first page.
- Suspension:
  - The owner's live token gets 401 at once, and the owner's login gets `403 account_suspended`.
  - The event's details, its place in the feed, and its image disappear from public view.
  - A second suspension and self-suspension are refused.
  - Unsuspension restores sign-in and visibility.
- Stamp rotation:
  - Password reset, promotion and TOTP enrollment each revoke earlier tokens.
  - A newly promoted admin is held at the TOTP gate until enrolled.
- Race: two admins demote each other at the same moment, over two runs.
  - Run 1: the bootstrap admin's request returned 204 and B's returned 409.
  - Run 2: the bootstrap admin's request returned 409 and B's returned 204.
  - In both runs exactly one demotion committed, and one active administrator remained.
- Lockout: five wrong codes get 401 each, and the sixth attempt gets `429 mfa_locked` even with a valid code.
- Audit:
  - Suspension, unsuspension, role change and approval each record the actor and details.
  - The user detail shows the account history.
  - Audit pages do not overlap.

Fresh database: the production image ran `--migrate` against a new, empty database.
- All 8 tables were created, along with the `security_stamp`, `suspended_at_utc` and `suspension_reason` columns.
- The `audit_entries` indexes (`ix_audit_entries_actor_id`, `ix_audit_entries_created_id`, `ix_audit_entries_target_id`) and `ix_users_created_id` were created.
- EF reported no pending model changes.

Screenshots:
- `docs/screenshots/phase-12-users-d.png` and `docs/screenshots/phase-12-users-m.png`
- `docs/screenshots/phase-12-user-suspended-d.png`
- `docs/screenshots/phase-12-user-detail-d.png`
- `docs/screenshots/phase-12-suspend-form-m.png`
- `docs/screenshots/phase-12-audit-d.png`
- `docs/screenshots/phase-12-admin-tabs-d.png`

## Acceptance

- [x] Users list with search, filters and keyset pagination
- [x] Suspend and unsuspend with a required reason; suspended owners cannot sign in and their events and images leave public view
- [x] Role change with self and last-admin guards, safe under concurrent requests (SERIALIZABLE)
- [x] Live JWTs revoked on suspension, role change, password reset and TOTP changes
- [x] Append-only audit log for user and event administration, with a read-only admin view
- [x] Per-account lockout of the second factor
- [x] Regression suites pass (backend, frontend, phase 11 end-to-end)
