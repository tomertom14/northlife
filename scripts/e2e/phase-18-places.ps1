<#
  End-to-end check of Phase 18 (places) against a running stack with Mailpit.
  The administrator must already have TOTP enrolled (Phase 11); pass its Base32 secret.
  The script creates two verified owners with their own places and one event, and deletes them at the end.

  Usage: powershell -File scripts/e2e/phase-18-places.ps1 -AdminEmail admin@northlife.local `
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
$marker = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$israel = [TimeZoneInfo]::FindSystemTimeZoneById('Israel Standard Time')
$local = [TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow, $israel)
$today = [int]$local.DayOfWeek

function Place-Body([string]$Name, [string]$ImageId, [object[]]$Hours, [int]$Revision = 0) {
  $body = @{
    name = "$Name $marker"; category = 'Cafe'; description = "מקום לבדיקה $marker"; locality = 'תל חי'
    address = 'רחוב הבדיקה 1'; latitude = 33.2345; longitude = 35.5801; phone = '04-6900000'
    website = 'https://example.com'; instagram = '@test.place'; studentPerk = '10% הנחה'; imageId = $ImageId; hours = $Hours
  }
  if ($Revision -gt 0) { $body.revision = $Revision }
  $body
}

# Status code of an anonymous GET, on Windows PowerShell 5.1 and PowerShell 7 alike.
function Status-Of([string]$Url) {
  try { [int](Invoke-WebRequest $Url -UseBasicParsing).StatusCode }
  catch { if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { throw } }
}

function Distance([double]$Lat1, [double]$Lon1, [double]$Lat2, [double]$Lon2) {
  $rad = [Math]::PI / 180
  $a = [Math]::Pow([Math]::Sin(($Lat2 - $Lat1) * $rad / 2), 2) +
    [Math]::Cos($Lat1 * $rad) * [Math]::Cos($Lat2 * $rad) * [Math]::Pow([Math]::Sin(($Lon2 - $Lon1) * $rad / 2), 2)
  2 * 6371.0088 * [Math]::Asin([Math]::Min(1.0, [Math]::Sqrt($a)))
}

Write-Host "== Setup"
$adminSession = Connect-WithTotp $AdminEmail $AdminPassword $AdminTotpSecret
$admin = $adminSession.Json.token
Check 'admin signs in with password and TOTP' ($adminSession.Status -eq 200 -and $admin)
$owner = New-VerifiedOwner 'places' $password
$other = New-VerifiedOwner 'otherplaces' $password
$image = New-Image $owner.Token $ImagePath
$otherImage = New-Image $other.Token $ImagePath

Write-Host "== Owner creates places"
$unverified = Call POST '/api/auth/register' @{ fullName = 'בדיקה לא מאומת'; email = "unverified.$marker@example.com"; password = $password; phone = '0501234567'; businessName = 'עסק לא מאומת' }
$blocked = Call POST '/api/manage/places' (Place-Body 'חסום' $image.id @()) $unverified.Json.token
Check 'an owner with an unverified email cannot submit a place (403 email_not_verified)' ($blocked.Status -eq 403 -and $blocked.Json.code -eq 'email_not_verified')

$overlap = Call POST '/api/manage/places' (Place-Body 'חופף' $image.id @(@{ day = 1; opens = 540; closes = 840 }, @{ day = 1; opens = 780; closes = 1080 })) $owner.Token
Check 'overlapping hours on one day are rejected (400 hours)' ($overlap.Status -eq 400 -and $overlap.Json.errors.hours)

# Open around the clock today; closed today but open in three days from 09:00 to 10:00.
$openBody = Place-Body 'פתוח היום' $image.id @(@{ day = $today; opens = 0; closes = 0 })
$closedDay = ($today + 3) % 7
$closedBody = Place-Body 'סגור היום' $image.id @(@{ day = $closedDay; opens = 540; closes = 600 })
$open = (Call POST '/api/manage/places' $openBody $owner.Token).Json
$closed = (Call POST '/api/manage/places' $closedBody $owner.Token).Json
Check 'new places start pending with revision 1' ($open.status -eq 'Pending' -and $closed.status -eq 'Pending' -and $open.revision -eq 1)
Check 'the Instagram handle is stored without @' ($open.instagram -eq 'test.place')

$hidden = Call GET "/api/places/$($open.id)"
$listed = Call GET "/api/places?q=$marker&pageSize=50"
Check 'a pending place is not public (404, not listed)' ($hidden.Status -eq 404 -and $listed.Json.totalCount -eq 0)
Check 'the image of a pending place is not public' ((Status-Of "$BaseUrl$($open.imageUrl)") -eq 404)

$foreignEdit = Call PUT "/api/manage/places/$($open.id)" (Place-Body 'השתלטות' $otherImage.id @() 1) $other.Token
Check 'another owner cannot edit the place (404)' ($foreignEdit.Status -eq 404)

Write-Host "== Moderation"
$queue = Call GET "/api/admin/places?status=Pending&search=$marker" $null $admin
Check 'both places wait in the admin queue' (@($queue.Json).Count -eq 2)
$rejected = Call POST "/api/admin/places/$($open.id)/reject" @{ revision = $open.revision; reason = 'חסרה כתובת מלאה' } $admin
Check 'the admin rejects with a reason' ($rejected.Status -eq 200 -and $rejected.Json.status -eq 'Rejected')
$mine = (Call GET '/api/manage/places' $null $owner.Token).Json | Where-Object id -eq $open.id
Check 'the owner sees the rejection reason' ($mine.status -eq 'Rejected' -and $mine.rejectionReason -eq 'חסרה כתובת מלאה')

$fixed = Call PUT "/api/manage/places/$($open.id)" (Place-Body 'פתוח היום' $image.id @(@{ day = $today; opens = 0; closes = 0 }) $mine.revision) $owner.Token
Check 'an owner edit sends it back to review and clears the reason' ($fixed.Json.status -eq 'Pending' -and -not $fixed.Json.rejectionReason -and $fixed.Json.revision -eq $mine.revision + 1)
$stale = Call POST "/api/admin/places/$($open.id)/approve" @{ revision = $mine.revision } $admin
Check 'approving an old revision is refused (409)' ($stale.Status -eq 409)
$approved = Call POST "/api/admin/places/$($open.id)/approve" @{ revision = $fixed.Json.revision } $admin
$approvedClosed = Call POST "/api/admin/places/$($closed.id)/approve" @{ revision = $closed.revision } $admin
Check 'the admin approves both' ($approved.Json.status -eq 'Published' -and $approvedClosed.Json.status -eq 'Published')
$audit = (Call GET '/api/admin/audit' $null $admin).Json.items
Check 'rejection and approval are in the audit log' (
  ($audit | Where-Object { $_.action -eq 'place.rejected' -and $_.targetId -eq $open.id }) -and
  ($audit | Where-Object { $_.action -eq 'place.approved' -and $_.targetId -eq $open.id }))

Write-Host "== Public directory"
$details = Call GET "/api/places/$($open.id)"
Check 'the approved place is public with its hours and contact details' ($details.Status -eq 200 -and $details.Json.hours.Count -eq 1 -and $details.Json.website -eq 'https://example.com')
$publicImage = Invoke-WebRequest "$BaseUrl$($open.imageUrl)" -UseBasicParsing
Check 'its image is public and cacheable' ([int]$publicImage.StatusCode -eq 200 -and "$($publicImage.Headers['Cache-Control'])" -like 'public*')
Check 'open all day today: open now, closing at midnight' (
  $details.Json.open.isOpen -and -not $details.Json.open.alwaysOpen -and $details.Json.open.nextKind -eq 'closes' -and
  $details.Json.open.nextMinute -eq 0 -and $details.Json.open.nextDaysAhead -eq 1)
$closedDetails = (Call GET "/api/places/$($closed.id)").Json
Check 'closed today: the next opening is in three days at 09:00' (
  -not $closedDetails.open.isOpen -and $closedDetails.open.nextKind -eq 'opens' -and $closedDetails.open.nextDay -eq $closedDay -and
  $closedDetails.open.nextMinute -eq 540 -and $closedDetails.open.nextDaysAhead -eq 3)
$openNow = Call GET "/api/places?q=$marker&openNow=true&pageSize=50"
Check 'the SQL "open now" filter returns only the open place' ($openNow.Json.totalCount -eq 1 -and $openNow.Json.items[0].id -eq $open.id)

# Across every public place, the database predicate and the C# status must agree.
$everything = (Call GET '/api/places?pageSize=50').Json
$openAll = (Call GET '/api/places?openNow=true&pageSize=50').Json
$expectedOpen = @($everything.items | Where-Object { $_.open.isOpen }).Count
Check "SQL open-now count equals the per-place status count ($($openAll.totalCount) of $($everything.totalCount))" ($openAll.totalCount -eq $expectedOpen)

$search = Call GET "/api/places?q=$marker&category=Cafe&locality=%D7%AA%D7%9C%20%D7%97%D7%99&pageSize=50"
Check 'search, category and town filters combine' ($search.Json.totalCount -eq 2)

$origin = @{ Lat = 33.2073; Lon = 35.5700 }
$near = (Call GET "/api/places?sort=near&latitude=$($origin.Lat)&longitude=$($origin.Lon)&pageSize=20").Json
$all = (Call GET '/api/places/map').Json.items
$bruteForce = $all | Sort-Object @{ Expression = { Distance $origin.Lat $origin.Lon $_.latitude $_.longitude } }, @{ Expression = { $_.id } } | Select-Object -First 20
$matches = ($near.items.Count -eq $bruteForce.Count)
for ($i = 0; $i -lt $near.items.Count -and $matches; $i++) {
  $expected = Distance $origin.Lat $origin.Lon $bruteForce[$i].latitude $bruteForce[$i].longitude
  if ([Math]::Abs($near.items[$i].distanceKm - $expected) -gt 0.01) { $matches = $false }
}
Check "near me equals a brute-force ranking of all $(@($all).Count) places" $matches
Check 'the map endpoint lists the new places with coordinates' (@($all | Where-Object { $_.id -eq $open.id -or $_.id -eq $closed.id }).Count -eq 2)

Write-Host "== Events at a place"
$start = [DateTimeOffset]::UtcNow.AddDays(3).Date.AddHours(17)
$eventBody = @{
  title = "ערב בדיקה במקום $marker"; description = 'ערב לבדיקת קישור בין אירוע למקום'; category = 'Music'; venueName = 'פתוח היום'
  locality = 'תל חי'; address = 'רחוב הבדיקה 1'; latitude = 33.2345; longitude = 35.5801
  startAt = $start.ToString('o'); endAt = $start.AddHours(2).ToString('o'); price = 0; imageId = $image.id
  organizerName = 'עסק לבדיקה'; tags = @(); placeId = $null
}
$otherPlace = (Call POST '/api/manage/places' (Place-Body 'של עסק אחר' $otherImage.id @()) $other.Token).Json
$eventBody.placeId = $otherPlace.id
$badLink = Call POST '/api/manage/events' $eventBody $owner.Token
Check 'an event cannot link to another owner''s place (400 placeId)' ($badLink.Status -eq 400 -and $badLink.Json.errors.placeId)
$eventBody.placeId = $open.id
$event = (Call POST '/api/manage/events' $eventBody $owner.Token).Json
Check 'an event links to the owner''s own place' ($event.placeId -eq $open.id)
$null = Call POST "/api/admin/events/$($event.id)/approve" @{ revision = $event.revision } $admin
$publicEvent = (Call GET "/api/events/$($event.id)").Json
Check 'the event page links back to the place' ($publicEvent.place.id -eq $open.id)
$withEvents = (Call GET "/api/places/$($open.id)").Json
Check 'the place page lists the upcoming event' (@($withEvents.upcomingEvents | Where-Object id -eq $event.id).Count -eq 1)

Write-Host "== Suspension"
$suspend = Call POST "/api/admin/users/$($owner.Id)/suspend" @{ reason = 'בדיקת הסתרת מקומות' } $admin
$whileSuspended = Call GET "/api/places/$($open.id)"
$eventWhileSuspended = (Call GET "/api/events/$($event.id)").Status
Check 'suspending the owner hides the place and its event' ($suspend.Status -eq 204 -and $whileSuspended.Status -eq 404 -and $eventWhileSuspended -eq 404)
$null = Call POST "/api/admin/users/$($owner.Id)/unsuspend" $null $admin
Check 'lifting the suspension brings the place back' ((Call GET "/api/places/$($open.id)").Status -eq 200)

Write-Host "== Cleanup"
$ownerToken = (Call POST '/api/auth/login' @{ email = $owner.Email; password = $password }).Json.token
$event = (Call GET '/api/manage/events' $null $ownerToken).Json | Where-Object id -eq $event.id
$null = Call DELETE "/api/manage/events/$($event.id)?revision=$($event.revision)" $null $ownerToken
foreach ($place in (Call GET '/api/manage/places' $null $ownerToken).Json) {
  $null = Call DELETE "/api/manage/places/$($place.id)?revision=$($place.revision)" $null $ownerToken
}
$null = Call DELETE "/api/manage/places/$($otherPlace.id)?revision=$($otherPlace.revision)" $null $other.Token
Check 'a deleted place disappears from the directory' ((Call GET "/api/places/$($open.id)").Status -eq 404 -and (Call GET "/api/places?q=$marker").Json.totalCount -eq 0)

Write-Host ''
if ($script:failures -gt 0) { Write-Host "$script:failures check(s) failed" -ForegroundColor Red; exit 1 }
Write-Host 'All Phase 18 checks passed.'
