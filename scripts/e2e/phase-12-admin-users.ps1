<#
  End-to-end check of Phase 12 (admin user management) against a running stack with Mailpit.
  The administrator must already have TOTP enrolled (Phase 11); pass its Base32 secret.
  Usage: powershell -File scripts/e2e/phase-12-admin-users.ps1 -AdminEmail admin@northlife.local `
           -AdminPassword '...' -AdminTotpSecret 'ABCD EFGH ...'
  Exits non-zero when any check fails.
#>
param(
  [string]$BaseUrl = 'http://localhost:10000',
  [string]$MailpitUrl = 'http://localhost:8025',
  [Parameter(Mandatory)][string]$AdminEmail,
  [Parameter(Mandatory)][string]$AdminPassword,
  [Parameter(Mandatory)][string]$AdminTotpSecret,
  [string]$ImagePath = ''
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ImagePath) { $ImagePath = Join-Path $here '..\..\docs\screenshots\phase-5-dashboard.png' }
. (Join-Path $here 'e2e-common.ps1')

$password = 'StrongPass123'

Write-Host "`n== Administrator session"
$adminSession = Connect-WithTotp $AdminEmail $AdminPassword $AdminTotpSecret
Check 'admin signs in with password and TOTP' ($adminSession.Status -eq 200 -and $adminSession.Json.user.mfaVerified -eq $true)
$admin = $adminSession.Json.token
$adminId = $adminSession.Json.user.id

Write-Host "`n== Setup: an owner with a published event"
$ownerA = New-VerifiedOwner 'suspend' $password
$image = New-Image $ownerA.Token $ImagePath
$startAt = [DateTimeOffset]::UtcNow.AddDays(2)
$eventBody = @{ title = 'אירוע לבדיקת השעיה'; description = 'נוצר בבדיקת קצה לקצה של שלב 12'; category = 'Culture'; venueName = 'מרכז'; locality = 'צפת'; address = 'רחוב 1'; latitude = 32.96; longitude = 35.49; startAt = $startAt.ToString('o'); endAt = $startAt.AddHours(2).ToString('o'); price = 0; imageId = $image.id; organizerName = 'עסק לבדיקה'; tags = @() }
$created = Call POST '/api/manage/events' $eventBody $ownerA.Token
$approved = Call POST "/api/admin/events/$($created.Json.id)/approve" @{ revision = $created.Json.revision } $admin
Check 'owner event is approved and public' ($approved.Status -eq 200 -and (Call GET "/api/events/$($created.Json.id)").Status -eq 200)
$eventDay = [DateTimeOffset]::UtcNow.AddDays(2).ToOffset([TimeSpan]::FromHours(3)).ToString('yyyy-MM-dd')
function Feed-HasEvent { ((Call GET "/api/events?period=range&from=$eventDay&to=$eventDay&pageSize=100").Json.items | Where-Object id -eq $created.Json.id) -ne $null }
Check 'event appears in the public feed for its day' (Feed-HasEvent)

Write-Host "`n== Access control"
Check 'a business owner cannot list users (403)' ((Call GET '/api/admin/users' $null $ownerA.Token).Status -eq 403)
Check 'anonymous callers cannot list users (401)' ((Call GET '/api/admin/users').Status -eq 401)

Write-Host "`n== User list: search and keyset pagination"
$found = Call GET "/api/admin/users?search=$([uri]::EscapeDataString($ownerA.Email))" $null $admin
Check 'search by email finds exactly the new owner' ($found.Status -eq 200 -and $found.Json.items.Count -eq 1 -and $found.Json.items[0].id -eq $ownerA.Id)
Check 'list row shows the published event count' ($found.Json.items[0].publishedEvents -eq 1)
$two = (Call GET '/api/admin/users?pageSize=2' $null $admin).Json
$page1 = (Call GET '/api/admin/users?pageSize=1' $null $admin).Json
$page2 = (Call GET "/api/admin/users?pageSize=1&cursor=$($page1.nextCursor)" $null $admin).Json
Check 'page 1 and page 2 of size 1 equal the first page of size 2' ($page1.nextCursor -and $page1.items[0].id -eq $two.items[0].id -and $page2.items[0].id -eq $two.items[1].id)
$seen = @{}; $previous = [DateTimeOffset]::MaxValue; $ordered = $true; $cursor = $null; $pages = 0
do {
  $path = '/api/admin/users?pageSize=3' + $(if ($cursor) { "&cursor=$cursor" } else { '' })
  $page = (Call GET $path $null $admin).Json
  foreach ($item in $page.items) {
    if ($seen.ContainsKey($item.id)) { $ordered = $false }
    $seen[$item.id] = $true
    $createdAt = [DateTimeOffset]::Parse($item.createdAt)
    if ($createdAt -gt $previous) { $ordered = $false }
    $previous = $createdAt
  }
  $cursor = $page.nextCursor; $pages++
} while ($cursor -and $pages -lt 200)
$all = (Call GET '/api/admin/users?pageSize=50' $null $admin).Json
Check "walking every page visits each user once, newest first ($($seen.Count) users, $pages pages)" ($ordered -and ($all.nextCursor -or $seen.Count -eq $all.items.Count))
Check 'a malformed cursor falls back to the first page' ((Call GET '/api/admin/users?pageSize=1&cursor=not-a-cursor' $null $admin).Json.items[0].id -eq $page1.items[0].id)

