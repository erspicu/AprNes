@echo off
REM Usage: bench_profile_ava.bat [analog-size]   (default: 4)
set SIZE=%1
if "%SIZE%"=="" set SIZE=4
"C:\ai_project\AprNes\AprNesAvalonia\bin\Release\net10.0\AprNesAvalonia.exe" --rom "C:\ai_project\AprNes\AprNesAvalonia\bin\Release\net10.0\tools\benchmark\ny2011.nes" --benchmark 30 --region NTSC --audio-mode 2 --ultra-analog --analog-output RF --analog-size %SIZE% --crt
