@echo off
REM buildAot10.bat – 重建 NesCoreNative AOT DLL + AprNesAOT10 WinForms host (.NET 10)
REM 輸出：
REM   NesCoreNative\bin\publish\NesCoreNative.dll   (Native AOT DLL)
REM   AprNesAOT10\bin\Release\net10.0-windows\AprNesAOT10.exe  (.NET 10 WinForms)
REM   並將 NesCoreNative.dll 複製到 AprNesAOT10 輸出目錄

setlocal
cd /d "%~dp0"

echo ============================================================
echo  Step 1: Build NesCoreNative ^(Native AOT DLL^)
echo ============================================================
cd NesCoreNative
dotnet publish -r win-x64 -c Release -o bin\publish
if %errorlevel% neq 0 (
    echo [FAIL] NesCoreNative AOT publish failed.
    exit /b 1
)
echo [OK] NesCoreNative.dll -^> NesCoreNative\bin\publish\NesCoreNative.dll
cd ..

echo.
echo ============================================================
echo  Step 2: Build AprNesAOT10 ^(.NET 10 WinForms host^)
echo ============================================================
cd AprNesAOT10
dotnet build -c Release
if %errorlevel% neq 0 (
    echo [FAIL] AprNesAOT10 build failed.
    exit /b 1
)
echo [OK] AprNesAOT10.exe -^> AprNesAOT10\bin\Release\net10.0-windows\AprNesAOT10.exe
cd ..

echo.
echo ============================================================
echo  Step 3: Copy NesCoreNative.dll to AprNesAOT10 output
echo ============================================================
set SRC=NesCoreNative\bin\publish\NesCoreNative.dll
set DST=AprNesAOT10\bin\Release\net10.0-windows\NesCoreNative.dll
copy /Y "%SRC%" "%DST%"
if %errorlevel% neq 0 (
    echo [WARN] Failed to copy NesCoreNative.dll to AprNesAOT10 output.
) else (
    echo [OK] Copied to %DST%
)

echo.
echo ============================================================
echo  Build complete.
echo  AprNesAOT10.exe : AprNesAOT10\bin\Release\net10.0-windows\AprNesAOT10.exe
echo  NesCoreNative   : AprNesAOT10\bin\Release\net10.0-windows\NesCoreNative.dll
echo ============================================================
endlocal
