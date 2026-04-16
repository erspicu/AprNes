@echo off
setlocal enabledelayedexpansion
title AprNes Stable Benchmark

REM --- Self-elevate guard: "elevated" arg prevents infinite loop ---
if /i "%~1"=="elevated" goto main

fsutil dirty query %SystemDrive% >nul 2>&1
if %errorLevel% EQU 0 goto main

echo Requesting admin privileges for CPU power management...
powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -ArgumentList 'elevated' -Verb RunAs"
exit /b 0

:main

REM --- Sanity checks ---
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
set "TMPFILE=%TEMP%\aprnes_bench_stable.txt"
set JIT_SEC=20
set TEST_SEC=20
set COOL_SEC=30
set CPU_CAP=80

echo ============================================================
echo   AprNes Stable Benchmark (CPU locked to %CPU_CAP%%%)
echo   ROM: ny2011.nes
echo   Config: NTSC / 1x / Audio Mode 0 (Pure Digital)
echo   Start: %date% %time:~0,8%
echo ============================================================
echo.

REM --- Cap CPU to CPU_CAP%% ---
echo [Setup] Capping CPU max state to %CPU_CAP%%%...
powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX %CPU_CAP% >nul 2>&1
powercfg -setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX %CPU_CAP% >nul 2>&1
powercfg -setactive SCHEME_CURRENT >nul 2>&1
echo [Setup] Done. Cooling %COOL_SEC%s before bench...
timeout /t %COOL_SEC% /nobreak >nul
echo.

REM --- Phase 0: JIT Warmup ---
echo [Phase 0] JIT Warmup (%JIT_SEC%s, discarded)
"%EXE%" --rom "%ROM%" --benchmark %JIT_SEC% --region NTSC --audio-mode 0 > "%TMPFILE%" 2>&1
for /f "tokens=7" %%f in ('findstr "BENCHMARK:" "%TMPFILE%"') do set "FPS_JIT=%%f"
echo   JIT: %FPS_JIT% FPS
echo.
echo [Cooling] %COOL_SEC%s...
timeout /t %COOL_SEC% /nobreak >nul
echo.

REM --- Phase 1: Run 2 ---
echo [Phase 1] Run 2 (%TEST_SEC%s)
"%EXE%" --rom "%ROM%" --benchmark %TEST_SEC% --region NTSC --audio-mode 0 > "%TMPFILE%" 2>&1
for /f "tokens=7" %%f in ('findstr "BENCHMARK:" "%TMPFILE%"') do set "FPS_RUN2=%%f"
echo   Run 2: %FPS_RUN2% FPS
echo.
echo [Cooling] %COOL_SEC%s...
timeout /t %COOL_SEC% /nobreak >nul
echo.

REM --- Phase 2: Run 3 ---
echo [Phase 2] Run 3 (%TEST_SEC%s)
"%EXE%" --rom "%ROM%" --benchmark %TEST_SEC% --region NTSC --audio-mode 0 > "%TMPFILE%" 2>&1
for /f "tokens=7" %%f in ('findstr "BENCHMARK:" "%TMPFILE%"') do set "FPS_RUN3=%%f"
echo   Run 3: %FPS_RUN3% FPS
echo.

del "%TMPFILE%" >nul 2>&1

REM --- Restore CPU to 100%% (ALWAYS) ---
echo [Cleanup] Restoring CPU max state to 100%%...
powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX 100 >nul 2>&1
powercfg -setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX 100 >nul 2>&1
powercfg -setactive SCHEME_CURRENT >nul 2>&1
echo [Cleanup] Done.
echo.

REM --- Best of 3 ---
for /f %%a in ('powershell -NoProfile -Command "[math]::Max([math]::Max([double]'%FPS_JIT%', [double]'%FPS_RUN2%'), [double]'%FPS_RUN3%')"') do set "FPS_BEST=%%a"

echo ============================================================
echo   RESULTS (CPU %CPU_CAP%%% cap)
echo ============================================================
echo   JIT:        %FPS_JIT% FPS
echo   Run 2:      %FPS_RUN2% FPS
echo   Run 3:      %FPS_RUN3% FPS
echo   Best of 3:  %FPS_BEST% FPS
echo ============================================================
echo   CPU cap restored to 100%%.
echo.
pause
