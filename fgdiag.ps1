Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class FgDiag {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
  [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO {
    public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags;
  }
  public static string Dump() {
    IntPtr fg = GetForegroundWindow();
    RECT r; GetWindowRect(fg, out r);
    uint pid; GetWindowThreadProcessId(fg, out pid);
    StringBuilder cn = new StringBuilder(128); GetClassName(fg, cn, 128);
    StringBuilder wt = new StringBuilder(256); GetWindowText(fg, wt, 256);
    IntPtr mon = MonitorFromWindow(fg, 1);
    MONITORINFO mi = new MONITORINFO(); mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
    GetMonitorInfo(mon, ref mi);
    string proc = "";
    try {
      var p = System.Diagnostics.Process.GetProcessById((int)pid);
      proc = p.ProcessName;
    } catch {}
    StringBuilder sb = new StringBuilder();
    sb.AppendLine("foreground hwnd=" + fg + " pid=" + pid + " proc=" + proc);
    sb.AppendLine("  class=[" + cn + "] title=[" + wt + "] rect=" + r.L + "," + r.T + " - " + r.R + "," + r.B);
    sb.AppendLine("  on monitor: " + mi.rcMonitor.L + "," + mi.rcMonitor.T + " - " + mi.rcMonitor.R + "," + mi.rcMonitor.B + (mi.dwFlags == 1 ? " [PRIMARY]" : " [SECONDARY]"));
    bool coversMon = r.L <= mi.rcMonitor.L && r.T <= mi.rcMonitor.T && r.R >= mi.rcMonitor.R && r.B >= mi.rcMonitor.B;
    sb.AppendLine("  covers full monitor: " + coversMon);
    return sb.ToString();
  }
}
"@
[FgDiag]::Dump()
