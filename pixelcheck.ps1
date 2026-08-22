param(
  [int]$X = 380, [int]$Y = 1040, [int]$W = 450, [int]$H = 40,
  [string]$OutFile = "D:\Project\AgentStatusBar\crop-strip.png"
)
Add-Type -AssemblyName System.Drawing
$b = New-Object System.Drawing.Bitmap($W, $H)
$g = [System.Drawing.Graphics]::FromImage($b)
$g.CopyFromScreen($X, $Y, 0, 0, (New-Object System.Drawing.Size($W, $H)))
$g.Dispose()

$orange = 0; $bright = 0; $sample = New-Object System.Collections.Generic.List[string]
for ($x = 0; $x -lt $W; $x++) {
  for ($y = 0; $y -lt $H; $y++) {
    $c = $b.GetPixel($x, $y)
    if ($c.R -gt 200 -and $c.G -gt 120 -and $c.G -lt 230 -and $c.B -lt 90) {
      $orange++
      if ($sample.Count -lt 8) { $sample.Add("(" + ($X+$x) + "," + ($Y+$y) + ") #" + $c.R.ToString("X2") + $c.G.ToString("X2") + $c.B.ToString("X2")) }
    }
    if ($c.R -gt 180 -and $c.G -gt 180 -and $c.B -gt 180) { $bright++ }
  }
}
Write-Output ("orange-ish pixels: " + $orange)
Write-Output ("bright text pixels: " + $bright)
$sample | ForEach-Object { Write-Output ("  sample " + $_) }

# 放大 4 倍保存便于人工/视觉复核
$big = New-Object System.Drawing.Bitmap($W * 4, $H * 4)
$gg = [System.Drawing.Graphics]::FromImage($big)
$gg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$gg.DrawImage($b, 0, 0, $W * 4, $H * 4)
$gg.Dispose()
$big.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose()
$b.Dispose()
Write-Output ("crop saved: " + $OutFile)
