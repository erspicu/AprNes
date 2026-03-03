@echo off
REM buildAot.bat – 重建 NesCoreNative AOT DLL + AprNesAOT WinForms host
REM 輸出：
REM   NesCoreNative\bin\publish\NesCoreNative.dll  (Native AOT)
REM   AprNesAOT\bin\Release\net8.0-windows\AprNesAOT.exe  (WinForms .NET 8)
REM   並將 NesCoreNative.dll 複製到 AprNesAOT 輸出目錄

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
echo  Step 2: Build AprNesAOT ^(.NET 8 WinForms host^)
echo ============================================================
cd AprNesAOT
dotnet build -c Release
if %errorlevel% neq 0 (
    echo [FAIL] AprNesAOT build failed.
    exit /b 1
)
echo [OK] AprNesAOT.exe -^> AprNesAOT\bin\Release\net8.0-windows\AprNesAOT.exe
cd ..

echo.
echo ============================================================
echo  Step 3: Copy NesCoreNative.dll to AprNesAOT output
echo ============================================================
set SRC=NesCoreNative\bin\publish\NesCoreNative.dll
set DST=AprNesAOT\bin\Release\net8.0-windows\NesCoreNative.dll
copy /Y "%SRC%" "%DST%"
if %errorlevel% neq 0 (
    echo [WARN] Failed to copy NesCoreNative.dll to AprNesAOT output.
) else (
    echo [OK] Copied to %DST%
)

echo.
echo ============================================================
echo  Build complete.
echo  AprNesAOT.exe : AprNesAOT\bin\Release\net8.0-windows\AprNesAOT.exe
echo  NesCoreNative : AprNesAOT\bin\Release\net8.0-windows\NesCoreNative.dll
echo ============================================================
endlocal
