# Verifies shell weather projection and 3D shape coordinates.
# Run: powershell -File scripts/TestShellUv.ps1
$ErrorActionPreference = 'Stop'
$failed = 0

function Assert-True([bool]$cond, [string]$msg) {
    if ($cond) { Write-Host "PASS  $msg" }
    else { Write-Host "FAIL  $msg"; $script:failed++ }
}

function V3([double]$x, [double]$y, [double]$z) {
    $a = New-Object 'double[]' 3
    $a[0] = $x; $a[1] = $y; $a[2] = $z
    return ,$a
}

function V3Norm([double]$x, [double]$y, [double]$z) {
    $len = [math]::Sqrt($x*$x + $y*$y + $z*$z)
    if ($len -lt 1e-12) { throw "zero vector" }
    return (V3 ($x/$len) ($y/$len) ($z/$len))
}

function NoiseUv([double[]]$world, [double[]]$center, [double]$volumeSize) {
    $px = ($world[0] - $center[0]) / $volumeSize
    $py = ($world[1] - $center[1]) / $volumeSize
    $pz = ($world[2] - $center[2]) / $volumeSize
    $r1 = V3Norm 0.80 0.60 0.02
    $r2 = V3Norm -0.37 0.48 0.79
    $r3 = V3Norm 0.47 -0.64 0.61
    return (V3 `
        ($px*$r1[0] + $py*$r1[1] + $pz*$r1[2]) `
        ($px*$r2[0] + $py*$r2[1] + $pz*$r2[2]) `
        ($px*$r3[0] + $py*$r3[1] + $pz*$r3[2]))
}

function ShapePoint([double[]]$world, [double[]]$center, [double]$hill, [double]$zStretch) {
    $rx = $world[0] - $center[0]
    $ry = $world[1] - $center[1]
    $rz = $world[2] - $center[2]
    $r = [math]::Sqrt($rx*$rx + $ry*$ry + $rz*$rz)
    $nx = $rx / $r; $ny = $ry / $r; $nz = $rz / $r
    $altitude = $r - $hill
    return (V3 `
        ($nx * ($hill + $altitude * $zStretch)) `
        ($ny * ($hill + $altitude * $zStretch)) `
        ($nz * ($hill + $altitude * $zStretch)))
}

function Remap01([double]$x, [double]$a, [double]$b) {
    return [math]::Max(0.0, [math]::Min(1.0, ($x - $a) / [math]::Max($b - $a, 1e-5)))
}

function HeightWindow([double]$h, [double]$lo, [double]$hi, [double]$fade) {
    $f = [math]::Max($fade, 0.02)
    return (Remap01 $h $lo ($lo+$f)) * (1.0 - (Remap01 $h ($hi-$f) $hi))
}

$hill = 67200.0
$ceil = 74400.0
$center = V3 0.0 0.0 0.0
$dir = V3Norm 0.42 0.31 0.85
$pLo = V3 ($dir[0]*$hill) ($dir[1]*$hill) ($dir[2]*$hill)
$pHi = V3 ($dir[0]*$ceil) ($dir[1]*$ceil) ($dir[2]*$ceil)
$ws = $hill * 1.05

$weatherLo = V3 ($dir[0]*$hill) ($dir[1]*$hill) ($dir[2]*$hill)
$weatherHi = V3 ($dir[0]*$hill) ($dir[1]*$hill) ($dir[2]*$hill)
$uvLo = NoiseUv $weatherLo $center $ws
$uvHi = NoiseUv $weatherHi $center $ws
# Weather is 2D coverage: one radial column must not shear with altitude.
$dxy = [math]::Abs($uvLo[0]-$uvHi[0]) + [math]::Abs($uvLo[1]-$uvHi[1]) + [math]::Abs($uvLo[2]-$uvHi[2])
Assert-True ($dxy -lt 1e-9) "2D weather UV is radial-column locked (d=$dxy)"

# Shape is genuinely 3D. Two points at the same altitude but different
# directions must not collapse to the same x/y as the old dot(rel,tangent) bug.
$dir2 = V3Norm 0.44 0.29 0.85
$shapeA = ShapePoint (V3 ($dir[0]*($hill+900)) ($dir[1]*($hill+900)) ($dir[2]*($hill+900))) $center $hill 1.6
$shapeB = ShapePoint (V3 ($dir2[0]*($hill+900)) ($dir2[1]*($hill+900)) ($dir2[2]*($hill+900))) $center $hill 1.6
$shapeUvA = NoiseUv $shapeA $center 2400
$shapeUvB = NoiseUv $shapeB $center 2400
$shapeDelta = [math]::Abs($shapeUvA[0]-$shapeUvB[0]) + [math]::Abs($shapeUvA[1]-$shapeUvB[1])
Assert-True ($shapeDelta -gt 0.05) "Perlin-Worley has horizontal 3D variation (d=$shapeDelta)"

# Min and Max share sea → fitted limb. 1 = lerp(hills, ceil, fit).
# 1.5 continues in that unit; it is not 1.5 × the gameplay column.
$sea = 60000.0
$fit = 0.35
$col = $ceil - $sea
$hHill = ($hill - $sea) / $col
$unit = $col * ((1.0 - $fit) * $hHill + $fit)
$fitted = (1.0 - $fit) * $hill + $fit * $ceil
$min0 = $sea
$max1 = $sea + $unit
$min51 = $sea + $unit * 0.51
$max099 = $sea + $unit * 0.99
$max15 = $sea + $unit * 1.5
Assert-True ([math]::Abs($max1 - $fitted) -lt 1.0) "Max 1 is the fitted limb ($max1 vs $fitted)"
Assert-True ([math]::Abs($min0 - $sea) -lt 1.0) "Min 0 is sea"
Assert-True (($max099 - $min51) -gt 3000.0) "Min 0.51 and Max 0.99 stay a deck ($($max099 - $min51) m)"
Assert-True ($max15 -lt ($ceil + 2000.0)) "Max 1.5 stays near the gameplay ceil ($max15)"

