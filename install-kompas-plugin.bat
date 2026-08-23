@echo off
setlocal

set ROOT=%~dp0
set REGASM=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe
set PLUGIN=%ROOT%dist\plugin\KompasBambuPlugin.dll
set KIT_SOURCE=%ROOT%KompasBambu.kit.config
set KITDIR=%ProgramData%\ASCON\KOMPAS-3D\24
set KITCONFIG=%KITDIR%\KompasBambu.kit.config

if not exist "%REGASM%" (
  echo RegAsm x64 not found: "%REGASM%"
  exit /b 1
)

if not exist "%PLUGIN%" (
  echo Plugin DLL not found. Build the project first.
  exit /b 1
)

if not exist "%KIT_SOURCE%" (
  echo Kit config template not found: "%KIT_SOURCE%"
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

if not exist "%KITDIR%" (
  echo KOMPAS-3D v24 kit config folder was not found: "%KITDIR%"
  exit /b 1
)

copy /y "%KIT_SOURCE%" "%KITCONFIG%" >nul
if errorlevel 1 (
  echo Failed to write KOMPAS kit config.
  exit /b 1
)

reg delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v KompasBambuHotkey /f 2>nul
taskkill /im kompas-bambu-hotkey.exe /f 2>nul

echo Installed. Restart KOMPAS-3D. "Bambu Studio" should appear in Applications.
exit /b 0
