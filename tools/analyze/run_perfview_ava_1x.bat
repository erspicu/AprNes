@echo off
REM Collect ETW trace for AprNesAvalonia Release 1x digital: CPU sampling + JIT events.
REM Output: temp\aprnesava_jit_1x.etl
cd /d C:\ai_project\AprNes
C:\ai_project\AprNes\temp\PerfView.exe /nogui /accepteula ^
  /LogFile:C:\ai_project\AprNes\temp\pv_jit_ava_1x.log ^
  /dataFile:C:\ai_project\AprNes\temp\aprnesava_jit_1x.etl ^
  /merge:true /zip:false ^
  /kernelEvents:Profile ^
  /clrEvents:Jit,JitTracing ^
  run C:\ai_project\AprNes\tools\analyze\bench_profile_ava_1x.bat
