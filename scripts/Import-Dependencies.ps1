#requires -Version 7.0
param(
    [Parameter(Mandatory=$true)][string]$EngineSource,
    [Parameter(Mandatory=$true)][string]$TEditSource
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$engine = (Get-Item -LiteralPath $EngineSource).FullName
$tedit = (Get-Item -LiteralPath $TEditSource).FullName
$manifest = Get-Content -LiteralPath (Join-Path $root 'dependency-manifest.json') -Raw | ConvertFrom-Json
function Assert-NoLink([string]$Path, [string]$Boundary) {
    $full = [IO.Path]::GetFullPath($Path)
    $base = [IO.Path]::GetFullPath($Boundary).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ($full -ne $base -and !$full.StartsWith($base+[IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path outside dependency boundary: $full"
    }
    while ($full) {
        if ((Test-Path -LiteralPath $full) -and ((Get-Item -LiteralPath $full -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Dependency symlink/reparse point rejected: $full"
        }
        if ($full -eq $base) { break }
        $full = Split-Path -Parent $full
    }
}
function Assert-Exact([string]$Path, $Item) {
    $file = Get-Item -LiteralPath $Path
    if ($file.PSIsContainer -or $file.Length -ne $Item.bytes -or (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Item.sha256) {
        throw "Dependency bytes/SHA mismatch; no overwrite allowed: $Path"
    }
}
# Validate every source and pre-existing destination BEFORE copying any file.
$plan = foreach ($item in @($manifest.engine) + @($manifest.tedit)) {
    if ($item.path -notmatch '^(engine|lib/TEdit)/[A-Za-z0-9._-]+\.dll$') { throw "Unsafe dependency path: $($item.path)" }
    $sourceRoot = if ($item.path.StartsWith('engine/')) { $engine } else { $tedit }
    $source = Join-Path $sourceRoot ([IO.Path]::GetFileName($item.path))
    $destination = Join-Path $root $item.path
    Assert-NoLink $source $sourceRoot
    Assert-NoLink $destination $root
    Assert-Exact $source $item
    if (Test-Path -LiteralPath $destination) { Assert-Exact $destination $item }
    [pscustomobject]@{ Source=$source; Destination=$destination }
}
$copied = 0
foreach ($entry in $plan) {
    if (Test-Path -LiteralPath $entry.Destination) { continue }
    New-Item -ItemType Directory -Path (Split-Path -Parent $entry.Destination) -Force | Out-Null
    [IO.File]::Copy($entry.Source, $entry.Destination, $false)
    $copied++
}
$result = & (Join-Path $PSScriptRoot 'Test-Dependencies.ps1')
[pscustomobject]@{ Copied=$copied; Passed=$result.Passed; Failed=$result.Failed; NetworkActions=0; ProductionTouched=$false }
