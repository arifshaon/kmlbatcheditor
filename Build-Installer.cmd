@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Release.ps1" -Installer
if errorlevel 1 (
  echo.
  echo Installer build failed. Review the message above.
  pause
  exit /b 1
)
echo.
echo Installer build completed successfully.
pause
