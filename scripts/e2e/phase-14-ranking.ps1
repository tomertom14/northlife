<#
  End-to-end check of Phase 14 (smart ranking, "near me", position bias) against a running stack
  seeded with `--seed-demo`. Some checks read the database through `docker exec`.
  Run the API with a short rollup cadence (Analytics__RollupIntervalSeconds=20, Analytics__IngestLagSeconds=30).
  Usage: powershell -File scripts/e2e/phase-14-ranking.ps1 -AdminEmail <admin> -AdminPassword <password> -AdminTotpSecret <base32>
#>
param(
  [string]$BaseUrl = 'http://localhost:10000',
  [string]$MailpitUrl = 'http://localhost:8025',
  [Parameter(Mandatory)][string]$AdminEmail,
  [Parameter(Mandatory)][string]$AdminPassword,
  [Parameter(Mandatory)][string]$AdminTotpSecret,
  [string]$PostgresContainer = 'northlife-postgres-1',
  [string]$ImagePath = ''
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ImagePath) { $ImagePath = Join-Path $here '..\..\docs\screenshots\phase-5-dashboard.png' }
. (Join-Path $here 'e2e-common.ps1')

function Sql([string]$Query) { $Query | docker exec -i $PostgresContainer psql -U northlife -d northlife -At -v ON_ERROR_STOP=1 }

function Distance([double]$lat1, [double]$lon1, [double]$lat2, [double]$lon2) {
  $r = [Math]::PI / 180
  $a = [Math]::Pow([Math]::Sin(($lat2 - $lat1) * $r / 2), 2) + [Math]::Cos($lat1 * $r) * [Math]::Cos($lat2 * $r) * [Math]::Pow([Math]::Sin(($lon2 - $lon1) * $r / 2), 2)
  # 1.0, not 1: with an integer literal PowerShell picks Math.Min(int, int) and truncates to 0.
  2 * 6371.0088 * [Math]::Asin([Math]::Min(1.0, [Math]::Sqrt($a)))
}

function Fnv1a([string]$Text) {
  [uint32]$hash = 2166136261
  foreach ($byte in [Text.Encoding]::UTF8.GetBytes($Text)) {
    $hash = $hash -bxor $byte
    $hash = [uint32](([uint64]$hash * 16777619) % 4294967296)
  }
  if ($hash -ge 2147483648) { return [int64]$hash - 4294967296 }
  [int64]$hash
}

$israel = [TimeZoneInfo]::FindSystemTimeZoneById('Israel Standard Time')
$today = [TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow, $israel).ToString('yyyy-MM-dd')
$until = [TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow.AddDays(13), $israel).ToString('yyyy-MM-dd')
$range = "period=range&from=$today&to=$until"
$origin = @{ Lat = 33.2073; Lon = 35.57 }

Write-Host "`n== Geohash"
Check 'C# and PL/pgSQL geohash encodings agree for every event' ((Sql "SELECT count(*) FROM events WHERE geohash <> geohash_encode(latitude::float8, longitude::float8, 9);") -eq '0')
$plan = Sql "SET enable_seqscan = off; EXPLAIN SELECT id FROM events WHERE geohash >= 'sv9' AND geohash < 'sv:' AND deleted_at_utc IS NULL;"
Check 'a geohash prefix is a range scan on ix_events_geohash (C collation)' (($plan -join ' ') -match 'ix_events_geohash')

Write-Host "`n== Near me: k nearest events"
$near = (Call GET "/api/events?$range&sort=near&latitude=$($origin.Lat)&longitude=$($origin.Lon)&pageSize=12").Json
$map = (Call GET "/api/events/map?$range&north=34&south=32&east=36.5&west=34").Json
$brute = $map.items | ForEach-Object { [pscustomobject]@{ Id = $_.id; Km = (Distance $origin.Lat $origin.Lon $_.latitude $_.longitude) } } | Sort-Object Km | Select-Object -First 12
Check "the 12 nearest match a brute-force haversine ranking of all $($map.items.Count) events" ((@($near.items.id) -join ',') -eq (@($brute.Id) -join ','))
$distances = @($near.items.distanceKm)
Check 'results come nearest first with their distance' ((($distances | Measure-Object -Maximum).Maximum -eq $distances[-1]) -and ($distances[0] -le $distances[1]))
$page2 = (Call GET "/api/events?$range&sort=near&latitude=$($origin.Lat)&longitude=$($origin.Lon)&pageSize=12&page=2").Json
Check 'page 2 continues outward without repeating page 1' ($page2.items[0].distanceKm -ge $distances[-1] -and -not ($page2.items.id | Where-Object { $_ -in $near.items.id }))
Check 'sorting by distance without a location is refused (400)' ((Call GET "/api/events?$range&sort=near").Status -eq 400)
Check 'a latitude without a longitude is refused (400)' ((Call GET "/api/events?$range&sort=hot&latitude=33.2").Status -eq 400)
Check 'an unknown sort is refused (400)' ((Call GET "/api/events?$range&sort=random").Status -eq 400)

