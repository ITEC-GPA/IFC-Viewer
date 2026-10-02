param([switch]$ExamplesOnly, [switch]$All, [switch]$DistributionOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$rhinoDir = 'C:\Program Files\Rhino 8'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outputPath = Join-Path $projectRoot 'build'
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$refs = @(
    "$projectRoot\bin\Meerkat.gha", "$rhinoDir\System\RhinoCommon.dll",
    "$rhinoDir\System\Newtonsoft.Json.dll", "$rhinoDir\Plug-ins\Grasshopper\Grasshopper.dll",
    "$rhinoDir\Plug-ins\Grasshopper\GH_IO.dll", 'System.Drawing.dll', 'System.Windows.Forms.dll'
)
$arguments = @('/nologo', '/platform:x64', '/target:exe', '/debug+', "/out:$outputPath\RhinoHarness.exe")
$arguments += $refs | ForEach-Object { "/reference:$_" }
$arguments += Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tests') -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilazione test fallita.' }
if ($DistributionOnly) { & "$outputPath\RhinoHarness.exe" $projectRoot 'distribution' }
elseif ($All) {
    & "$outputPath\RhinoHarness.exe" $projectRoot 'test'
    if ($LASTEXITCODE -ne 0) { throw 'Test Rhino fallito.' }
    & "$outputPath\RhinoHarness.exe" $projectRoot 'examples'
}
elseif ($ExamplesOnly) { & "$outputPath\RhinoHarness.exe" $projectRoot 'examples' }
else { & "$outputPath\RhinoHarness.exe" $projectRoot 'test' }
if ($LASTEXITCODE -ne 0) { throw 'Test Rhino fallito.' }
