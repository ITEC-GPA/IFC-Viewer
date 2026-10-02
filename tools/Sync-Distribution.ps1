param([string]$PluginPath = '')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = Join-Path $projectRoot 'bin'
function Get-Sha256([string]$Path) {
    $hash = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { return ([BitConverter]::ToString($hash.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $hash.Dispose() }
}
function Assert-DistributionPath([string]$Target) {
    $absolute = [IO.Path]::GetFullPath($Target)
    if (-not $absolute.StartsWith($destination + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Percorso fuori da bin: $absolute" }
    for ($part = $absolute; $part.Length -ge $destination.Length; $part = Split-Path $part -Parent) {
        if ((Test-Path -LiteralPath $part) -and ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Percorso con collegamento/junction: $part" }
    }
}
$files = [ordered]@{
    'Meerkat.gha' = $(if ($PluginPath) { [IO.Path]::GetFullPath($PluginPath) } else { Join-Path $destination 'Meerkat.gha' })
    'ifc_reader.py' = Join-Path $projectRoot 'engine\ifc_reader.py'
    'Setup.ps1' = Join-Path $projectRoot 'installer\Setup.ps1'
    'Setup.cmd' = Join-Path $projectRoot 'Setup.cmd'
    'requirements.txt' = Join-Path $projectRoot 'requirements.txt'
    'README.md' = Join-Path $projectRoot 'DISTRIBUZIONE.md'
    'COMPONENTI.md' = Join-Path $projectRoot 'COMPONENTI.md'
    'Meerkat.gh' = Join-Path $projectRoot 'Meerkat.gh'
    'Meerkat.ghx' = Join-Path $projectRoot 'Meerkat.ghx'
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'examples') -File) {
    if ($file.Extension -in @('.gh', '.ghx', '.ifc', '.zip', '.md', '.json')) { $files['examples/' + $file.Name] = $file.FullName }
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets') -File -Recurse) {
    $files[$file.FullName.Substring($projectRoot.TrimEnd('\').Length + 1).Replace('\', '/')] = $file.FullName
}
foreach ($source in $files.Values) {
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Distribuzione incompleta, manca: $source" }
}
foreach ($name in $files.Keys) { Assert-DistributionPath (Join-Path $destination $name) }
Assert-DistributionPath (Join-Path $destination 'distribution.json')
if ((Test-Path -LiteralPath $destination) -and ((Get-Item -LiteralPath $destination -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'bin non puo essere un collegamento/junction.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($entry in $files.GetEnumerator()) {
    $target = Join-Path $destination $entry.Key
    $parent = Split-Path $target -Parent
    if ((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Cartella di distribuzione non valida: $parent" }
    if ((Test-Path -LiteralPath $target) -and ((Get-Item -LiteralPath $target -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "File di distribuzione non valido: $target" }
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    if ([IO.Path]::GetFullPath($entry.Value) -ine [IO.Path]::GetFullPath($target)) { Copy-Item -LiteralPath $entry.Value -Destination $target -Force }
}
$manifest = @($files.Keys | Sort-Object | ForEach-Object {
    [ordered]@{ path = $_; sha256 = Get-Sha256 (Join-Path $destination $_) }
})
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destination 'distribution.json') -Encoding UTF8
Write-Host "Distribuzione Meerkat pronta: $destination"
