@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-FFmpeg.ps1"
set "setupExit=%errorlevel%"
if not "%setupExit%"=="0" echo FFmpeg setup failed. Check the error above.
pause
exit /b %setupExit%
