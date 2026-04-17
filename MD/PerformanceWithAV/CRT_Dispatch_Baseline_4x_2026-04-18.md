# AprNesAvalonia CRT Dispatch Baseline (4x) — 2026-04-18

**測試目的**：量測 Phase 0 的 MSBuild `CrtImpl` 切換下，Scalar 與 SIMD 兩條 CRT 管線 baseline FPS。電腦目前為**鎖頻狀態**，過往 MEMORY.md 中的數字已不適用，本檔為新基準。

---

## 測試條件

| 項目 | 設定 |
|------|------|
| 專案 | AprNesAvalonia (Avalonia 11.3.13 / .NET 10) |
| 組態 | Release (TieredPGO ON) |
| CPU | AMD Ryzen 7 3700X 8-Core Processor |
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

**切換機制**：`dotnet build -p:CrtImpl=Scalar|Simd`（Phase 0 build-time 切換；Phase 1 將改為 runtime `--crt-strategy` CLI）。

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

| CrtImpl | Run 1 (JIT) | Run 2 | Run 3 | **平均 FPS** | 即時倍率 |
|:-------:|:-----------:|:-----:|:-----:|:------------:|:--------:|
| Scalar | 106.51 | 111.58 | 107.65 | **109.62** | 1.82x |
| Simd | 111.06 | 111.47 | 117.67 | **114.57** | 1.91x |

### Speedup 分析

| 比較 | Scalar FPS | SIMD FPS | Speedup |
|------|:----------:|:--------:|:-------:|
| Scalar → SIMD | 109.62 | 114.57 | **1.05x** |

> **NES 即時 FPS**：60.0988（NTSC）。平均 FPS ÷ 60.0988 = 即時倍率；≥ 1.0x 即可流暢運行。

---

## 觀察與分析（4x vs 8x 對照）

### 1. Speedup 在 4x 與 8x 相同（1.05x）

| Size | Scalar | SIMD | Speedup |
|:----:|:------:|:----:|:-------:|
| 4x (1024×840) | 109.62 | 114.57 | **1.05x** |
| 8x (2048×1680) | 73.31 | 76.66 | **1.05x** |

**原先假設**：8x 的 1.05x 是被記憶體頻寬擋住；降到 4x 應該能看到更大 SIMD 優勢。
**實測結果**：4x 的 speedup **完全相同**。假設不成立。

### 2. 推論：瓶頸不在 CRT 階段，也不在記憶體頻寬

頻寬試算：
- 8x 寫出 = 3.44M pixel × 76.66 FPS × 4B = **1.05 GB/s**
- 4x 寫出 = 860K pixel × 114.57 FPS × 4B = **394 MB/s**

DDR4/DDR5 動輒 20-40 GB/s，1 GB/s 連零頭都算不上。8x 單幀 13.8 MB 仍可進 L3（32MB），4x 單幀 3.4 MB 穩進 L2。**兩者都不是記憶體 bound**。

真正瓶頸：
- **NTSC 物理解調**（21.477 MHz 波形重建 + coherent demod + RF AM）— 純量浮點密集，`Ntsc.cs` 有 `Vector<T>` 但 SIMD 版沒對它做特化
- **5×256-tap FIR audio DSP**（mode 2 最重）— 同樣是 `Vector<T>` 等級優化
- **兩者在 Scalar / Simd 兩種 build 下實作相同**，所以 speedup 固定 1.05x ≈ CRT 佔總時間的占比

### 3. 修正後的 Phase 2 GPU 預期（阿姆達爾定律）

若只加速 CRT：
- 假設 GPU 把 CRT 做到零時間，**最多只省下 ~5% CPU**
- 實際 speedup = 1 / (1 − 0.05) = **1.05x**
- **完全不值得投入 Phase 2 4-6 天的工作量**

要有意義的加速，**必須把 NTSC 也搬到 GPU**（原 v3 stretch goal）：
- NTSC + CRT 合佔 ~40-60% 總時間（粗估）
- GPU 全吃下後，speedup = 1 / (1 − 0.5) = **2x** 才值得
- 需要把 `Ntsc.cs` 也納入 Phase 2 重構（不能等 v3）

### 4. 對 Phase 1 runtime dispatch 的影響
- Phase 1 refactor（scalar/simd runtime 切換）本身**不會帶來 FPS 改善**（兩者差 5%）
- 只作為架構準備：讓 Phase 2 GPU backend 能替換 CRT 部分
- **建議**：Phase 1 與 Phase 2 規劃合併 — 同步重構 `Ntsc.cs` 成 `INtscBackend` 抽象，才能把 NTSC 也接上 GPU

### 5. ARM 平台啟示
Scalar 只比 SIMD 慢 5% → ARM 上只用 `Vector<T>`（NEON 自動）也能達 ~95% 於 x86 SIMD 效能。**Phase 3 NEON 專屬 backend 基本不需要做**。

---

## 後續里程碑

本檔為 **Phase 0 baseline**。當 Phase 1（runtime dispatch 重構）與 Phase 2（GPU backend）完成後，將在同檔追加：
- runtime `--crt-strategy` 切換的 FPS 結果（驗證 0-overhead）
- GPU backend 的 FPS（目標 ≥ 2x SIMD）
- ARM NEON backend（如 Phase 3 實作）

參考：[MD/gpu/CRT_GPU_Design.md](../gpu/CRT_GPU_Design.md)
