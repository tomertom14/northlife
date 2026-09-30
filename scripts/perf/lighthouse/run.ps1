<#
  Runs the containerised Lighthouse audit against the local stack (scripts/local/start-stack.ps1).
  The container joins the app container's network namespace, so http://localhost:10000 inside it is the app
  itself: the Google Maps key restricted to localhost keeps working and no host software sits in between.

  Usage: powershell -File scripts/perf/lighthouse/run.ps1 [-Runs 3] [-Tag after] [-OutDir docs/evaluation/lighthouse]
#>
param(
  [int]$Runs = 3,
  [string]$Tag = 'run',
  [string]$OutDir = '',
  [string]$AppContainer = 'northlife-app'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..\..')).Path
if (-not $OutDir) { $OutDir = Join-Path $root 'docs\evaluation\lighthouse' }
New-Item -ItemType Directory -Force $OutDir | Out-Null

docker build -t northlife-lighthouse (Join-Path $root 'scripts\perf\lighthouse') | Out-Null
foreach ($formFactor in 'mobile', 'desktop') {
  docker run --rm --network "container:$AppContainer" -v "${OutDir}:/out" northlife-lighthouse $formFactor $Runs $Tag
}
