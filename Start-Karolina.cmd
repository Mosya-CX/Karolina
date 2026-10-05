@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Karolina.ps1" %*
set "result=%ERRORLEVEL%"
if not "%result%"=="0" (
  echo.
  echo Karolina startup failed with exit code %result%.
  pause
)
exit /b %result%
