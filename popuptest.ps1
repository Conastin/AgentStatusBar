Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class PT {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

  public static string Popups(uint targetPid) {
    StringBuilder sb = new StringBuilder();
    int n = 0;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid == targetPid && IsWindowVisible(h)) {
        RECT r; GetWindowRect(h, out r);
        if (r.B - r.T > 100) {
          n++;
          sb.Append("rect=" + r.L + "," + r.T + " - " + r.R + "," + r.B);
        }
      }
      return true;
    }, IntPtr.Zero);
    if (n == 0) sb.Append("(closed)");
    return sb.ToString();
  }
}
"@
Add-Type -AssemblyName System.Windows.Forms
$proc = Get-Process AgentStatusBar -ErrorAction Stop

function Click-At([int]$x, [int]$y) {
  [PT]::SetCursorPos($x, $y) | Out-Null
  Start-Sleep -Milliseconds 300
  [PT]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
  [PT]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
}

Click-At 120 1064
Start-Sleep -Milliseconds 1500
Write-Output ("open #1 (click x=120):  " + [PT]::Popups([uint32]$proc.Id))
Start-Sleep -Milliseconds 3000
Write-Output ("after 3s refresh:       " + [PT]::Popups([uint32]$proc.Id))

# Esc 关闭（弹窗激活态，能收到按键）
[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
Start-Sleep -Milliseconds 800
Write-Output ("after Esc:              " + [PT]::Popups([uint32]$proc.Id))

Click-At 190 1064
Start-Sleep -Milliseconds 1500
Write-Output ("open #2 (click x=190):  " + [PT]::Popups([uint32]$proc.Id))

[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
Start-Sleep -Milliseconds 500
[PT]::SetCursorPos(960, 300) | Out-Null
