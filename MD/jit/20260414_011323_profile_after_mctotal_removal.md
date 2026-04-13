# AprNes JIT Profile — After masterClockTotal Removal

- **Date**: 2026-04-14 01:13:23
- **Branch**: `master` @ 7533ebd
- **Build**: Debug x64, .NET Framework 4.8.1
- **Config**: NTSC, Audio Mode 2, Ultra Analog RF, CRT, 4x resolution
- **FPS (3-run avg)**: **63.23** (63.56 / 62.68 / 63.45)

---

## 1. FPS Delta

| Build | 3-run avg | Run_NTSC Excl% |
|-------|-----------|----------------|
| 726b072 (warm, pre-removal) | 62.59 | 9.7% |
| **7533ebd (this, post-removal)** | **63.23** | **9.1%** |
| Delta | **+0.64 FPS (+1.0%)** | **−0.6%** |

Dead-code removal (`masterClockTotal++` × 5 sites in fast path, ~21.5M
inc/sec) directly recovers 0.6% of CPU budget, producing the expected
~1% FPS gain.

---

## 2. Top Methods (58876 samples)

| Excl% | Method |
|-------|--------|
| 21.6% | `Crt_Render` lambda |
| 19.3% | `Curvature+Convergence` lambda |
| 18.1% | `ppu_step_new` |
| **9.1%** | **`Run_NTSC`** |
| 7.2% | `DemodulateRow_Core` |
| 3.4% | `PpuPhase4_SpriteEvalAndInit` |
| 3.4% | `ApplyHorizontalBlur` lambda |
| 3.0% | `apu_step` |
| 2.6% | `GenerateWaveform` |

Hot ordering unchanged. CRT pipeline still dominant.

---

## 3. FPS Trajectory (all on master)

| Commit | Build state | FPS | Main loop Excl% |
|--------|-------------|-----|-----------------|
| 83adc81 | master baseline (pre-refactor) | 53-55 | run() 10.7% |
| 2780287 | static dispatch merged | ~56 | Run_NTSC 9.7% |
| 47f7876 | + FDS fixes | ~57 | Run_NTSC 9.7% |
| b60c023 | + palCache fix | ~62 | Run_NTSC 9.7% |
| **7533ebd** | **+ masterClockTotal removed** | **63.23** | **Run_NTSC 9.1%** |

Cumulative gain over pre-refactor master: ~+15-20% FPS (depending on
how much of the apparent delta is session-state drift).
