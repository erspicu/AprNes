@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul 2>&1
title AprNes Full Pipeline Benchmark

:: ============================================================
:: AprNes Full Pipeline Benchmark
:: Tests: NTSC + AccuracyOptA + Audio Mode 2 (Modern Stereo)
::        + Analog + Ultra Analog + CRT
:: Resolutions: 2x, 4x, 6x, 8x (20s each, 30s cooldown)
:: ============================================================

set "EXE=%~dp0AprNes.exe"
set "ROM=%~dp0tools\benchmark\ny2011.nes"
set "TMPFILE=%TEMP%\aprnes_bench.txt"
set JIT_SEC=10
set TEST_SEC=30
set COOL_SEC=30

echo ============================================================
echo   AprNes Full Pipeline Benchmark
echo   ROM: ny2011.nes
echo   Config: NTSC / AccuracyOptA=ON / Audio Mode 2 (Modern)
echo           Analog + Ultra Analog + CRT
echo   Duration: %TEST_SEC%s per run, %COOL_SEC%s cooldown
echo ============================================================
echo.

:: ── Phase 0: JIT Warmup ──
echo [Phase 0] JIT Warmup (%JIT_SEC%s, result discarded)
"%EXE%" --rom "%ROM%" --benchmark %JIT_SEC% --region NTSC --accuracy A --audio-dsp --audio-mode 2 --ultra-analog --crt --analog-size 2 > "%TMPFILE%" 2>&1
for /f "tokens=7" %%f in ('findstr "BENCHMARK:" "%TMPFILE%"') do set "FPS_JIT=%%f"
echo   JIT warmup: %FPS_JIT% FPS (discarded)
echo.
echo [Cooling] Waiting %COOL_SEC%s...
timeout /t %COOL_SEC% /nobreak >nul
echo.

:: ── Phase 1-4: Benchmark each resolution ──
set IDX=0
for %%S in (2 4 6 8) do (
    set /a IDX+=1
    set /a W=256*%%S
    set /a H=210*%%S

    echo ============================================================
    echo   [!IDX!/4] AnalogSize=%%Sx  ^(!W!x!H!^)
    echo ============================================================

    echo --- Running %TEST_SEC%s ---
    "%EXE%" --rom "%ROM%" --benchmark %TEST_SEC% --region NTSC --accuracy A --audio-dsp --audio-mode 2 --ultra-analog --crt --analog-size %%S > "%TMPFILE%" 2>&1
    for /f "tokens=7" %%f in ('findstr "BENCHMARK:" "%TMPFILE%"') do set "FPS_%%S=%%f"

    echo   Result: !FPS_%%S! FPS
    echo.

    :: Cooldown (skip after last)
    if %%S NEQ 8 (
        echo [Cooling] Waiting %COOL_SEC%s...
        timeout /t %COOL_SEC% /nobreak >nul
        echo.
    )
)

:: Cleanup temp file
del "%TMPFILE%" >nul 2>&1

:: ── Results Summary ──
echo.
echo ============================================================
echo   BENCHMARK RESULTS
echo ============================================================
echo.
echo   Configuration:
echo     Region:       NTSC
echo     AccuracyOptA: ON (per-dot secondary OAM evaluation)
echo     Audio:        Mode 2 (Modern Stereo)
echo     Video:        Analog + Ultra Analog + CRT
echo     Test ROM:     ny2011.nes
echo     Duration:     %TEST_SEC%s per resolution
echo.
echo   Results:
echo     2x  (512x420)   : %FPS_2% FPS
echo     4x  (1024x840)  : %FPS_4% FPS
echo     6x  (1536x1260) : %FPS_6% FPS
echo     8x  (2048x1680) : %FPS_8% FPS
echo.
echo   (NES realtime = 60.10 FPS, any value above that runs full speed)
echo ============================================================
echo.
pause
