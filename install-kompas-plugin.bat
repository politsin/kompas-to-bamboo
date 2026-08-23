@echo off
setlocal

set ROOT=%~dp0
set REGASM=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe
set PLUGIN=%ROOT%dist\plugin\KompasBambuPlugin.dll
set HOTKEY=%ROOT%dist\kompas-bambu-hotkey.exe

if not exist "%REGASM%" (
  echo RegAsm x64 not found: "%REGASM%"
  exit /b 1
)

if not exist "%PLUGIN%" (
  echo Plugin DLL not found. Build the project first.
  exit /b 1
)

"%REGASM%" "%PLUGIN%" /codebase
if errorlevel 1 (
  echo Admin COM registration failed, trying per-user COM registration...
  call :register_user_com
  if errorlevel 1 exit /b 1
)

if exist "%HOTKEY%" (
  reg add HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v KompasBambuHotkey /t REG_SZ /d "\"%HOTKEY%\"" /f
  start "" "%HOTKEY%"
)

echo Installed. Restart KOMPAS-3D, then enable/load "Bambu Studio" in Applications/Libraries.
echo Ctrl+Shift+S works while a KOMPAS window is active.
exit /b 0

:register_user_com
set CLSID={DE7E8C03-25F4-497E-92EE-1C52B93A06E4}
set ASSEMBLY=KompasBambuPlugin, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
set CLASS=KompasBambu.KompasBambuPlugin
set PROGID=KompasBambu.Plugin

reg add HKCU\Software\Classes\%PROGID% /ve /d "%CLASS%" /f || exit /b 1
reg add HKCU\Software\Classes\%PROGID%\CLSID /ve /d "%CLSID%" /f || exit /b 1
reg add HKCU\Software\Classes\CLSID\%CLSID% /ve /d "%CLASS%" /f || exit /b 1
reg add HKCU\Software\Classes\CLSID\%CLSID%\Kompas_Library /f || exit /b 1
reg add HKCU\Software\Classes\CLSID\%CLSID%\ProgId /ve /d "%PROGID%" /f || exit /b 1
reg add HKCU\Software\Classes\CLSID\%CLSID%\InprocServer32 /ve /d "mscoree.dll" /f || exit /b 1
reg add HKCU\Software\Classes\CLSID\%CLSID%\InprocServer32 /v ThreadingModel /t REG_SZ /d Both /f || exit /b 1
reg add HKCU\Software\Classes\CLSID\%CLSID%\InprocServer32 /v Class /t REG_SZ /d "%CLASS%" /f || exit /b 1
reg add HKCU\Software\Classes\CLSID\%CLSID%\InprocServer32 /v Assembly /t REG_SZ /d "%ASSEMBLY%" /f || exit /b 1
reg add HKCU\Software\Classes\CLSID\%CLSID%\InprocServer32 /v RuntimeVersion /t REG_SZ /d "v4.0.30319" /f || exit /b 1
reg add HKCU\Software\Classes\CLSID\%CLSID%\InprocServer32 /v CodeBase /t REG_SZ /d "file:///%PLUGIN:\=/%" /f || exit /b 1
reg add HKCU\Software\Classes\CLSID\%CLSID%\InprocServer32\1.0.0.0 /v Class /t REG_SZ /d "%CLASS%" /f || exit /b 1
reg add HKCU\Software\Classes\CLSID\%CLSID%\InprocServer32\1.0.0.0 /v Assembly /t REG_SZ /d "%ASSEMBLY%" /f || exit /b 1
reg add HKCU\Software\Classes\CLSID\%CLSID%\InprocServer32\1.0.0.0 /v RuntimeVersion /t REG_SZ /d "v4.0.30319" /f || exit /b 1
reg add HKCU\Software\Classes\CLSID\%CLSID%\InprocServer32\1.0.0.0 /v CodeBase /t REG_SZ /d "file:///%PLUGIN:\=/%" /f || exit /b 1
exit /b 0
