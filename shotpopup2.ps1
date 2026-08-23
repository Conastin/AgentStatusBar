Add-Type @"
using System;
using System.Runtime.InteropServices;
public class SP2 {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  static IntPtr stripH = IntPtr.Zero;
  static IntPtr popH = IntPtr.Zero;
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
  public static IntPtr FindPopup(uint targetPid) {
    popH = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid == targetPid && IsWindowVisible(h)) {
        RECT r; GetWindowRect(h, out r);
        if (r.B - r.T > 100) popH = h;
      }
      return true;
    }, IntPtr.Zero);
    return popH;
  }
  public static RECT GetRect(IntPtr h) { RECT r; GetWindowRect(h, out r); return r; }
  public static void ClickStrip(IntPtr h) {
    IntPtr lp = (IntPtr)((10 << 16) | (50 & 0xFFFF));
    SendMessage(h, 0x0201, (IntPtr)0x1, lp);
    SendMessage(h, 0x0202, IntPtr.Zero, lp);
  }
}
"@
Add-Type -AssemblyName System.Drawing
$proc = Get-Process AgentStatusBar -ErrorAction Stop
$strip = [SP2]::FindStrip([uint32]$proc.Id)
if ($strip -eq [IntPtr]::Zero) { Write-Output "strip not found"; exit 1 }
[SP2]::ClickStrip($strip)
Start-Sleep -Milliseconds 1600
$pop = [SP2]::FindPopup([uint32]$proc.Id)
if ($pop -eq [IntPtr]::Zero) { Write-Output "popup not found"; exit 1 }
$r = [SP2]::GetRect($pop)
Write-Output ("popup rect: " + $r.L + "," + $r.T + " - " + $r.R + "," + $r.B + "  (w=" + ($r.R - $r.L) + " h=" + ($r.B - $r.T) + ")")
$w = $r.R - $r.L; $hgt = $r.B - $r.T
$b = New-Object System.Drawing.Bitmap($w, $hgt)
$g = [System.Drawing.Graphics]::FromImage($b)
$g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size($w, $hgt)))
$g.Dispose()
$b.Save("D:\Project\AgentStatusBar\popup-shot.png", [System.Drawing.Imaging.ImageFormat]::Png)
$b.Dispose()
Write-Output "saved popup-shot.png"
