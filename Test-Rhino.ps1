param([string]$RhinoDir = 'C:\Program Files\Rhino 8', [switch]$ExamplesOnly, [switch]$All, [switch]$DistributionOnly, [int]$ExampleStart = 1, [int]$ExampleEnd = 18)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$outputPath = Join-Path $projectRoot 'build\harness'
$pluginDirectory = if ($env:MEERKAT_PLUGIN_DIR) { $env:MEERKAT_PLUGIN_DIR } else { Join-Path $projectRoot 'bin' }
# Rhino.Inside host on .NET 8, the runtime of Meerkat, compiled against the plug-in under test (tests\RhinoHarness.csproj).
& dotnet build (Join-Path $projectRoot 'tests\RhinoHarness.csproj') -c Release --nologo -v:minimal "-p:RhinoDir=$RhinoDir" ('-p:MeerkatPluginDir=' + $pluginDirectory.TrimEnd('\')) "-p:OutDir=$outputPath"
if ($LASTEXITCODE -ne 0) { throw 'Compilazione test fallita.' }
function Invoke-Harness([string]$Mode) {
    $completed = $false
    $savedRhinoDirectory = $env:MEERKAT_RHINO_DIR
    try {
        $env:MEERKAT_RHINO_DIR = $RhinoDir
        # Started through dotnet: a freshly built apphost can be refused by the antivirus.
        & dotnet (Join-Path $outputPath 'RhinoHarness.dll') $projectRoot $Mode | ForEach-Object { if ($_ -eq 'HARNESS_COMPLETED') { $completed = $true }; Write-Output $_ }
    } finally { $env:MEERKAT_RHINO_DIR = $savedRhinoDirectory }
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
