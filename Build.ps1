param([string]$RhinoDir = 'C:\Program Files\Rhino 8')
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$outputPath = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs = @(
    "$RhinoDir\System\RhinoCommon.dll",
    "$RhinoDir\System\Newtonsoft.Json.dll",
    "$RhinoDir\Plug-ins\Grasshopper\Grasshopper.dll",
    "$RhinoDir\Plug-ins\Grasshopper\GH_IO.dll",
    'System.Drawing.dll', 'System.Windows.Forms.dll',
    'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll'
)
$arguments = @('/nologo', '/target:library', '/optimize+', "/out:$outputPath\IfcViewer.gha")
$arguments += $refs | ForEach-Object { "/reference:$_" }
$arguments += Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilazione fallita.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'engine\ifc_reader.py') -Destination $outputPath -Force
Write-Host "Plugin pronto: $outputPath\IfcViewer.gha"
