Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class MouseH {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
}
"@

function Count-Orange([int]$X, [int]$Y, [int]$W, [int]$H) {
  $b = New-Object System.Drawing.Bitmap($W, $H)
  $g = [System.Drawing.Graphics]::FromImage($b)
  $g.CopyFromScreen($X, $Y, 0, 0, (New-Object System.Drawing.Size($W, $H)))
  $g.Dispose()
  $o = 0
  for ($x = 0; $x -lt $W; $x++) {
    for ($y = 0; $y -lt $H; $y++) {
      $c = $b.GetPixel($x, $y)
      if ($c.R -gt 200 -and $c.G -gt 120 -and $c.G -lt 230 -and $c.B -lt 90) { $o++ }
    }
  }
  $b.Dispose()
  return $o
}

Write-Output ("before hover: " + (Count-Orange 30 1044 300 36))
[MouseH]::SetCursorPos(880, 1064) | Out-Null
[MouseH]::mouse_event(1, 0, 0, 0, [UIntPtr]::Zero) # MOUSEEVENTF_MOVE，触发悬停判定
Start-Sleep -Milliseconds 1500
Write-Output ("hovering taskbar icons: " + (Count-Orange 30 1044 300 36))
Start-Sleep -Milliseconds 1500
Write-Output ("still hovering: " + (Count-Orange 30 1044 300 36))
[MouseH]::SetCursorPos(960, 300) | Out-Null
[MouseH]::mouse_event(1, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 800
Write-Output ("after hover: " + (Count-Orange 30 1044 300 36))
