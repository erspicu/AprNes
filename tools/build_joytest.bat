@echo off
echo Building JoyTest.cs ...
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo ERROR: csc.exe not found at %CSC%
    pause & exit /b 1
)
"%CSC%" /target:winexe /platform:x64 /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:JoyTest.exe JoyTest.cs
if %ERRORLEVEL% EQU 0 (
    echo Build OK — launching JoyTest.exe ...
    start JoyTest.exe
) else (
    echo Build FAILED
    pause
)
