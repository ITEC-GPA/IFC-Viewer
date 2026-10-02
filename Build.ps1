param([string]$RhinoDir = 'C:\Program Files\Rhino 8')
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$outputPath = Join-Path $projectRoot 'build\compiler'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs = @(
    "$RhinoDir\System\RhinoCommon.dll",
    "$RhinoDir\System\Newtonsoft.Json.dll",
    "$RhinoDir\Plug-ins\Grasshopper\Grasshopper.dll",
    "$RhinoDir\Plug-ins\Grasshopper\GH_IO.dll",
    'System.Drawing.dll', 'System.Windows.Forms.dll',
    'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll'
)
if (-not (Test-Path -LiteralPath $compiler)) { throw "Compilatore non trovato: $compiler" }
foreach ($reference in $refs | Where-Object { [IO.Path]::IsPathRooted($_) }) {
    if (-not (Test-Path -LiteralPath $reference)) { throw "Riferimento Rhino mancante: $reference" }
}
& (Join-Path $projectRoot 'tools\Build-Layout.ps1') -CleanModules
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$arguments = @('/nologo', '/target:library', '/optimize+', "/out:$outputPath\Meerkat.gha")
$arguments += $refs | ForEach-Object { "/reference:$_" }
$arguments += Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets\Icons\png') -Filter '*.png' | ForEach-Object { '/resource:' + $_.FullName + ',Meerkat.Icons.' + $_.Name }
$arguments += '/resource:' + (Join-Path $projectRoot 'Assets\Brand\icon-24.png') + ',Meerkat.Icons.Meerkat.png'
$arguments += Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilazione fallita.' }
& (Join-Path $projectRoot 'tools\Sync-Distribution.ps1') -PluginPath (Join-Path $outputPath 'Meerkat.gha')
Write-Host "Plugin pronto: $projectRoot\bin\Meerkat.gha"
