@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Release.ps1"
if errorlevel 1 (
  echo.
  echo Portable build failed. Review the message above.
  pause
  exit /b 1
)
echo.
echo Portable build completed successfully.
pause
