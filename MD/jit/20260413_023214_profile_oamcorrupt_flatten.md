# AprNes JIT / CPU Profiling Report (OAM Corrupt Flatten + Session Summary)

- **Date**: 2026-04-13 02:32:14
- **Branch**: `master`
- **Build**: Debug x64, .NET Framework 4.8.1
- **ROM**: ny2011.nes (Mapper 0)
- **Duration**: 30s benchmark, 59656 CPU samples
- **Config**: NTSC, Audio Mode 2, Ultra Analog, RF Output, CRT, 4x resolution
- **Benchmark FPS**: 64.56

---

## 1. FPS Trend — Full Pipeline Optimization Series

| # | Optimization | FPS | Delta |
|---|-------------|-----|-------|
| 0 | Baseline (pre-NTSC optimizations) | 60.59 | — |
| 1 | DemodulateRow Composite/SVideo merge | 63.72 | +5.2% |
| 2 | ntscScanBuf byte[] → byte* | 59.79 | (noise) |
| 3 | Modulo 6 branchless elimination | 63.14 | stable |
| 4 | RunWaveformLoop 4-step ILP + xorshift chunk | 63.47 | stable |
| 5 | CRT Convergence fixed-point accumulator | 62.58 | stable |
| 6 | **OAM corrupt if-flatten** | **64.56** | **+6.6% from baseline** |

Best result so far: **64.56 FPS** — comfortably above 60.10 real-time target.

---

## 2. CPU Exclusive Top 10

| Excl% | Method |
|-------|--------|
| 22.1% | `Crt_Render` |
| 19.8% | `Curvature+Convergence` |
| 17.6% | `ppu_step_new` |
| 8.1% | `run` |
| 7.5% | `DemodulateRow_Core` |
| 3.6% | `PpuPhase4` |
| 3.1% | `HorizontalBlur` |
| 3.1% | `apu_step` |
| 2.5% | `GenerateWaveform` |

---

## 3. Subsystem Summary

| Subsystem | % |
|-----------|---|
| CRT Pipeline | 45.0% |
| PPU Core | 21.6% |
| NTSC Analog | 10.0% |
| CPU Core | 8.6% |
| APU Core | 3.1% |
