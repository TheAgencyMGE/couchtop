<#
  Generates the original Couchtop icon (assets/Couchtop.ico and assets/Couchtop.png).
  A glossy blue rounded tile holding a 2x2 grid of white "channel" tiles. No external artwork is used.
#>
param([string]$OutDir = (Join-Path $PSScriptRoot '..\assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    $outer = New-RoundedPath (6*$s) (6*$s) (244*$s) (244*$s) (56*$s)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $size), ([System.Drawing.Color]::FromArgb(255, 110, 212, 250)), ([System.Drawing.Color]::FromArgb(255, 24, 150, 210))
    $g.FillPath($grad, $outer)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 255, 255, 255)), ([float][Math]::Max(1, 6*$s))
    $g.DrawPath($pen, $outer)

    $tile = 84 * $s; $gap = 16 * $s; $start = (256*$s - (2*$tile + $gap)) / 2
    for ($row = 0; $row -lt 2; $row++) {
        for ($col = 0; $col -lt 2; $col++) {
            $tx = $start + $col * ($tile + $gap); $ty = $start + $row * ($tile + $gap) + 6*$s
            $tp = New-RoundedPath $tx $ty $tile ($tile * 0.8) (18*$s)
            $tb = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(245, 255, 255, 255))
            $g.FillPath($tb, $tp)
        }
    }
    $dot = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 53, 180, 229))
    $cx = $start + $tile / 2; $cy = $start + 6*$s + $tile * 0.4
    $g.FillEllipse($dot, $cx - 18*$s, $cy - 18*$s, 36*$s, 36*$s)

    $gloss = New-RoundedPath (20*$s) (14*$s) (216*$s) (96*$s) (44*$s)
    $gb = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, (14*$s)), (New-Object System.Drawing.PointF 0, (110*$s)), ([System.Drawing.Color]::FromArgb(90, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255))
    $g.FillPath($gb, $gloss)
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,($ms.ToArray())
    if ($size -eq 256) { $bmp.Save((Join-Path $OutDir 'Couchtop.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $w.Write($png) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'Couchtop.ico'), $ico.ToArray())
Write-Host "Icon written to $OutDir"
