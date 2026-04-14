@echo off
REM Usage: bench_profile.bat [analog-size]   (default: 4)
set SIZE=%1
if "%SIZE%"=="" set SIZE=4
"C:\ai_project\AprNes\AprNes\bin\Debug\AprNes.exe" --rom "C:\ai_project\AprNes\AprNes\bin\Debug\tools\benchmark\ny2011.nes" --benchmark 30 --region NTSC --audio-mode 2 --ultra-analog --analog-output RF --analog-size %SIZE% --crt
