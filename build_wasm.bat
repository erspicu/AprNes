@echo off
setlocal
echo [Build] AprNesWasm - Release
echo.

dotnet publish AprNesWasm -c Release -o publish_wasm

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [FAIL] Build failed
    exit /b 1
)

echo.
echo [OK] Published to publish_wasm\wwwroot\
