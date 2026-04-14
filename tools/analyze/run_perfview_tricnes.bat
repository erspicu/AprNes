@echo off
cd /d C:\ai_project\AprNes
C:\ai_project\AprNes\temp\PerfView.exe /nogui /accepteula ^
  /LogFile:C:\ai_project\AprNes\temp\pv_jit_tricnes.log ^
  /dataFile:C:\ai_project\AprNes\temp\tricnes_jit.etl ^
  /merge:true /zip:false ^
  /kernelEvents:Profile ^
  /clrEvents:Jit,JitTracing ^
  run C:\ai_project\AprNes\tools\analyze\bench_profile_tricnes.bat
