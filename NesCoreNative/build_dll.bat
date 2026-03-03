@echo off
REM Build NesCoreNative as Native AOT DLL (win-x64 release)
REM Output: bin\publish\NesCoreNative.dll
REM Requires: .NET 8 SDK + Visual Studio Build Tools (MSVC)

cd /d "%~dp0"
dotnet publish -r win-x64 -c Release -o bin\publish
if %errorlevel% neq 0 (
    echo [FAIL] AOT publish failed.
    exit /b 1
)
echo [OK] NesCoreNative.dll -> bin\publish\NesCoreNative.dll
