<#
  End-to-end check of Phase 13 (analytics) against a running stack with Mailpit and PostgreSQL.
  The administrator must have TOTP enrolled; pass its Base32 secret. Some checks read or seed the
  database through `docker exec` on the PostgreSQL container.
  Run the API with a short rollup cadence and a generous analytics rate limit, for example:
    Analytics__RollupIntervalSeconds=20  Analytics__IngestLagSeconds=30
    RateLimiting__AnalyticsPermitsPerMinute=5000  Metrics__AllowPrivateNetwork=true
  Usage: powershell -File scripts/e2e/phase-13-analytics.ps1 -AdminEmail <admin> -AdminPassword <password> -AdminTotpSecret <base32>
  Exits non-zero when any check fails.
#>
param(
  [string]$BaseUrl = 'http://localhost:10000',
  [string]$MailpitUrl = 'http://localhost:8025',
  [Parameter(Mandatory)][string]$AdminEmail,
  [Parameter(Mandatory)][string]$AdminPassword,
  [Parameter(Mandatory)][string]$AdminTotpSecret,
  [string]$PostgresContainer = 'northlife-postgres-1',
  [string]$DatabaseUser = 'northlife',
  [string]$DatabaseName = 'northlife',
  [string]$ImagePath = ''
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ImagePath) { $ImagePath = Join-Path $here '..\..\docs\screenshots\phase-5-dashboard.png' }
. (Join-Path $here 'e2e-common.ps1')

function Sql([string]$Query) {
  $Query | docker exec -i $PostgresContainer psql -U $DatabaseUser -d $DatabaseName -At -v ON_ERROR_STOP=1
}

function Track([string]$Visitor, [object[]]$Interactions, [string]$UserAgent = '') {
  if ($UserAgent) {
    # Invoke-WebRequest sets its own User-Agent; curl.exe sends exactly what the check needs.
    $json = @{ visitorId = $Visitor; interactions = $Interactions } | ConvertTo-Json -Depth 5 -Compress
    $file = [IO.Path]::GetTempFileName()
    [IO.File]::WriteAllText($file, $json)
    $out = & curl.exe --silent -X POST "$BaseUrl/api/analytics/events" -H 'Content-Type: application/json' -H "User-Agent: $UserAgent" --data-binary "@$file"
    Remove-Item $file
    return [pscustomobject]@{ Status = 202; Json = ($out | ConvertFrom-Json) }
  }
  Call POST '/api/analytics/events' @{ visitorId = $Visitor; interactions = $Interactions }
}

function Seen([string]$EventId, [string]$Type, [int]$Position = 1) {
  @{ eventId = $EventId; type = $Type; source = 'Feed'; position = $Position }
}

$password = 'StrongPass123'

Write-Host "`n== Setup: an owner with two published events and one pending"
$admin = (Connect-WithTotp $AdminEmail $AdminPassword $AdminTotpSecret).Json.token
$owner = New-VerifiedOwner 'analytics' $password
$other = New-VerifiedOwner 'analytics-other' $password
$image = New-Image $owner.Token $ImagePath
$startAt = [DateTimeOffset]::UtcNow.AddDays(1)
function New-Event([string]$Title) {
  $body = @{ title = $Title; description = 'נוצר בבדיקת קצה לקצה של שלב 13'; category = 'Music'; venueName = 'מרכז'; locality = 'צפת'; address = 'רחוב 1'; latitude = 32.96; longitude = 35.49; startAt = $startAt.ToString('o'); endAt = $startAt.AddHours(3).ToString('o'); price = 0; imageId = $image.id; organizerName = 'עסק לבדיקה'; tags = @() }
  (Call POST '/api/manage/events' $body $owner.Token).Json
}
$eventA = New-Event 'הופעה לבדיקת סטטיסטיקה'
$eventB = New-Event 'סדנה לבדיקת סטטיסטיקה'
$eventC = New-Event 'אירוע שעדיין ממתין'
foreach ($item in $eventA, $eventB) { $null = Call POST "/api/admin/events/$($item.id)/approve" @{ revision = $item.revision } $admin }
Check 'two events published, one pending' ((Call GET "/api/events/$($eventA.id)").Status -eq 200 -and (Call GET "/api/events/$($eventC.id)").Status -eq 404)

