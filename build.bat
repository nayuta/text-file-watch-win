@echo off
setlocal enabledelayedexpansion

rem Build TextFileWatch (WinForms) from the repo root.
rem Usage: build.bat [Debug|Release]

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Release"

if /I not "%CONFIG%"=="Debug" if /I not "%CONFIG%"=="Release" (
  echo Invalid configuration: "%CONFIG%"
  echo Usage: %~nx0 [Debug^|Release]
  exit /b 2
)

where dotnet >nul 2>nul
if errorlevel 1 (
  echo dotnet not found. Install the .NET SDK 6+ and ensure "dotnet" is on PATH.
  exit /b 1
)

rem Repo root = directory containing this script
set "ROOT=%~dp0"
pushd "%ROOT%" >nul

set "PROJECT=TextFileWatch.csproj"
if not exist "%PROJECT%" (
  echo Project not found: "%CD%\%PROJECT%"
  popd >nul
  exit /b 1
)

echo Building %PROJECT% (%CONFIG%)...
dotnet build "%PROJECT%" -c "%CONFIG%"
set "EXITCODE=%ERRORLEVEL%"

popd >nul
exit /b %EXITCODE%
