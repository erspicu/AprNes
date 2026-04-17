@echo off
setlocal enabledelayedexpansion

set "EXE=%~dp0AprNesAvalonia.exe"
set "ROM=%~dp0tools\benchmark\ny2011.nes"
if not exist "%ROM%" set "ROM=%~dp0..\..\..\..\etc\Mega Man 5 (USA).nes"

if not exist "%EXE%" (
    echo ERROR: AprNesAvalonia.exe not found at %EXE%
    pause
    exit /b 1
)
if not exist "%ROM%" (
    echo ERROR: benchmark ROM not found
    echo   Expected: %~dp0tools\benchmark\ny2011.nes
    echo   Or:       %~dp0..\..\..\..\etc\Mega Man 5 (USA).nes
    pause
    exit /b 1
)

set "LOG=%~dp0gui_benchmark.log"
set "TRACE=%~dp0gui_benchmark.trace.log"

set TEST_SEC=20
set COOL_SEC=10
set "FLAGS_COMMON=--rom "%ROM%" --gui-benchmark %TEST_SEC% --analog --ultra-analog --analog-output RF --analog-size 8 --crt --audio-dsp --audio-mode 2"

echo ============================================================
echo   AprNesAvalonia GUI Benchmark
echo   ROM:        %ROM%
echo   Duration:   %TEST_SEC%s per run
echo   Cooldown:   %COOL_SEC%s between runs
echo   Flags:      ultra-analog + RF + 8x + CRT + DSP mode 2
echo   Strategies: scalar / simd / gpu (render-thread D3D11)
echo ============================================================
echo.

set "STRATEGIES=scalar simd gpu"
set "RUN_IDX=0"

for %%S in (%STRATEGIES%) do (
    set "STRAT=%%S"
    echo ============================================================
    echo   Strategy: !STRAT!
    echo ============================================================

    for /L %%R in (1,1,3) do (
        set /a RUN_IDX+=1
        set "LABEL=Run %%R"
        if %%R==1 set "LABEL=Run 1 (JIT warmup, discarded)"

        echo.
        echo --- !LABEL!  [strategy=!STRAT!] ---
        del "%LOG%" 2>nul

        "%EXE%" %FLAGS_COMMON% --crt-strategy !STRAT!

        if exist "%LOG%" (
            for /f "tokens=5 delims= " %%f in ('findstr /C:"FPS presented" "%LOG%"') do set "FPS_R=%%f"
            for /f "tokens=5 delims= " %%f in ('findstr /C:"FPS produced"  "%LOG%"') do set "FPS_E=%%f"
            set "FPS_R=!FPS_R:(=!"
            set "FPS_E=!FPS_E:(=!"
            echo   Presented: !FPS_R! FPS   Emu: !FPS_E! FPS

            if %%R==2 (
                set "!STRAT!_R2_R=!FPS_R!"
                set "!STRAT!_R2_E=!FPS_E!"
            )
            if %%R==3 (
                set "!STRAT!_R3_R=!FPS_R!"
                set "!STRAT!_R3_E=!FPS_E!"
            )
        ) else (
            echo   WARNING: gui_benchmark.log not found - run %%R failed
        )

        if %%R LSS 3 (
            echo --- Cooling %COOL_SEC%s ---
            timeout /t %COOL_SEC% /nobreak >nul
        )
    )

    echo.
    echo --- Cooling %COOL_SEC%s before next strategy ---
    timeout /t %COOL_SEC% /nobreak >nul
)

echo.
echo ============================================================
echo   GUI BENCHMARK SUMMARY (avg of Run 2 + Run 3)
echo ============================================================
echo.
echo   strategy  ^| presented FPS    ^| emu FPS
echo   ----------+------------------+--------------

for %%S in (%STRATEGIES%) do (
    set "STRAT=%%S"
    set "AVG_R=--"
    set "AVG_E=--"
    if defined !STRAT!_R2_R (
        for /f %%a in ('powershell -NoProfile -Command "($([double]'!%%S_R2_R!' + [double]'!%%S_R3_R!') / 2).ToString('F2')"') do set "AVG_R=%%a"
    )
    if defined !STRAT!_R2_E (
        for /f %%a in ('powershell -NoProfile -Command "($([double]'!%%S_R2_E!' + [double]'!%%S_R3_E!') / 2).ToString('F2')"') do set "AVG_E=%%a"
    )
    echo   !STRAT!      ^| !AVG_R! FPS      ^| !AVG_E! FPS
)

echo.
echo Full per-run log: %LOG% (last run only, overwritten each iteration)
echo Lifecycle trace:  %TRACE%
echo.
pause
