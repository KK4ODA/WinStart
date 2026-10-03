<#
.SYNOPSIS
    Regenerates src\WinStart\Assets\WinStart.ico.

.DESCRIPTION
    The application icon is drawn in code so it can be reproduced and tweaked without an image
    editor: a blue rounded square with a white "start" triangle inside a timer ring.
    Run this script only when the design changes; the generated .ico is committed.
#>
[CmdletBinding()]
param(
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'
if (-not $OutFile) { $OutFile = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\WinStart\Assets\WinStart.ico' }
Add-Type -AssemblyName System.Drawing

function New-IconFrame([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [single]$size
    $pad = $s * 0.04
    $box = New-Object System.Drawing.RectangleF $pad, $pad, ($s - 2 * $pad), ($s - 2 * $pad)
    $d = $s * 0.44   # corner diameter

    $shape = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shape.AddArc($box.X, $box.Y, $d, $d, 180, 90)
    $shape.AddArc($box.Right - $d, $box.Y, $d, $d, 270, 90)
    $shape.AddArc($box.Right - $d, $box.Bottom - $d, $d, $d, 0, 90)
    $shape.AddArc($box.X, $box.Bottom - $d, $d, $d, 90, 90)
    $shape.CloseFigure()

    $top = [System.Drawing.Color]::FromArgb(255, 58, 142, 255)
    $bottom = [System.Drawing.Color]::FromArgb(255, 18, 84, 200)
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush $box, $top, $bottom, 90.0
    $g.FillPath($fill, $shape)

    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $cx = $s / 2
    $cy = $s / 2

    if ($size -ge 32) {
        # Timer ring, open at the top left, around a smaller triangle.
        $r = $s * 0.30
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ($s * 0.075)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawArc($pen, ($cx - $r), ($cy - $r), (2 * $r), (2 * $r), -50, 290)
        $pen.Dispose()
        $t = $s * 0.30
    }
    else {
        # Small sizes: just the triangle, so it stays crisp.
        $t = $s * 0.52
    }

    $triangle = @(
        (New-Object System.Drawing.PointF ($cx - $t * 0.36), ($cy - $t * 0.5)),
        (New-Object System.Drawing.PointF ($cx - $t * 0.36), ($cy + $t * 0.5)),
        (New-Object System.Drawing.PointF ($cx + $t * 0.52), $cy)
    )
    $g.FillPolygon($white, $triangle)

    $white.Dispose(); $fill.Dispose(); $shape.Dispose(); $g.Dispose()
    return $bitmap
}

function Get-FrameBytes([System.Drawing.Bitmap]$bitmap) {
    $size = $bitmap.Width
    $stream = New-Object System.IO.MemoryStream
    if ($size -ge 256) {
        # Large frames are stored as PNG.
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return , $stream.ToArray()
    }

    # Smaller frames are classic 32-bit bitmaps: header, bottom-up BGRA rows, then a 1-bit mask.
    $writer = New-Object System.IO.BinaryWriter $stream
    $writer.Write([int]40); $writer.Write([int]$size); $writer.Write([int]($size * 2))
    $writer.Write([int16]1); $writer.Write([int16]32); $writer.Write([int]0)
    $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $c = $bitmap.GetPixel($x, $y)
            $writer.Write([byte]$c.B); $writer.Write([byte]$c.G); $writer.Write([byte]$c.R); $writer.Write([byte]$c.A)
        }
    }
    $maskRow = New-Object byte[] ([int]([math]::Ceiling($size / 32.0)) * 4)
    for ($y = 0; $y -lt $size; $y++) { $writer.Write($maskRow) }
    $writer.Flush()
    return , $stream.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$frames = foreach ($size in $sizes) {
    $bitmap = New-IconFrame $size
    [pscustomobject]@{ Size = $size; Bytes = (Get-FrameBytes $bitmap) }
    $bitmap.Dispose()
}

New-Item -ItemType Directory -Force (Split-Path -Parent $OutFile) | Out-Null
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $dimension = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
    $w.Write([byte]$dimension); $w.Write([byte]$dimension); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]$frame.Bytes.Length); $w.Write([int]$offset)
    $offset += $frame.Bytes.Length
}
foreach ($frame in $frames) { $w.Write([byte[]]$frame.Bytes) }
$w.Flush()
[System.IO.File]::WriteAllBytes($OutFile, $out.ToArray())
Write-Host "Wrote $OutFile ($($out.Length) bytes, sizes: $($sizes -join ', '))"
