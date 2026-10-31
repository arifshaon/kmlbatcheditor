@echo off
setlocal

title Simple KML Editor - Build Portable Package

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Release.ps1"
if errorlevel 1 (
  echo.
  echo Simple KML Editor portable build failed.
  echo Review the error message above.
  pause
  exit /b 1
)

echo.
echo Simple KML Editor portable build completed successfully.
echo.
echo Portable package folder:
echo   %~dp0artifacts\portable
echo.

if exist "%~dp0artifacts\portable" (
  explorer.exe "%~dp0artifacts\portable"
)

pause
