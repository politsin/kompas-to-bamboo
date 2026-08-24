@echo off
setlocal

set TARGET_KLE=C:\Program Files\ASCON\KOMPAS-3D v24 Home\Libs\KompasBambuDummy.kle
set KIT_TARGET=%ProgramData%\ASCON\KOMPAS-3D\24\KompasBambuDummy.kit.config

net session >nul 2>&1
if errorlevel 1 (
  echo This uninstaller must be run as Administrator.
  exit /b 1
)

del /q "%TARGET_KLE%" 2>nul
del /q "%KIT_TARGET%" 2>nul

echo Removed Bambu Dummy Library.
exit /b 0
