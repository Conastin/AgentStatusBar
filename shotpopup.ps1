Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class SP {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  static IntPtr stripH = IntPtr.Zero;
  public static IntPtr FindStrip(uint targetPid) {
    stripH = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid == targetPid && IsWindowVisible(h)) {
        RECT r; GetWindowRect(h, out r);
        if (r.B - r.T > 15 && r.B - r.T < 50 && r.R - r.L > 40 && r.R - r.L < 500) stripH = h;
      }
      return true;
    }, IntPtr.Zero);
    return stripH;
  }
  public static void ClickStrip(IntPtr h) {
    IntPtr lp = (IntPtr)((10 << 16) | (50 & 0xFFFF));
    SendMessage(h, 0x0201, (IntPtr)0x1, lp);
    SendMessage(h, 0x0202, IntPtr.Zero, lp);
  }
}
"@
Add-Type -AssemblyName System.Drawing
$proc = Get-Process AgentStatusBar -ErrorAction Stop
$strip = [SP]::FindStrip([uint32]$proc.Id)
if ($strip -eq [IntPtr]::Zero) { Write-Output "strip not found"; exit 1 }
[SP]::ClickStrip($strip)
Start-Sleep -Milliseconds 1600

$b = New-Object System.Drawing.Bitmap(400, 220)
$g = [System.Drawing.Graphics]::FromImage($b)
$g.CopyFromScreen(24, 830, 0, 0, (New-Object System.Drawing.Size(400, 220)))
$g.Dispose()
$b.Save("D:\Project\AgentStatusBar\popup-shot.png", [System.Drawing.Imaging.ImageFormat]::Png)
$b.Dispose()
Write-Output "saved popup-shot.png (region 24,830 - 424,1050)"
