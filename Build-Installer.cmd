@echo off
setlocal

title Simple KML Editor - Build Installer

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Release.ps1" -Installer
if errorlevel 1 (
  echo.
  echo Simple KML Editor installer build failed.
  echo Review the error message above.
  pause
  exit /b 1
)

echo.
echo Simple KML Editor installer build completed successfully.
echo.
echo Installer folder:
echo   %~dp0artifacts\installer
echo.

if exist "%~dp0artifacts\installer" (
  explorer.exe "%~dp0artifacts\installer"
)

pause
