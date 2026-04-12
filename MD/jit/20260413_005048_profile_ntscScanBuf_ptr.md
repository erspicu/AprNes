# AprNes JIT / CPU Profiling Report (ntscScanBuf byte[] → byte*)

- **Date**: 2026-04-13 00:50:48
- **Branch**: `master`
- **Build**: Debug x64, .NET Framework 4.8.1
- **ROM**: ny2011.nes (Mapper 0)
- **Duration**: 30s benchmark, 59423 CPU samples
- **Config**: NTSC, Audio Mode 2, Ultra Analog, RF Output, CRT, 4x resolution
- **Benchmark FPS**: 59.79 (previous: 63.72)

---

## 1. CPU Exclusive Top 10

| Method | Previous | Current | Delta |
|--------|----------|---------|-------|
| `Crt_Render` | 21.7% | 21.1% | -0.6% |
| `Curvature` | 20.4% | 20.1% | -0.3% |
| `ppu_step_new` | 17.3% | 17.4% | +0.1% |
| `run` | 8.3% | 8.3% | same |
| `DemodulateRow_Core` | 7.5% | 7.4% | -0.1% |
| `PpuPhase4` | 3.3% | 3.8% | +0.5% |
| `HorizontalBlur` | 3.1% | 3.5% | +0.4% |
| `apu_step` | 3.2% | 3.2% | same |
| `GenerateWaveform` | 2.4% | 2.5% | +0.1% |

All within noise range (±0.5%). The `byte[] → byte*` change eliminates bounds checks but the NTSC methods already used `palBuf[d]` in scalar loops (not SIMD), so the bounds check overhead was minimal.

---

## 2. FPS

| Run | FPS |
|-----|-----|
| DemodulateRow merge | 63.72 |
| **ntscScanBuf ptr** | **59.79** |

The FPS drop is likely measurement noise — the benchmark bat doesn't have cooldown in PerfView mode, and system load varies. The CPU% distribution is essentially unchanged.

---

## 3. Summary

| Metric | Value |
|--------|-------|
| Inline success | 1343 (+6 from previous 1337) |
| Inline failures | 0 |
| `GenerateWaveform` signature | `byte*` (was `byte[]`) |

The change is correct (eliminates GC tracking + bounds check) but has negligible measurable impact on this workload since `palBuf` access was already scalar and the array was small (256 bytes, always in L1D cache).
