@echo off
setlocal

set SOURCE_KLE=C:\Program Files\ASCON\KOMPAS-3D v24 Home\Libs\GRAPHIC.KLE
set TARGET_KLE=C:\Program Files\ASCON\KOMPAS-3D v24 Home\Libs\KompasBambuDummy.kle
set KIT_SOURCE=%~dp0KompasBambuDummy.kit.config
set KIT_TARGET=%ProgramData%\ASCON\KOMPAS-3D\24\KompasBambuDummy.kit.config

net session >nul 2>&1
if errorlevel 1 (
  echo This installer must be run as Administrator.
  exit /b 1
)

if not exist "%SOURCE_KLE%" (
  echo Source KLE not found: "%SOURCE_KLE%"
  exit /b 1
)

if not exist "%KIT_SOURCE%" (
  echo Kit config template not found: "%KIT_SOURCE%"
  exit /b 1
)

copy /y "%SOURCE_KLE%" "%TARGET_KLE%" >nul
if errorlevel 1 (
  echo Failed to copy dummy KLE library.
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-Content -LiteralPath '%KIT_SOURCE%' -Raw | Set-Content -LiteralPath '%KIT_TARGET%' -Encoding Unicode"
if errorlevel 1 (
  echo Failed to write dummy kit config.
  exit /b 1
)

echo Installed Bambu Dummy Library.
exit /b 0