Write-Host "`n== Hot now"
$hot = (Call GET "/api/events?$range&sort=hot&pageSize=12").Json
Check 'the hot ranking returns a full page without distances' ($hot.items.Count -eq 12 -and -not ($hot.items | Where-Object { $_.distanceKm -ne $null }))
$hotNear = (Call GET "/api/events?$range&sort=hot&pageSize=12&latitude=$($origin.Lat)&longitude=$($origin.Lon)").Json
Check 'with a location the hot ranking reports distances' (-not ($hotNear.items | Where-Object { $_.distanceKm -eq $null }))
$orders = 1..100 | ForEach-Object { ((Call GET "/api/events?$range&sort=hot&pageSize=8").Json.items.id) -join ',' }
$modal = $orders | Group-Object | Sort-Object Count -Descending | Select-Object -First 1
$explored = 100 - $modal.Count
Write-Host "INFO  $explored of 100 first pages were shuffled (configured rate 10%)"
Check 'randomised top-N exploration shuffles about one first page in ten' ($explored -ge 2 -and $explored -le 25)

Write-Host "`n== Position bias (fitted on the simulated month)"
$admin = (Connect-WithTotp $AdminEmail $AdminPassword $AdminTotpSecret).Json.token
$bias = (Call GET '/api/admin/analytics/position-bias' $null $admin).Json
foreach ($row in $bias) {
  Write-Host ("INFO  position {0,2}: smoothed {1:0.000}  EM {2:0.000}  truth {3:0.000}  naive {4:0.000}  ({5} impressions)" -f $row.position, $row.propensity, $row.rawPropensity, [Math]::Pow($row.position, -0.6), $row.naiveRatio, $row.impressions)
}
Check 'propensities cover 12 positions and start at 1' ($bias.Count -eq 12 -and $bias[0].propensity -eq 1)
Check 'smoothed propensities never increase with position' (-not (1..11 | Where-Object { $bias[$_].propensity -gt $bias[$_ - 1].propensity + 1e-9 }))
$error4 = (1..3 | ForEach-Object { [Math]::Abs($bias[$_].propensity - [Math]::Pow($bias[$_].position, -0.6)) } | Measure-Object -Average).Average
Check ("positions 2-4 are within 0.1 of the simulated attention k^-0.6 (mean error {0:0.000})" -f $error4) ($error4 -lt 0.1)
Check 'business owners cannot read the model (403)' ((Call GET '/api/admin/analytics/position-bias' $null (New-VerifiedOwner 'bias-probe' 'StrongPass123').Token).Status -eq 403)

Write-Host "`n== Context keys and inverse-propensity weighting"
$owner = New-VerifiedOwner 'ranking' 'StrongPass123'
$image = New-Image $owner.Token $ImagePath
$startAt = [DateTimeOffset]::UtcNow.AddDays(2)
function New-PublishedEvent([string]$Title) {
  $body = @{ title = $Title; description = 'נוצר בבדיקת קצה לקצה של שלב 14'; category = 'Culture'; venueName = 'מרכז'; locality = 'צפת'; address = 'רחוב 1'; latitude = 32.96; longitude = 35.49; startAt = $startAt.ToString('o'); endAt = $startAt.AddHours(2).ToString('o'); price = 0; imageId = $image.id; organizerName = 'עסק'; tags = @() }
  $created = (Call POST '/api/manage/events' $body $owner.Token).Json
  $null = Call POST "/api/admin/events/$($created.id)/approve" @{ revision = $created.revision } $admin
  $created.id
}
$top = New-PublishedEvent 'אירוע שנלחץ במקום הראשון'
$deep = New-PublishedEvent 'אירוע שנלחץ במקום השמיני'
$seenOnly = New-PublishedEvent 'אירוע שרק נראה'
$context = 'time|today||||||p1'
$visitor = [guid]::NewGuid().ToString()
$tracked = Call POST '/api/analytics/events' @{ visitorId = $visitor; interactions = @(
  @{ eventId = $top; type = 'DetailView'; source = 'Feed'; position = 1; context = $context },
  @{ eventId = $deep; type = 'DetailView'; source = 'Feed'; position = 8; context = $context },
  @{ eventId = $seenOnly; type = 'Impression'; source = 'Feed'; position = 3; context = $context }
) }
Check 'three feed interactions recorded' ($tracked.Json.recorded -eq 3)
Check 'the list context is stored as its 32-bit FNV-1a hash' ((Sql "SELECT DISTINCT context_key FROM interactions WHERE visitor_id = '$visitor';") -eq [string](Fnv1a $context))
$scores = $null
for ($attempt = 0; $attempt -lt 30; $attempt++) {
  $scores = Sql "SELECT (SELECT log_score FROM event_popularity WHERE event_id = '$deep') - (SELECT log_score FROM event_popularity WHERE event_id = '$top');"
  if ($scores) { break }
  Start-Sleep -Seconds 4
}
$theta8 = ($bias | Where-Object position -eq 8).propensity
$expected = [Math]::Log([Math]::Min(1 / $theta8, 5))
Write-Host ("INFO  log popularity gap {0:0.000}, expected ln(min(1/theta8, 5)) = {1:0.000}" -f [double]$scores, $expected)
Check 'a click at position 8 is credited 1/theta8 times a click at position 1' ([Math]::Abs([double]$scores - $expected) -lt 0.01)
Check 'an impression alone adds no popularity' ((Sql "SELECT count(*) FROM event_popularity WHERE event_id = '$seenOnly';") -eq '0')

Write-Host "`n$(if ($failures) { "$failures check(s) failed" } else { 'All checks passed' })"
exit $failures
