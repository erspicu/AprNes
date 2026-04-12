# AprNes JIT / CPU Profiling Report (Modulo 6 Elimination)

- **Date**: 2026-04-13 00:58:48
- **Branch**: `master`
- **Build**: Debug x64, .NET Framework 4.8.1
- **ROM**: ny2011.nes (Mapper 0)
- **Duration**: 30s benchmark, 60187 CPU samples
- **Config**: NTSC, Audio Mode 2, Ultra Analog, RF Output, CRT, 4x resolution
- **Benchmark FPS**: 63.14

---

## 1. FPS Trend (Full Pipeline: RF Ultra + CRT 4x)

| Optimization | FPS | Delta |
|-------------|-----|-------|
| Baseline (before DemodRow merge) | 60.59 | — |
| DemodulateRow merge | 63.72 | +5.2% |
| ntscScanBuf byte* | 59.79 | (noise) |
| **Modulo 6 elimination** | **63.14** | **+5.5% from baseline** |

FPS stabilizing around 63 — the DemodRow merge was the big win, subsequent changes are within noise.

---

## 2. CPU Exclusive Top 10

| Method | Previous | Current | Delta |
|--------|----------|---------|-------|
| `Crt_Render` | 21.1% | 21.7% | +0.6% |
| `Curvature` | 20.1% | 19.5% | -0.6% |
| `ppu_step_new` | 17.4% | 17.4% | same |
| `run` | 8.3% | 8.2% | -0.1% |
| `DemodulateRow_Core` | 7.4% | 7.6% | +0.2% |
| `HorizontalBlur` | 3.5% | 3.7% | +0.2% |
| `PpuPhase4` | 3.8% | 3.5% | -0.3% |
| `apu_step` | 3.2% | 3.0% | -0.2% |
| `GenerateWaveform` | 2.5% | 2.4% | -0.1% |

All within noise (±0.6%). The `% 6` operations were only executed once per scanline (at method entry), so their elimination saves ~10-20 cycles × 240 scanlines = ~4800 cycles/frame — negligible vs total frame budget.

---

## 3. Changes Made

3 hot-path `% 6` replaced with branchless sign-extension wrap:

| Location | Before | After |
|----------|--------|-------|
| `tModQ` init | `((phase0 - wQ_half + 2) % 6 + 6) % 6` | `phase0 + 5` + wrap |
| `tModI` init | `((phase0 - kWinI_half) % 6 + 6) % 6` | `phase0 + 3` + wrap |
| Jitter phase | `(phase0 + offset) % 6` | `phase0 + offset` + wrap |

Math proof: `-wQ_half + 2 ≡ 5 (mod 6)` for wQ_half ∈ {9, 27}; `-kWinI_half ≡ 3 (mod 6)` for kWinI_half = 9.
