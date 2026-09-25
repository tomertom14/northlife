<#
  Starts the whole NorthLife stack in Docker for local QA: PostgreSQL, Mailpit and the production
  image of the app on http://localhost:10000 (optionally Prometheus and Grafana).
  Works on Windows PowerShell 5.1 and PowerShell 7. Reads settings from .env.

  Usage:
    powershell -File scripts/local/start-stack.ps1 [-Build] [-BootstrapAdmin] [-SeedDemo] [-Monitoring]

    -Build           rebuild the northlife:local image first (after code changes)
    -BootstrapAdmin  create the administrator from the BootstrapAdmin__* values in .env
    -SeedDemo        add the demo catalogue and 30 days of simulated traffic (once)
    -Monitoring      also start Prometheus (:9090) and Grafana (:3000)
#>
param(
  [switch]$Build,
  [switch]$BootstrapAdmin,
  [switch]$SeedDemo,
  [switch]$Monitoring,
  [int]$Port = 10000
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..')).Path
Set-Location $root
if (-not (Test-Path .env)) { throw 'Copy .env.example to .env and fill it in first (see docs/local-qa.md).' }

$settings = @{}
Get-Content .env | Where-Object { $_ -match '^[A-Za-z_][A-Za-z0-9_]*=' } | ForEach-Object {
  $key, $value = $_ -split '=', 2
  $settings[$key] = $value.Trim()
}
function Setting([string]$Key, [string]$Default = '') { if ($settings[$Key]) { $settings[$Key] } else { $Default } }

Write-Host 'Starting PostgreSQL and Mailpit...'
docker compose up -d postgres mailpit | Out-Null
if ($Monitoring) {
  Write-Host 'Starting Prometheus and Grafana...'
  docker compose --profile monitoring up -d prometheus grafana | Out-Null
}

if ($Build -or -not (docker image ls -q northlife:local)) {
  Write-Host 'Building the northlife:local image (a few minutes the first time)...'
  docker build -t northlife:local . | Out-Null
}

# The compose network, whatever the project folder is called.
$postgres = docker compose ps -q postgres
$network = (docker inspect $postgres --format '{{range $name, $_ := .NetworkSettings.Networks}}{{$name}}{{end}}').Trim()
for ($i = 0; $i -lt 30; $i++) {
  if ((docker inspect $postgres --format '{{.State.Health.Status}}') -eq 'healthy') { break }
  Start-Sleep -Seconds 1
}

$user = Setting 'POSTGRES_USER' 'northlife'
$database = Setting 'POSTGRES_DB' 'northlife'
$common = @(
  '--network', $network,
  '-e', "Database__Url=postgresql://${user}:$(Setting 'POSTGRES_PASSWORD')@postgres:5432/$database",
  '-e', "Authentication__JwtKey=$(Setting 'Authentication__JwtKey')",
  '-e', 'Email__Provider=Smtp', '-e', 'Email__Smtp__Host=mailpit', '-e', 'Email__Smtp__Port=1025',
  '-e', "Email__PublicBaseUrl=http://localhost:$Port",
  '-e', "GoogleMaps__ApiKey=$(Setting 'GoogleMaps__ApiKey')",
  '-e', "GoogleMaps__MapId=$(Setting 'GoogleMaps__MapId')",
  '-e', "Google__ClientId=$(Setting 'Google__ClientId')",
  '-e', "Demo__OwnerPassword=$(Setting 'Demo__OwnerPassword')",
  # Local conveniences: numbers appear within a minute, and Prometheus may scrape without a token.
  '-e', 'Analytics__RollupIntervalSeconds=20', '-e', 'Analytics__IngestLagSeconds=30',
  '-e', 'Metrics__AllowPrivateNetwork=true',
  '-v', 'northlife-images:/var/data/images', '-v', 'northlife-keys:/var/data/keys'
)

Write-Host 'Applying database migrations...'
docker run --rm @common northlife:local --migrate | Out-Null

if ($BootstrapAdmin) {
  Write-Host 'Creating the administrator...'
  $admin = @('-e', "BootstrapAdmin__Email=$(Setting 'BootstrapAdmin__Email')", '-e', "BootstrapAdmin__Password=$(Setting 'BootstrapAdmin__Password')",
    '-e', "BootstrapAdmin__FullName=$(Setting 'BootstrapAdmin__FullName' 'NorthLife Admin')", '-e', "BootstrapAdmin__Phone=$(Setting 'BootstrapAdmin__Phone' '0500000000')",
    '-e', "BootstrapAdmin__BusinessName=$(Setting 'BootstrapAdmin__BusinessName' 'NorthLife')")
  docker run --rm @common @admin northlife:local --bootstrap-admin | Out-Null
}

if ($SeedDemo) {
  Write-Host 'Seeding the demo catalogue and simulated traffic...'
  docker run --rm @common -e 'Analytics__WorkerEnabled=false' northlife:local --seed-demo 2>&1 |
    Select-String 'Demo data' | ForEach-Object { ($_.Line | ConvertFrom-Json).Message }
}

if (docker ps -a -q --filter 'name=^northlife-app$') {
  docker container stop northlife-app | Out-Null
  docker container rm northlife-app | Out-Null
}
docker run -d --name northlife-app -p "${Port}:10000" @common northlife:local | Out-Null
for ($i = 0; $i -lt 40; $i++) {
  try {
    if ((Invoke-WebRequest "http://localhost:$Port/health/ready" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200) { break }
  } catch { Start-Sleep -Milliseconds 750 }
}

Write-Host ''
Write-Host "NorthLife     http://localhost:$Port"
Write-Host 'Mailpit       http://localhost:8025   (every email the app sends)'
if ($Monitoring) { Write-Host 'Grafana       http://localhost:3000   (dashboard "NorthLife operations")' }
Write-Host "Metrics       http://localhost:$Port/metrics"
