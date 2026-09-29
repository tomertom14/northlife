<#
  End-to-end check of Phase 17 (automatic event approval) against a running stack with Mailpit.
  The administrator must already have TOTP enrolled (Phase 11); pass its Base32 secret.
  Some checks read or reset rows through `docker exec`, and the daily-run check restarts the app
  container once. The script puts the original settings back and deletes the events it created.

  A run in "Approve" mode publishes every pending event that passes the terms, not only the test
  events. The script therefore stops before those checks when other upcoming events are waiting for
  review, unless -AllowApprovingOtherEvents is given.

  Usage: powershell -File scripts/e2e/phase-17-auto-moderation.ps1 -AdminEmail admin@northlife.local `
           -AdminPassword '...' -AdminTotpSecret 'ABCD EFGH ...'
  Exits non-zero when any check fails.
#>
param(
  [string]$BaseUrl = 'http://localhost:10000',
  [string]$MailpitUrl = 'http://localhost:8025',
  [Parameter(Mandatory)][string]$AdminEmail,
  [Parameter(Mandatory)][string]$AdminPassword,
  [Parameter(Mandatory)][string]$AdminTotpSecret,
  [string]$PostgresContainer = 'northlife-postgres-1',
  [string]$AppContainer = 'northlife-app',
  [string]$ImagePath = '',
  [switch]$AllowApprovingOtherEvents
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ImagePath) { $ImagePath = Join-Path $here '..\..\docs\screenshots\phase-5-dashboard.png' }
. (Join-Path $here 'e2e-common.ps1')

function Sql([string]$Query) { $Query | docker exec -i $PostgresContainer psql -U northlife -d northlife -At -v ON_ERROR_STOP=1 }

$password = 'StrongPass123'
$israel = [TimeZoneInfo]::FindSystemTimeZoneById('Israel Standard Time')
$marker = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
# A day no earlier run used, so the duplicate check only sees this run's events on it.
$day = [DateTimeOffset]::UtcNow.Date.AddDays(40 + ($marker % 60))
$created = New-Object System.Collections.Generic.List[string]

# Every test event gets words of its own (label + marker), so only the deliberate copy looks alike.
function New-Event([string]$Token, [string]$ImageId, [string]$Label, [string]$Title, [string]$Description, [string]$Category,
  [int]$UtcHour = 15, [double]$Latitude = 32.9646, [double]$Longitude = 35.4960) {
  $start = [DateTimeOffset]::new($day.Year, $day.Month, $day.Day, $UtcHour, 0, 0, [TimeSpan]::Zero)
  $body = @{
    title = "$Title $Label$marker"; description = $Description; category = $Category; venueName = 'מתחם הבדיקות'
    locality = 'צפת'; address = 'רחוב הבדיקה 1'; latitude = $Latitude; longitude = $Longitude
    startAt = $start.ToString('o'); endAt = $start.AddHours(2).ToString('o'); price = 40; imageId = $ImageId
    organizerName = 'עסק לבדיקה'; tags = @()
  }
  $result = Call POST '/api/manage/events' $body $Token
  if ($result.Status -eq 201 -or $result.Status -eq 200) { $created.Add($result.Json.id) }
  $result.Json
}

function Admin-Event([string]$Id) {
  (Call GET "/api/admin/events?search=$marker" $null $admin).Json | Where-Object id -eq $Id | Select-Object -First 1
}

function Held-Codes([string]$Id) {
  $item = Admin-Event $Id
  if ($item.autoReview.outcome -ne 'Held') { return @() }
  @($item.autoReview.reasons | ForEach-Object { $_.code })
}

function Save-Settings($Settings) { Call PUT '/api/admin/auto-moderation/settings' $Settings $admin }

Write-Host "`n== Administrator session"
$adminSession = Connect-WithTotp $AdminEmail $AdminPassword $AdminTotpSecret
Check 'admin signs in with password and TOTP' ($adminSession.Status -eq 200 -and $adminSession.Json.user.mfaVerified -eq $true)
$admin = $adminSession.Json.token
$adminId = $adminSession.Json.user.id

$overview = Call GET '/api/admin/auto-moderation' $null $admin
Check 'the admin reads the automatic approval overview' ($overview.Status -eq 200 -and $overview.Json.settings.runAt -match '^\d{2}:\d{2}$')
$original = $overview.Json.settings
# New test accounts are minutes old; the age term is covered by the unit tests.
$testSettings = $original | Select-Object *
$testSettings.minAccountAgeDays = 0
$testSettings.minApprovedEvents = 3
$testSettings.maxAutoApprovalsPerOwnerPerDay = 5
$testSettings.mode = 'NotesOnly'