Write-Host "`n== Suspension"
Check 'owner session is valid before suspension' ((Call GET '/api/auth/session' $null $ownerA.Token).Status -eq 200)
$noReason = Call POST "/api/admin/users/$($ownerA.Id)/suspend" @{ reason = '  ' } $admin
Check 'suspension without a reason is refused (400 reason_required)' ($noReason.Status -eq 400 -and $noReason.Json.code -eq 'reason_required')
Check 'admin suspends the owner (204)' ((Call POST "/api/admin/users/$($ownerA.Id)/suspend" @{ reason = 'ספאם חוזר בבדיקה' } $admin).Status -eq 204)
Check 'the owner live token is rejected at once (401)' ((Call GET '/api/auth/session' $null $ownerA.Token).Status -eq 401)
$blocked = Call POST '/api/auth/login' @{ email = $ownerA.Email; password = $password }
Check 'suspended owner cannot sign in (403 account_suspended)' ($blocked.Status -eq 403 -and $blocked.Json.code -eq 'account_suspended')
Check 'suspended owner event details are hidden (404)' ((Call GET "/api/events/$($created.Json.id)").Status -eq 404)
Check 'suspended owner event leaves the public feed' (-not (Feed-HasEvent))
Check 'suspended owner event image is no longer public (404)' ((Call GET "/api/images/$($image.id)").Status -eq 404)
$suspendedList = (Call GET "/api/admin/users?status=suspended&search=$([uri]::EscapeDataString($ownerA.Email))" $null $admin).Json
Check 'status filter lists the owner as suspended' ($suspendedList.items.Count -eq 1 -and $suspendedList.items[0].suspended -eq $true)
$again = Call POST "/api/admin/users/$($ownerA.Id)/suspend" @{ reason = 'שוב' } $admin
Check 'suspending twice is refused (400 already_suspended)' ($again.Status -eq 400 -and $again.Json.code -eq 'already_suspended')
Check 'admin cannot suspend themself (400 cannot_suspend_self)' ((Call POST "/api/admin/users/$adminId/suspend" @{ reason = 'בדיקה' } $admin).Json.code -eq 'cannot_suspend_self')
Check 'admin unsuspends the owner (204)' ((Call POST "/api/admin/users/$($ownerA.Id)/unsuspend" $null $admin).Status -eq 204)
Check 'owner signs in again after unsuspension' ((Call POST '/api/auth/login' @{ email = $ownerA.Email; password = $password }).Status -eq 200)
Check 'owner event is public again' ((Call GET "/api/events/$($created.Json.id)").Status -eq 200 -and (Feed-HasEvent))

Write-Host "`n== Password reset revokes other sessions"
$ownerB = New-VerifiedOwner 'promote' $password
$null = Call POST '/api/auth/forgot-password' @{ email = $ownerB.Email }
$resetToken = Get-MailToken $ownerB.Email '/manage/reset-password'
$null = Call POST '/api/auth/reset-password' @{ token = $resetToken; password = 'ResetPass789' }
Check 'a token issued before the reset is rejected (401)' ((Call GET '/api/auth/session' $null $ownerB.Token).Status -eq 401)
$password = 'ResetPass789'
$ownerBToken = (Call POST '/api/auth/login' @{ email = $ownerB.Email; password = $password }).Json.token

Write-Host "`n== Role change"
Check 'admin cannot change their own role (400 cannot_change_own_role)' ((Call POST "/api/admin/users/$adminId/role" @{ role = 'BusinessOwner' } $admin).Json.code -eq 'cannot_change_own_role')
Check 'admin promotes the second owner to Admin (204)' ((Call POST "/api/admin/users/$($ownerB.Id)/role" @{ role = 'Admin' } $admin).Status -eq 204)
Check 'the promoted user old token is rejected (401)' ((Call GET '/api/auth/session' $null $ownerBToken).Status -eq 401)
$promoted = Call POST '/api/auth/login' @{ email = $ownerB.Email; password = $password }
Check 'new admin signs in without MFA as role Admin' ($promoted.Json.user.role -eq 'Admin' -and $promoted.Json.user.mfaVerified -eq $false)
$gate = Call GET '/api/admin/users' $null $promoted.Json.token
Check 'new admin is held at the TOTP gate (403 mfa_required)' ($gate.Status -eq 403 -and $gate.Json.code -eq 'mfa_required')
$setupB = (Call POST '/api/auth/security/totp/setup' $null $promoted.Json.token).Json
$enabledB = Call POST '/api/auth/security/totp/enable' @{ code = (Get-Totp $setupB.secret) } $promoted.Json.token
$adminB = $enabledB.Json.auth.token
Check 'enabling TOTP revokes the pre-TOTP session (401)' ((Call GET '/api/auth/session' $null $promoted.Json.token).Status -eq 401)
Check 'after TOTP the new admin can list users' ((Call GET '/api/admin/users?pageSize=1' $null $adminB).Status -eq 200)

