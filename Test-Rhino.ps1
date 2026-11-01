param([switch]$ExamplesOnly, [switch]$All, [switch]$DistributionOnly, [int]$ExampleStart = 1, [int]$ExampleEnd = 18)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$rhinoDir = 'C:\Program Files\Rhino 8'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outputPath = Join-Path $projectRoot 'build'
$pluginDirectory = if ($env:MEERKAT_PLUGIN_DIR) { $env:MEERKAT_PLUGIN_DIR } else { Join-Path $projectRoot 'bin' }
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$refs = @(
    "$pluginDirectory\Meerkat.gha", "$rhinoDir\System\RhinoCommon.dll",
    "$rhinoDir\System\Newtonsoft.Json.dll", "$rhinoDir\Plug-ins\Grasshopper\Grasshopper.dll",
    "$rhinoDir\Plug-ins\Grasshopper\GH_IO.dll", 'System.Drawing.dll', 'System.Windows.Forms.dll'
)
$arguments = @('/nologo', '/platform:x64', '/target:exe', '/debug+', "/out:$outputPath\RhinoHarness.exe")
$arguments += $refs | ForEach-Object { "/reference:$_" }
$arguments += Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tests') -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilazione test fallita.' }
function Invoke-Harness([string]$Mode) {
    $completed = $false
    & "$outputPath\RhinoHarness.exe" $projectRoot $Mode | ForEach-Object { if ($_ -eq 'HARNESS_COMPLETED') { $completed = $true }; Write-Output $_ }
    if ($LASTEXITCODE -ne 0 -or -not $completed) { throw "Test Rhino incompleto o fallito: $Mode" }
}
function Invoke-Examples([int]$Start, [int]$End) {
    $env:MEERKAT_EXAMPLE_START = [string]$Start
    $env:MEERKAT_EXAMPLE_END = [string]$End
    Invoke-Harness 'examples'
}
if ($DistributionOnly) { Invoke-Harness 'distribution' }
elseif ($All) {
    Invoke-Harness 'test'
    # Native Rhino test documents and unit changes must not outlive their suite.
    # Keep each editing workflow in a fresh RhinoCore process on this host.
    Invoke-Examples 1 8
    Invoke-Examples 9 9
    Invoke-Examples 10 10
    Invoke-Examples 11 11
    foreach ($number in 12..18) { Invoke-Examples $number $number }
}
elseif ($ExamplesOnly) { Invoke-Examples $ExampleStart $ExampleEnd }
else { Invoke-Harness 'test' }
