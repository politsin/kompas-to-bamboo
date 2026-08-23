@echo off
setlocal

set ROOT=%~dp0
set CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set OUT=%ROOT%dist\plugin

if not exist "%CSC%" (
  echo csc.exe not found: "%CSC%"
  exit /b 1
)

if not exist "%OUT%" mkdir "%OUT%"

"%CSC%" /nologo /target:library /platform:x64 /optimize+ ^
  /out:"%OUT%\KompasBambuPlugin.dll" ^
  "%ROOT%plugin\Properties\AssemblyInfo.cs" ^
  "%ROOT%plugin\KompasBambuPlugin.cs" ^
  /reference:System.dll ^
  /reference:System.Windows.Forms.dll
