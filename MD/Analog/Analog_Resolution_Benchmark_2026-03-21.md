# Analog Mode Multi-Resolution Benchmark

**日期**: 2026-03-21
**測試目的**: 比較不同 AnalogSize (2x/4x/6x/8x) 下完整類比模擬管線的效能

---

## 測試條件

| 項目 | 設定 |
|------|------|
| 組態 | Release (x64) |
| AccuracyOptA | ON |
| AnalogMode | 1 (Enabled) |
| UltraAnalog | 1 (Level 3 物理路徑) |
| CRT | 1 (Stage 2 電子束光學) |
| AnalogOutput | RF |
| 音效輸出 | OFF (headless) |
| 畫面顯示 | OFF (headless, 無 GPU rendering) |
| 測試時長 | 20 秒 / 回合 |
| 測試 ROM | Mega Man 5 (USA).nes (Mapper 004, MMC3) |
| 冷卻時間 | 每回合前 30 秒 |
| SIMD 優化 | S01 (vFw 常數提升) + S02 (SIMD 像素打包) |

**完整管線**:
```
PPU per-scanline → Ntsc.DecodeScanline (21.477 MHz waveform + coherent demodulation + RF AM modulation)
→ linearBuffer → CrtScreen.Render (Gaussian scanline bloom) → AnalogScreenBuf
```

**測試協議**: 3 次法 — 第 1 次為 JIT/TieredPGO 暖機不採計 → sleep 30s → 第 2 次（有效）→ sleep 30s → 第 3 次（有效）→ 取 Run 2、Run 3 平均

---

## 測試結果

### 各解析度 FPS 對照

| AnalogSize | 解析度 | 像素數 | Run 1 (JIT) | Run 2 | Run 3 | **平均 FPS** | 即時倍率 |
|:----------:|:------:|:------:|:-----------:|:-----:|:-----:|:------------:|:--------:|
| 2x | 512×420 | 215.0K | 106.65 | 106.10 | 105.46 | **105.78** | 1.76x |
| 4x | 1024×840 | 860.2K | 107.32 | 107.15 | 107.48 | **107.31** | 1.79x |
| 6x | 1536×1260 | 1935.4K | 88.67 | 89.60 | 89.67 | **89.63** | 1.49x |
| 8x | 2048×1680 | 3440.6K | 103.37 | 100.75 | 103.38 | **102.06** | 1.70x |

### 效能縮放分析

| 比較 | 基準 (2x) FPS | 目標 FPS | 像素比 | FPS 比 | 備註 |
|------|:------------:|:--------:|:------:|:------:|------|
| 2x → 2x | 105.78 | 105.78 | 1.0x | 1.00x | 基準 |
| 2x → 4x | 105.78 | 107.31 | 4.0x | 1.01x |  |
| 2x → 6x | 105.78 | 89.63 | 9.0x | 0.85x |  |
| 2x → 8x | 105.78 | 102.06 | 16.0x | 0.96x |  |

> **NES 即時 FPS**: 60.0988 FPS（NTSC）。平均 FPS ÷ 60.0988 = 即時倍率，≥ 1.0x 即可流暢運行。

---

## 分析

### CrtScreen 渲染路徑與 SIMD 命中率

| AnalogSize | DstW | CrtScreen 路徑 | SIMD 狀態 |
|:----------:|:----:|:--------------:|:---------:|
| 2x | 512 | **scalar** (線性插值) | ❌ 無 SIMD |
| 4x | 1024 | **is1to1** (DstW=SrcW) | ✅ 全 SIMD (S01+S02) |
| 6x | 1536 | **scalar** (線性插值) | ❌ 無 SIMD |
| 8x | 2048 | **isDouble** (DstW=2×SrcW) | ✅ SIMD 計算 + scalar 雙寫 |

### 關鍵發現

1. **4x 反而比 2x 快** (+1.4%): 4x 走 `is1to1` SIMD 最佳路徑（S01+S02 全受益），雖然像素數 4 倍，但 SIMD 加速完全覆蓋。2x 走 scalar 線性插值路徑，無法利用 SIMD。

2. **6x 最慢** (89.63 FPS): 像素數 9 倍 + scalar 路徑（N=6 不是 4 或 8 的整數倍，無法走 is1to1/isDouble），雙重劣勢導致效能最低。

3. **8x 比 6x 快** (+13.9%): 雖然像素數 16 倍（vs 9 倍），但 8x 走 `isDouble` SIMD 路徑，SIMD 計算優勢彌補了像素增量。不過 isDouble 路徑仍有 scalar 雙寫迴圈（每個 SIMD pixel 需寫入 2 個 output pixel），效能略低於 4x。

4. **Stage 1 (Ntsc) 為主要瓶頸**: 2x 與 4x 幾乎相同（105.78 vs 107.31），表示 CrtScreen (Stage 2) 的佔比很小。主要時間花在 Ntsc.DecodeScanline（21.477 MHz waveform 解調），這部分與 AnalogSize 無關。

### 建議

- **推薦 AnalogSize=4x**: 效能最佳（107.31 FPS, 1.79x 即時），且為 1:1 SIMD 路徑
- **AnalogSize=6x 需優化**: 考慮為 N=6 新增專用 SIMD 路徑，或將 6x 改為先算 1024 寬再 1.5x nearest-neighbor 放大
- **所有解析度均可即時運行**: 最慢的 6x 仍有 1.49x 即時倍率
