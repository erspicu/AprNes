# Analog Mode Multi-Resolution Benchmark

**日期**: 2026-03-22
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
| 2x | 512×420 | 215.0K | 123.81 | 122.86 | 123.95 | **123.41** | 2.05x |
| 4x | 1024×840 | 860.2K | 108.56 | 108.22 | 108.56 | **108.39** | 1.80x |
| 6x | 1536×1260 | 1935.4K | 77.07 | 71.64 | 76.62 | **74.13** | 1.23x |
| 8x | 2048×1680 | 3440.6K | 68.56 | 68.45 | 68.55 | **68.50** | 1.14x |

### 效能縮放分析

| 比較 | 基準 (2x) FPS | 目標 FPS | 像素比 | FPS 比 | 備註 |
|------|:------------:|:--------:|:------:|:------:|------|
| 2x → 2x | 123.41 | 123.41 | 1.0x | 1.00x | 基準 |
| 2x → 4x | 123.41 | 108.39 | 4.0x | 0.88x |  |
| 2x → 6x | 123.41 | 74.13 | 9.0x | 0.60x |  |
| 2x → 8x | 123.41 | 68.50 | 16.0x | 0.56x |  |

> **NES 即時 FPS**: 60.0988 FPS（NTSC）。平均 FPS ÷ 60.0988 = 即時倍率，≥ 1.0x 即可流暢運行。
