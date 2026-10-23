#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Push-Location $root
try {
    & "$root/scripts/Test-Dependencies.ps1" | Select-Object Passed,Failed | Format-Table
    & dotnet build "$root/src/NativeWorker/NativeWorker.csproj" -c Release -o "$root/worker" --nologo
    if ($LASTEXITCODE -ne 0) { throw "NativeWorker build failed: $LASTEXITCODE" }
    & dotnet build "$root/src/Generator/Generator.csproj" -c Release -o "$root/app" --nologo
    if ($LASTEXITCODE -ne 0) { throw "Generator build failed: $LASTEXITCODE" }
    & dotnet build "$root/src/Studio/Studio.csproj" -c Release -o "$root/studio" --nologo -p:RestoreLockedMode=true
    if ($LASTEXITCODE -ne 0) { throw "Studio build failed: $LASTEXITCODE" }
    Write-Host '仅本地构建完成；没有部署/启停或配置写入。' -ForegroundColor Green
}
finally { Pop-Location }
