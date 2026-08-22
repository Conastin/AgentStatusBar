Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class PT2 {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
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
        if (r.B - r.T > 100) { n++; sb.Append(r.L + "," + r.T + " - " + r.R + "," + r.B); }
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
  [PT2]::SetCursorPos($x, $y) | Out-Null
  Start-Sleep -Milliseconds 300
  [PT2]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
  [PT2]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
}

Click-At 120 1064
Start-Sleep -Milliseconds 1200
$r1 = [PT2]::Popups([uint32]$proc.Id)
Start-Sleep -Milliseconds 2500
$r2 = [PT2]::Popups([uint32]$proc.Id)
[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
Start-Sleep -Milliseconds 600
Click-At 190 1064
Start-Sleep -Milliseconds 1200
$r3 = [PT2]::Popups([uint32]$proc.Id)
[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
[PT2]::SetCursorPos(960, 300) | Out-Null
Write-Output ("open#1: " + $r1)
Write-Output ("3s later: " + $r2)
Write-Output ("open#2: " + $r3)
Write-Output ("stable: " + (($r1 -eq $r2) -and ($r1 -eq $r3)))
