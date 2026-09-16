@echo off
setlocal
cd /d "%~dp0"
if not exist "AvorionAdmin.Api.exe" (
  echo Missing AvorionAdmin.Api.exe.
  pause
  exit /b 1
)
if not exist "data" mkdir "data"
set "Avorion__DataDirectory=%CD%\data"
start "" /min powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%CD%\Open-OrionAdmin.ps1" -Port 5088
echo OrionAdmin is starting at http://127.0.0.1:5088/server/control
echo Keep this window open. Before Ctrl+C, safely stop any running Avorion server in the UI.
"AvorionAdmin.Api.exe" --urls "http://127.0.0.1:5088"
set "ORION_EXIT=%ERRORLEVEL%"
echo OrionAdmin exited with code %ORION_EXIT%.
if not "%ORION_EXIT%"=="0" pause
exit /b %ORION_EXIT%
