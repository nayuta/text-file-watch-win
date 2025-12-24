@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"

if /I "%CONFIG%"=="/?" goto :help
if /I "%CONFIG%"=="-h" goto :help
if /I "%CONFIG%"=="--help" goto :help

if /I not "%CONFIG%"=="Debug" if /I not "%CONFIG%"=="Release" (
echo Invalid configuration: "%CONFIG%"
echo Use: %~nx0 ^[Debug^|Release^]
exit /b 2
)

set "EXE_NAME=TextFileWatch.exe"
set "ROOT=%~dp0"

rem Close running app (with confirmation)
tasklist /FI "IMAGENAME eq %EXE_NAME%" | find /I "%EXE_NAME%" >nul
if not errorlevel 1 (
echo %EXE_NAME% is currently running.
choice /C YN /M "Close it now?"
if errorlevel 2 (
echo Aborted.
exit /b 1
)

echo Attempting graceful close...
taskkill /IM "%EXE_NAME%" >nul 2>&1
timeout /t 2 /nobreak >nul

tasklist /FI "IMAGENAME eq %EXE_NAME%" | find /I "%EXE_NAME%" >nul
if not errorlevel 1 (
echo %EXE_NAME% is still running.
choice /C YN /M "Force close it?"
if errorlevel 2 (
echo Aborted.
exit /b 1
)
taskkill /F /T /IM "%EXE_NAME%" >nul 2>&1
)
)

rem Build
call "%ROOT%build.bat" %CONFIG%
if errorlevel 1 exit /b %errorlevel%

rem Find exe (expected path first, then fallback search)
set "EXE=%ROOT%bin\%CONFIG%\net6.0-windows\%EXE_NAME%"
if exist "%EXE%" goto :run

set "EXE="
for /r "%ROOT%bin\%CONFIG%" %%F in ("%EXE_NAME%") do (
set "EXE=%%F"
goto :run
)

echo Built successfully, but couldn't find "%EXE_NAME%" under "%ROOT%bin\%CONFIG%".
exit /b 3

:run
echo Running: "%EXE%"
start "" "%EXE%"
exit /b 0

:help
echo Build and run TextFileWatch, optionally closing a running instance first.
echo.
echo Usage:
echo   %~nx0 ^[Debug^|Release^]
exit /b 0
