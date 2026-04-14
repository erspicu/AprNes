@echo off
REM Usage: run_perfview.bat [analog-size]   (default: 4)
set SIZE=%1
if "%SIZE%"=="" set SIZE=4
cd /d C:\ai_project\AprNes
C:\ai_project\AprNes\temp\PerfView.exe /nogui /accepteula ^
  /LogFile:C:\ai_project\AprNes\temp\pv_jit.log ^
  /dataFile:C:\ai_project\AprNes\temp\aprnes_jit.etl ^
  /merge:true /zip:false ^
  /kernelEvents:Profile ^
  /clrEvents:Jit,JitTracing ^
  run C:\ai_project\AprNes\tools\analyze\bench_profile.bat %SIZE%