try {
  Write-Host "`n== Access control"
  $owner = New-VerifiedOwner 'automod' $password
  Check 'a business owner cannot read the settings (403)' ((Call GET '/api/admin/auto-moderation' $null $owner.Token).Status -eq 403)
  Check 'a business owner cannot change the settings (403)' ((Call PUT '/api/admin/auto-moderation/settings' $testSettings $owner.Token).Status -eq 403)
  Check 'a business owner cannot start a run (403)' ((Call POST '/api/admin/auto-moderation/run' $null $owner.Token).Status -eq 403)
  Check 'anonymous callers must sign in (401)' ((Call GET '/api/admin/auto-moderation').Status -eq 401)

  Write-Host "`n== Settings validation"
  $bad = $testSettings | Select-Object *
  $bad.duplicateSimilarity = 0.2; $bad.runAt = '25:00'
  $invalid = Save-Settings $bad
  Check 'out-of-range settings are refused with field errors (400)' ($invalid.Status -eq 400 -and $invalid.Json.errors.duplicateSimilarity -and $invalid.Json.errors.runAt)
  $saved = Save-Settings $testSettings
  Check 'valid settings are saved' ($saved.Status -eq 200 -and $saved.Json.settings.mode -eq 'NotesOnly' -and $saved.Json.settings.minAccountAgeDays -eq 0)

  Write-Host "`n== Owner A earns trust: three events approved by the admin"
  $imageA = New-Image $owner.Token $ImagePath
  $seeds = @(
    (New-Event $owner.Token $imageA.id 'sa' 'סדנת קרמיקה' 'יוצרים ספלים וקערות מחומר, כולל שריפה בתנור' 'Workshops' 6),
    (New-Event $owner.Token $imageA.id 'sb' 'הרצאה על כוכבי לכת' 'מבט בטלסקופ על צדק ושבתאי אחרי ההרצאה' 'Culture' 8),
    (New-Event $owner.Token $imageA.id 'sc' 'טיול זריחה בנחל' 'מסלול מעגלי קל עם עצירה לקפה בנקודת תצפית' 'Outdoors' 10)
  )
  $approvedSeeds = @($seeds | Where-Object { (Call POST "/api/admin/events/$($_.id)/approve" @{ revision = $_.revision } $admin).Status -eq 200 })
  Check 'the admin approves the three seed events' ($approvedSeeds.Count -eq 3)

  Write-Host "`n== Notes only: the service writes notes and publishes nothing"
  $clean = New-Event $owner.Token $imageA.id 'a1' 'ערב גאז על הגג' 'הרכב מקומי מנגן סטנדרטים ואלתורים, כיבוד קל ומקומות ישיבה על הגג' 'Music' 17
  $notesRun = Call POST '/api/admin/auto-moderation/run' $null $admin
  Check 'a manual run in notes-only mode succeeds' ($notesRun.Status -eq 200 -and $notesRun.Json.mode -eq 'NotesOnly' -and $notesRun.Json.approved -eq 0)
  $cleanRow = Admin-Event $clean.id
  Check 'the clean event would be approved but is still pending' ($cleanRow.status -eq 'Pending' -and $cleanRow.autoReview.outcome -eq 'WouldApprove')

  $others = @((Call GET '/api/admin/events?status=Pending' $null $admin).Json | Where-Object {
      $_.ownerId -ne $owner.Id -and [DateTimeOffset]::Parse($_.endAt) -gt [DateTimeOffset]::UtcNow })
  if ($others.Count -gt 0 -and -not $AllowApprovingOtherEvents) {
    Write-Host "SKIP  $($others.Count) other upcoming event(s) wait for review; approve-mode checks would publish them. Re-run with -AllowApprovingOtherEvents." -ForegroundColor Yellow
  } else {
    Write-Host "`n== Approve mode"
    $testSettings.mode = 'Approve'
    $null = Save-Settings $testSettings
    $approveRun = Call POST '/api/admin/auto-moderation/run' $null $admin
    Check 'a manual run in approve mode succeeds' ($approveRun.Status -eq 200 -and $approveRun.Json.approved -ge 1)
    $published = Admin-Event $clean.id
    Check 'the clean event from the trusted owner is published' ($published.status -eq 'Published' -and $published.autoReview.outcome -eq 'Approved')
    Check 'the published event is public' ((Call GET "/api/events/$($clean.id)").Status -eq 200)
    $audit = (Call GET '/api/admin/audit?pageSize=50' $null $admin).Json.items
    $auto = $audit | Where-Object { $_.action -eq 'event.approved' -and $_.targetId -eq $clean.id } | Select-Object -First 1
    Check 'the approval is audited with no actor and marked automatic' ($auto -and $null -eq $auto.actorId -and $null -eq $auto.actorName -and $auto.details.automatic -eq $true)
    Check 'the manual run is audited with the admin as actor' (@($audit | Where-Object { $_.action -eq 'automoderation.run' -and $_.actorId -eq $adminId }).Count -ge 2)
    Check 'settings changes are audited' (@($audit | Where-Object { $_.action -eq 'automoderation.settings_changed' -and $_.details.changes.mode.to -eq 'Approve' }).Count -ge 1)

    Write-Host "`n== Events that stay for a human"
    $phone = New-Event $owner.Token $imageA.id 'a2' 'שוק איכרים' 'תוצרת מקומית וגבינות. להזמנות 054-1234567' 'Food' 11
    $telAviv = New-Event $owner.Token $imageA.id 'a3' 'מסיבת ריקודים' 'תקליטנים אורחים עד הבוקר' 'Nightlife' 18 32.0853 34.7818
    $copyBody = @{
      title = $published.title; description = $published.description; category = 'Music'; venueName = 'מתחם הבדיקות'; locality = 'צפת'
      address = 'רחוב הבדיקה 1'; latitude = 32.9650; longitude = 35.4965; startAt = $published.startAt; endAt = $published.endAt
      price = 40; imageId = $imageA.id; organizerName = 'עסק לבדיקה'; tags = @()
    }
    $copy = (Call POST '/api/manage/events' $copyBody $owner.Token).Json; $created.Add($copy.id)
    $ownerB = New-VerifiedOwner 'automodnew' $password
    $imageB = New-Image $ownerB.Token $ImagePath
    $newcomer = New-Event $ownerB.Token $imageB.id 'b1' 'תערוכת צילום' 'צלמי הגליל מציגים נופים ודיוקנאות מהשנה האחרונה' 'Culture' 12
    $heldRun = Call POST '/api/admin/auto-moderation/run' $null $admin
    Check 'the run finishes' ($heldRun.Status -eq 200)
    Check 'a phone number in the text holds the event (contact_details)' ((Held-Codes $phone.id) -contains 'contact_details')
    Check 'an event in Tel Aviv is held (outside_region)' ((Held-Codes $telAviv.id) -contains 'outside_region')
    Check 'a copy of a published event is held (possible_duplicate)' ((Held-Codes $copy.id) -contains 'possible_duplicate')
    $copyReason = (Admin-Event $copy.id).autoReview.reasons | Where-Object code -eq 'possible_duplicate'
    Check 'the duplicate note names the original event' ($copyReason.values.eventId -eq $clean.id)
    Check 'a new owner without published events is held (owner_few_approvals)' ((Held-Codes $newcomer.id) -contains 'owner_few_approvals')
    Check 'held events are still pending and private' ((Admin-Event $phone.id).status -eq 'Pending' -and (Call GET "/api/events/$($phone.id)").Status -eq 404)

    Write-Host "`n== Daily cap per owner"
    # One automatic approval today already (the clean event); with a cap of 2 only one more passes.
    $testSettings.maxAutoApprovalsPerOwnerPerDay = 2
    $null = Save-Settings $testSettings
    $capFirst = New-Event $owner.Token $imageA.id 'a5' 'סיור טעימות יין' 'יקבים קטנים מהרי הגליל מציגים את הבציר החדש' 'Food' 13
    $capSecond = New-Event $owner.Token $imageA.id 'a6' 'משחק כדורגל שכונתי' 'טורניר חמישיות פתוח לכל הגילים במגרש העירוני' 'Sports' 14
    $null = Call POST '/api/admin/auto-moderation/run' $null $admin
    Check 'the first event within the cap is published' ((Admin-Event $capFirst.id).status -eq 'Published')
    Check 'the second event hits the daily cap (owner_daily_cap)' ((Held-Codes $capSecond.id) -contains 'owner_daily_cap')

    Write-Host "`n== Runs at the same moment"
    # A run takes a fraction of a second; retry in the rare case the requests did not overlap.
    for ($attempt = 1; $attempt -le 3; $attempt++) {
      $statuses = Invoke-Simultaneously @(
        @{ Path = '/api/admin/auto-moderation/run'; Body = @{}; Token = $admin },
        @{ Path = '/api/admin/auto-moderation/run'; Body = @{}; Token = $admin },
        @{ Path = '/api/admin/auto-moderation/run'; Body = @{}; Token = $admin }
      )
      Write-Host "INFO  attempt $attempt, simultaneous run statuses: $($statuses -join ', ')"
      if ($statuses -contains 409) { break }
    }
    Check 'the advisory lock lets one run through and refuses the overlap (409)' (($statuses -contains 200) -and ($statuses -contains 409) -and @($statuses | Where-Object { $_ -notin 200, 409 }).Count -eq 0)
  }

  Write-Host "`n== The daily run happens once a day, also across a restart"
  $testSettings.mode = 'NotesOnly'
  $israelNow = [TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow, $israel)
  if ($israelNow.TimeOfDay -lt [TimeSpan]::FromMinutes(5)) {
    Write-Host 'SKIP  too close to Israel midnight for a run time earlier today; run again after 00:05.' -ForegroundColor Yellow
  } else {
    $testSettings.runAt = $israelNow.AddMinutes(-2).ToString('HH:mm')
    $null = Save-Settings $testSettings
    $since = [DateTimeOffset]::UtcNow.AddSeconds(-1).UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ss') + 'Z'
    $null = Sql "UPDATE auto_moderation_settings SET last_scheduled_run_date = NULL;"
    $scheduled = 0
    for ($i = 0; $i -lt 30 -and $scheduled -eq 0; $i++) {
      Start-Sleep -Seconds 3
      $scheduled = [int](Sql "SELECT count(*) FROM auto_moderation_runs WHERE trigger = 'Scheduled' AND started_at_utc >= '$since' AND finished_at_utc IS NOT NULL;")
    }
    Check 'the worker starts the due daily run by itself' ($scheduled -eq 1)
    $today = $israelNow.ToString('yyyy-MM-dd')
    Check "today's run is claimed ($today)" ((Sql 'SELECT last_scheduled_run_date FROM auto_moderation_settings;') -eq $today)
    docker restart $AppContainer | Out-Null
    for ($i = 0; $i -lt 40; $i++) {
      try { if ((Invoke-WebRequest "$BaseUrl/health/ready" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200) { break } } catch { Start-Sleep -Milliseconds 750 }
    }
    Start-Sleep -Seconds 45
    $afterRestart = [int](Sql "SELECT count(*) FROM auto_moderation_runs WHERE trigger = 'Scheduled' AND started_at_utc >= '$since';")
    Check 'after a restart the day still has exactly one scheduled run' ($afterRestart -eq 1)
    $admin = (Connect-WithTotp $AdminEmail $AdminPassword $AdminTotpSecret).Json.token
    $next = (Call GET '/api/admin/auto-moderation' $null $admin).Json.nextRunAt
    Check 'the next run is tomorrow' ([DateTimeOffset]::Parse($next) -gt [DateTimeOffset]::UtcNow.AddHours(12))
  }

  Write-Host "`n== Switched off"
  $testSettings.mode = 'Off'
  $null = Save-Settings $testSettings
  $off = Call POST '/api/admin/auto-moderation/run' $null $admin
  Check 'a manual run is refused while the service is off (409 mode_off)' ($off.Status -eq 409 -and $off.Json.code -eq 'mode_off')
}
finally {
  Write-Host "`n== Cleanup"
  $restored = Save-Settings $original
  Check 'the original settings are restored' ($restored.Status -eq 200 -and $restored.Json.settings.mode -eq $original.mode -and $restored.Json.settings.runAt -eq $original.runAt)
  foreach ($id in $created) {
    $row = Admin-Event $id
    if ($row) { $null = Call DELETE "/api/admin/events/$id`?revision=$($row.revision)" $null $admin }
  }
  Write-Host "INFO  deleted $($created.Count) test event(s)"
}

Write-Host "`n$(if ($failures) { "$failures check(s) failed" } else { 'All checks passed' })"
exit $failures
