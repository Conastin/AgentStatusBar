Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class ZT {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll", SetLastError=true)] public static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

  static IntPtr foundStrip = IntPtr.Zero;
  static IntPtr foundTb = IntPtr.Zero;

  public static IntPtr FindStrip(uint targetPid) {
    foundStrip = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid == targetPid && IsWindowVisible(h)) {
        StringBuilder cn = new StringBuilder(128); GetClassName(h, cn, 128);
        if (cn.ToString().StartsWith("WindowsForms10")) foundStrip = h;
      }
      return true;
    }, IntPtr.Zero);
    return foundStrip;
  }
  public static IntPtr GetTb() { foundTb = FindWindow("Shell_TrayWnd", null); return foundTb; }

  public static string Rel(IntPtr a, IntPtr b) {
    // a 相对 b 的位置：向上走能遇到 a 则 a 在 b 上方
    IntPtr h = b;
    for (int i = 0; i < 100; i++) {
      h = GetWindow(h, 3); // GW_HWNDPREV
      if (h == IntPtr.Zero) return "BELOW";
      if (h == a) return "ABOVE";
    }
    return "UNKNOWN";
  }
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
}
"@
$proc = Get-Process AgentStatusBar -ErrorAction Stop
$strip = [ZT]::FindStrip([uint32]$proc.Id)
$tb = [ZT]::GetTb()
Write-Output ("strip=" + $strip + " tb=" + $tb)
Write-Output ("strip exstyle = 0x" + [ZT]::GetWindowLong($strip, -20).ToString("X"))
Write-Output ("tb    exstyle = 0x" + [ZT]::GetWindowLong($tb, -20).ToString("X"))
Write-Output ("before: strip is " + [ZT]::Rel($strip, $tb) + " taskbar")

# 实验1: 插到任务栏上方（程序当前的做法）
$r1 = [ZT]::SetWindowPos($strip, $tb, 0, 0, 0, 0, 0x1 -bor 0x2 -bor 0x10)
Write-Output ("exp1 insert-after-tb ret=" + $r1 + " err=" + [Runtime.InteropServices.Marshal]::GetLastWin32Error() + " -> strip is " + [ZT]::Rel($strip, $tb) + " taskbar")

# 实验2: 显式 HWND_TOPMOST (-1)
$r2 = [ZT]::SetWindowPos($strip, [IntPtr](-1), 0, 0, 0, 0, 0x1 -bor 0x2 -bor 0x10)
Write-Output ("exp2 HWND_TOPMOST ret=" + $r2 + " -> strip is " + [ZT]::Rel($strip, $tb) + " taskbar")
