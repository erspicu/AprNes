@echo off
REM Collect ETW trace with HARDWARE PMU counters enabled.
REM AMD/Intel PMU supports ~4-6 programmable slots; keep counter list <= 4.
REM Available counters: `temp\PerfView.exe ListCpuCounters /AcceptEULA`
REM Output: temp\aprnes_pmu.etl (input for PmuAnalyzer)
cd /d C:\ai_project\AprNes
C:\ai_project\AprNes\temp\PerfView.exe /nogui /accepteula ^
  /LogFile:C:\ai_project\AprNes\temp\pv_pmu.log ^
  /dataFile:C:\ai_project\AprNes\temp\aprnes_pmu.etl ^
  /merge:true /zip:false ^
  /kernelEvents:Profile ^
  /CpuCounters:"Timer:10000,IcacheMisses:65536,IcacheIssues:65536,TotalCycles:65536" ^
  /clrEvents:Jit,JitTracing ^
  run C:\ai_project\AprNes\tools\analyze\bench_profile.bat
