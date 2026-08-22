Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class PT3 {
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

  public static string Popups(uint targetPid) {
    StringBuilder sb = new StringBuilder();
    int n = 0;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid == targetPid && IsWindowVisible(h)) {
        RECT r; GetWindowRect(h, out r);
        if (r.B - r.T > 100) { n++; sb.Append(r.L + "," + r.T + " - " + r.R + "," + r.B); }
      }
      return true;
    }, IntPtr.Zero);
    if (n == 0) sb.Append("(closed)");
    return sb.ToString();
  }

  public static void ClickStrip(IntPtr h) {
    IntPtr lp = (IntPtr)((10 << 16) | (50 & 0xFFFF)); // client (50,10)
    SendMessage(h, 0x0201, (IntPtr)0x1, lp); // WM_LBUTTONDOWN
    SendMessage(h, 0x0202, IntPtr.Zero, lp); // WM_LBUTTONUP
  }

  public static string RectStr(IntPtr h) {
    RECT r; GetWindowRect(h, out r);
    return r.L + "," + r.T + " - " + r.R + "," + r.B;
  }
}
"@
$proc = Get-Process AgentStatusBar -ErrorAction Stop
$strip = [PT3]::FindStrip([uint32]$proc.Id)
if ($strip -eq [IntPtr]::Zero) { Write-Output "strip not found"; exit 1 }
Write-Output ("strip rect: " + [PT3]::RectStr($strip))

[PT3]::ClickStrip($strip)
Start-Sleep -Milliseconds 1500
$r1 = [PT3]::Popups([uint32]$proc.Id)
Start-Sleep -Milliseconds 2500
$r2 = [PT3]::Popups([uint32]$proc.Id)
[PT3]::ClickStrip($strip)
Start-Sleep -Milliseconds 800
$r3 = [PT3]::Popups([uint32]$proc.Id)

Write-Output ("popup open:  " + $r1)
Write-Output ("after 2.5s:  " + $r2)
Write-Output ("click again: " + $r3)
