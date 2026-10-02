@echo off
if exist "%~dp0GitHubSync.exe" (
  start "" "%~dp0GitHubSync.exe"
  exit /b
)
set /p APP_VERSION=<"%~dp0VERSION"
if exist "%~dp0artifacts\GitHubSync-%APP_VERSION%-Portable\GitHubSync.exe" (
  start "" "%~dp0artifacts\GitHubSync-%APP_VERSION%-Portable\GitHubSync.exe"
  exit /b
)
echo Build the portable app first: powershell.exe -File tools\Build-Portable.ps1
exit /b 1
