@echo off
setlocal

set PROJECT=AprNesAvalonia\AprNesAvalonia.csproj
set OUT_DEBUG=AprNesAvalonia\bin\Debug\net10.0\AprNesAvalonia.exe
set OUT_RELEASE=AprNesAvalonia\bin\Release\net10.0\AprNesAvalonia.exe
set OUT_WIN=publish\win-x64\AprNesAvalonia.exe
set OUT_LINUX=publish\linux-arm64\AprNesAvalonia

echo ============================================================
echo  AprNesAvalonia Build (Debug)
echo ============================================================

dotnet build "%PROJECT%" -c Debug --nologo -v minimal
if errorlevel 1 (
    echo.
    echo [BUILD FAILED] Debug
    pause
    exit /b 1
)

echo.
echo [BUILD OK] ^> %OUT_DEBUG%
echo.

echo ============================================================
echo  AprNesAvalonia Build (Release)
echo ============================================================

dotnet build "%PROJECT%" -c Release --nologo -v minimal
if errorlevel 1 (
    echo.
    echo [BUILD FAILED] Release
    pause
    exit /b 1
)

echo.
echo [BUILD OK] ^> %OUT_RELEASE%
echo.

echo ============================================================
echo  Publish: win-x64 (single-file, self-contained)
echo ============================================================

dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  --output publish\win-x64 --nologo -v minimal
if errorlevel 1 (
    echo.
    echo [PUBLISH FAILED] win-x64
    pause
    exit /b 1
)

echo.
echo [PUBLISH OK] ^> %OUT_WIN%
echo.

echo ============================================================
echo  Publish: linux-arm64 (single-file, self-contained)
echo ============================================================

dotnet publish "%PROJECT%" -c Release -r linux-arm64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  --output publish\linux-arm64 --nologo -v minimal
if errorlevel 1 (
    echo.
    echo [PUBLISH FAILED] linux-arm64
    pause
    exit /b 1
)

echo.
echo [PUBLISH OK] ^> %OUT_LINUX%
echo.

echo ============================================================
echo  All done.
echo ============================================================
echo   Debug   : %OUT_DEBUG%
echo   Release : %OUT_RELEASE%
echo   win-x64 : %OUT_WIN%
echo   linux-arm64 : %OUT_LINUX%
echo.

endlocal
