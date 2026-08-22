Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class ZDiag {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

  public static string ZList() {
    StringBuilder sb = new StringBuilder();
    int idx = 0;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      if (!IsWindowVisible(h)) return true;
      RECT r;
      GetWindowRect(h, out r);
      if (r.R - r.L < 2 || r.B - r.T < 2) return true;
      // 只关心屏幕底部区域（y > 1000）的窗口
      if (r.T < 1000) { idx++; return true; }
      uint pid;
      GetWindowThreadProcessId(h, out pid);
      StringBuilder cn = new StringBuilder(128);
      GetClassName(h, cn, 128);
      StringBuilder wt = new StringBuilder(128);
      GetWindowText(h, wt, 128);
      sb.AppendLine("z#" + idx + " hwnd=" + h + " pid=" + pid + " class=[" + cn + "] rect=" + r.L + "," + r.T + " - " + r.R + "," + r.B + " title=[" + wt + "]");
      idx++;
      return true;
    }, IntPtr.Zero);
    return sb.ToString();
  }
}
"@
[ZDiag]::ZList()
