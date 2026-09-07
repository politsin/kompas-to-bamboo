@echo off
setlocal

set "ROOT=%~dp0"
set "OUT=%ROOT%rtw\bin"
set "SRC=%ROOT%rtw\KompasBambuRtw.cpp"
set "RTW=%OUT%\KompasBambu.rtw"
set "GXX=g++.exe"

where "%GXX%" >nul 2>nul
if errorlevel 1 (
  if exist "%USERPROFILE%\scoop\apps\mingw\current\bin\g++.exe" (
    set "GXX=%USERPROFILE%\scoop\apps\mingw\current\bin\g++.exe"
  ) else (
    echo g++.exe not found in PATH.
    exit /b 1
  )
)

if not exist "%OUT%" mkdir "%OUT%"

"%GXX%" -shared -municode -Os -static -static-libgcc -static-libstdc++ -o "%RTW%" "%SRC%" -Wl,--out-implib,"%OUT%\KompasBambu.a" -lshell32 -luser32
if errorlevel 1 exit /b 1

echo Built "%RTW%"
