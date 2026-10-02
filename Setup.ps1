param([string]$Python = '', [switch]$RegisterOnly, [switch]$RuntimeOnly)
$ErrorActionPreference = 'Stop'
$installer = Join-Path $PSScriptRoot 'bin\Setup.ps1'
if (-not (Test-Path -LiteralPath $installer)) { throw 'Prima compilare con Build.ps1 per creare bin.' }
& $installer @PSBoundParameters
