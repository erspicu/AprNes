@echo off
REM Collect ETW trace: CPU sampling + JIT events.
REM Requires: temp\PerfView.exe (download from https://github.com/microsoft/perfview/releases)
REM Output: temp\aprnes_jit.etl (input for EtlAnalyzer)
cd /d C:\ai_project\AprNes
C:\ai_project\AprNes\temp\PerfView.exe /nogui /accepteula ^
  /LogFile:C:\ai_project\AprNes\temp\pv_jit.log ^
  /dataFile:C:\ai_project\AprNes\temp\aprnes_jit.etl ^
  /merge:true /zip:false ^
  /kernelEvents:Profile ^
  /clrEvents:Jit,JitTracing ^
  run C:\ai_project\AprNes\tools\analyze\bench_profile.bat
