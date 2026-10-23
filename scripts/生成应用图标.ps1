param(
    [string]$InputImage,
    [ValidateRange(0,0.1)][double]$CropInsetRatio = 0.02,
    [ValidateRange(0.1,0.4)][double]$CornerRadiusRatio = 0.22
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$assets = Join-Path $root 'assets/branding'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
if ($InputImage) { Copy-Item -LiteralPath $InputImage -Destination (Join-Path $assets 'icon-source.png') }
Add-Type -AssemblyName System.Drawing
$image = [System.Drawing.Image]::FromFile((Join-Path $assets 'icon-source.png'))
try {
    if ($image.Width -ne $image.Height) { throw '图标原图必须为正方形；不得自动裁掉用户图像。' }
    # The supplied artwork has a blue/purple square outside its dark badge.
    # Crop ONLY that outer rim, then use a supersampled transparent rounded mask.
    # Do not recolor, redraw, or remove any of the three central world islands.
    $renderSize = 2048
    $render = [System.Drawing.Bitmap]::new($renderSize, $renderSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $canvas = [System.Drawing.Graphics]::FromImage($render)
    $shape = [System.Drawing.Drawing2D.GraphicsPath]::new()
    try {
        $canvas.Clear([System.Drawing.Color]::Transparent)
        $canvas.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $canvas.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $canvas.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $diameter = [single]($renderSize * $CornerRadiusRatio * 2)
        $far = [single]($renderSize - $diameter)
        $shape.AddArc(0,0,$diameter,$diameter,180,90)
        $shape.AddArc($far,0,$diameter,$diameter,270,90)
        $shape.AddArc($far,$far,$diameter,$diameter,0,90)
        $shape.AddArc(0,$far,$diameter,$diameter,90,90)
        $shape.CloseFigure()
        $canvas.SetClip($shape)
        $inset = [single]([Math]::Ceiling($image.Width * $CropInsetRatio))
        $cropSize = [single]($image.Width - 2 * $inset)
        $canvas.DrawImage($image, [System.Drawing.Rectangle]::new(0,0,$renderSize,$renderSize), $inset,$inset,$cropSize,$cropSize,[System.Drawing.GraphicsUnit]::Pixel)
    } finally { $shape.Dispose(); $canvas.Dispose() }
    $frames = @()
    try { foreach ($size in @(16,20,24,32,40,48,64,96,128,256,512)) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $stream = [System.IO.MemoryStream]::new()
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($render, [System.Drawing.Rectangle]::new(0,0,$size,$size))
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $bytes = $stream.ToArray()
            if ($size -eq 512) { [IO.File]::WriteAllBytes((Join-Path $assets 'MapStudio.png'), $bytes) }
            else { $frames += [pscustomobject]@{ Size=$size; Bytes=$bytes } }
        } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    } } finally { $render.Dispose() }
    $file = [IO.File]::Create((Join-Path $assets 'MapStudio.ico'))
    $writer = [IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    } finally { $writer.Dispose() }
} finally { $image.Dispose() }
Get-Item "$assets/icon-source.png","$assets/MapStudio.png","$assets/MapStudio.ico" | ForEach-Object {
    [pscustomobject]@{ Path=[IO.Path]::GetRelativePath($root, $_.FullName).Replace('\','/'); Bytes=$_.Length; SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
} | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $assets 'source-manifest.json')
