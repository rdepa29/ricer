@echo off
setlocal EnableExtensions

rem entry point for pkgman
if exist "%~dp0ricer.ps1" goto :run
echo [ricer] ricer.ps1 missing next to this script - clone https://github.com/rdepa29/ricer
exit /b 1
:run
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0ricer.ps1" %*
endlocal
