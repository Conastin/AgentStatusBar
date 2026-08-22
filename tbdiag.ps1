Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class TBDiag {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

  public static string Dump() {
    StringBuilder sb = new StringBuilder();
    IntPtr tb = FindWindow("Shell_TrayWnd", null);
    RECT r;
    GetWindowRect(tb, out r);
    sb.AppendLine("taskbar hwnd=" + tb + " rect=" + r.L + "," + r.T + " - " + r.R + "," + r.B);
    EnumChildWindows(tb, delegate(IntPtr h, IntPtr l) {
      if (IsWindowVisible(h)) {
        RECT cr;
        GetWindowRect(h, out cr);
        StringBuilder cn = new StringBuilder(128);
        GetClassName(h, cn, 128);
        sb.AppendLine("  child hwnd=" + h + " class=[" + cn + "] rect=" + cr.L + "," + cr.T + " - " + cr.R + "," + cr.B);
      }
      return true;
    }, IntPtr.Zero);
    return sb.ToString();
  }

  public static string DumpAppWindows(uint targetPid) {
    StringBuilder sb = new StringBuilder();
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint pid;
      GetWindowThreadProcessId(h, out pid);
      if (pid == targetPid && IsWindowVisible(h)) {
        RECT cr;
        GetWindowRect(h, out cr);
        StringBuilder cn = new StringBuilder(128);
        GetClassName(h, cn, 128);
        sb.AppendLine("  appwin hwnd=" + h + " class=[" + cn + "] rect=" + cr.L + "," + cr.T + " - " + cr.R + "," + cr.B);
      }
      return true;
    }, IntPtr.Zero);
    return sb.ToString();
  }
}
"@
Write-Output "=== taskbar ==="
[TBDiag]::Dump()
$p = Get-Process AgentStatusBar -ErrorAction SilentlyContinue
if ($p) {
  Write-Output ("=== AgentStatusBar pid=" + $p.Id + " alive, windows: ===")
  [TBDiag]::DumpAppWindows([uint32]$p.Id)
} else {
  Write-Output "=== AgentStatusBar NOT RUNNING ==="
}
