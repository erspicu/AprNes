# AprNesAvalonia Release — JIT Profile (8x Analog)

- **Date**: 2026-04-14 16:47
- **Target**: `AprNesAvalonia` (Avalonia 11 + .NET 10)
- **Build**: Release x64, TieredPGO=ON
- **Config**: NTSC, Audio Mode 2, Ultra Analog RF, CRT, **8x resolution**, ROM=ny2011
- **Warm-up FPS**: 57.27 (1719 frames / 30.01s)
- **Profile FPS**: 55.26 (1658 frames / 30.00s)
- **CPU samples**: 154,151

---

## Top Methods (Exclusive)

| Excl% | Method |
|-------|--------|
| **44.4%** | `Parallel.ForWorker` inner lambda (TPL) |
| **30.1%** | `Crt_Render` lambda |
| 4.8%  | `ppu_step_new` |
| 2.2%  | `DemodulateRow_Core` |
| 1.8%  | `Run_NTSC` |
| 1.0%  | `PpuPhase4_SpriteEvalAndInit` |
| 1.0%  | `ApplyFullFrameCurvatureAndConvergence` lambda |
| 0.8%  | `DecodeScanline` |
| 0.8%  | `ApplyHorizontalBlur` lambda |
| 0.7%  | `apu_step` |
| 0.1%  | `NestedTick7_NTSC` |

---

## 4x → 6x → 8x Scaling

| Metric | 4x | 6x | **8x** |
|--------|-----|-----|--------|
| FPS (warm) | 77.25 | 64.10 | **57.27** |
| FPS (profile) | 74.67 | 64.18 | 55.26 |
| Pixels/frame (rel) | 1.0x | 2.25x | 4.0x |
| Crt_Render Excl% | 24.2% | 38.8% | 30.1% |
| Parallel.ForWorker Excl% | 16.6% | 31.8% | **44.4%** |
| ppu_step_new Excl% | 16.6% | 7.2% | 4.8% |
| Run_NTSC Excl% | 6.0% | 2.8% | 1.8% |
| CRT pipeline total | ~52% | ~77% | **~80%** |

---

## 觀察

1. **8x FPS 沒有按像素比例線性下降** — 4→8x 是 4 倍像素量，FPS 只掉
   26%（77.25→57.27）。代表 CRT 管線在高解析度下**每像素成本有下降**
   （可能是記憶體頻寬/cache 成瓶頸而非純計算）。
2. **Parallel.ForWorker 反超 Crt_Render** — 在 8x 下 TPL 調度 lambda
   自身占比 44.4% 大於 Crt_Render 的 30.1%。這**不是**單純的調度開銷，
   而是 `Parallel.For` 的 RangeWorker 把部分內層工作 inline 到自己的
   lambda 裡（JIT 可能把某些 call 合併）。合起來 CRT 管線仍是 ~74%。
3. **Emulation core 剩 ~8%** — Run_NTSC + ppu_step_new + apu_step 總和
   7.3%，已經不是瓶頸。
4. **TPL 調度可能有實質 overhead** — Parallel.ForWorker 比例逐級上升
   （16.6 → 31.8 → 44.4），建議檢查 per-scanline 分工是否過細；改為
   per-N-scanlines 或 block-based 可能降低調度開銷。

---

## 建議方向（按投報比）

1. **CRT inner loop SIMD 化** — `Crt_Render` 與 `DemodulateRow_Core`
   都是 per-pixel float 運算，適合 `Vector<float>` 或 `Vector256`
2. **調整 Parallel 顆粒度** — 從 per-scanline 改成 per-N-scanlines
   （例如 8 或 16 一批），降低 ForWorker inline 開銷
3. **內部 render 降到 4x，輸出再 upscale** — 若視覺差異可接受，可以
   直接省 2-4 倍像素成本
