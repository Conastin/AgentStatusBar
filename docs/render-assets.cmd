@echo off
rem Regenerate README showcase images in docs\img (fake data, off-screen render).
rem Uses the in-box .NET Framework compiler - no SDK needed (same as build.cmd).
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [ERROR] .NET Framework C# compiler not found.
  exit /b 1
)

"%CSC%" /nologo /target:exe /optimize+ /codepage:65001 ^
 /out:"%TEMP%\AgentStatusBar-render-assets.exe" ^
 /r:System.dll /r:System.Drawing.dll ^
 render-assets.cs

if errorlevel 1 (
  echo [ERROR] Render script build failed.
  exit /b 1
)

"%TEMP%\AgentStatusBar-render-assets.exe" "%~dp0img"
endlocal
