$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$projectRoot = $PSScriptRoot
$destination = Join-Path $projectRoot 'IFC_Viewer_Pacchetto.zip'
$staging = Join-Path $projectRoot ('IFC_Viewer_Pacchetto.' + [guid]::NewGuid().ToString('N') + '.zip')
$files = @('IFC_Viewer.gh', 'IFC_Viewer.ghx', 'README.md', 'COMPONENTI.md', 'Setup.cmd', 'Setup.ps1', 'Build.ps1', 'Build-Examples.ps1', 'Test-Rhino.ps1', 'Package.ps1', 'requirements.txt')
foreach ($folder in @('src', 'engine', 'tests', 'dist', 'examples')) {
    $files += Get-ChildItem -LiteralPath (Join-Path $projectRoot $folder) -File | Where-Object { $_.Extension -notin @('.pdb', '.png') } | ForEach-Object { "$folder\$($_.Name)" }
}
try {
    $archive = [IO.Compression.ZipFile]::Open($staging, 'Create')
    try {
        foreach ($file in $files) {
            $source = Join-Path $projectRoot $file
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $source, ('IFC_Viewer/' + $file.Replace('\', '/')), 'Optimal') | Out-Null
        }
    } finally { $archive.Dispose() }
    if (Test-Path -LiteralPath $destination) {
        $backupPath = Join-Path $projectRoot ('IFC_Viewer_Pacchetto.backup.' + [guid]::NewGuid().ToString('N') + '.zip')
        [IO.File]::Replace($staging, $destination, $backupPath)
        Remove-Item -LiteralPath $backupPath
    }
    else { [IO.File]::Move($staging, $destination) }
} finally { if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging } }
Write-Host "Pacchetto creato: $destination"
