$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$distribution = Join-Path $projectRoot ('test-output\portable-' + [guid]::NewGuid().ToString('N') + '\Meerkat')
$binPath = Join-Path $projectRoot 'bin'
$manifest = @(Get-Content -LiteralPath (Join-Path $binPath 'distribution.json') -Raw | ConvertFrom-Json)
foreach ($entry in $manifest) {
    $target = Join-Path $distribution $entry.path
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $binPath $entry.path) -Destination $target
}
if (Test-Path -LiteralPath (Join-Path (Split-Path $distribution -Parent) 'src')) { throw 'Test portabilita non isolato.' }
& (Join-Path $distribution 'Setup.ps1') -RuntimeOnly -Python (Join-Path $projectRoot '.venv\Scripts\python.exe')
$savedDirectory = $env:MEERKAT_PLUGIN_DIR
try {
    $env:MEERKAT_PLUGIN_DIR = $distribution
    & (Join-Path $projectRoot 'Test-Rhino.ps1') -DistributionOnly
} finally { $env:MEERKAT_PLUGIN_DIR = $savedDirectory }
Set-Content -LiteralPath (Join-Path $projectRoot 'test-output\last-portable-output.txt') -Value $distribution
Write-Host "PASS: distribuzione isolata con runtime locale: $distribution"
