# AprNesAvalonia Release — JIT Profile

- **Date**: 2026-04-14 13:51
- **Branch**: `master` @ 54879f1
- **Target**: `AprNesAvalonia` (Avalonia 11 + .NET 10)
- **Build**: Release x64, TieredPGO=ON
- **Config**: NTSC, Audio Mode 2, Ultra Analog RF, CRT, 4x resolution, ROM=ny2011
- **Warm-up FPS**: 77.25 (2318 frames / 30.01s)
- **Profile FPS**: 74.67 (2240 frames / 30.00s, with ETW overhead)
- **CPU time sampled**: 62,159 ms / 62,159 samples

---

## Top Methods (Exclusive)

| Excl% | Method |
|-------|--------|
| 24.2% | `Crt_Render` lambda (parallel worker) |
| 16.6% | `Parallel.ForWorker` inner lambda (TPL overhead) |
| 16.6% | `ppu_step_new` |
| 8.5%  | `DemodulateRow_Core` |
| **6.0%** | **`Run_NTSC`** |
| 3.3%  | `PpuPhase4_SpriteEvalAndInit` |
| 2.7%  | `DecodeScanline` |
| 2.6%  | `ApplyHorizontalBlur` lambda |
| 2.3%  | `apu_step` |
| 1.2%  | `ApplyFullFrameCurvatureAndConvergence` lambda |
| 0.3%  | `DoBranch` |
| 0.2%  | `NestedTick7_NTSC` |

## Top Methods (Inclusive)

| Incl% | Method |
|-------|--------|
| 47.8% | `Run_NTSC` / `run` (emulation core) |
| 47.4% | `Parallel.ForWorker` (CRT pipeline) |
| 37.5% | `ppu_step_new` |
| 24.4% | `Crt_Render` lambda |
| 11.6% | `DecodeScanline` |
| 8.6%  | `DemodulateRow_Core` |
| 4.8%  | `RenderScreen` |
| 3.7%  | `PpuPhase4_SpriteEvalAndInit` |

---

## Comparison: Debug WinForms vs Release Avalonia

| Metric | AprNes Debug (7533ebd) | **AprNesAvalonia Release** |
|--------|------------------------|----------------------------|
| FPS (3-run warm) | 63.23 | **77.25** (+22%) |
| Run_NTSC Excl% | 9.1% | **6.0%** |
| ppu_step_new Excl% | 18.1% | 16.6% |
| Crt_Render Excl% | 21.6% | 24.2% |

Release + TieredPGO brings expected JIT wins on the emulation core
(`Run_NTSC` 9.1% → 6.0%). CRT pipeline relative weight rises because
the emulation core shrank faster than the rendering lambdas.

---

## Observations

1. **CRT rendering is now dominant** (~45% incl when you add Crt_Render
   + DemodulateRow + Blur + Curvature). Further FPS gains likely need
   CRT-side optimization (SIMD, lower resolution path, or skipping
   some passes).
2. **TPL overhead visible** — `Parallel.ForWorker` inner lambda is
   16.6% exclusive. This is the cost of parallelizing per-scanline
   CRT passes; worth investigating whether fewer, coarser-grained
   parallel regions would reduce scheduling overhead.
3. **NestedTick7_NTSC at 0.2%** — de-recursion + structural unroll
   work is doing its job; hot-path dispatch is no longer a bottleneck.
4. **NesCore emulation core shrunk** to ~30% of CPU; CRT/filters now
   drive FPS.

---

## Next Steps (if pursuing more FPS)

- Investigate `Parallel.ForWorker` granularity — current per-scanline
  parallelism may be too fine
- SIMD-ize `DemodulateRow_Core` and `ApplyHorizontalBlur`
- Check if CRT pipeline can run at lower internal resolution
- Profile `Crt_Render` lambda in isolation to find hot inner loops
