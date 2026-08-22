@echo off
rem Build with the in-box .NET Framework compiler - no SDK needed.
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [ERROR] .NET Framework C# compiler not found.
  exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 ^
 /out:AgentStatusBar.exe ^
 /r:System.dll /r:System.Core.dll /r:System.Drawing.dll ^
 /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
 src\Native.cs src\Ui.cs src\Settings.cs src\StatusEngine.cs src\Phrases.cs ^
 src\StateDot.cs src\PopupForm.cs src\TaskbarStrip.cs src\TrayApp.cs src\Program.cs

if errorlevel 1 (
  echo [ERROR] Build failed.
  exit /b 1
)
echo [OK] AgentStatusBar.exe built.
endlocal
