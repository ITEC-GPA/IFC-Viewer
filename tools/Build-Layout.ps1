param([switch]$CleanModules)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\', '/')
$rootPrefix = $projectRoot + [IO.Path]::DirectorySeparatorChar

function Assert-ProjectPath([string]$Path) {
    $absolute = [IO.Path]::GetFullPath($Path)
    if (-not $absolute.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Percorso fuori dal progetto: $absolute"
    }
    return $absolute
}

function Assert-NoReparsePoints([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Pulizia rifiutata: collegamento/junction $Path" }
    if ($item.PSIsContainer) {
        foreach ($child in Get-ChildItem -LiteralPath $Path -Force) { Assert-NoReparsePoints $child.FullName }
    }
}

if ($CleanModules) {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'src\Meerkat.csproj'))) { throw 'Root Meerkat non riconosciuta.' }
    # Never walk dependencies, worktrees, generated distribution or local data.
    $excluded = @('.git', '.venv', 'venv', 'node_modules', 'packages', '.codex', '.agents', '.aws', '__pycache__', 'build', 'test-output', 'exports', 'dist')
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($projectRoot)
    $targets = [Collections.Generic.List[string]]::new()
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($child in Get-ChildItem -LiteralPath $directory -Directory -Force) {
            if ($excluded -contains $child.Name) { continue }
            if ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
            if (Test-Path -LiteralPath (Join-Path $child.FullName '.git')) { continue }
            if ($child.Name -ieq 'bin') {
                if ($directory -eq $projectRoot) { continue }
                $target = Assert-ProjectPath $child.FullName
                Assert-NoReparsePoints $target
                $targets.Add($target)
            } else { $pending.Push($child.FullName) }
        }
    }
    # Validate all candidates before deleting any directory. Root/bin is excluded.
    foreach ($target in $targets) {
        if ((Split-Path $target -Leaf) -ine 'bin' -or $target -ieq (Join-Path $projectRoot 'bin')) { throw "Target di pulizia non valido: $target" }
        if (Test-Path -LiteralPath $target) {
            $resolved = (Resolve-Path -LiteralPath $target).ProviderPath
            $null = Assert-ProjectPath $resolved
            Assert-NoReparsePoints $resolved
            Remove-Item -LiteralPath $resolved -Recurse -Force
            Write-Host "Rimossa bin di modulo: $resolved"
        }
    }
}
