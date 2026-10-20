$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
& (Join-Path $projectRoot '.venv\Scripts\python.exe') (Join-Path $projectRoot 'tests\create_examples.py')
if ($LASTEXITCODE -ne 0) { throw 'Creazione modelli degli esempi fallita.' }
& (Join-Path $projectRoot '.venv\Scripts\python.exe') (Join-Path $projectRoot 'tests\create_numeric_example.py')
if ($LASTEXITCODE -ne 0) { throw 'Creazione modello numerico fallita.' }
& (Join-Path $projectRoot '.venv\Scripts\python.exe') (Join-Path $projectRoot 'tests\create_advanced_examples.py')
if ($LASTEXITCODE -ne 0) { throw 'Creazione esempi avanzati fallita.' }
& (Join-Path $projectRoot 'Test-Rhino.ps1') -All
& (Join-Path $projectRoot '.venv\Scripts\python.exe') (Join-Path $projectRoot 'tests\check_examples.py')
if ($LASTEXITCODE -ne 0) { throw 'Verifica CSV degli esempi fallita.' }
