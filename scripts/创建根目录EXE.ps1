param([Parameter(Mandatory=$true)][string]$PackageRoot)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PackageRoot)
$assembly = Join-Path $root '程序/studio/UTSL.MapStudio.dll'
if (!(Test-Path -LiteralPath $assembly)) { throw '先复制正式Studio和依赖，再创建根目录EXE。' }
$sdk = (& dotnet --list-sdks | Select-String '^10\.' | Select-Object -Last 1).ToString().Split(' ')[0]
$dotnet = Split-Path (Get-Command dotnet).Source
Add-Type -Path (Join-Path $dotnet "sdk/$sdk/Microsoft.NET.HostModel.dll")
$hostTemplate = Get-ChildItem (Join-Path $dotnet 'packs/Microsoft.NETCore.App.Host.win-x64') -Directory | Where-Object { $_.Name -like '10.*' } | Sort-Object { [version]$_.Name } | Select-Object -Last 1
$source = Join-Path $hostTemplate.FullName 'runtimes/win-x64/native/apphost.exe'
$target = Join-Path $root 'UTSL地图生成器.exe'
# A real GUI apphost, not a script/shortcut or an extra launcher process. The
# relative DLL path allows all managed/native dependencies to remain under 程序.
[Microsoft.NET.HostModel.AppHost.HostWriter]::CreateAppHost($source, $target, '程序/studio/UTSL.MapStudio.dll', $true, $assembly, $false, $false, $null)
Get-Item -LiteralPath $target | ForEach-Object { [pscustomobject]@{ Path=$_.FullName; Bytes=$_.Length; SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash; Description=$_.VersionInfo.FileDescription; Company=$_.VersionInfo.CompanyName; Copyright=$_.VersionInfo.LegalCopyright } }
