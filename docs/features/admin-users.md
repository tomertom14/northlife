# Admin user management and audit log

Administrators can find accounts, suspend and restore them, change roles, and see an append-only record of every administrative action. Introduced in phase 12.

## What an administrator sees

- **Users** (`/manage/admin/users`):
  - Search by name, email or business, and filter by role and by status (active, suspended, unverified email).
  - Each row shows the role, suspension and verification badges, whether 2FA is on, and how many events the user has published or has pending.
  - "Show more" loads the next page.
- **A user** (`/manage/admin/users/{id}`):
  - Account facts, the user's events with their status, and the account's history.
  - Actions: change role, suspend with a required reason, or lift a suspension.
  - An administrator cannot suspend or re-role their own account.
- **Audit log** (`/manage/admin/audit`): every administrative action, newest first. Entries cannot be edited or deleted.
- **Unusual traffic** card on the moderation page (phase 13): events whose views spiked in the last hour.

## What suspension does

When an account is suspended, all of this happens at the same moment:
- The owner's current session stops working. The next request returns 401.
- Signing in again fails with `403 account_suspended`, after the password step and also on Google sign-in.
- Every event of that owner disappears from public view: the feed, event pages, the map, editors' picks, recommendations and the event images.
  - Public queries add `Owner.SuspendedAtUtc IS NULL`.
  - The image endpoint applies the same visibility rule.
- Lifting the suspension reverses all of it. Nothing is deleted.

## How it works

### Keyset pagination

The user list and the audit log page with a cursor instead of an offset:
- The cursor encodes the last row's `(created_at, id)`.
- The next page is `WHERE (created_at, id) < (@cursorCreatedAt, @cursorId) ORDER BY created_at DESC, id DESC LIMIT n + 1`.
- PostgreSQL compares the row values directly, and the composite index `(created_at_utc, id)` serves it.
- Fetching `n + 1` rows tells whether there is another page without counting.

Compared with `OFFSET`:
- Page k costs O(log n + page size) instead of O(k · page size).
- Rows inserted while someone is paging never shift items between pages or show one twice.

The cursor is opaque Base64url. A malformed cursor simply falls back to the first page.

### Guard rules and concurrency

The rules are small pure functions (`AdminUserRules`), unit-tested on their own:
- You cannot suspend yourself or change your own role.
- You cannot suspend an account that is already suspended.
- The change must leave at least one active administrator.

The last rule is subject to **write skew**. Two administrators, A and B, each demote the other at the same moment:
- Each transaction reads "two active admins", so each check passes.
- Each writes a different row, so row locks never conflict.
- Both commit, and the system has no administrator.

READ COMMITTED and REPEATABLE READ both allow this. The changes therefore run under **SERIALIZABLE** isolation. PostgreSQL's serializable snapshot isolation detects the read-write dependency cycle and aborts one transaction with SQLSTATE 40001, which the API reports as `409 concurrent_change`.

The end-to-end script fires both demotions simultaneously. In repeated runs exactly one committed and the other got 409; which one won varied between runs. The remaining administrator restores the demoted one afterwards.

### Session revocation

Suspension, unsuspension and role changes rotate the account's security stamp, so its JWTs fail immediately. The mechanism is described in [identity.md](identity.md#revocable-sessions).

### Audit log

`audit_entries` is append-only: actor, action, target type and id, JSON details (`jsonb`), and time.

| Action | Details |
| --- | --- |
| `user.suspended` | reason |
| `user.unsuspended` | previous reason |
| `user.role_changed` | from, to |
| `event.created` | title |
| `event.updated` | title, revision |
| `event.approved`, `event.rejected`, `event.highlighted`, `event.unhighlighted` | title, status before and after, rejection reason |
| `event.deleted` | title |
| `automoderation.settings_changed` | each changed setting with its old and new value (phase 17) |
| `automoderation.run` | mode and counts of a manual run (phase 17) |

- An entry without an actor was written by the automatic event approval service (phase 17, [auto-moderation.md](auto-moderation.md)); its approvals also carry `automatic: true`. The pages show the actor as "אישור אוטומטי".
- The entry is added to the same unit of work as the change, so it commits or rolls back with it.
- No endpoint updates or deletes entries.
- The user page shows entries where the user is the target or the actor.

## API

All endpoints require the `AdminWithMfa` policy.

| Endpoint | Notes |
| --- | --- |
| `GET /api/admin/users?search&role&status&cursor&pageSize` | Page size 20, at most 50. |
| `GET /api/admin/users/{id}` | Details, up to 50 events, and the 20 newest audit entries. |
| `POST /api/admin/users/{id}/suspend` | `{ reason }`, 1 to 500 characters. |
| `POST /api/admin/users/{id}/unsuspend` | |
| `POST /api/admin/users/{id}/role` | `{ role: "BusinessOwner" or "Admin" }` |
| `GET /api/admin/audit?cursor&pageSize` | |

Errors carry a `code`:

| Code | Status |
| --- | --- |
| `cannot_suspend_self` | 400 |
| `cannot_change_own_role` | 400 |
| `already_suspended` | 400 |
| `reason_required` | 400 |
| `last_admin` | 409 |
| `concurrent_change` | 409 |

## Tests

- Unit (`AdminUserTests`): guard rules, keyset cursor round-trip and rejection, revoked sessions returning 401.
- End to end (`scripts/e2e/phase-12-admin-users.ps1`, 44 checks):
  - Access control.
  - Search, and pagination consistency: pages of size 1 equal a page of size 2, and a full walk visits each user once, newest first.
  - Suspension effects on sessions, sign-in, the feed, event pages and images.
  - Guards.
  - Role changes with token revocation and the TOTP gate for new admins.
  - The simultaneous-demotion race.
  - The second-factor lockout.
  - The audit trail.
