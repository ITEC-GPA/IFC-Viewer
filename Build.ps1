param([string]$RhinoDir = 'C:\Program Files\Rhino 8', [string]$DistributionDirectory = '')
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$outputPath = Join-Path $projectRoot 'build\compiler'
$project = Join-Path $projectRoot 'src\Meerkat.csproj'
# Meerkat targets .NET 8 (net8.0-windows): the SDK project is built with dotnet, the Rhino references are those of the local Rhino 8.
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Comando dotnet non trovato: installare SDK .NET 8 o successivo.' }
$refs = @(
    "$RhinoDir\System\netcore\RhinoCommon.dll",
    "$RhinoDir\System\netcore\Newtonsoft.Json.dll",
    "$RhinoDir\Plug-ins\Grasshopper\Grasshopper.dll",
    "$RhinoDir\Plug-ins\Grasshopper\GH_IO.dll"
)
foreach ($reference in $refs) {
    if (-not (Test-Path -LiteralPath $reference)) { throw "Riferimento Rhino mancante: $reference" }
}

# The commit of the build, read at load by src/PluginBuildCheck.cs: HEAD of this checkout, worktree or submodule.
function Get-HeadCommit([string]$Root) {
    $gitDir = Join-Path $Root '.git'
    if (Test-Path -LiteralPath $gitDir -PathType Leaf) {
        $gitDir = [IO.Path]::GetFullPath([IO.Path]::Combine($Root, ((Get-Content -LiteralPath $gitDir -Raw).Trim() -replace '^gitdir:\s*', '')))
    }
    if (-not (Test-Path -LiteralPath (Join-Path $gitDir 'HEAD') -PathType Leaf)) { return $null }
    $common = $gitDir
    if (Test-Path -LiteralPath (Join-Path $gitDir 'commondir') -PathType Leaf) {
        $common = [IO.Path]::GetFullPath([IO.Path]::Combine($gitDir, (Get-Content -LiteralPath (Join-Path $gitDir 'commondir') -Raw).Trim()))
    }
    $head = (Get-Content -LiteralPath (Join-Path $gitDir 'HEAD') -Raw).Trim()
    if ($head -notmatch '^ref:\s*(\S+)$') { return $head }
    $reference = $Matches[1]
    foreach ($directory in $gitDir, $common) {
        $file = [IO.Path]::Combine($directory, $reference)
        if (Test-Path -LiteralPath $file -PathType Leaf) { return (Get-Content -LiteralPath $file -Raw).Trim() }
    }
    $packed = Join-Path $common 'packed-refs'
    if (Test-Path -LiteralPath $packed -PathType Leaf) {
        foreach ($line in Get-Content -LiteralPath $packed) {
            if ($line -match ('^([0-9a-fA-F]{40}) ' + [regex]::Escape($reference) + '$')) { return $Matches[1] }
        }
    }
    return $null
}
$commit = Get-HeadCommit $projectRoot
# Directory.Build.targets writes the BuildCommit attribute from SourceRevisionId; an empty value writes none.
$sourceRevision = ''
if ($commit -match '^[0-9a-fA-F]{40}$') { $sourceRevision = $commit }
else { Write-Warning 'Commit del checkout non trovato: il plugin non potra verificare la propria bin.' }
# The .gha is built in build\compiler, then Sync-Distribution copies it with the other files into bin (or -DistributionDirectory).
# Directory.Build.targets also removes the module bins before the build (tools\Build-Layout.ps1 -CleanModules).
& dotnet build $project -c Release --nologo -v:minimal "-p:RhinoDir=$RhinoDir" "-p:OutDir=$outputPath" '-p:MeerkatSyncDistribution=false' "-p:SourceRevisionId=$sourceRevision"
if ($LASTEXITCODE -ne 0) { throw 'Compilazione fallita.' }
& (Join-Path $projectRoot 'tools\Sync-Distribution.ps1') -PluginPath (Join-Path $outputPath 'Meerkat.gha') -DistributionDirectory $DistributionDirectory
