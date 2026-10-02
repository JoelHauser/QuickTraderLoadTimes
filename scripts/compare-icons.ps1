<#
.SYNOPSIS
    Compares two EFT item icon caches pixel by pixel, matching icons by the game's icon hash.

.DESCRIPTION
    Each cache folder holds <n>.png plus index.json (icon hash -> n). For every hash in both, the
    two PNGs are decoded and compared. A pixel "differs" when any channel (A, R, G, B) differs by
    more than -Tolerance; an icon "differs" when more than -MaxDifferentPercent of its pixels do.

    Use it to check that a faster render path draws the same icons as the game: compare a vanilla
    cold run and a FastIconRender cold run against the real cache, and look at whether the fast
    run differs more than the vanilla run does (two vanilla renders are not always bit-identical).

.EXAMPLE
    .\compare-icons.ps1
        Real cache vs. the newest cold-cache run.
.EXAMPLE
    .\compare-icons.ps1 -A ...\cold-cache\20261002-101500-vanilla -B ...\cold-cache\20261002-102300-fast
#>
param(
    [string]$SPTPath = "H:\SPT4.1.X",
    [string]$A,
    [string]$B,
    [int]$Tolerance = 8,
    [double]$MaxDifferentPercent = 0.5,
    [int]$ShowWorst = 10
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$coldRoot = Join-Path $SPTPath "BepInEx\plugins\Hurryitup\cold-cache"
if (-not $A) { $A = Join-Path $SPTPath "SPT_Runtime\user\sptappdata\live" }
if (-not $B) {
    $newest = Get-ChildItem $coldRoot -Directory -Filter "20*" -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $newest) { throw "No cold-cache run folder in $coldRoot; pass -B." }
    $B = $newest.FullName
}

function Read-Index([string]$dir) {
    $path = Join-Path $dir "index.json"
    if (-not (Test-Path $path)) { throw "No index.json in $dir" }
    $map = @{}
    (Get-Content $path -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $map[$_.Name] = [int]$_.Value }
    return $map
}

function Read-Pixels([string]$file) {
    $bmp = New-Object System.Drawing.Bitmap $file
    try {
        $rect = New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height
        $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $bytes = New-Object byte[] ($data.Stride * $bmp.Height)
            [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
            return [pscustomobject]@{ W = $bmp.Width; H = $bmp.Height; Stride = $data.Stride; Bytes = $bytes }
        } finally { $bmp.UnlockBits($data) }
    } finally { $bmp.Dispose() }
}

# The per-pixel loop in C#: a PowerShell loop over ~10k pixels per icon is far too slow.
Add-Type -Language CSharp -TypeDefinition @"
public static class IconDiff {
    public static int[] Compare(byte[] a, byte[] b, int width, int height, int stride, int tolerance) {
        int differing = 0, maxDelta = 0;
        for (int y = 0; y < height; y++) {
            int row = y * stride;
            for (int x = 0; x < width; x++) {
                int i = row + x * 4, worst = 0;
                for (int c = 0; c < 4; c++) {
                    int d = a[i + c] - b[i + c]; if (d < 0) d = -d;
                    if (d > worst) worst = d;
                }
                if (worst > maxDelta) maxDelta = worst;
                if (worst > tolerance) differing++;
            }
        }
        return new int[] { differing, maxDelta };
    }
}
"@

$ia = Read-Index $A
$ib = Read-Index $B
$common = @($ib.Keys | Where-Object { $ia.ContainsKey($_) })
Write-Host "A: $A ($($ia.Count) icons)"
Write-Host "B: $B ($($ib.Count) icons)"
Write-Host "icons in both: $($common.Count); only in B: $($ib.Count - $common.Count)"

$identical = 0; $close = 0; $sizeMismatch = 0; $missing = 0
$results = New-Object System.Collections.Generic.List[object]
foreach ($hash in $common) {
    $fa = Join-Path $A ("{0}.png" -f $ia[$hash]); $fb = Join-Path $B ("{0}.png" -f $ib[$hash])
    if (-not (Test-Path $fa) -or -not (Test-Path $fb)) { $missing++; continue }
    $pa = Read-Pixels $fa; $pb = Read-Pixels $fb
    if ($pa.W -ne $pb.W -or $pa.H -ne $pb.H) {
        $sizeMismatch++
        $results.Add([pscustomobject]@{ Hash = $hash; Size = "$($pa.W)x$($pa.H) vs $($pb.W)x$($pb.H)"; DiffPct = 100.0; MaxDelta = 255; A = $fa; B = $fb })
        continue
    }
    $r = [IconDiff]::Compare($pa.Bytes, $pb.Bytes, $pa.W, $pa.H, $pa.Stride, $Tolerance)
    $pct = 100.0 * $r[0] / ($pa.W * $pa.H)
    if ($r[1] -eq 0) { $identical++ } elseif ($pct -le $MaxDifferentPercent) { $close++ }
    $results.Add([pscustomobject]@{ Hash = $hash; Size = "$($pa.W)x$($pa.H)"; DiffPct = [math]::Round($pct, 2); MaxDelta = $r[1]; A = $fa; B = $fb })
}

$differs = $results.Count - $identical - $close
Write-Host ""
Write-Host ("identical: {0}; within tolerance (<= {1}% of pixels off by > {2}): {3}; DIFFERENT: {4} (size mismatch {5}); missing files {6}" -f `
    $identical, $MaxDifferentPercent, $Tolerance, $close, $differs, $sizeMismatch, $missing)
if ($results.Count -gt 0) {
    $sorted = $results | Sort-Object DiffPct -Descending
    $median = ($results | Sort-Object DiffPct)[[int][math]::Floor(($results.Count - 1) / 2)].DiffPct
    Write-Host ("differing pixels per icon: median {0}%, worst {1}%" -f $median, $sorted[0].DiffPct)
    Write-Host ""
    Write-Host "worst ${ShowWorst}:"
    $sorted | Select-Object -First $ShowWorst | Format-Table Hash, Size, DiffPct, MaxDelta, B -AutoSize | Out-String -Width 220 | Write-Host
}
