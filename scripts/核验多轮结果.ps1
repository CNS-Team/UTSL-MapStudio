param([Parameter(Mandatory=$true)][string]$Evidence,[Parameter(Mandatory=$true)][string]$SmallRounds,[Parameter(Mandatory=$true)][string]$LargeRounds)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$checks=[System.Collections.Generic.List[object]]::new()
function Check([string]$Name,[bool]$Pass,[object]$Fact){$checks.Add([ordered]@{Name=$Name;Pass=$Pass;Fact=$Fact})}
if(Test-Path -LiteralPath (Join-Path $Evidence 'generation-independent-checks.json')){throw '禁止覆盖既有验收。'}
New-Item -ItemType Directory -Force -Path $Evidence | Out-Null
$rounds=Get-Content (Join-Path $SmallRounds 'result.json') -Raw|ConvertFrom-Json
$large=Get-Content (Join-Path $LargeRounds 'result.json') -Raw|ConvertFrom-Json
$rows=@($rounds.Results)+@($large.Results)
foreach($set in @($rounds,$large)){foreach($product in $set.Products){
    $actual=Get-Item -LiteralPath $product.Path
    Check "tested final runtime $($product.Path)" ($actual.Length -eq $product.Bytes -and (Get-FileHash -LiteralPath $actual.FullName).Hash -eq $product.SHA256) $product
}}
foreach($row in $rows){
    $label=$row.Round;$out=$row.Output
    Check "$label UI completion + real preview" ($row.Success -and $row.Frames -gt 0) @{Frames=$row.Frames;Result=$row.Result}
    Check "$label COMPLETE marker" (Test-Path -LiteralPath (Join-Path $out 'COMPLETE.json')) $out
    $record=Get-Content (Join-Path $out 'generation-result.json') -Raw|ConvertFrom-Json
    Check "$label three saved worlds" ($record.Worlds.Count -eq 3) @($record.Worlds.File.Path)
    Check "$label exactly two seams" ($record.Seams.Count -eq 2) $record.Seams.Count
    foreach($world in $record.Worlds){
        $actual=Get-Item -LiteralPath $world.File.Path
        Check "$label $($world.Spec.Role) bytes+SHA" ($actual.Length -eq $world.File.Bytes -and (Get-FileHash -LiteralPath $actual.FullName).Hash -eq $world.File.Sha256) $world.File
        $native=Get-Content (Join-Path $out "native-load-$($world.Spec.Role)/native-result.json") -Raw|ConvertFrom-Json
        Check "$label $($world.Spec.Role) real native load dimensions/difficulty" ($native.width -eq $record.Options.Width -and $native.height -eq $record.Options.Height -and $native.difficulty -eq 2) $native
        Check "$label $($world.Spec.Role) no network/plugins" (!$native.networkStarted -and !$native.pluginsLoaded) $native.pid
        Check "$label $($world.Spec.Role) exact central spawn" ($native.spawnX -eq $record.Options.Width/2 -and $native.spawnY -eq $world.SpawnY) @{X=$native.spawnX;Y=$native.spawnY}
        if($record.Options.NativeSeeds -contains 'skyblock'){Check "$label $($world.Spec.Role) skyblock flag saved+loaded" ([bool]$native.skyblockWorld) $native.skyblockWorld}
        if($record.Options.NativeSeeds -contains 'drunk'){Check "$label $($world.Spec.Role) drunk flag saved+loaded" ([bool]$native.drunkWorld) $native.drunkWorld}
    }
    foreach($seam in $record.Seams){Check "$label full-depth exact seam $($seam.Left)-$($seam.Right)" ($seam.Mismatches -eq 0 -and $seam.LeftDigest -eq $seam.RightDigest -and $seam.ComparedTiles -eq $record.Options.Overlap*$record.Options.Height) $seam}
    foreach($artifact in (Get-Content (Join-Path $out 'artifact-manifest.json') -Raw|ConvertFrom-Json)){
        $actual=Get-Item -LiteralPath (Join-Path $out $artifact.path)
        Check "$label archive $($artifact.path)" ($actual.Length -eq $artifact.file.Bytes -and (Get-FileHash -LiteralPath $actual.FullName).Hash -eq $artifact.file.Sha256) $artifact.file
    }
    $panorama=Join-Path $out '三联世界长图.png';$im=[Drawing.Image]::FromFile($panorama)
    try{Check "$label actual panorama size" ($im.Width -eq ($record.Options.Width*3-160)/2 -and $im.Height -eq $record.Options.Height/2) @{Width=$im.Width;Height=$im.Height;Bytes=(Get-Item $panorama).Length;SHA256=(Get-FileHash $panorama).Hash}}finally{$im.Dispose()}
    if($record.Options.WorldLayout -eq 'normal-stitch'){
        $normal=Get-Content (Join-Path $out 'normal-stitch-result.json') -Raw|ConvertFrom-Json
        Check "$label common physical seaY" (@($normal.audits.SeaY|Select-Object -Unique).Count -eq 1) @($normal.audits.SeaY)
        Check "$label exact inland before/after" (@($normal.audits|Where-Object {$_.InlandBefore -ne $_.InlandAfter}).Count -eq 0) @($normal.audits.InlandBefore)
    }else{
        $seed=Get-Content (Join-Path $out 'native-source/seed-options.json') -Raw|ConvertFrom-Json
        foreach($id in $record.Options.NativeSeeds){Check "$label requested native flag $id" (@($seed.flags|Where-Object {$_.ServerConfigName -eq $id -and $_.Enabled}).Count -eq 1) $id}
        foreach($id in $record.Options.SeedTraits){Check "$label requested trait $id" (@($seed.traits|Where-Object {$_.id -eq $id -and $_.Enabled}).Count -eq 1) $id}
    }
    if($record.Options.Foundations.Mode -eq 'skyblock-auto'){
        $foundations=Get-Content (Join-Path $out 'foundation-result.json') -Raw|ConvertFrom-Json
        Check "$label foundation evidence exists" ($null -ne $foundations) (Join-Path $out 'foundation-result.json')
        foreach($id in $record.Options.Foundations.Features){Check "$label foundation $id actually present" (@($foundations.after|Where-Object {$_.Id -eq $id -and $_.Present}).Count -eq 1) $id}
    }
    Check "$label game E2E remains UNVERIFIED" ($record.PlayerE2E -eq 'UNVERIFIED') $record.Status
}
$failed=@($checks|Where-Object {!$_.Pass}).Count
[ordered]@{At=(Get-Date -Format o);Passed=$checks.Count-$failed;Failed=$failed;Checks=$checks;Rounds=$rows;ProductionTouched=$false;GameE2E='UNVERIFIED'}|ConvertTo-Json -Depth 18|Set-Content -LiteralPath (Join-Path $Evidence 'generation-independent-checks.json') -Encoding utf8
if($failed -gt 0){throw "$failed generated-world checks failed"}
