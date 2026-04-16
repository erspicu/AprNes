@echo off
setlocal enabledelayedexpansion
title AprNes Benchmark

REM --- Mode dispatch ---
REM Argument "elevated" means we already chose Stable and self-elevated.
REM Argument "full" means we chose Full Performance (no admin needed).
if /i "%~1"=="elevated" goto stable_main
if /i "%~1"=="full"     goto full_main

REM --- Interactive prompt ---
echo ============================================================
echo   AprNes Benchmark - Mode Selection
echo ============================================================
echo.
echo   [Y] Stable mode
echo       - CPU locked to 80%% via powercfg (boost disabled)
echo       - Highly reproducible FPS across runs
echo       - Requires admin (UAC prompt)
echo       - Absolute FPS lower than full speed
echo.
echo   [N] Full performance mode
echo       - CPU runs at 100%% with boost
echo       - Highest absolute FPS
echo       - Run-to-run variance can be +/- 2 to 5 FPS due to
echo         thermal throttle and boost fluctuation
echo       - No admin required
echo.

REM Default is Full mode: pressing Enter without input keeps CHOICE=N.
set "CHOICE=N"
set /p CHOICE=Run stable benchmark? [y/N] (default: N, Full mode):

if /i "%CHOICE%"=="Y" goto elevate_check
goto full_main

:elevate_check
fsutil dirty query %SystemDrive% >nul 2>&1
if %errorLevel% EQU 0 goto stable_main

echo.
echo Requesting admin privileges for CPU power management...
powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -ArgumentList 'elevated' -Verb RunAs"
exit /b 0

REM ============================================================
REM Common sanity checks (reached from either mode)
REM ============================================================
:sanity
if not exist "%~dp0AprNes.exe" (
    echo ERROR: AprNes.exe not found at %~dp0
    pause
    exit /b 1
)
if not exist "%~dp0tools\benchmark\ny2011.nes" (
    echo ERROR: Benchmark ROM not found at %~dp0tools\benchmark\ny2011.nes
    pause
    exit /b 1
)
set "EXE=%~dp0AprNes.exe"
set "ROM=%~dp0tools\benchmark\ny2011.nes"
set "TMPFILE=%TEMP%\aprnes_bench_%MODE_TAG%.txt"
set JIT_SEC=20
set TEST_SEC=20
set COOL_SEC=30
goto :eof

REM ============================================================
REM STABLE MODE: cap CPU 80%, run, restore 100%
REM ============================================================
:stable_main
set "MODE_TAG=stable"
set "MODE_LABEL=Stable (CPU 80%%)"
call :sanity
set CPU_CAP=80

echo ============================================================
echo   MODE: %MODE_LABEL%
echo   ROM: ny2011.nes / NTSC / 1x / Audio Mode 0
echo   Start: %date% %time:~0,8%
echo ============================================================
echo.

echo [Setup] Capping CPU max state to %CPU_CAP%%%...
powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX %CPU_CAP% >nul 2>&1
powercfg -setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX %CPU_CAP% >nul 2>&1
powercfg -setactive SCHEME_CURRENT >nul 2>&1
echo [Setup] Done. Cooling %COOL_SEC%s before bench...
timeout /t %COOL_SEC% /nobreak >nul
echo.

call :run_bench

echo [Cleanup] Restoring CPU max state to 100%%...
powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX 100 >nul 2>&1
powercfg -setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX 100 >nul 2>&1
powercfg -setactive SCHEME_CURRENT >nul 2>&1
echo [Cleanup] Done.
echo.
goto show_results

REM ============================================================
REM FULL MODE: no admin, no CPU cap
REM ============================================================
:full_main
set "MODE_TAG=full"
set "MODE_LABEL=Full performance (CPU 100%%)"
call :sanity

echo ============================================================
echo   MODE: %MODE_LABEL%
echo   ROM: ny2011.nes / NTSC / 1x / Audio Mode 0
echo   Start: %date% %time:~0,8%
echo ============================================================
echo.
echo [Note] No CPU cap applied. FPS may vary +/- 2-5 across runs.
echo.

call :run_bench
goto show_results

REM ============================================================
REM Shared benchmark routine (3 runs + best of 3)
REM ============================================================
:run_bench
echo [Phase 0] JIT Warmup (%JIT_SEC%s, discarded)
"%EXE%" --rom "%ROM%" --benchmark %JIT_SEC% --region NTSC --audio-mode 0 > "%TMPFILE%" 2>&1
for /f "tokens=7" %%f in ('findstr "BENCHMARK:" "%TMPFILE%"') do set "FPS_JIT=%%f"
echo   JIT: %FPS_JIT% FPS
echo.
echo [Cooling] %COOL_SEC%s...
timeout /t %COOL_SEC% /nobreak >nul
echo.

echo [Phase 1] Run 2 (%TEST_SEC%s)
"%EXE%" --rom "%ROM%" --benchmark %TEST_SEC% --region NTSC --audio-mode 0 > "%TMPFILE%" 2>&1
for /f "tokens=7" %%f in ('findstr "BENCHMARK:" "%TMPFILE%"') do set "FPS_RUN2=%%f"
echo   Run 2: %FPS_RUN2% FPS
echo.
echo [Cooling] %COOL_SEC%s...
timeout /t %COOL_SEC% /nobreak >nul
echo.

echo [Phase 2] Run 3 (%TEST_SEC%s)
"%EXE%" --rom "%ROM%" --benchmark %TEST_SEC% --region NTSC --audio-mode 0 > "%TMPFILE%" 2>&1
for /f "tokens=7" %%f in ('findstr "BENCHMARK:" "%TMPFILE%"') do set "FPS_RUN3=%%f"
echo   Run 3: %FPS_RUN3% FPS
echo.

del "%TMPFILE%" >nul 2>&1
goto :eof

:show_results
for /f %%a in ('powershell -NoProfile -Command "[math]::Max([math]::Max([double]'%FPS_JIT%', [double]'%FPS_RUN2%'), [double]'%FPS_RUN3%')"') do set "FPS_BEST=%%a"

echo ============================================================
echo   RESULTS - %MODE_LABEL%
echo ============================================================
echo   JIT:        %FPS_JIT% FPS
echo   Run 2:      %FPS_RUN2% FPS
echo   Run 3:      %FPS_RUN3% FPS
echo   Best of 3:  %FPS_BEST% FPS
echo ============================================================
echo.
pause
