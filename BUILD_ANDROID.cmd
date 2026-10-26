@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -File "%~dp0tools\build-android.ps1"
exit /b %errorlevel%
