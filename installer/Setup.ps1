param([string]$Python = '', [switch]$RegisterOnly, [switch]$RuntimeOnly)
$ErrorActionPreference = 'Stop'
if ($RegisterOnly -and $RuntimeOnly) { throw 'Scegliere RegisterOnly oppure RuntimeOnly.' }
$pluginFolder = [IO.Path]::GetFullPath($PSScriptRoot)
if (-not (Test-Path -LiteralPath (Join-Path $pluginFolder 'Meerkat.gha'))) { throw 'Meerkat.gha mancante. Eseguire Setup dalla cartella bin distribuita.' }
if (-not $RegisterOnly) {
    $runtimePython = Join-Path $pluginFolder '.venv\Scripts\python.exe'
    if (-not (Test-Path -LiteralPath $runtimePython)) {
        if (-not $Python) {
            if (Get-Command py -ErrorAction SilentlyContinue) {
                foreach ($version in @('3.12', '3.11')) {
                    try { $candidate = & py "-$version" -c "import sys,struct; assert struct.calcsize('P')==8; print(sys.executable)" 2>$null }
                    catch { continue }
                    if ($LASTEXITCODE -eq 0 -and $candidate) { $Python = [string]$candidate; break }
                }
            }
            if (-not $Python) { throw 'Installare Python x64 3.12 o 3.11, oppure indicare Setup.ps1 -Python percorso\python.exe.' }
        }
        & $Python -c "import sys,struct; assert struct.calcsize('P')==8 and (3,9)<=sys.version_info[:2]<=(3,12), 'Richiesto Python x64 3.9-3.12'"
        if ($LASTEXITCODE -ne 0) { throw 'Interprete non compatibile con le wheel IfcOpenShell 0.8.3.' }
        & $Python -m venv (Join-Path $pluginFolder '.venv')
        if ($LASTEXITCODE -ne 0) { throw 'Serve Python x64 compatibile con IfcOpenShell 0.8.3. Eseguire Setup.ps1 -Python percorso\python.exe.' }
    }
    & $runtimePython -c "import sys,struct; assert struct.calcsize('P')==8 and (3,9)<=sys.version_info[:2]<=(3,12); print('Python',sys.version); print('AVVISO: runtime legacy; aggiornare a Python 3.12 in una nuova distribuzione.' if sys.version_info[:2]<(3,11) else '')"
    if ($LASTEXITCODE -ne 0) { throw 'Il runtime esistente non e Python x64 utilizzabile. Preparare una nuova distribuzione.' }
    & $runtimePython -m pip install -r (Join-Path $pluginFolder 'requirements.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Installazione IfcOpenShell fallita.' }
    & $runtimePython -c 'import ifcopenshell, ifctester; print(ifcopenshell.version)'
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
