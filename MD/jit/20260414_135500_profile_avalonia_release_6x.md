# AprNesAvalonia Release — JIT Profile (6x Analog)

- **Date**: 2026-04-14 13:55
- **Target**: `AprNesAvalonia` (Avalonia 11 + .NET 10)
- **Build**: Release x64, TieredPGO=ON
- **Config**: NTSC, Audio Mode 2, Ultra Analog RF, CRT, **6x resolution**, ROM=ny2011
- **Warm-up FPS**: 64.10 (1924 frames / 30.02s)
- **Profile FPS**: 64.18 (1926 frames / 30.01s)
- **CPU samples**: 116,841

---

## Top Methods (Exclusive)

| Excl% | Method |
|-------|--------|
| **38.8%** | `Crt_Render` lambda |
| **31.8%** | `Parallel.ForWorker` inner lambda (TPL) |
| 7.2%  | `ppu_step_new` |
| 3.5%  | `DemodulateRow_Core` |
| 2.8%  | `Run_NTSC` |
| 1.5%  | `PpuPhase4_SpriteEvalAndInit` |
| 1.3%  | `ApplyHorizontalBlur` lambda |
| 1.3%  | `DecodeScanline` |
| 1.1%  | `apu_step` |
| 1.1%  | `ApplyFullFrameCurvatureAndConvergence` lambda |
| 0.2%  | `DoBranch` |
| 0.1%  | `NestedTick7_NTSC` |

---

## 4x vs 6x Comparison

| Metric | 4x | **6x** | Δ |
|--------|-----|--------|---|
| FPS (warm) | 77.25 | **64.10** | −17% |
| Crt_Render Excl% | 24.2% | **38.8%** | +14.6pp |
| Parallel.ForWorker Excl% | 16.6% | **31.8%** | +15.2pp |
| ppu_step_new Excl% | 16.6% | 7.2% | −9.4pp |
| Run_NTSC Excl% | 6.0% | 2.8% | −3.2pp |
| DemodulateRow Excl% | 8.5% | 3.5% | −5.0pp |

6x 把 CRT 管線負擔幾乎翻倍（24.2→38.8%），Parallel.ForWorker 內層工作量
也近乎 double。Emulation core（ppu_step_new + Run_NTSC）相對比例反而下降
——FPS 掉 17% 幾乎全部來自 CRT 像素數 (6²/4² = 2.25 倍像素)。

---

## 結論

**CRT pipeline 瓶頸更明顯**：6x 下 Crt_Render + Parallel + Demodulate +
Blur + Curvature ≈ **77.5% CPU**，emulation core 跌到 ~12%。

想撐 60+ FPS 在 6x，優化焦點必須在 CRT 管線：
- `Crt_Render` 內層 inner loop（掃描線級 per-pixel 計算）SIMD 化
- 降低 Parallel.For 呼叫頻率 / 合併 pass
- 評估是否能在內部維持較低解析度，最後才 upscale 到 6x
