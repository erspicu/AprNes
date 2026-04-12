# AprNes JIT / CPU Profiling Report (Curvature Convergence Fixed-Point)

- **Date**: 2026-04-13 02:01:52
- **Branch**: `master`
- **Build**: Debug x64, .NET Framework 4.8.1
- **ROM**: ny2011.nes (Mapper 0)
- **Duration**: 30s benchmark, 59630 CPU samples
- **Config**: NTSC, Audio Mode 2, Ultra Analog, RF Output, CRT, 4x resolution
- **Benchmark FPS**: 62.58

---

## 1. FPS Trend (Full Pipeline: RF Ultra + CRT 4x)

| Optimization | FPS |
|-------------|-----|
| Baseline (pre-DemodRow merge) | 60.59 |
| DemodulateRow merge | 63.72 |
| ntscScanBuf byte* | 59.79 (noise) |
| Modulo 6 elimination | 63.14 |
| RunWaveformLoop ILP | 63.47 |
| **Curvature fixed-point** | **62.58** |

FPS in noise range (~62-64). The fixed-point change targets the Convergence path within CRT Curvature (19.8%), but the method is memory-bound (gather from `map[]`→`tmp[]`), so ALU savings are absorbed.

---

## 2. CPU Exclusive Top 10

| Method | Previous | Current | Delta |
|--------|----------|---------|-------|
| `Crt_Render` | 21.7% | 21.6% | -0.1% |
| `Curvature` | 20.3% | **19.8%** | **-0.5%** |
| `ppu_step_new` | 17.4% | 17.3% | -0.1% |
| `run` | 8.0% | 8.3% | +0.3% |
| `DemodulateRow_Core` | 7.7% | 7.5% | -0.2% |
| `PpuPhase4` | 3.7% | 3.6% | -0.1% |
| `HorizontalBlur` | 3.1% | 3.1% | same |
| `apu_step` | 3.0% | 3.1% | +0.1% |
| `GenerateWaveform` | 2.4% | 2.5% | +0.1% |

Curvature dropped 0.5% — small but consistent with eliminating per-pixel float→int conversion.

---

## 3. Change Made

Convergence loop: replaced `(int)(tx * step + baseOffset) - 1024` (float mul + add + cvttss2si per pixel) with fixed-point accumulator `iFx += stepFx; ioff = iFx >> 16` (pure integer add + shift).

Loop unrolling (4x) was evaluated but rejected — method is memory-bound (scatter/gather via `map[]`), so ALU unrolling has uncertain ROI with code size tradeoff.
