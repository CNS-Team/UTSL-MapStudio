param([Parameter(Mandatory=$true)][string]$Evidence)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
if(Test-Path -LiteralPath $Evidence){throw '拒绝覆盖旧双构建证据。'}
New-Item -ItemType Directory -Path $Evidence | Out-Null
$targets=@(@{Name='NativeWorker';Dir='worker';Assembly='UTSL.WorldGen.Worker'},@{Name='Generator';Dir='app';Assembly='UTSL.ThreeWorlds'},@{Name='Studio';Dir='studio';Assembly='UTSL.MapStudio'})
$rounds=@()
foreach($round in @(1,2)){
    $files=@()
    foreach($target in $targets){
        $log=Join-Path $Evidence "build-$round-$($target.Name).log"
        & dotnet build "$root/src/$($target.Name)/$($target.Name).csproj" -c Release -t:Rebuild --no-restore -o "$root/$($target.Dir)" --nologo 2>&1 | Tee-Object -FilePath $log | Out-String | Write-Output
        if($LASTEXITCODE -ne 0){throw "$($target.Name) failed"}
        if((Get-Content $log -Raw) -match ': warning '){throw '不接受有产品warning的冻结件。'}
        foreach($ext in @('exe','dll','pdb')){
            $p=Join-Path $root "$($target.Dir)/$($target.Assembly).$ext"
            $f=Get-Item -LiteralPath $p
            $files += [pscustomobject]@{Path=$f.FullName;Bytes=$f.Length;SHA256=(Get-FileHash -LiteralPath $p).Hash}
        }
    }
    $rounds += ,$files
}
$checks=for($i=0;$i -lt $rounds[0].Count;$i++){
    [pscustomobject]@{Path=$rounds[0][$i].Path;Bytes=$rounds[1][$i].Bytes;SHA256=$rounds[1][$i].SHA256;Same=($rounds[0][$i].SHA256 -eq $rounds[1][$i].SHA256 -and $rounds[0][$i].Bytes -eq $rounds[1][$i].Bytes)}
}
[pscustomobject]@{At=(Get-Date -Format o);Rounds=$rounds;Checks=$checks;ProductionTouched=$false} | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 (Join-Path $Evidence 'result.json')
if($checks.Same -contains $false){throw '双构建不一致。'}
