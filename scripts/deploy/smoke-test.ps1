<#
  Checks a deployed NorthLife (or the local stack) from the outside: health, the app shell and its caching,
  compression, the public API, the browser configuration and that /metrics is closed.

  Usage: powershell -File scripts/deploy/smoke-test.ps1 -BaseUrl https://northlife.onrender.com
  Exit code 0 when every check passes; warnings do not fail the run.
#>
param([Parameter(Mandatory)][string]$BaseUrl)
$ErrorActionPreference = 'Stop'
$BaseUrl = $BaseUrl.TrimEnd('/')
Add-Type -AssemblyName System.Net.Http

$handler = New-Object System.Net.Http.HttpClientHandler
$handler.AllowAutoRedirect = $false
$handler.AutomaticDecompression = [System.Net.DecompressionMethods]::None
$client = New-Object System.Net.Http.HttpClient($handler)
$client.Timeout = [TimeSpan]::FromSeconds(60)
$script:failures = 0

function Get-Url([string]$Path, [hashtable]$Headers = @{}) {
  $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, "$BaseUrl$Path")
  foreach ($name in $Headers.Keys) { $request.Headers.TryAddWithoutValidation($name, $Headers[$name]) | Out-Null }
  $response = $client.SendAsync($request).GetAwaiter().GetResult()
  $body = if ($response.Content.Headers.ContentEncoding.Count -eq 0) { $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() } else { '' }
  [pscustomobject]@{ Status = [int]$response.StatusCode; Response = $response; Body = $body }
}
function Check([string]$Name, [bool]$Ok, [string]$Detail = '') {
  if ($Ok) { Write-Host "PASS  $Name $Detail" -ForegroundColor Green }
  else { Write-Host "FAIL  $Name $Detail" -ForegroundColor Red; $script:failures++ }
}
function Warn([string]$Name, [string]$Detail) { Write-Host "WARN  $Name $Detail" -ForegroundColor Yellow }

# The first request can wake a sleeping or just-deployed service.
$live = Get-Url '/health/live'
Check 'liveness' ($live.Status -eq 200) "($($live.Status))"
$ready = Get-Url '/health/ready'
Check 'readiness (database reachable)' ($ready.Status -eq 200) "($($ready.Status))"

$shell = Get-Url '/'
Check 'app shell' ($shell.Status -eq 200 -and $shell.Body -match '<app-root') "($($shell.Status))"
$cache = "$($shell.Response.Headers.CacheControl)"
Check 'index.html is revalidated' ($cache -match 'no-cache') "($cache)"
$compressed = Get-Url '/' @{ 'Accept-Encoding' = 'br, gzip' }
$encoding = ($compressed.Response.Content.Headers.ContentEncoding -join ',')
Check 'app shell is compressed' ($encoding -match 'br|gzip') "($encoding)"

$main = [regex]::Match($shell.Body, 'src="(main-[A-Za-z0-9_]+\.js)"').Groups[1].Value
if ($main) {
  $asset = Get-Url "/$main"
  Check 'hashed bundle cached for a year' ("$($asset.Response.Headers.CacheControl)" -match 'immutable') "($main)"
} else { Check 'hashed bundle found in index.html' $false }

$fallback = Get-Url '/manage/login'
Check 'deep link served by the app' ($fallback.Status -eq 200 -and $fallback.Body -match '<app-root') "($($fallback.Status))"

$events = Get-Url '/api/events?period=today&pageSize=5'
Check 'public events API' ($events.Status -eq 200 -and $events.Body -match '"items"') "($($events.Status))"
$places = Get-Url '/api/places?pageSize=5'
Check 'public places API' ($places.Status -eq 200 -and $places.Body -match '"items"') "($($places.Status))"

$config = Get-Url '/api/config/public'
Check 'browser configuration' ($config.Status -eq 200) "($($config.Status))"
if ($config.Status -eq 200) {
  $settings = $config.Body | ConvertFrom-Json
  if (-not $settings.googleMapsApiKey) { Warn 'Google Maps' 'no API key: the map page says it is unavailable' }
  if (-not $settings.googleMapsMapId -or $settings.googleMapsMapId -eq 'DEMO_MAP_ID') { Warn 'Google Maps' 'no production Map ID (DEMO_MAP_ID is for development only)' }
  if (-not $settings.googleClientId) { Warn 'Google sign-in' 'no client ID: the Google button is hidden' }
}

$metrics = Get-Url '/metrics'
if ($BaseUrl -match '//(localhost|127.0.0.1)') { Warn '/metrics' 'not checked: the local stack opens it to private networks' }
else { Check '/metrics is not public' ($metrics.Status -ne 200) "($($metrics.Status))" }

if ($BaseUrl.StartsWith('https://')) {
  $httpClient = New-Object System.Net.Http.HttpClient($handler)
  try {
    $redirect = $httpClient.GetAsync(($BaseUrl -replace '^https://', 'http://') + '/').GetAwaiter().GetResult()
    Check 'http redirects to https' ([int]$redirect.StatusCode -in 301, 302, 307, 308) "($([int]$redirect.StatusCode))"
  } catch { Warn 'http redirect' $_.Exception.Message }
}

Write-Host ''
if ($script:failures -eq 0) { Write-Host 'All checks passed.' -ForegroundColor Green; exit 0 }
Write-Host "$($script:failures) check(s) failed." -ForegroundColor Red
exit 1
