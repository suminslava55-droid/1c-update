# Генерирует app.ico для 1C Update
param(
    [string]$Path = (Join-Path $PSScriptRoot 'app.ico'),
    [string]$Glyph = '1C'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [float]($size * 0.04)
    $rect = [System.Drawing.RectangleF]::new($pad, $pad, $size - 2 * $pad, $size - 2 * $pad)
    $radius = [float]($size * 0.22)
    $path = New-RoundedRect $rect.X $rect.Y $rect.Width $rect.Height $radius

    $c1 = [System.Drawing.Color]::FromArgb(255, 128, 44, 10)
    $c2 = [System.Drawing.Color]::FromArgb(255, 232, 132, 40)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 45.0)
    $g.FillPath($brush, $path)

    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(80, 255, 255, 255)), ([float][Math]::Max(1.0, $size * 0.012))
    $g.DrawPath($pen, $path)

    $fontSize = [float]($size * 0.44)
    $font = New-Object System.Drawing.Font 'Segoe UI', $fontSize, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
    $textBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $textRect = New-Object System.Drawing.RectangleF 0, ([float]($size * 0.02)), $size, $size
    $g.DrawString($Glyph, $font, $textBrush, $textRect, $fmt)

    $g.Dispose()
    $fs = New-Object System.IO.MemoryStream
    $bmp.Save($fs, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $fs.ToArray()
    $fs.Dispose()
    $bmp.Dispose()
    return , $bytes
}

$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($s in $sizes) { $pngs += , (New-IconPng $s) }

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $bw.Write($png) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($Path, $ms.ToArray())
$bw.Dispose()
$ms.Dispose()

Write-Host ("Иконка создана: {0} ({1} байт)" -f $Path, (Get-Item $Path).Length)
