# AprNes JIT / CPU Profiling Report (Full Pipeline: RF Ultra + CRT + 4x + Audio DSP 2)

- **Date**: 2026-04-12 23:47:00
- **Branch**: `master`
- **Build**: Debug x64, .NET Framework 4.8.1
- **ROM**: ny2011.nes (Mapper 0)
- **Duration**: 30s benchmark, 60137 CPU samples (2 cores utilized)
- **Config**: NTSC, Audio Mode 2 (Modern Stereo), Ultra Analog, RF Output, CRT, 4x resolution
- **Benchmark FPS**: ~60.59 (barely real-time at 60.10 target)

---

## 1. CPU Exclusive Top 15 — Full Pipeline

| Excl% | Samples | Method | Category |
|-------|---------|--------|----------|
| **21.1%** | 12679 | `Crt_Render` (lambda) | **CRT** |
| **20.7%** | 12478 | `ApplyFullFrameCurvatureAndConvergence` (lambda) | **CRT** |
| **17.4%** | 10471 | `ppu_step_new` | **PPU Core** |
| 8.2% | 4931 | `run` | **CPU Core** |
| **7.2%** | 4338 | `DemodulateRow` | **NTSC Decode** |
| 3.7% | 2218 | `PpuPhase4_SpriteEvalAndInit` | **PPU Core** |
| **3.5%** | 2088 | `ApplyHorizontalBlur` (lambda) | **CRT** |
| 3.0% | 1817 | `apu_step` | **APU Core** |
| **2.4%** | 1419 | `GenerateWaveform` | **NTSC Encode** |
| 0.4% | 237 | `CpuRead` | CPU Core |
| 0.4% | 235 | `ppu_r_2002` | PPU Core |
| 0.2% | 121 | `DecodeScanline` | NTSC Decode |
| 0.2% | 108 | `DoBranch` | CPU Core |

---

## 2. Cost Breakdown by Subsystem

| Subsystem | Excl% Total | Methods |
|-----------|-------------|---------|
| **CRT Pipeline** | **45.3%** | Crt_Render (21.1%) + Curvature (20.7%) + HBlur (3.5%) |
| **PPU Core** | **21.3%** | ppu_step_new (17.4%) + PpuPhase4 (3.7%) + ppu_r_2002 (0.4%) |
| **NTSC Analog** | **9.6%** | DemodulateRow (7.2%) + GenerateWaveform (2.4%) |
| **CPU Core** | **8.8%** | run (8.2%) + CpuRead (0.4%) + DoBranch (0.2%) |
| **APU Core** | **3.0%** | apu_step |
| **Other** | **~2%** | Mapper, IO, misc |

---

## 3. Key Observations

### CRT is the dominant bottleneck (45.3%)
- `Crt_Render` and `ApplyFullFrameCurvatureAndConvergence` together take **41.8%** — these are the scanline bloom/phosphor and barrel distortion passes
- Both are parallelized via `Parallel.For` lambdas but still dominate at 4x resolution (1024x960)
- At 8x (2048x1920) these would be ~4x worse → well below 60 FPS

### NTSC encode/decode is significant (9.6%)
- `DemodulateRow` (FIR demodulation) at 7.2% is the YIQ→RGB decode per scanline
- `GenerateWaveform` (21.477 MHz waveform) at 2.4% is the composite encode
- Both scale linearly with resolution

### Emulator core is well-optimized (33.1%)
- PPU + CPU + APU core combined = 33.1% of full pipeline
- In baseline mode these were ~91% — now compressed to 1/3 by DSP overhead
- All previous optimizations (SR latch bitwise, SWAR, inline, APU catchup) contribute here

### Audio DSP (Mode 2) is lightweight
- Not appearing in top 15 — the AudioPlus per-cycle push + oversampling/filtering is negligible compared to video DSP

---

## 4. JIT Stats

| Metric | Baseline | Full Pipeline |
|--------|----------|---------------|
| JIT methods | 208 | ~320+ (CRT/NTSC lambdas) |
| Inline success | 1096 | **1335** |
| Inline failures | 0 | 0 |

More inlines due to CRT/NTSC helper methods being aggressively inlined into the parallel lambdas.

---

## 5. Optimization Priorities for Full Pipeline

| Priority | Target | Current% | Potential |
|----------|--------|----------|-----------|
| **1** | CRT Curvature+Bloom | 45.3% | **GPU acceleration (shader)** |
| **2** | NTSC DemodulateRow | 7.2% | SIMD vectorization (.NET 10) |
| **3** | NTSC GenerateWaveform | 2.4% | LUT-based encode |
| 4 | PPU ppu_step_new | 17.4% | Near limit on CPU |
| 5 | CRT HorizontalBlur | 3.5% | GPU or SIMD |

**Conclusion**: For the full analog+CRT pipeline, the bottleneck is overwhelmingly in the **video post-processing** (55% combined CRT+NTSC), not the emulator core. GPU acceleration for CRT/NTSC is the clear path to 60+ FPS at high resolutions.
