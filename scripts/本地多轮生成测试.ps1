param([Parameter(Mandatory=$true)][string]$PackageRoot,[Parameter(Mandatory=$true)][string]$Evidence,[switch]$LargeOnly)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $Evidence){throw '不得覆盖既有多轮测试。'}
New-Item -ItemType Directory -Path $Evidence | Out-Null
$package=[IO.Path]::GetFullPath($PackageRoot)
$exe=Join-Path $package 'UTSL地图生成器.exe'
$rounds=@(
    @{Id='01-独立标准三图';Seed='20261023';Layout='normal-stitch';Seeds=@();Traits=@();Foundation='disabled'},
    @{Id='02-巨树浮岛双地牢';Seed='Pigeon-X-20261023-a';Layout='continuous';Seeds=@();Traits=@('extraLivingTrees','extraFloatingIslands','dualDungeons');Foundation='disabled'},
    @{Id='03-空岛基础环境补全';Seed='Pigeon-X-20261023-sky';Layout='continuous';Seeds=@('skyblock');Traits=@();Foundation='skyblock-auto'},
    @{Id='04-醉酒与地下探险';Seed='Pigeon-X-20261023-b';Layout='continuous';Seeds=@('drunk');Traits=@('biggerAbandonedHouses','portalGunInChests');Foundation='disabled'}
)
if($LargeOnly){$rounds=@(@{Id='05-大世界大师';Seed='20261023';Layout='continuous';Seeds=@();Traits=@();Foundation='disabled';Width=8400;Height=2400})}
$products=foreach($relative in @('UTSL地图生成器.exe','程序/studio/UTSL.MapStudio.dll','程序/app/UTSL.ThreeWorlds.dll','程序/worker/UTSL.WorldGen.Worker.dll','程序/engine/OTAPI.dll')){
    $file=Get-Item -LiteralPath (Join-Path $package $relative)
    [ordered]@{Path=$file.FullName;Bytes=$file.Length;SHA256=(Get-FileHash -LiteralPath $file.FullName).Hash}
}
$products | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Evidence 'runtime-products.json') -Encoding utf8
$results=@()
foreach($round in $rounds){
    $id=$round.Id
    $output=Join-Path $package ('地图/本地多轮测试-'+$id+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
    $ui=Join-Path $Evidence $id
    $options=Join-Path $Evidence ($id+'-options.json')
    $width=if($round.Width){$round.Width}else{4200};$height=if($round.Height){$round.Height}else{1200}
    [ordered]@{Name='泰拉大陆';Seed=$round.Seed;Width=$width;Height=$height;Difficulty=2;EdgeInset=40;Style='classic';WorldLayout=$round.Layout;NativeSeeds=$round.Seeds;SeedTraits=$round.Traits;LivePreview=$true;PreviewIntervalMs=1000;Foundations=@{Mode=$round.Foundation;Features=@('dungeon','temple','jungle','crimson','corruption','shimmer')};Output=$output}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $options -Encoding utf8
    $p=Start-Process -FilePath $exe -ArgumentList @('--ui-generate',$options,$ui) -WorkingDirectory $package -WindowStyle Normal -PassThru
    [ordered]@{Round=$id;PID=$p.Id;Executable=$exe;Options=$options;Output=$output;Started=(Get-Date -Format o)}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $Evidence 'current.json') -Encoding utf8
    $p.WaitForExit()
    $result=Join-Path $ui 'result.json';$success=$false;$frames=$null
    if(Test-Path -LiteralPath $result){$actual=Get-Content -LiteralPath $result -Raw|ConvertFrom-Json;$success=$actual.success -and $actual.livePreviewPassed;$frames=$actual.actualPreviewFramesDisplayed}
    $results += [ordered]@{Round=$id;PID=$p.Id;ExitCode=$p.ExitCode;Success=$success;Frames=$frames;Output=$output;Result=$result;Complete=(Test-Path -LiteralPath (Join-Path $output 'COMPLETE.json'));Finished=(Get-Date -Format o)}
    [ordered]@{Results=$results;Status=if($results.Success -contains $false){'FAILURE_REQUIRES_FIX'}else{'RUNNING'};ProductionTouched=$false}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $Evidence 'progress.json') -Encoding utf8
    if(!$success){throw "本地生成失败：$id；保留原失败件，不运行后续轮次。"}
}
[ordered]@{Results=$results;Status='LOCAL_UI_GENERATION_ROUNDS_PASSED';Products=$products;ProductionTouched=$false;GameE2E='UNVERIFIED'}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $Evidence 'result.json') -Encoding utf8
