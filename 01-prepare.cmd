@echo off
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\prepare.ps1"
if errorlevel 1 pause
