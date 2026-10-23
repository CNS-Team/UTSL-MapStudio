#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $root 'dependency-manifest.json') -Raw | ConvertFrom-Json
$rows = foreach ($item in @($manifest.engine) + @($manifest.tedit)) {
    if ($item.path -notmatch '^(engine|lib/TEdit)/[A-Za-z0-9._-]+\.dll$') { throw "Unsafe dependency path: $($item.path)" }
    $path = Join-Path $root $item.path
    $exists = Test-Path -LiteralPath $path -PathType Leaf
    $pass = $exists -and (Get-Item -LiteralPath $path).Length -eq $item.bytes -and
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $item.sha256
    [pscustomobject]@{ Path=$item.path; Exists=$exists; ExpectedBytes=$item.bytes; ExpectedSHA256=$item.sha256; Pass=$pass }
}
$failed = @($rows | Where-Object { !$_.Pass }).Count
[pscustomobject]@{ Passed=$rows.Count-$failed; Failed=$failed; Items=@($rows) }
if ($failed) { throw "$failed locked dependencies missing or mismatched. See docs/DEPENDENCIES.md." }
