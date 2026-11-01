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
if ($commit -match '^[0-9a-fA-F]{40}$') {
    $commitSource = Join-Path $outputPath 'BuildCommit.g.cs'
    [IO.File]::WriteAllText($commitSource, '[assembly: System.Reflection.AssemblyMetadata("BuildCommit", "' + $commit + '")]')
    $arguments += $commitSource
}
else { Write-Warning 'Commit del checkout non trovato: il plugin non potra verificare la propria bin.' }
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilazione fallita.' }
& (Join-Path $projectRoot 'tools\Sync-Distribution.ps1') -PluginPath (Join-Path $outputPath 'Meerkat.gha')
Write-Host "Plugin pronto: $projectRoot\bin\Meerkat.gha"
