# AprNes JIT / CPU Profiling Report (DemodulateRow Merge)

- **Date**: 2026-04-13 00:19:03
- **Branch**: `master`
- **Build**: Debug x64, .NET Framework 4.8.1
- **ROM**: ny2011.nes (Mapper 0)
- **Duration**: 30s benchmark, 60202 CPU samples
- **Config**: NTSC, Audio Mode 2, Ultra Analog, RF Output, CRT, 4x resolution
- **Benchmark FPS**: 63.72 (previous: 60.59, **+5.2%**)

---

## 1. CPU Exclusive Top 10 — Before vs After

| Method | Before | After | Delta |
|--------|--------|-------|-------|
| `Crt_Render` | 21.1% | 21.7% | +0.6% (noise) |
| `Curvature+Convergence` | 20.7% | 20.4% | -0.3% (noise) |
| `ppu_step_new` | 17.4% | 17.3% | -0.1% |
| `run` | 8.2% | 8.3% | +0.1% |
| `DemodulateRow` | 7.2% | — | merged |
| **`DemodulateRow_Core`** | — | **7.5%** | single impl |
| `PpuPhase4` | 3.7% | 3.3% | -0.4% |
| `HorizontalBlur` | 3.5% | 3.1% | -0.4% |
| `apu_step` | 3.0% | 3.2% | +0.2% |
| `GenerateWaveform` | 2.4% | 2.4% | same |

---

## 2. FPS Improvement

| Metric | Before | After | Delta |
|--------|--------|-------|-------|
| **FPS** | 60.59 | **63.72** | **+5.2%** |
| Inline success | 1335 | 1337 | +2 |

The +5.2% FPS improvement is significant. Two factors:
1. **L1I cache savings**: ~2-3 KB of duplicated SIMD machine code eliminated
2. **JIT optimization**: single method body allows better register allocation across all callers

---

## 3. Subsystem Breakdown

| Subsystem | Before | After |
|-----------|--------|-------|
| CRT Pipeline | 45.3% | 45.2% |
| PPU Core | 21.3% | 20.8% |
| NTSC Analog | 9.6% | 9.9% |
| CPU Core | 8.8% | 8.8% |
| APU Core | 3.0% | 3.2% |
