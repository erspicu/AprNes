@echo off
REM Collect ETW trace for AprNesAvalonia Release: CPU sampling + JIT events.
REM Output: temp\aprnesava_jit.etl
cd /d C:\ai_project\AprNes
C:\ai_project\AprNes\temp\PerfView.exe /nogui /accepteula ^
  /LogFile:C:\ai_project\AprNes\temp\pv_jit_ava.log ^
  /dataFile:C:\ai_project\AprNes\temp\aprnesava_jit.etl ^
  /merge:true /zip:false ^
  /kernelEvents:Profile ^
  /clrEvents:Jit,JitTracing ^
  run C:\ai_project\AprNes\tools\analyze\bench_profile_ava.bat
