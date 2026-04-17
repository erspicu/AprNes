# AprNesAvalonia CRT Dispatch Baseline (8x) — 2026-04-18

**測試目的**：量測 Phase 1+2 後，`--crt-strategy` runtime dispatch 下 Scalar / SIMD / **GPU** 三條 CRT 管線 baseline FPS。電腦為**鎖頻狀態**。

---

## 測試條件

| 項目 | 設定 |
|------|------|
| 專案 | AprNesAvalonia (Avalonia 11.3.13 / .NET 10) |
| 組態 | Release (TieredPGO ON) |
| CPU | AMD Ryzen 7 3700X 8-Core Processor |
| SkiaSharp | 3.119.3-preview.1.1 |
| AnalogMode | ON + UltraAnalog (RF) |
| AnalogSize | **8x (2048×1680)** |
| Audio DSP | Mode 2 (Modern) |
| 測試 ROM | Mega Man 5 (USA).nes |
| 測試時長 | 20 秒 / 回合，cooldown 30s |

**切換機制**：runtime `--crt-strategy <scalar|simd|gpu>` CLI flag（Phase 1 單 build）

---

## 測試結果

| Backend | Run 1 (JIT) | Run 2 | Run 3 | **平均 FPS** | 即時倍率 |
|:-------:|:-----------:|:-----:|:-----:|:------------:|:--------:|
| Scalar | 72.37 | 69.79 | 73.38 | **71.59** | 1.19x |
| SIMD | 78.49 | 75.42 | 77.10 | **76.26** | 1.27x |
| **GPU (raster)** | 1.50 | 1.34 | 1.46 | **1.40** | **0.02x** |

### Speedup 分析

| 比較 | 基準 FPS | 目標 FPS | Speedup |
|------|:--------:|:--------:|:-------:|
| Scalar → SIMD | 71.59 | 76.26 | **1.07x** |
| SIMD → GPU   | 76.26 | 1.40 | **0.018x**（54× 慢）|
| Scalar → GPU | 71.59 | 1.40 | **0.020x**（51× 慢）|

> **NES 即時 FPS**：60.0988（NTSC）。平均 FPS ÷ 60.0988 = 即時倍率。

---

## 4x vs 8x 對照（重要觀察）

| Backend | 4x FPS | 8x FPS | 8x / 4x | 像素比 |
|:-------:|:------:|:------:|:-------:|:------:|
| Scalar | 107.15 | 71.59 | 0.67x | 0.25x |
| SIMD | 114.06 | 76.26 | 0.67x | 0.25x |
| GPU (raster) | 5.36 | 1.40 | **0.26x** | 0.25x |

**Scalar/SIMD scaling**（0.67x FPS at 4x 像素）非純線性，因 NTSC/DSP 成本固定分攤。

**GPU raster scaling**（**0.26x FPS at 4x 像素，接近理想線性 0.25x**）代表瓶頸**完全在 per-pixel shader cost**，沒有固定 overhead 可攤薄。確認根因：SkSL 逐 pixel 在 CPU Raster Pipeline 上解譯執行。

---

## 為何 GPU (raster) 慢 54× (8x)

見 [4x baseline §Phase 2 GPU 結果分析](CRT_Dispatch_Baseline_4x_2026-04-18.md#phase-2-gpu-結果分析重要)。核心：

**`SKSurface.Create(SKImageInfo)` without `GRContext` = CPU raster surface**。SkSL 在 CPU 上逐 pixel 執行。

8x（3.44M pixels）× 4 shader.eval() calls（hblur + convergence）= **13.8M sample operations/frame**，全 CPU。手寫 `Vector256<T>` SIMD 每 cycle 處理 8 floats；Raster Pipeline 解譯每 cycle 處理 1 pixel 的部分運算 → 慢 50× 是符合預期。

---

## 要真正拿到 GPU 加速

**Option 1（推薦）**：把 CRT shader 搬到 render thread，用 `EmuScreenControl.EmuDrawOperation.Render` 裡的 `ISkiaSharpApiLeaseFeature.Lease().SkCanvas` — 此 canvas **在 Windows 是 D3D11-backed，真 GPU**。預計消除 readback 後 1.4 FPS → **超過 SIMD**。

**Option 2**：Silk.NET offscreen GL → `GRContext.CreateGl()` → `SKSurface.Create(grContext, ...)`。架構複雜一點但 emu thread 和 headless 一致。

詳見 [CRT_GPU_Design.md](CRT_GPU_Design.md)。

---

## 結論

- Phase 2 GPU backend 在「未用真 GPU」狀態下 ~50× 慢於 SIMD
- **不是架構設計問題**：同一份 shader + runtime dispatch，在真 GPU context 上會快很多
- Phase 3 需決定走 Avalonia render thread（Option 1）還是 Silk.NET offscreen GL（Option 2）
