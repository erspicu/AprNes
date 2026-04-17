@echo off
:: Launcher for gui_benchmark_full.ps1
:: Forwards args to the PowerShell script.
::
:: Usage:
::   gui_benchmark_full.bat                      - uses default (10x stress test)
::   gui_benchmark_full.bat -Size 8              - 8x
::   gui_benchmark_full.bat -Size 10 -Duration 30
::   gui_benchmark_full.bat -Size 12             - experimental 12x (3072x2520)

setlocal

:: Default: 10x when no args (current test focus; UI picker pending)
set "ARGS=%*"
if "%ARGS%"=="" set "ARGS=-Size 10"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0gui_benchmark_full.ps1" %ARGS%
set "EXIT=%ERRORLEVEL%"
pause
exit /b %EXIT%