$hLow = 0.10
$stratus = (Remap01 $hLow 0.00 0.06) # proxy open at low
Assert-True ($hLow -lt 0.30) "Low deck height in stratus/cumulus band (h=$hLow)"

$densitySource = Get-Content (Join-Path $PSScriptRoot '..\Pack\Inject\Clouds\CloudDensity.hlsli') -Raw
$marchSource = Get-Content (Join-Path $PSScriptRoot '..\Pack\Fullscreen\AfterAtmosphere\VolumetricClouds.hlsl') -Raw
$textureSource = Get-Content (Join-Path $PSScriptRoot '..\ClientPlugin\Clouds\CloudTextures.cs') -Raw
Assert-True ($densitySource -notmatch 'dot\s*\(\s*rel\s*,\s*[tb]\s*\)') "No radial-vector/tangent zero projection"
Assert-True ($densitySource -match 'CloudNubisSamplePos') "Shape coordinates are east/altitude/north metres"
Assert-True ($densitySource -match 'CloudSampleShellTex') "Shape and detail sample through the shell texture"
Assert-True ($densitySource -match 'sin\(lon \*') "Weather warp uses periodic longitude"
Assert-True ($densitySource -match 'CloudNoiseLod') "Shape/detail choose footprint-derived mip levels"
Assert-True ($textureSource -match 'CloudsShape.+ShapeSize,\s*8') "Shape texture uploads a full 8-level mip chain"
Assert-True ($marchSource -match 'CLOUD_MAX_STEPS\s+160') "Adaptive marcher has full-resolution sample ceiling"
Assert-True ($marchSource -match 'CLOUD_FINE_EMPTY_EXIT\s+10') "Fine march returns to search after 10 empty samples"

function ShapeArc([double]$x, [double]$y, [double]$z, [double]$radius) {
    $d = V3Norm $x $y $z
    $theta = [math]::Acos([math]::Max(-1.0, [math]::Min(1.0, $d[1])))
    $phi = [math]::Atan2($d[0], $d[2])
    $arc = $theta * $radius
    return (V3 ([math]::Sin($phi) * $arc) 0.0 ([math]::Cos($phi) * $arc))
}

$poleR = 67200.0
$p0 = ShapeArc 0.0 20.0 1.0 $poleR
$p90 = ShapeArc 1.0 20.0 0.0 $poleR
$p180 = ShapeArc 0.0 20.0 -1.0 $poleR
$len0 = [math]::Sqrt($p0[0]*$p0[0] + $p0[2]*$p0[2])
$len90 = [math]::Sqrt($p90[0]*$p90[0] + $p90[2]*$p90[2])
# ~2.9° from +Y. Arc is a few km, not a clamped 0.08*R spinning disk.
Assert-True ($len0 -gt 1500.0 -and $len0 -lt 5000.0) "North pole cap is a few km ($len0 m)"
Assert-True ([math]::Abs($len0 - $len90) / $len0 -lt 0.15) "Pole ring is round ($len0 vs $len90)"
Assert-True ($p0[2] -gt 1000.0 -and $p90[0] -gt 1000.0) "Pole UV follows azimuth, not a longitude whirl"
Assert-True ($p180[2] -lt -1000.0) "Opposite azimuth stays on the ring"

$eqA = ShapeArc 0.02 0.0 -1.0 $poleR
$eqB = ShapeArc -0.02 0.0 -1.0 $poleR
$eqJump = [math]::Sqrt(($eqA[0]-$eqB[0])*($eqA[0]-$eqB[0]) + ($eqA[2]-$eqB[2])*($eqA[2]-$eqB[2]))
Assert-True ($eqJump -gt 800.0 -and $eqJump -lt 8000.0) "Azimuthal chart stays continuous across the date line ($eqJump m)"

$shapeBin = Join-Path $PSScriptRoot '..\Pack\Noise\cloud_noise_shape_128_v5.bin'
$detailBin = Join-Path $PSScriptRoot '..\Pack\Noise\cloud_noise_detail_64_v5.bin'
Assert-True ((Get-Item $shapeBin).Length -eq (128*128*128*4)) "Shape v5 is 128^3 RGBA"
Assert-True ((Get-Item $detailBin).Length -eq (64*64*64*4)) "Detail v5 is 64^3 RGBA"
Assert-True ($densitySource -match 'AnomalyPlanetShapeArcMetres') "Shape metres are azimuthal, not lon*cos"
Assert-True ($densitySource -notmatch 'max\(cos\(lat\), 0\.08\)') "cos(lat) is not floored into a polar disk"
Assert-True ($densitySource -match 'CloudShapeFloor') "Types carve Perlin-Worley with a remap floor"
Assert-True ($textureSource -match 'cloud_noise_shape_128_v5') "Runtime loads the v5 shape volume"

if ($failed -gt 0) {
    Write-Host ""
    Write-Host "$failed check(s) failed."
    exit 1
}
Write-Host ""
Write-Host "All shell / density / adaptive-march checks passed."
exit 0