Write-Host "`n== Two admins demote each other at the same moment"
# Each check alone passes (the other admin remains), so only SERIALIZABLE isolation stops both
# commits and a system with no active administrator.
$race = Invoke-Simultaneously @(
  @{ Path = "/api/admin/users/$($ownerB.Id)/role"; Body = @{ role = 'BusinessOwner' }; Token = $admin },
  @{ Path = "/api/admin/users/$adminId/role"; Body = @{ role = 'BusinessOwner' }; Token = $adminB }
)
Write-Host "INFO  race statuses: bootstrap admin demotes B = $($race[0]), B demotes bootstrap admin = $($race[1])"
Check 'at most one of the two demotions commits' (-not ($race[0] -eq 204 -and $race[1] -eq 204))
Check 'the losing request is refused with 401, 409 or last_admin, never 500' (@($race | Where-Object { $_ -notin 204, 400, 401, 409 }).Count -eq 0)
# Count straight after the race, with whichever administrator still holds a valid session.
$survivor = if ($race[1] -eq 204) { $adminB } else { $admin }
$activeAdmins = (Call GET '/api/admin/users?role=Admin&status=active&pageSize=50' $null $survivor).Json.items
Check "at least one active administrator remains ($(@($activeAdmins).Count))" (@($activeAdmins).Count -ge 1)
if ($race[1] -eq 204) {
  # B won: B restores the bootstrap administrator, who signs in again.
  $null = Call POST "/api/admin/users/$adminId/role" @{ role = 'Admin' } $adminB
  $admin = (Connect-WithTotp $AdminEmail $AdminPassword $AdminTotpSecret).Json.token
}
if ($race[0] -ne 204) { $null = Call POST "/api/admin/users/$($ownerB.Id)/role" @{ role = 'BusinessOwner' } $admin }
Check 'a demoted admin token cannot act any more (401)' ((Call POST "/api/admin/users/$adminId/role" @{ role = 'BusinessOwner' } $adminB).Status -eq 401)

Write-Host "`n== Second-factor lockout"
$challengeB = Call POST '/api/auth/login' @{ email = $ownerB.Email; password = $password }
$wrong = 1..5 | ForEach-Object { (Call POST '/api/auth/mfa' @{ mfaToken = $challengeB.Json.mfaToken; code = '000000' }).Status }
Check 'five wrong codes are each refused (401)' (@($wrong | Where-Object { $_ -ne 401 }).Count -eq 0)
$locked = Call POST '/api/auth/mfa' @{ mfaToken = $challengeB.Json.mfaToken; code = (Get-Totp $setupB.secret 1) }
Check 'the sixth attempt is locked even with a valid code (429 mfa_locked)' ($locked.Status -eq 429 -and $locked.Json.code -eq 'mfa_locked')

Write-Host "`n== Audit log"
$audit = (Call GET '/api/admin/audit?pageSize=50' $null $admin).Json.items
$suspendEntry = $audit | Where-Object { $_.action -eq 'user.suspended' -and $_.targetId -eq $ownerA.Id } | Select-Object -First 1
Check 'suspension is audited with actor and reason' ($suspendEntry -and $suspendEntry.actorId -eq $adminId -and $suspendEntry.details.reason -eq 'ספאם חוזר בבדיקה')
Check 'unsuspension is audited' (@($audit | Where-Object { $_.action -eq 'user.unsuspended' -and $_.targetId -eq $ownerA.Id }).Count -eq 1)
$roleEntry = $audit | Where-Object { $_.action -eq 'user.role_changed' -and $_.targetId -eq $ownerB.Id -and $_.details.to -eq 'Admin' } | Select-Object -First 1
Check 'role change is audited with before and after' ($roleEntry -and $roleEntry.details.from -eq 'BusinessOwner')
Check 'event approval is audited' (@($audit | Where-Object { $_.action -eq 'event.approved' -and $_.targetId -eq $created.Json.id }).Count -eq 1)
$detail = (Call GET "/api/admin/users/$($ownerA.Id)" $null $admin).Json
Check 'user detail shows the owner events and account history' ($detail.events.Count -eq 1 -and @($detail.audit | Where-Object action -like 'user.*').Count -eq 2)
$auditPage1 = (Call GET '/api/admin/audit?pageSize=2' $null $admin).Json
$auditPage2 = (Call GET "/api/admin/audit?pageSize=2&cursor=$($auditPage1.nextCursor)" $null $admin).Json
Check 'audit log pages without overlap' ($auditPage1.nextCursor -and -not ($auditPage2.items.id | Where-Object { $_ -in $auditPage1.items.id }))

Write-Host "`n$(if ($failures) { "$failures check(s) failed" } else { 'All checks passed' })"
exit $failures
