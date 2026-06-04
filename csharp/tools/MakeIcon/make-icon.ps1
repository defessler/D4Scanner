# Generates the D4Scanner app icon (Assets/app.ico) at multiple sizes.
# One-off generator — run from the repo root:  pwsh csharp/tools/MakeIcon/make-icon.ps1
# Design: a Diablo-IV diamond medallion — crimson outer diamond, antique-gold inner
# diamond, on the app's near-black stone background, with a small gem highlight.
Add-Type -AssemblyName System.Drawing

function Make-Bitmap([int]$sz) {
    $bmp = New-Object System.Drawing.Bitmap $sz, $sz, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    # near-black D4 stone background
    $g.Clear([System.Drawing.Color]::FromArgb(255, 0x16, 0x15, 0x1A))

    $m  = $sz * 0.08
    $cx = $sz / 2.0
    $cy = $sz / 2.0
    $r  = $sz / 2.0 - $m

    # outer diamond — crimson
    $pts = @(
        (New-Object System.Drawing.PointF($cx,      ($cy - $r))),
        (New-Object System.Drawing.PointF(($cx + $r), $cy)),
        (New-Object System.Drawing.PointF($cx,      ($cy + $r))),
        (New-Object System.Drawing.PointF(($cx - $r), $cy))
    )
    $crimson = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xB2, 0x2F, 0x2F))
    $g.FillPolygon($crimson, $pts)

    # thin dark bevel ring between the two diamonds
    $rb = $r * 0.74
    $bpts = @(
        (New-Object System.Drawing.PointF($cx,       ($cy - $rb))),
        (New-Object System.Drawing.PointF(($cx + $rb), $cy)),
        (New-Object System.Drawing.PointF($cx,       ($cy + $rb))),
        (New-Object System.Drawing.PointF(($cx - $rb), $cy))
    )
    $bevel = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x16, 0x15, 0x1A))
    $g.FillPolygon($bevel, $bpts)

    # inner diamond — antique gold
    $ri = $r * 0.56
    $ipts = @(
        (New-Object System.Drawing.PointF($cx,       ($cy - $ri))),
        (New-Object System.Drawing.PointF(($cx + $ri), $cy)),
        (New-Object System.Drawing.PointF($cx,       ($cy + $ri))),
        (New-Object System.Drawing.PointF(($cx - $ri), $cy))
    )
    $amber = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xD4, 0xA7, 0x30))
    $g.FillPolygon($amber, $ipts)

    # gem highlight (upper-left facet) for larger sizes
    if ($sz -ge 32) {
        $hs = [int]($sz * 0.10)
        $hx = [int]($cx - $ri * 0.45)
        $hy = [int]($cy - $ri * 0.55)
        $hi = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(220, 0xF0, 0xC0, 0x4A))
        $g.FillEllipse($hi, $hx, $hy, $hs, $hs)
    }

    $g.Dispose()
    return $bmp
}

$sizes   = @(16, 32, 48, 256)
$pngData = @()
foreach ($sz in $sizes) {
    $bmp = Make-Bitmap $sz
    $ms  = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngData += , $ms.ToArray()
    $bmp.Dispose(); $ms.Dispose()
}

$n          = $pngData.Count
$dataOffset = 6 + 16 * $n

$out = New-Object System.IO.MemoryStream
$w   = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$n)   # ICONDIR

$off = $dataOffset
for ($i = 0; $i -lt $n; $i++) {
    $sz = $sizes[$i]
    $d  = $pngData[$i]
    $field = if ($sz -ge 256) { [byte]0 } else { [byte]$sz }
    $w.Write($field)                 # width  (0 => 256)
    $w.Write($field)                 # height (0 => 256)
    $w.Write([byte]0)                # colorCount
    $w.Write([byte]0)                # reserved
    $w.Write([uint16]1)              # planes
    $w.Write([uint16]32)             # bitCount
    $w.Write([uint32]$d.Length)      # bytesInRes
    $w.Write([uint32]$off)           # imageOffset
    $off += $d.Length
}
foreach ($d in $pngData) { $w.Write($d, 0, $d.Length) }
$w.Flush()

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$icoPath   = Join-Path $scriptDir '..\..\D4Scanner.App\Assets\app.ico'
$icoPath   = [System.IO.Path]::GetFullPath($icoPath)
[System.IO.File]::WriteAllBytes($icoPath, $out.ToArray())
Write-Host ("Generated {0} ({1} bytes, {2} sizes)" -f $icoPath, $out.Length, $n)
$w.Dispose(); $out.Dispose()