Write-Host "`n== Ingestion rules"
$visitor0 = [guid]::NewGuid().ToString()
Check 'an empty visitor id is refused (400)' ((Call POST '/api/analytics/events' @{ visitorId = [guid]::Empty.ToString(); interactions = @((Seen $eventA.id 'Impression')) }).Status -eq 400)
$tooMany = 1..51 | ForEach-Object { Seen $eventA.id 'Impression' }
Check 'a batch over 50 interactions is refused (400)' ((Call POST '/api/analytics/events' @{ visitorId = $visitor0; interactions = $tooMany }).Status -eq 400)
$first = Track $visitor0 @((Seen $eventA.id 'Impression'), (Seen $eventA.id 'Impression'), (Seen $eventA.id 'DetailView'), @{ eventId = $eventA.id; type = 'Navigate'; source = 'Details' })
Check 'a batch is accepted (202) and duplicates inside it count once (3 of 4)' ($first.Status -eq 202 -and $first.Json.received -eq 4 -and $first.Json.recorded -eq 3)
Check 'the same interactions within 30 minutes are not counted again' ((Track $visitor0 @((Seen $eventA.id 'Impression'), (Seen $eventA.id 'DetailView'))).Json.recorded -eq 0)
Check 'unknown and unpublished events are ignored' ((Track $visitor0 @((Seen ([guid]::NewGuid().ToString()) 'Impression'), (Seen $eventC.id 'Impression'))).Json.recorded -eq 0)
Check 'crawlers are accepted but not counted' ((Track ([guid]::NewGuid().ToString()) @((Seen $eventA.id 'Impression')) 'Mozilla/5.0 (compatible; Googlebot/2.1)').Json.recorded -eq 0)
$partitions = Sql "SELECT count(*) FROM pg_inherits WHERE inhparent = 'interactions'::regclass;"
Check "raw interactions are range-partitioned by month ($partitions partitions)" ([int]$partitions -ge 4)
$month = [DateTimeOffset]::UtcNow.ToString('yyyy') + 'm' + [DateTimeOffset]::UtcNow.ToString('MM')
Check "new rows land in this month's partition (interactions_y$month)" ((Sql "SELECT count(*) FROM interactions_y$month WHERE visitor_id = '$visitor0';") -eq '3')

Write-Host "`n== 300 visitors: counts and unique visitors"
$visitors = 1..300 | ForEach-Object { [guid]::NewGuid().ToString() }
$recorded = 0
for ($index = 0; $index -lt $visitors.Count; $index++) {
  $batch = @((Seen $eventA.id 'Impression' 1), (Seen $eventA.id 'DetailView' 1))
  if ($index -lt 100) { $batch += (Seen $eventB.id 'Impression' 2) }
  $recorded += (Track $visitors[$index] $batch).Json.recorded
}
Check "all 700 visitor interactions are recorded ($recorded)" ($recorded -eq 700)

Write-Host "`n== Rollup to hourly and daily statistics"
$analytics = $null
for ($attempt = 0; $attempt -lt 40; $attempt++) {
  $analytics = (Call GET '/api/manage/analytics?days=7' $null $owner.Token).Json
  if ($analytics.totals.detailViews -ge 301) { break }
  Start-Sleep -Seconds 5
}
Check 'the worker rolls interactions up within the ingest lag plus one interval' ($analytics.totals.detailViews -eq 301)
Check 'totals count impressions, views and navigations exactly (401 / 301 / 1)' ($analytics.totals.impressions -eq 401 -and $analytics.totals.detailViews -eq 301 -and $analytics.totals.navigations -eq 1)
$rowA = $analytics.events | Where-Object id -eq $eventA.id
$rowB = $analytics.events | Where-Object id -eq $eventB.id
Write-Host "INFO  unique visitors: event A $($rowA.uniqueVisitors), event B $($rowB.uniqueVisitors), owner total $($analytics.totals.uniqueVisitors) (true values 301, 100, 301)"
# Small sets use linear counting; its only error is two visitors landing in one of the 16,384
# registers. For 100 visitors that averages 0.3 collisions, and 5 or more happen about 2 in 100,000
# runs, so the tolerance is 4 (6 for 301 visitors).
Check 'HyperLogLog unique visitors per event are within the sketch error (301 and 100)' ([Math]::Abs($rowA.uniqueVisitors - 301) -le 6 -and [Math]::Abs($rowB.uniqueVisitors - 100) -le 4)
Check 'the owner total is a union, not a sum: visitors of both events count once (about 301, not 401)' ([Math]::Abs($analytics.totals.uniqueVisitors - 301) -le 6)
Check 'click-through rate is views over impressions' ([Math]::Abs($analytics.totals.clickThroughRate - [Math]::Round(301 / 401, 4)) -lt 0.0001)
Check 'the daily series has 7 zero-filled days ending today' ($analytics.daily.Count -eq 7 -and $analytics.daily[6].detailViews -eq 301 -and $analytics.daily[0].detailViews -eq 0)
Check 'the pending event is listed with no traffic' ((($analytics.events | Where-Object id -eq $eventC.id).impressions) -eq 0)
Check 'another owner sees none of these numbers' ((Call GET '/api/manage/analytics?days=7' $null $other.Token).Json.totals.impressions -eq 0)
Check 'anonymous callers cannot read owner analytics (401)' ((Call GET '/api/manage/analytics').Status -eq 401)
$popularity = Sql "SELECT (SELECT log_score FROM event_popularity WHERE event_id = '$($eventA.id)') > (SELECT log_score FROM event_popularity WHERE event_id = '$($eventB.id)');"
Check 'decayed popularity ranks the more engaging event higher' ($popularity -eq 't')

