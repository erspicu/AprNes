# AprNes JIT / CPU Profiling Report (RunWaveformLoop ILP Optimization)

- **Date**: 2026-04-13 01:50:55
- **Branch**: `master`
- **Build**: Debug x64, .NET Framework 4.8.1
- **ROM**: ny2011.nes (Mapper 0)
- **Duration**: 30s benchmark, 60015 CPU samples
- **Config**: NTSC, Audio Mode 2, Ultra Analog, RF Output, CRT, 4x resolution
- **Benchmark FPS**: 63.47

---

## 1. FPS Trend (Full Pipeline: RF Ultra + CRT 4x)

| Optimization | FPS |
|-------------|-----|
| Baseline (pre-DemodRow merge) | 60.59 |
| DemodulateRow merge | 63.72 |
| ntscScanBuf byte* | 59.79 (noise) |
| Modulo 6 elimination | 63.14 |
| **RunWaveformLoop ILP** | **63.47** |

Stable at ~63 FPS. The ILP optimization reduces per-dot computation but `GenerateWaveform` is only 2.4% of total — the gains are absorbed into noise.

---

## 2. CPU Exclusive Top 10

| Method | Previous | Current | Delta |
|--------|----------|---------|-------|
| `Crt_Render` | 21.7% | 21.7% | same |
| `Curvature` | 19.5% | 20.3% | +0.8% (noise) |
| `ppu_step_new` | 17.4% | 17.4% | same |
| `run` | 8.2% | 8.0% | -0.2% |
| `DemodulateRow_Core` | 7.6% | 7.7% | +0.1% |
| `PpuPhase4` | 3.5% | 3.7% | +0.2% |
| `HorizontalBlur` | 3.7% | 3.1% | -0.6% |
| `apu_step` | 3.0% | 3.0% | same |
| `GenerateWaveform` | 2.4% | 2.4% | same |

All within noise. `GenerateWaveform` steady at 2.4% — the ILP changes improve instruction throughput but don't change the method's overall CPU share.

---

## 3. Changes Made

| Optimization | Before | After |
|-------------|--------|-------|
| Herringbone matrix muls/dot | 16 | 10 (4-step lookahead) |
| Herringbone data dependency | sequential s0→s1→s2→s3 | parallel h0/h1/h2/h3 |
| Xorshift ops/dot | 12 (3×4 samples) | 3 (1×chunked) |
| ea pointer | `ea[tMod+k]` per sample | `ePtr[k]` cached |

---

## 4. Full Pipeline Optimization Series

| # | Optimization | FPS | Delta |
|---|-------------|-----|-------|
| 0 | Baseline (full pipeline) | 60.59 | — |
| 1 | DemodulateRow merge (L1I cache) | 63.72 | +5.2% |
| 2 | ntscScanBuf byte* | — | noise |
| 3 | Modulo 6 elimination | 63.14 | stable |
| 4 | **RunWaveformLoop ILP** | **63.47** | **stable ~63** |

The CRT pipeline (45%+ of CPU) remains the dominant bottleneck. Further gains require GPU acceleration.
