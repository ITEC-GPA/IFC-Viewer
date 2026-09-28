param([string]$Python = '')
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$runtimePython = Join-Path $projectRoot '.venv\Scripts\python.exe'
if (!(Test-Path -LiteralPath $runtimePython)) {
    if ($Python) { & $Python -m venv (Join-Path $projectRoot '.venv') }
    else { & py -3.9 -m venv (Join-Path $projectRoot '.venv') }
    if ($LASTEXITCODE -ne 0) { throw 'Serve Python 3.9-3.13 a 64 bit. Eseguire Setup.ps1 -Python percorso\python.exe.' }
}
& $runtimePython -m pip install -r (Join-Path $projectRoot 'requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'Installazione IfcOpenShell fallita.' }
Write-Host 'Runtime IFC pronto.'
$libraries = Join-Path $env:APPDATA 'Grasshopper\Libraries'
New-Item -ItemType Directory -Path $libraries -Force | Out-Null
$linkPath = Join-Path $libraries 'IFCViewer.ghlink'
$pluginFolder = Join-Path $projectRoot 'dist'
if (Test-Path -LiteralPath $linkPath) {
    $existingLink = (Get-Content -LiteralPath $linkPath -Raw).Trim()
    if ($existingLink -ne $pluginFolder) { throw "Esiste gia un IFCViewer.ghlink diretto a $existingLink. Aggiornare quel collegamento manualmente." }
}
[IO.File]::WriteAllText($linkPath, $pluginFolder, [Text.UTF8Encoding]::new($false))
Write-Host "Componenti registrati: $linkPath"
Write-Host 'Riavviare Rhino e aprire IFC_Viewer.gh.'
