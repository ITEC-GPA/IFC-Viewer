param([string]$Python = '', [switch]$RegisterOnly, [switch]$RuntimeOnly)
$ErrorActionPreference = 'Stop'
if ($RegisterOnly -and $RuntimeOnly) { throw 'Scegliere RegisterOnly oppure RuntimeOnly.' }
$pluginFolder = [IO.Path]::GetFullPath($PSScriptRoot)
if (-not (Test-Path -LiteralPath (Join-Path $pluginFolder 'Meerkat.gha'))) { throw 'Meerkat.gha mancante. Eseguire Setup dalla cartella bin distribuita.' }
if (-not $RegisterOnly) {
    $runtimePython = Join-Path $pluginFolder '.venv\Scripts\python.exe'
    if (-not (Test-Path -LiteralPath $runtimePython)) {
        if ($Python) { & $Python -m venv (Join-Path $pluginFolder '.venv') }
        else { & py -3.9 -m venv (Join-Path $pluginFolder '.venv') }
        if ($LASTEXITCODE -ne 0) { throw 'Serve Python x64 compatibile con IfcOpenShell 0.8.3. Eseguire Setup.ps1 -Python percorso\python.exe.' }
    }
    & $runtimePython -m pip install -r (Join-Path $pluginFolder 'requirements.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Installazione IfcOpenShell fallita.' }
    & $runtimePython -c 'import ifcopenshell; print(ifcopenshell.version)'
    if ($LASTEXITCODE -ne 0) { throw 'Verifica runtime IFC fallita.' }
    Write-Host 'Runtime IFC pronto nella cartella di Meerkat.'
}
if (-not $RuntimeOnly) {
    $libraries = Join-Path $env:APPDATA 'Grasshopper\Libraries'
    $linkPath = Join-Path $libraries 'Meerkat.ghlink'
    $legacyLink = Join-Path $libraries 'IFCViewer.ghlink'
    $oldFolder = Join-Path (Split-Path $pluginFolder -Parent) 'dist'
    if (Test-Path -LiteralPath $linkPath) {
        $existing = (Get-Content -LiteralPath $linkPath -Raw).Trim()
        if ($existing -ine $pluginFolder) { throw "Meerkat e gia registrato da $existing. Aggiornare Meerkat.ghlink per spostare l'installazione." }
    }
    $removeLegacy = $false
    if (Test-Path -LiteralPath $legacyLink) {
        $existing = (Get-Content -LiteralPath $legacyLink -Raw).Trim()
        if ($existing -ine $oldFolder -and $existing -ine $pluginFolder) { throw "Vecchio IFC Viewer registrato da $existing. Disattivare quel collegamento prima di installare Meerkat (stessi GUID)." }
        $removeLegacy = $true
    }
    New-Item -ItemType Directory -Path $libraries -Force | Out-Null
    [IO.File]::WriteAllText($linkPath, $pluginFolder, [Text.UTF8Encoding]::new($false))
    if ($removeLegacy) { Remove-Item -LiteralPath $legacyLink -Force }
    Write-Host "Meerkat registrato: $linkPath"
}
Write-Host 'Riavviare Rhino 8 e aprire Meerkat.gh oppure uno degli examples.'
