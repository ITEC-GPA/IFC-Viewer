$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$projectRoot = $PSScriptRoot
function Get-Sha256([string]$Path) {
    $hash = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { return ([BitConverter]::ToString($hash.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $hash.Dispose() }
}
$binPath = Join-Path $projectRoot 'bin'
$manifestPath = Join-Path $binPath 'distribution.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Prima eseguire Build.ps1: manca la distribuzione bin.' }
$manifest = @(Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json)
$files = @('distribution.json')
foreach ($entry in $manifest) {
    $source = [IO.Path]::GetFullPath((Join-Path $binPath $entry.path))
    if (-not $source.StartsWith($binPath + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Percorso non valido nel manifest.' }
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "File mancante in bin: $($entry.path)" }
    if ((Get-Sha256 $source) -ine $entry.sha256) { throw "File cambiato dopo la build: $($entry.path). Eseguire nuovamente Build.ps1." }
    $files += $entry.path
}
$destination = Join-Path $projectRoot 'Meerkat_Pacchetto.zip'
$staging = Join-Path $projectRoot ('build\Meerkat.' + [guid]::NewGuid().ToString('N') + '.zip')
New-Item -ItemType Directory -Path (Split-Path $staging -Parent) -Force | Out-Null
try {
    $archive = [IO.Compression.ZipFile]::Open($staging, 'Create')
    try {
        foreach ($file in $files) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $binPath $file), ('Meerkat/' + $file.Replace('\', '/')), 'Optimal') | Out-Null
        }
    } finally { $archive.Dispose() }
    if (Test-Path -LiteralPath $destination) {
        $backupPath = Join-Path $projectRoot ('build\Meerkat.backup.' + [guid]::NewGuid().ToString('N') + '.zip')
        [IO.File]::Replace($staging, $destination, $backupPath)
        Remove-Item -LiteralPath $backupPath
    }
    else { [IO.File]::Move($staging, $destination) }
} finally { if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging } }
Write-Host "Pacchetto distribuibile da bin: $destination"
