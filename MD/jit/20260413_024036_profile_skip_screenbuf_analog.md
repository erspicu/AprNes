# AprNes JIT / CPU Profiling Report (Skip ScreenBuf1x in Analog Mode)

- **Date**: 2026-04-13 02:40:36
- **Branch**: `master`
- **Build**: Debug x64, .NET Framework 4.8.1
- **ROM**: ny2011.nes (Mapper 0)
- **Duration**: 30s benchmark, 59498 CPU samples
- **Config**: NTSC, Audio Mode 2, Ultra Analog, RF Output, CRT, 4x resolution
- **Benchmark FPS**: 63.55

---

## 1. FPS Trend — Full Pipeline Optimization Series

| # | Optimization | FPS |
|---|-------------|-----|
| 0 | Baseline | 60.59 |
| 1 | DemodulateRow merge | 63.72 |
| 2 | ntscScanBuf byte* | 59.79 (noise) |
| 3 | Modulo 6 elimination | 63.14 |
| 4 | RunWaveformLoop ILP | 63.47 |
| 5 | CRT Convergence fixed-point | 62.58 |
| 6 | OAM corrupt flatten | 64.56 |
| 7 | **Skip ScreenBuf1x analog** | **63.55** |

Stable in the 62-64 range. The ScreenBuf1x skip saves ~300 KB/frame of memory writes but the effect is within noise — the writes were to a hot buffer already in L1D cache.

---

## 2. CPU Exclusive Top 10

| Excl% | Method | Previous |
|-------|--------|----------|
| 21.6% | `Crt_Render` | 22.1% |
| 20.0% | `Curvature` | 19.8% |
| **17.1%** | **`ppu_step_new`** | **17.6%** |
| 8.6% | `run` | 8.1% |
| 7.7% | `DemodulateRow_Core` | 7.5% |
| 3.6% | `PpuPhase4` | 3.6% |
| 3.2% | `HorizontalBlur` | 3.1% |
| 3.1% | `apu_step` | 3.1% |
| 2.4% | `GenerateWaveform` | 2.5% |

`ppu_step_new` dropped 0.5% (17.6→17.1%) — consistent with skipping the per-pixel ScreenBuf1x write + dot 1 SWAR fill in analog mode.

---

## 3. Memory Savings (Analog Mode)

| Eliminated write | Per frame |
|-----------------|-----------|
| ScreenBuf1x per-pixel (uint) | 61,440 × 4 = ~240 KB |
| ScreenBuf1x dot-1 SWAR fill | 240 × 1024 = ~240 KB |
| **Total** | **~480 KB/frame** |
