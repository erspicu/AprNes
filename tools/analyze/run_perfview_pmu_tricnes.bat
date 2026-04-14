@echo off
cd /d C:\ai_project\AprNes
C:\ai_project\AprNes\temp\PerfView.exe /nogui /accepteula ^
  /LogFile:C:\ai_project\AprNes\temp\pv_pmu_tricnes.log ^
  /dataFile:C:\ai_project\AprNes\temp\tricnes_pmu.etl ^
  /merge:true /zip:false ^
  /kernelEvents:Profile ^
  /CpuCounters:"Timer:10000,IcacheMisses:65536,IcacheIssues:65536,TotalCycles:65536" ^
  /clrEvents:Jit,JitTracing ^
  run C:\ai_project\AprNes\tools\analyze\bench_profile_tricnes.bat
