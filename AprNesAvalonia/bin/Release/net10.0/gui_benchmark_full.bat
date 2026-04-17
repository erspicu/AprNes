@echo off
:: Launcher: runs gui_benchmark_full.ps1 via PowerShell with ExecutionPolicy
:: bypass. This avoids cmd.exe's parentheses/encoding pitfalls that broke the
:: previous all-bat version. All arguments passed to this .bat are forwarded
:: to the .ps1 script (e.g. -Duration 30, -Cooldown 5, -RomPath "...").

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0gui_benchmark_full.ps1" %*
set "EXIT=%ERRORLEVEL%"
pause
exit /b %EXIT%
