@echo off
setlocal

set ROOT=%~dp0
set REGASM=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe
set PLUGIN=%ROOT%dist\plugin\KompasBambuPlugin.dll

if not exist "%REGASM%" (
  echo RegAsm x64 not found: "%REGASM%"
  exit /b 1
)

if not exist "%PLUGIN%" (
  echo Plugin DLL not found. Build the project first.
  exit /b 1
)

net session >nul 2>&1
if errorlevel 1 (
  echo This installer must be run as Administrator.
  echo KOMPAS-3D libraries are registered under HKLM, and per-user COM registration is not reliable for KOMPAS discovery.
  exit /b 1
)

"%REGASM%" "%PLUGIN%" /codebase
if errorlevel 1 (
  echo COM registration failed.
  exit /b 1
)

reg delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v KompasBambuHotkey /f 2>nul
taskkill /im kompas-bambu-hotkey.exe /f 2>nul

echo Installed. Restart KOMPAS-3D, then enable/load "Bambu Studio" in Applications/Libraries.
exit /b 0
