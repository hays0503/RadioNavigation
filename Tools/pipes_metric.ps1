# pipes_metric.ps1 — numeric metric for the sextant "Pipes" stand (SEXTANT_PLAN §16.22+).
# Compares two screenshot_frame PNGs (eyes vs ocular) numerically — the model
# cannot view images, so all acceptance runs on numbers.
# Geometry: 1920x1080 backbuffer; ocular disc r=250 @ (960,540) (§16.13-16.14).
#
# Usage:
#   survey (pick the door rect):  pipes_metric.ps1 -A a.png -B b.png
#   measure (frozen rect):        pipes_metric.ps1 -A a.png -B b.png -Rect "x,y,w,h"
param(
    [Parameter(Mandatory = $true)][string]$A,
    [Parameter(Mandatory = $true)][string]$B,
    [string]$Rect = "",
    [int]$Step = 2
)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"

function Read-Bitmap([string]$path) {
    $bmp = [System.Drawing.Bitmap]::FromFile($path)
    $w = $bmp.Width
    $h = $bmp.Height
    $r = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $bmp.LockBits($r, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $stride = $data.Stride
    $bmp.UnlockBits($data)
    $bmp.Dispose()
    return @{ Bytes = $bytes; Stride = $stride; W = $w; H = $h }
}

function Summarize($rs, $gs, $bs, $maxR, $maxG, $maxB, $n, $black) {
    if ($n -eq 0) { return "n=0" }
    $meanR = 0; $meanG = 0; $meanB = 0
    for ($i = 0; $i -lt $n; $i++) { $meanR += $rs[$i]; $meanG += $gs[$i]; $meanB += $bs[$i] }
    $meanR = $meanR / $n; $meanG = $meanG / $n; $meanB = $meanB / $n
    $sa = $rs.ToArray(); $sb = $gs.ToArray(); $sc = $bs.ToArray()
    [Array]::Sort($sa); [Array]::Sort($sb); [Array]::Sort($sc)
    $m = [int]($n / 2)
    $bf = $black / $n
    return ("n={0} mean={1:F1},{2:F1},{3:F1} med={4},{5},{6} b_r={7:F1} b_g={8:F1} max={9},{10},{11} blackfrac={12:F3}" -f `
        $n, $meanR, $meanG, $meanB, $sa[$m], $sb[$m], $sc[$m], ($meanB - $meanR), ($meanB - $meanG), $maxR, $maxG, $maxB, $bf)
}

function Measure-Disc($img, [int]$step) {
    $rs = New-Object System.Collections.Generic.List[int]
    $gs = New-Object System.Collections.Generic.List[int]
    $bs = New-Object System.Collections.Generic.List[int]
    $maxR = 0; $maxG = 0; $maxB = 0
    $n = 0; $black = 0
    for ($y = 0; $y -lt $img.H; $y += $step) {
        for ($x = 0; $x -lt $img.W; $x += $step) {
            $dx = $x - 960; $dy = $y - 540
            if (($dx * $dx + $dy * $dy) -gt 62500) { continue }
            $i = $y * $img.Stride + $x * 4
            $rr = $img.Bytes[$i + 2]; $gg = $img.Bytes[$i + 1]; $bb = $img.Bytes[$i]
            $rs.Add($rr); $gs.Add($gg); $bs.Add($bb)
            if ($rr -gt $maxR) { $maxR = $rr }
            if ($gg -gt $maxG) { $maxG = $gg }
            if ($bb -gt $maxB) { $maxB = $bb }
            $n++
            if ($rr -le 10 -and $gg -le 10 -and $bb -le 10) { $black++ }
        }
    }
    return Summarize $rs $gs $bs $maxR $maxG $maxB $n $black
}

function Measure-Rect($img, [int]$rx, [int]$ry, [int]$rw, [int]$rh, [int]$step) {
    $rs = New-Object System.Collections.Generic.List[int]
    $gs = New-Object System.Collections.Generic.List[int]
    $bs = New-Object System.Collections.Generic.List[int]
    $maxR = 0; $maxG = 0; $maxB = 0
    $n = 0; $black = 0
    for ($y = $ry; $y -lt ($ry + $rh) -and $y -lt $img.H; $y += $step) {
        for ($x = $rx; $x -lt ($rx + $rw) -and $x -lt $img.W; $x += $step) {
            $i = $y * $img.Stride + $x * 4
            $rr = $img.Bytes[$i + 2]; $gg = $img.Bytes[$i + 1]; $bb = $img.Bytes[$i]
            $rs.Add($rr); $gs.Add($gg); $bs.Add($bb)
            if ($rr -gt $maxR) { $maxR = $rr }
            if ($gg -gt $maxG) { $maxG = $gg }
            if ($bb -gt $maxB) { $maxB = $bb }
            $n++
            if ($rr -le 10 -and $gg -le 10 -and $bb -le 10) { $black++ }
        }
    }
    return Summarize $rs $gs $bs $maxR $maxG $maxB $n $black
}

function Measure-Bright($img, [int]$step) {
    # bright-surface population inside the disc: min channel > 110 (lit
    # surfaces, not dark bg) and max < 250 (excludes pure-white UI text).
    # Rect-free tint metric — no manual rect picking needed.
    $rs = New-Object System.Collections.Generic.List[int]
    $gs = New-Object System.Collections.Generic.List[int]
    $bs = New-Object System.Collections.Generic.List[int]
    $maxR = 0; $maxG = 0; $maxB = 0
    $n = 0; $black = 0
    for ($y = 0; $y -lt $img.H; $y += $step) {
        for ($x = 0; $x -lt $img.W; $x += $step) {
            $dx = $x - 960; $dy = $y - 540
            if (($dx * $dx + $dy * $dy) -gt 62500) { continue }
            $i = $y * $img.Stride + $x * 4
            $rr = $img.Bytes[$i + 2]; $gg = $img.Bytes[$i + 1]; $bb = $img.Bytes[$i]
            $mn = $rr; if ($gg -lt $mn) { $mn = $gg }; if ($bb -lt $mn) { $mn = $bb }
            $mx = $rr; if ($gg -gt $mx) { $mx = $gg }; if ($bb -gt $mx) { $mx = $bb }
            if ($mn -le 110 -or $mx -ge 250) { continue }
            $rs.Add($rr); $gs.Add($gg); $bs.Add($bb)
            if ($rr -gt $maxR) { $maxR = $rr }
            if ($gg -gt $maxG) { $maxG = $gg }
            if ($bb -gt $maxB) { $maxB = $bb }
            $n++
        }
    }
    return Summarize $rs $gs $bs $maxR $maxG $maxB $n $black
}

function Survey-Grid($img) {
    # coarse block means over the central 800x600 (block 100px, sample step 8)
    $lines = @()
    for ($by = 240; $by -lt 840; $by += 100) {
        $row = @()
        for ($bx = 560; $bx -lt 1360; $bx += 100) {
            $sr = 0; $sg = 0; $sb = 0; $c = 0
            for ($y = $by; $y -lt ($by + 100); $y += 8) {
                for ($x = $bx; $x -lt ($bx + 100); $x += 8) {
                    $i = $y * $img.Stride + $x * 4
                    $sr += $img.Bytes[$i + 2]; $sg += $img.Bytes[$i + 1]; $sb += $img.Bytes[$i]
                    $c++
                }
            }
            $row += ("[{0},{1}]={2},{3},{4}" -f $bx, $by, [int]($sr / $c), [int]($sg / $c), [int]($sb / $c))
        }
        $lines += ($row -join " ")
    }
    return $lines
}

foreach ($p in @($A, $B)) {
    $img = Read-Bitmap $p
    Write-Output ("IMG {0} {1}x{2}" -f $p, $img.W, $img.H)
    Write-Output ("DISC " + (Measure-Disc $img $Step))
    Write-Output ("BRIGHT " + (Measure-Bright $img $Step))
    if ($Rect -ne "") {
        $t = $Rect.Split(",")
        Write-Output (("RECT {0}: " -f $Rect) + (Measure-Rect $img ([int]$t[0]) ([int]$t[1]) ([int]$t[2]) ([int]$t[3]) $Step))
    }
    else {
        Write-Output "GRID (block x,y: mean R,G,B):"
        foreach ($l in (Survey-Grid $img)) { Write-Output ("  " + $l) }
    }
}
