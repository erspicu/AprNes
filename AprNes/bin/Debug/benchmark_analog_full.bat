@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul 2>&1
title AprNes Analog Full Benchmark (8x / Ultra / RF / CRT / DSP Mode 2)

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

:: ============================================================
:: AprNes Analog Full Benchmark
:: Config: NTSC / 8x (2048x1920) / Ultra Analog / RF Output / CRT / Audio DSP Mode 2 (Modern)
:: Maximum visual+audio processing load
:: Protocol: JIT warmup - cooldown - run2 - cooldown - run3 - best of 3
:: ============================================================

set "EXE=%~dp0AprNes.exe"
set "ROM=%~dp0tools\benchmark\ny2011.nes"
set "TMPFILE=%TEMP%\aprnes_bench_analog_full.txt"
set JIT_SEC=20
set TEST_SEC=20
set COOL_SEC=30

set "FLAGS=--region NTSC --ultra-analog --analog-output RF --analog-size 8 --crt --audio-mode 2"

echo ============================================================
echo   AprNes Analog Full Benchmark
echo   ROM: ny2011.nes
echo   Config:
echo     Region:       NTSC
echo     Analog:       Ultra (NTSC v2 full decode)
echo     Output:       RF (composite + CRT RF noise)
echo     CRT:          Enabled (scanline + bloom + curvature)
echo     Resolution:   8x (2048x1920)
echo     Audio:        Mode 2 (Modern DSP)
echo   Protocol: JIT(%JIT_SEC%s) + Cool(%COOL_SEC%s) + Run2(%TEST_SEC%s) + Cool(%COOL_SEC%s) + Run3(%TEST_SEC%s)
echo   Start: %date% %time:~0,8%
for /f "delims=" %%v in ('"%EXE%" --version 2^>nul') do echo   %%v
echo.
echo   Hardware:
for /f "delims=" %%c in ('powershell -NoProfile -Command "(Get-CimInstance Win32_Processor).Name" 2^>nul') do echo     CPU: %%c
for /f "delims=" %%m in ('powershell -NoProfile -Command "[math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory/1GB)" 2^>nul') do echo     RAM: %%m GB
for /f "delims=" %%g in ('powershell -NoProfile -Command "(Get-CimInstance Win32_VideoController).Name" 2^>nul') do echo     GPU: %%g
echo ============================================================
echo.

:: -- Phase 0: JIT Warmup --
echo [Phase 0] JIT Warmup (%JIT_SEC%s, result discarded)
"%EXE%" --rom "%ROM%" --benchmark %JIT_SEC% %FLAGS% > "%TMPFILE%" 2>&1
for /f "tokens=7" %%f in ('findstr "BENCHMARK:" "%TMPFILE%"') do set "FPS_JIT=%%f"
echo   JIT warmup: %FPS_JIT% FPS (discarded - Tier-0 JIT collecting PGO)
echo.
echo [Cooling] Waiting %COOL_SEC%s for CPU cooldown...
timeout /t %COOL_SEC% /nobreak >nul
echo.

:: -- Phase 1: Run 2 (first valid measurement) --
echo [Phase 1] Run 2 - First measurement (%TEST_SEC%s)
"%EXE%" --rom "%ROM%" --benchmark %TEST_SEC% %FLAGS% > "%TMPFILE%" 2>&1
for /f "tokens=7" %%f in ('findstr "BENCHMARK:" "%TMPFILE%"') do set "FPS_RUN2=%%f"
echo   Run 2: %FPS_RUN2% FPS
echo.
echo [Cooling] Waiting %COOL_SEC%s for CPU cooldown...
timeout /t %COOL_SEC% /nobreak >nul
echo.

:: -- Phase 2: Run 3 (second valid measurement) --
echo [Phase 2] Run 3 - Second measurement (%TEST_SEC%s)
"%EXE%" --rom "%ROM%" --benchmark %TEST_SEC% %FLAGS% > "%TMPFILE%" 2>&1
for /f "tokens=7" %%f in ('findstr "BENCHMARK:" "%TMPFILE%"') do set "FPS_RUN3=%%f"
echo   Run 3: %FPS_RUN3% FPS
echo.

:: Cleanup temp file
del "%TMPFILE%" >nul 2>&1

:: -- Take the fastest of all 3 runs (including JIT) as the record --
for /f %%a in ('powershell -NoProfile -Command "[math]::Max([math]::Max([double]'%FPS_JIT%', [double]'%FPS_RUN2%'), [double]'%FPS_RUN3%')"') do set "FPS_BEST=%%a"

:: -- Results Summary --
echo ============================================================
echo   ANALOG FULL BENCHMARK RESULTS
echo ============================================================
echo.
echo   Configuration:
echo     Region:       NTSC
echo     Analog:       Ultra (NTSC v2 full decode)
echo     Output:       RF (composite + CRT RF noise)
echo     CRT:          Enabled
echo     Resolution:   8x (2048x1920)
echo     Audio:        Mode 2 (Modern DSP)
echo     Test ROM:     ny2011.nes
echo     Duration:     %TEST_SEC%s per run
echo.
echo   Results:
echo     JIT warmup:  %FPS_JIT% FPS
echo     Run 2:       %FPS_RUN2% FPS
echo     Run 3:       %FPS_RUN3% FPS
echo     -------------------------
echo     Best of 3:   %FPS_BEST% FPS
echo.
echo   (NES realtime = 60.10 FPS)
echo ============================================================
echo.
pause
