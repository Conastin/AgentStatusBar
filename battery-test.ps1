Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class IH {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  public const uint LEFTDOWN = 2, LEFTUP = 4, MOVE = 1;
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

Write-Output ("[t0] baseline: " + (Count-Orange 30 1044 300 36))

# 1) 点击任务栏图标（激活一个应用）
[IH]::SetCursorPos(880, 1064) | Out-Null
[IH]::mouse_event([IH]::MOVE, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 300
[IH]::mouse_event([IH]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
[IH]::mouse_event([IH]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 1200
Write-Output ("[t1] after taskbar icon click: " + (Count-Orange 30 1044 300 36))

# 2) 开一个新窗口（notepad）制造 z 序扰动
$np = Start-Process notepad -PassThru
Start-Sleep -Milliseconds 1800
Write-Output ("[t2] notepad open: " + (Count-Orange 30 1044 300 36))

# 3) 关闭它
Stop-Process -Id $np.Id -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 1200
Write-Output ("[t3] notepad closed: " + (Count-Orange 30 1044 300 36))

# 4) 再点一次任务栏（图标跳转列表是右键，先左键）
[IH]::SetCursorPos(920, 1064) | Out-Null
[IH]::mouse_event([IH]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
[IH]::mouse_event([IH]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 1500
Write-Output ("[t4] second taskbar click: " + (Count-Orange 30 1044 300 36))

# 5) 鼠标回到桌面中部
[IH]::SetCursorPos(960, 300) | Out-Null
Start-Sleep -Milliseconds 800
Write-Output ("[t5] final: " + (Count-Orange 30 1044 300 36))
