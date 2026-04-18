# AprNesAvalonia CRT Dispatch — Phase 3A GUI Baseline — 2026-04-18

**測試目的**：Phase 3A（render-thread D3D11 真 GPU）下三條 CRT backend 的 GUI 模式實效 FPS。對比 Phase 0/1/2 的 headless raster 基線，驗證把 SkSL shader 搬到 Avalonia `ISkiaSharpApiLeaseFeature` 能否解除 raster Pipeline 瓶頸。

---

## 測試條件

| 項目 | 設定 |
|------|------|
| 專案 | AprNesAvalonia (Avalonia 11.3.13 / .NET 10) |
| 組態 | Release (TieredPGO ON) |
| CPU | AMD Ryzen 7 3700X 8-Core |
| GPU | Windows D3D11 backend (via Avalonia Skia lease) |
| SkiaSharp | 3.119.3-preview.1.1 |
| AnalogMode | ON + UltraAnalog (RF) |
| CRT | ON |
| Audio DSP | Mode 2 (Modern) |
| 測試 ROM | ny2011.nes |
| 測試時長 | 20s / 回合，cooldown 10s |
| 測試協議 | 3-run（JIT warmup discard + avg of Run 2 & 3） |
| Headless? | **否** — 用 GUI + `--gui-benchmark` 量測 |
| 切換機制 | runtime `--crt-strategy <scalar\|simd\|gpu>` |

**兩個 FPS 的意義**：
- **Emu FPS**（produced）：emulator 核心每秒產出幾幀（`OnFrameReady` 計數）
- **Presented FPS**：render thread 每秒畫到螢幕幾幀（`EmuDrawOperation.Render` 計數）
- 兩者差異 = vsync + render-thread 丟幀

---

## 8x 結果（2048 × 1680 = 3.44M pixels）

| Backend | Presented | Emu |
|:-------:|:---------:|:---:|
| scalar | 54.60 | 64.75 |
| simd | 58.43 | 71.84 |
| **gpu** | **59.24** | **121.42** |

**觀察**：Presented 三個都接近 vsync 60 FPS → 視覺流暢度看起來一樣。真正差異在 **emu**：GPU 把 CRT 卸到 render thread，emu thread 省下整整 50 FPS 的預算（~1.69× SIMD）。

## 10x 結果（2560 × 2100 = 5.38M pixels）

| Backend | Presented | Emu | GPU 比率 |
|:-------:|:---------:|:---:|:--------:|
| scalar | 29.49 | 45.22 | 0.53× / 0.45× |
| simd | 30.24 | 50.28 | 0.55× / 0.50× |
| **gpu** | **55.14** | **100.93** | — |

**GPU / SIMD speedup：Presented 1.82×，Emu 2.00×**

**關鍵**：10x 逼出 CPU 天花板：
- scalar/simd 都掉到 ~30 FPS presented — 不再 vsync-cap，完全 CPU bound
- SIMD 比 scalar 只快 11% emu / 3% presented（兩者都餵不飽 vsync）
- GPU 55 FPS presented 仍接近 60 — 超越 SIMD 近兩倍

---

## Scaling 分析

| Backend | 8x→10x Emu | 8x→10x Pres | 像素比 |
|:-------:|:----------:|:-----------:|:------:|
| scalar | 64 → 45 | 54 → 29 | 1.56× |
| simd | 72 → 50 | 58 → 30 | 1.56× |
| **gpu** | **121 → 101** | **59 → 55** | **1.56×** |

- CPU paths 在 10x 幾乎崩盤（presented 砍半）
- **GPU emu 僅降 17%**（121→101）— CRT 卸載後，emu 只剩 NTSC + DSP，成本不隨解析度縮放
- **GPU presented 降 7%**（59→55）— 仍接近 vsync，只在極高解析度開始看到 shader fillrate cost

---

## 為什麼 4x/8x 時看不出 GPU 優勢？

- 4x / 8x CPU 路徑也能達 vsync ceiling（~59 FPS）
- Presented 都 cap 在 60 → 肉眼看起來三個一樣流暢
- **但 GPU 有 40% CPU 餘裕還沒用上**
- 10x 把這個餘裕用光 → CPU 路徑崩了，GPU 仍站得住

## 為什麼 10x 下 GPU presented 只有 55 不是 60？

三個可能 bottleneck：
1. **Shader fillrate**：5.4M pixels × ~30 shader ops × 2 passes（main + phosphor writeback） ≈ **324M ops/frame**
2. **Phosphor GPU writeback**：每幀兩次完整 shader 執行（main + prev surface）
3. **Emu→Render sync overhead**：emu 產 100 FPS 但 render 只 sample 55，中間有幀被丟

Phosphor writeback 是最可優化項（用 snapshot-copy 取代 re-render）。

---

## 跨 Phase 對照

| Metric | Phase 0 (MSBuild switch) | Phase 1+2 (raster, headless) | **Phase 3A (D3D11, GUI)** |
|:------:|:-----------------------:|:-----------------------------:|:-------------------------:|
| 架構 | build-time | runtime dispatch, raster GPU | runtime dispatch, real GPU |
| GPU backend @ 8x | — | 1.40 FPS | **59.24 FPS (42× improvement)** |
| GPU backend @ 4x | — | 5.36 FPS | ~60 FPS (vsync) |
| GPU backend @ 10x | — | (not tested, probably ~1 FPS) | **55.14 FPS** |

Phase 3A 是整個 GPU 計畫的轉捩點 — 真正用到 D3D11 shader。

---

## 高解析度預測

基於 10x 結果外推：

| 條件 | Resolution | Pixels | Predicted GPU Presented | Predicted GPU / SIMD |
|------|:----------:|:------:|:-----------------------:|:--------------------:|
| 4x | 1024×840 | 860K | ~60 (vsync) | 1.0× vsync, ~1.7× emu |
| 8x | 2048×1680 | 3.44M | ~59 (vsync) | 1.0× vsync, ~1.7× emu |
| **10x** | **2560×2100** | **5.38M** | **55** | **1.82× / 2.00×** |
| 12x | 3072×2520 | 7.74M | ~40 | ~2.0× / 2.2× |
| 4K 填高 | 2633×2160 | 5.69M | ~52 | ~1.9× / 2.0× |
| 4K 整數 10x | 2560×2100 | 5.38M | 55（同上） | 1.82× / 2.00× |

高解析度下 GPU 是**唯一能跑的 backend**。

---

## 結論

1. **Phase 3A 成功**：真 GPU 加速到位。
2. **GPU 是高解析度唯一解**：10x+ CPU 路徑崩壞，GPU 仍流暢。
3. **即使 vsync-cap 時 GPU 也有意義**：emu thread 省下 40-50% CPU → 更重的 NTSC/Audio DSP/mapper 都能跑。
4. **下一步優化方向**：
   - **Phosphor writeback 改 snapshot-copy**（最直接，省一次 shader pass）
   - 更複雜 shader / NTSC 也搬 GPU（原 §15 Phase 2 擴展）
   - 12x+ 解析度 + HDR（如螢幕支援）

---

## 相關文件

- [CRT_GPU_Design.md](CRT_GPU_Design.md) — 架構設計（Phase 0-3）
- [CRT_Dispatch_Baseline_4x_2026-04-18.md](CRT_Dispatch_Baseline_4x_2026-04-18.md) — Phase 1+2 raster 4x
- [CRT_Dispatch_Baseline_8x_2026-04-18.md](CRT_Dispatch_Baseline_8x_2026-04-18.md) — Phase 1+2 raster 8x
