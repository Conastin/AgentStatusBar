param(
  [int]$X = 30, [int]$Y = 1044, [int]$W = 300, [int]$H = 36
)
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

function Count-Strip {
  $b = New-Object System.Drawing.Bitmap($W, $H)
  $g = [System.Drawing.Graphics]::FromImage($b)
  $g.CopyFromScreen($X, $Y, 0, 0, (New-Object System.Drawing.Size($W, $H)))
  $g.Dispose()
  $orange = 0
  for ($x = 0; $x -lt $W; $x++) {
    for ($y = 0; $y -lt $H; $y++) {
      $c = $b.GetPixel($x, $y)
      if ($c.R -gt 200 -and $c.G -gt 120 -and $c.G -lt 230 -and $c.B -lt 90) { $orange++ }
    }
  }
  $b.Dispose()
  return $orange
}

$before = Count-Strip
Write-Output ("baseline orange px: " + $before)

# 打开开始菜单（Ctrl+Esc）
[System.Windows.Forms.SendKeys]::SendWait("^{ESC}")
Start-Sleep -Milliseconds 1800
$during = Count-Strip
Write-Output ("start menu open, orange px: " + $during)

# 关闭
[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
Start-Sleep -Milliseconds 900
$after = Count-Strip
Write-Output ("start menu closed, orange px: " + $after)
