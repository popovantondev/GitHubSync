@echo off
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Build-Launcher.ps1"
exit /b %errorlevel%
