@echo off
setlocal
chcp 437 >nul
title GitHubSync - Read-only Check
cd /d "%~dp0"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Release-UploadWatchdog.ps1" -CheckOnly -ProgressFile "%~1"
echo.
pause
