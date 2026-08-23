@echo off
setlocal

set ROOT=%~dp0
set REGASM=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe
set PLUGIN=%ROOT%dist\plugin\KompasBambuPlugin.dll
set KITCONFIG=%ProgramData%\ASCON\KOMPAS-3D\24\KompasBambu.kit.config

if exist "%PLUGIN%" (
  "%REGASM%" "%PLUGIN%" /unregister
)

del /q "%KITCONFIG%" 2>nul
reg delete HKCU\Software\Classes\KompasBambu.Plugin /f 2>nul
reg delete HKCU\Software\Classes\CLSID\{DE7E8C03-25F4-497E-92EE-1C52B93A06E4} /f 2>nul
reg delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v KompasBambuHotkey /f 2>nul
taskkill /im kompas-bambu-hotkey.exe /f 2>nul
