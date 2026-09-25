@echo off
setlocal EnableExtensions
rem ricer.bat - canonical entry point for the ricer package manager.
rem Lives next to ricer.ps1 in this checkout (OneDrive - MICDS\Apps\ricer).
if exist "%~dp0ricer.ps1" goto :run
echo [ricer] ricer.ps1 missing next to this script - clone https://github.com/rdepa29/ricer
exit /b 1
:run
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0ricer.ps1" %*
endlocal