Write-Host "`n== Reset my history"
Check 'forget deletes a visitor history (204)' ((Call POST '/api/analytics/forget' @{ visitorId = $visitors[0] }).Status -eq 204)
Check 'after forgetting, the same interaction counts again' ((Track $visitors[0] @((Seen $eventA.id 'Impression'))).Json.recorded -eq 1)
Check 'other visitors are still deduplicated' ((Track $visitors[1] @((Seen $eventA.id 'Impression'))).Json.recorded -eq 0)

Write-Host "`n== Spike detection on seeded hourly history"
# 7 days of quiet history, then a sudden last complete hour: event A with healthy navigation
# (an organic surge), event B with many views and no navigation (looks automated).
$null = Sql @"
INSERT INTO event_stats_hourly (event_id, hour_utc, impressions, detail_views, navigations, shares)
SELECT e.id, date_trunc('hour', now()) - make_interval(hours => h), 20, 3 + (h % 3), CASE WHEN h % 2 = 0 THEN 1 ELSE 0 END, 0
FROM generate_series(2, 168) AS h CROSS JOIN (VALUES ('$($eventA.id)'::uuid), ('$($eventB.id)'::uuid)) AS e(id)
ON CONFLICT (event_id, hour_utc) DO UPDATE SET impressions = EXCLUDED.impressions, detail_views = EXCLUDED.detail_views, navigations = EXCLUDED.navigations;
INSERT INTO event_stats_hourly (event_id, hour_utc, impressions, detail_views, navigations, shares) VALUES
  ('$($eventA.id)', date_trunc('hour', now()) - interval '1 hour', 150, 40, 12, 2),
  ('$($eventB.id)', date_trunc('hour', now()) - interval '1 hour', 200, 45, 0, 0)
ON CONFLICT (event_id, hour_utc) DO UPDATE SET impressions = EXCLUDED.impressions, detail_views = EXCLUDED.detail_views, navigations = EXCLUDED.navigations, shares = EXCLUDED.shares;
"@
$anomalies = (Call GET '/api/admin/analytics/anomalies' $null $admin).Json
$anomalyA = $anomalies | Where-Object eventId -eq $eventA.id
$anomalyB = $anomalies | Where-Object eventId -eq $eventB.id
Write-Host "INFO  z-scores: event A $($anomalyA.zScore) ($($anomalyA.kind)), event B $($anomalyB.zScore) ($($anomalyB.kind))"
Check 'admins see both spikes (z >= 3)' ($anomalyA.zScore -ge 3 -and $anomalyB.zScore -ge 3)
Check 'a surge with healthy navigation is labelled "surge"' ($anomalyA.kind -eq 'surge')
Check 'views that lead nowhere are labelled "suspicious"' ($anomalyB.kind -eq 'suspicious')
Check 'business owners cannot read the anomaly list (403)' ((Call GET '/api/admin/analytics/anomalies' $null $owner.Token).Status -eq 403)
$trend = (Call GET '/api/manage/analytics?days=7' $null $owner.Token).Json.events
Check 'the owner sees both events marked as taking off' ((($trend | Where-Object id -eq $eventA.id).trending) -and (($trend | Where-Object id -eq $eventB.id).trending))

Write-Host "`n== Partition retention and operations metrics"
$null = Sql "CREATE TABLE IF NOT EXISTS interactions_y2020m01 PARTITION OF interactions FOR VALUES FROM ('2020-01-01 00:00+00') TO ('2020-02-01 00:00+00');"
$dropped = Sql "SELECT analytics_drop_interaction_partitions(now() - interval '90 days');"
$remaining = Sql "SELECT count(*) FROM pg_inherits WHERE inhparent = 'interactions'::regclass AND inhrelid::regclass::text = 'interactions_y2020m01';"
Check "retention drops whole partitions older than 90 days ($dropped dropped)" ([int]$dropped -ge 1 -and $remaining -eq '0')
$metrics = Invoke-WebRequest "$BaseUrl/metrics" -UseBasicParsing
Check 'Prometheus metrics are served to the private network' ($metrics.StatusCode -eq 200 -and $metrics.Content -match 'northlife_analytics_interactions_recorded_total \d+')
Check 'metrics carry no per-event labels (low cardinality)' (-not ($metrics.Content -match [regex]::Escape($eventA.id)))

Write-Host "`n$(if ($failures) { "$failures check(s) failed" } else { 'All checks passed' })"
exit $failures
