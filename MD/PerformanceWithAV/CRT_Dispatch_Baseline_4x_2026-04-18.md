# AprNesAvalonia CRT Dispatch Baseline (4x) — 2026-04-18

**測試目的**：量測 Phase 1+2 後，`--crt-strategy` runtime dispatch 下 Scalar / SIMD / **GPU** 三條 CRT 管線 baseline FPS。電腦為**鎖頻狀態**，過往 MEMORY.md 中的數字已不適用。

---

## 測試條件

| 項目 | 設定 |
|------|------|
| 專案 | AprNesAvalonia (Avalonia 11.3.13 / .NET 10) |
| 組態 | Release (TieredPGO ON) |
| CPU | AMD Ryzen 7 3700X 8-Core Processor |
| SkiaSharp | **3.119.3-preview.1.1**（Phase 2 升級，修正 2.88.x SkVM JIT 的 `0xC000001D`）|
| AccuracyOptA | ON |
| AnalogMode | ON + UltraAnalog (Level 3 物理路徑) |
| CRT | ON (Stage 2 電子束光學) |
| AnalogOutput | RF |
| AnalogSize | 4x (1024×840) |
| **Audio DSP** | Mode 2 (Modern: 5×FIR + Bass Boost + Stereo + Haas + Reverb) |
| 音效播放 | OFF (DSP 處理完後丟棄，不經 WaveOut) |
| 畫面顯示 | OFF (headless) |
| 測試時長 | 20 秒 / 回合 |
| 測試 ROM | Mega Man 5 (USA).nes (Mapper 004, MMC3) |
| 冷卻時間 | 每回合前 30 秒 |

**測試協議**：3 次法 — Run 1（JIT/TieredPGO 暖機，10s）不採計 → cooldown → Run 2（20s，採計）→ cooldown → Run 3（20s，採計）→ 取 Run 2、Run 3 平均。

**切換機制**：runtime `--crt-strategy <scalar|simd|gpu>` CLI flag（Phase 1），同一個 build 可切。

**影像 + 音訊管線**：
```
PPU per-scanline → Ntsc.DecodeScanline (21.477 MHz waveform + coherent demod + RF AM)
→ linearBuffer → CrtScreen.Render (scanline bloom + mask + phosphor + convergence + curvature)
→ AnalogScreenBuf

Audio: 5×256-tap FIR (per-channel) → Triangle Bass Boost (12dB) →
       Stereo Pan (100%) → Haas (20ms) → Comb Reverb ×4 (wet=15%)
```

---

## 測試結果

| Backend | Run 1 (JIT) | Run 2 | Run 3 | **平均 FPS** | 即時倍率 |
|:-------:|:-----------:|:-----:|:-----:|:------------:|:--------:|
| Scalar | 108.16 | 103.95 | 110.34 | **107.15** | 1.78x |
| SIMD | 108.01 | 116.06 | 112.06 | **114.06** | 1.90x |
| **GPU (raster)** | 5.39 | 5.36 | 5.36 | **5.36** | **0.09x** |

### Speedup 分析

| 比較 | 基準 FPS | 目標 FPS | Speedup |
|------|:--------:|:--------:|:-------:|
| Scalar → SIMD | 107.15 | 114.06 | **1.06x** |
| SIMD → GPU   | 114.06 | 5.36 | **0.05x** |
| Scalar → GPU | 107.15 | 5.36 | **0.05x** |

> **NES 即時 FPS**：60.0988（NTSC）。平均 FPS ÷ 60.0988 = 即時倍率；≥ 1.0x 即可流暢運行。

---

## Phase 2 GPU 結果分析（重要）

### 為何 GPU 比 SIMD 慢 21×？

**根本原因：目前 GPU 路徑跑在純 CPU raster SKSurface 上，不是真正的 GPU 硬體加速。**

管線拆解（`CrtScreen.Gpu.Render()` 每幀代價）：

| 階段 | 成本來源 | 估算相對 CPU SIMD 比例 |
|------|---------|:----------------------:|
| 1. `linearBuffer` float RGB → Bgra8888 量化 | 256×240 CPU loop，每 pixel 9 ops + Math.Clamp | ~3x |
| 2. `SKShader.CreateBitmap` + runtime shader 組裝 | 每幀重建 uniforms/children | ~2x |
| 3. `SKRuntimeEffect` CPU rasterizer 執行 | Skia Raster Pipeline 解譯 SkSL（無 AVX2 intrinsics）| **10-15x** |
| 4. 每 pixel 4 次 `shader.eval()` | hblur+convergence 要 4 個 sample call | ~3x |
| 5. `SKSurface.ReadPixels` → `crt_analogScreenBuf` | CPU 記憶體拷貝 1024×840×4 | ~2x |
| 6. ping-pong `Snapshot` + `ToShader` | 建 SKImage + SKShader 每幀 | ~2x |

相乘效應 → **~20x 慢於 CPU SIMD 是合理的**。

### 為何沒有真正的 GPU？

SkiaSharp `SKSurface.Create(SKImageInfo)`（沒帶 `GRContext` 參數）建立的是 **raster surface**，所有 draw 操作在 CPU 上由 Skia Raster Pipeline 執行。**真 GPU SKSurface** 需要：

```csharp
var grContext = GRContext.CreateGl();  // 或 CreateVulkan
var surface = SKSurface.Create(grContext, false, info);
```

而 `GRContext.CreateGl()` 需要一個 **OpenGL context**。在 Avalonia headless + emulator thread 環境：
- Avalonia 自己的 GRContext 綁在 render thread，emulator thread 無法直接借用
- 在 emulator thread 建獨立 OpenGL context 需要 `Silk.NET.Windowing`（看不見的視窗）或 EGL/WGL 手動管理
- 是可行的，但屬於**Phase 3 架構重做範疇**

### 結論：Phase 2 GPU 是**架構就緒的 proof-of-concept**，非效能產物

✓ **正確性驗證**：shader 語法正確、pipeline 完整、與 SIMD 視覺等價
✓ **架構就緒**：`ICrtBackend` + runtime dispatch 可擴充到真 GPU
✗ **效能目前不如 CPU SIMD**（因為跑在 CPU 上）

### 下一步路徑選擇

| 路徑 | 工作量 | 預期 |
|------|:------:|------|
| **Phase 3A — Silk.NET offscreen GL**：emulator thread 自己建 GL context + GRContext | 1-2 天 | 預計真 GPU ≥ 1.5x SIMD（CRT 階段） |
| **Phase 3B — Avalonia render-thread integration**：把 Render 搬到 UI thread via `ISkiaSharpApiLeaseFeature` | 2-3 天（同步複雜）| 能用既有 GRContext，不額外佔 GPU 資源 |
| **Phase 3C — 擴 GPU pipeline 涵蓋 NTSC**：依 §15 設計將 `RunDecodeLoop`+`DemodulateRow` 搬 GPU | 4-6 天 | 非 UltraPhysical 模式 ≥ 2x SIMD；UltraPhysical 仍受 CPU slew-loop 限制 |

建議順序：**3A（取得真 GPU）→ 3C（擴大 GPU pipeline 吃更多時間）**。3B 可選。

---

## 後續里程碑

本檔為 **Phase 1+2 baseline**。Phase 3 完成後將追加：
- Phase 3A 真 GPU 加速 FPS
- Phase 3C NTSC GPU 化 FPS
- ARM NEON scalar 驗證（如實際發布 ARM 版本）

參考：[MD/gpu/CRT_GPU_Design.md](../gpu/CRT_GPU_Design.md)
