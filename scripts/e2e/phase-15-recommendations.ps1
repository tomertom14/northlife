<#
  End-to-end check of Phase 15 (recommendations) against a running stack seeded with `--seed-demo`.
  Some checks read the database through `docker exec`; the last one runs the offline evaluation in
  the production image.
  Usage: powershell -File scripts/e2e/phase-15-recommendations.ps1
#>
param(
  [string]$BaseUrl = 'http://localhost:10000',
  [string]$PostgresContainer = 'northlife-postgres-1',
  [string]$Image = 'northlife:local'
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$MailpitUrl = ''
. (Join-Path $here 'e2e-common.ps1')

function Sql([string]$Query) { $Query | docker exec -i $PostgresContainer psql -U northlife -d northlife -At -v ON_ERROR_STOP=1 }

function Open-Events([string]$Visitor, [object[]]$Events, [switch]$Navigate) {
  $interactions = @()
  foreach ($item in $Events) {
    $interactions += @{ eventId = $item.id; type = 'DetailView'; source = 'Feed'; position = 1; context = 'e2e' }
    if ($Navigate) { $interactions += @{ eventId = $item.id; type = 'Navigate'; source = 'Details' } }
  }
  (Call POST '/api/analytics/events' @{ visitorId = $Visitor; interactions = $interactions }).Json.recorded
}

function Category-Share($Items, [string[]]$Categories) {
  if (@($Items).Count -eq 0) { return 0 }
  (@($Items | Where-Object { $_.event.category -in $Categories }).Count) / @($Items).Count
}

$israel = [TimeZoneInfo]::FindSystemTimeZoneById('Israel Standard Time')
$today = [TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow, $israel).ToString('yyyy-MM-dd')
$until = [TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow.AddDays(13), $israel).ToString('yyyy-MM-dd')
$upcoming = (Call GET "/api/events?period=range&from=$today&to=$until&pageSize=100").Json.items

Write-Host "`n== Model"
$modelled = Sql "SELECT count(DISTINCT source_event_id) FROM event_similarities;"
Check "the worker stored neighbours for every upcoming event ($modelled sources, $($upcoming.Count) upcoming)" ([int]$modelled -ge $upcoming.Count)
Check 'each event keeps at most 20 ranked neighbours' ((Sql "SELECT max(rank) FROM event_similarities;") -eq '20')
Check 'no event is its own neighbour' ((Sql "SELECT count(*) FROM event_similarities WHERE source_event_id = target_event_id;") -eq '0')

Write-Host "`n== Cold start"
$fresh = [guid]::NewGuid().ToString()
$cold = (Call GET "/api/recommendations?visitorId=$fresh&limit=8").Json
Check 'a visitor with no history gets popular events, not personalised ones' (-not $cold.personalised -and $cold.items.Count -eq 8 -and -not ($cold.items | Where-Object reason -ne 'popular'))

Write-Host "`n== Personalisation follows behaviour"
$music = @($upcoming | Where-Object { $_.category -in 'Music', 'Nightlife' } | Select-Object -First 3)
$outdoors = @($upcoming | Where-Object { $_.category -in 'Outdoors', 'Sports' } | Select-Object -First 4)
Check 'the demo catalogue has music and outdoor events to open' ($music.Count -eq 3 -and $outdoors.Count -eq 4)
$null = Open-Events $fresh $music
$afterMusic = (Call GET "/api/recommendations?visitorId=$fresh&limit=8").Json
$musicShare = Category-Share $afterMusic.items 'Music', 'Nightlife'
Write-Host ("INFO  after opening 3 music events: {0:P0} of 8 picks are music or nightlife" -f $musicShare)
Check 'opening events makes the list personalised at once (no rollup wait)' ($afterMusic.personalised)
Check 'picks explain themselves with an event the visitor opened' (@($afterMusic.items | Where-Object { $_.reason -eq 'similar' -and $_.becauseOfEventId -in $music.id }).Count -ge 1)
Check 'events already opened are not recommended again' (-not ($afterMusic.items.event.id | Where-Object { $_ -in $music.id }))
$null = Open-Events $fresh $outdoors -Navigate
$afterOutdoors = (Call GET "/api/recommendations?visitorId=$fresh&limit=8").Json
$outdoorShare = Category-Share $afterOutdoors.items 'Outdoors', 'Sports'
$outdoorBefore = Category-Share $afterMusic.items 'Outdoors', 'Sports'
Write-Host ("INFO  outdoor and sport picks: {0:P0} before, {1:P0} after opening and navigating to 4 outdoor events" -f $outdoorBefore, $outdoorShare)
Check 'the list shifts towards the newer, stronger interest' ($outdoorShare -gt $outdoorBefore)
# The feed decides what "tomorrow" means (events overlapping tomorrow); picks must come from that list.
$scoped = (Call GET "/api/recommendations?visitorId=$fresh&limit=8&period=tomorrow").Json
$tomorrowFeed = (Call GET "/api/events?period=tomorrow&pageSize=100").Json.items.id
$outside = @($scoped.items | Where-Object { $_.event.id -notin $tomorrowFeed })
Check "recommendations respect the feed's period filter ($(@($scoped.items).Count) picks, all in tomorrow's feed)" ($scoped.items.Count -ge 1 -and $outside.Count -eq 0)

Write-Host "`n== More like this"
$source = $music[0]
$similar = (Call GET "/api/events/$($source.id)/similar?limit=6").Json
Check 'similar events exclude the event itself' ($similar.Count -ge 1 -and -not ($similar.id | Where-Object { $_ -eq $source.id }))
$sameFamily = @($similar | Where-Object { $_.category -in 'Music', 'Nightlife' }).Count
Check "most similar events share the event's kind ($sameFamily of $($similar.Count))" ($sameFamily -ge [Math]::Ceiling($similar.Count / 2))
Check 'an unknown event has no similar events' ((Call GET "/api/events/$([guid]::NewGuid())/similar").Json.Count -eq 0)

Write-Host "`n== Forgetting resets personalisation"
Check 'reset my history returns 204' ((Call POST '/api/analytics/forget' @{ visitorId = $fresh }).Status -eq 204)
Check 'after forgetting, the visitor is a cold start again' (-not (Call GET "/api/recommendations?visitorId=$fresh").Json.personalised)

Write-Host "`n== Offline evaluation in the production image"
$report = docker run --rm $Image --evaluate-recommendations /tmp/evaluation.md 2>&1 | Out-String
Check 'the evaluation runs without a database and prints the results table' ($report -match '\| Method \|' -and $report -match 'Oracle')
$blend = [regex]::Match($report, '\| Blend \(γ = [^|]+\| ([0-9.]+) \|').Groups[1].Value
$random = [regex]::Match($report, '\| Random \| ([0-9.]+) \|').Groups[1].Value
Write-Host "INFO  precision@5: blend $blend, random $random"
Check 'the blend beats random on precision@5' ([double]$blend -gt [double]$random)

Write-Host "`n$(if ($failures) { "$failures check(s) failed" } else { 'All checks passed' })"
exit $failures
