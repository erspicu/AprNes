# AprNesAvalonia CRT Dispatch Baseline — 2026-04-18

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
| AnalogSize | 8x (2048×1680) |
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
| Scalar | 73.69 | 74.17 | 72.46 | **73.31** | 1.22x |
| Simd | 77.60 | 77.76 | 75.57 | **76.66** | 1.28x |

### Speedup 分析

| 比較 | Scalar FPS | SIMD FPS | Speedup |
|------|:----------:|:--------:|:-------:|
| Scalar → SIMD | 73.31 | 76.66 | **1.05x** |

> **NES 即時 FPS**：60.0988（NTSC）。平均 FPS ÷ 60.0988 = 即時倍率；≥ 1.0x 即可流暢運行。

---

## 觀察與分析

### 1. SIMD vs Scalar Speedup 只有 1.05x
這個數字遠低於直覺預期（典型 AVX2 加速 2-4x）。原因分析：

- **"Scalar" 不是純量**：`CrtScreen.cs`（命名為 scalar 但其實）已大量使用 `System.Numerics.Vector<T>`（跨平台 SIMD 抽象），在 x86 上會自動選到最寬的 SIMD（AVX2）。真正純量部分只在邊界、尾端循環、與無法向量化的控制流。
- **CrtScreen.Simd.cs 的額外增益**：主要來自 `Vector256<T>` / `Avx2.GatherVector256` 等特化 intrinsics 與 `[SkipLocalsInit]`；這些對少數熱點貢獻 ~3-5%，但不是整個管線的主導。
- **瓶頸不在 CRT 階段**：UltraAnalog + DSP Mode 2 下，大量 CPU 時間花在 NTSC 物理解調（21.477 MHz 波形重建 + coherent demod + RF AM modulation）與 5×256-tap FIR audio DSP，CRT 後處理只佔管線的一部分。降低該比例後，SIMD 加速 CRT 的邊際效益自然縮水。

### 2. 對 GPU Phase 2 的含意
- 若 GPU 只加速 CRT 階段，**預期整體加速也將有限**（可能僅 10-30%），因為 NTSC 解調仍在 CPU。
- 要真正釋放 GPU 優勢，**需要把 NTSC 合成（v3 stretch goal）一併搬到 GPU**。這驗證了 §4 中將 NTSC 列為 Phase 2 延伸而非可選項的判斷。
- Phase 2 目標從「CRT ≥ 2x SIMD」修正為「**加 NTSC 後整體 pipeline ≥ 1.5x SIMD**」比較務實。

### 3. Scalar 既然只比 SIMD 慢 5%
- ARM 平台若暫時以 scalar（`Vector<T>`）為主，實用性很高 — `Vector<T>` 在 ARM 會自動用 NEON。
- 短期內不需要急於做 NEON 專屬 backend（Phase 3）；Phase 1 runtime dispatch 就能讓 ARM 用 scalar 路徑跑到合理效能。

### 4. 鎖頻影響
本機為 AMD Ryzen 7 3700X（8C/16T），鎖頻後實測值比 MEMORY.md 歷史紀錄（NTSC v2 8x = 79.03 FPS） 低約 3%。屬於預期範圍，代表熱機與頻率穩定後的保守基準。

---

## 後續里程碑

本檔為 **Phase 0 baseline**。當 Phase 1（runtime dispatch 重構）與 Phase 2（GPU backend）完成後，將在同檔追加：
- runtime `--crt-strategy` 切換的 FPS 結果（驗證 0-overhead）
- GPU backend 的 FPS（目標 ≥ 2x SIMD）
- ARM NEON backend（如 Phase 3 實作）

參考：[MD/gpu/CRT_GPU_Design.md](../gpu/CRT_GPU_Design.md)
