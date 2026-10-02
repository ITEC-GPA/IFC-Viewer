$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$suite = Join-Path $projectRoot ('test-output\layout-' + [guid]::NewGuid().ToString('N'))
$fixture = Join-Path $suite 'project'
function Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    Write-Host "PASS: $Message"
}
function Marker([string]$Relative) {
    $target = Join-Path $fixture $Relative
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Set-Content -LiteralPath $target -Value 'keep or remove as specified'
}
Marker 'src\Meerkat.csproj'
New-Item -ItemType Directory -Path (Join-Path $fixture 'tools') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'tools\Build-Layout.ps1') -Destination (Join-Path $fixture 'tools\Build-Layout.ps1')
foreach ($name in @('bin\root.keep', 'src\bin\old.dll', 'modules\nested\bin\old.dll', 'src\keep.cs', '.venv\bin\keep', 'node_modules\package\bin\keep', 'build\bin\keep', 'vendor\repo\.git\marker', 'vendor\repo\bin\keep')) { Marker $name }
& (Join-Path $fixture 'tools\Build-Layout.ps1') -CleanModules
Check (-not (Test-Path -LiteralPath (Join-Path $fixture 'src\bin'))) 'Remove module bin'
Check (-not (Test-Path -LiteralPath (Join-Path $fixture 'modules\nested\bin'))) 'Remove nested module bin'
foreach ($name in @('bin\root.keep', 'src\keep.cs', '.venv\bin\keep', 'node_modules\package\bin\keep', 'build\bin\keep', 'vendor\repo\bin\keep')) { Check (Test-Path -LiteralPath (Join-Path $fixture $name)) "Preserve $name" }
& (Join-Path $fixture 'tools\Build-Layout.ps1') -CleanModules
Check (Test-Path -LiteralPath (Join-Path $fixture 'bin\root.keep')) 'Repeated cleanup preserves root bin'

# A bin with a junction must fail before any destructive operation.
$outside = Join-Path $suite 'outside'
New-Item -ItemType Directory -Path $outside -Force | Out-Null
Set-Content -LiteralPath (Join-Path $outside 'keep.txt') -Value 'outside must survive'
Marker 'src\bin\old.dll'
New-Item -ItemType Junction -Path (Join-Path $fixture 'src\bin\linked') -Target $outside | Out-Null
$rejected = $false
try { & (Join-Path $fixture 'tools\Build-Layout.ps1') -CleanModules } catch { $rejected = $_.Exception.Message -like '*junction*' }
Check $rejected 'Reject reparse point inside deletion candidate'
Check ((Test-Path -LiteralPath (Join-Path $outside 'keep.txt')) -and (Test-Path -LiteralPath (Join-Path $fixture 'src\bin\old.dll'))) 'No partial deletion on rejected junction'
Write-Host "Fixture retained for inspection: $suite"
