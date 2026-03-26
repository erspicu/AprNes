# Analog + Audio DSP Benchmark — Mode 1 (Authentic)

**日期**: 2026-03-26
**測試目的**: 量測 Analog + Audio DSP Mode 1 (Authentic) 在不同解析度下的完整管線效能

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
| **Audio DSP** | **Mode 1 (Authentic)** |
| 音效播放 | OFF (DSP 處理完後丟棄，不經 WaveOut) |
| 畫面顯示 | OFF (headless, 無 GPU rendering) |
| 測試時長 | 20 秒 / 回合 |
| 測試 ROM | Mega Man 5 (USA).nes (Mapper 004, MMC3) |
| 冷卻時間 | 每回合前 30 秒 |

**影像管線**:
```
PPU per-scanline → Ntsc.DecodeScanline (21.477 MHz waveform + coherent demodulation + RF AM modulation)
→ linearBuffer → CrtScreen.Render (Gaussian scanline bloom) → AnalogScreenBuf
```

**音訊 DSP 管線 (Mode 1)**:
```
3D DAC LUT → 90Hz HPF → 256-tap FIR (128 polyphase) → Console Model IIR LPF → 60Hz Buzz → RF Crosstalk
```

**測試協議**: 3 次法 — 第 1 次為 JIT/TieredPGO 暖機不採計 → sleep 30s → 第 2 次（有效）→ sleep 30s → 第 3 次（有效）→ 取 Run 2、Run 3 平均

---

## 測試結果

### 各解析度 FPS 對照

| AnalogSize | 解析度 | 像素數 | Run 1 (JIT) | Run 2 | Run 3 | **平均 FPS** | 即時倍率 |
|:----------:|:------:|:------:|:-----------:|:-----:|:-----:|:------------:|:--------:|
| 2x | 512×420 | 215.0K | 116.33 | 116.42 | 116.62 | **116.52** | 1.94x |
| 4x | 1024×840 | 860.2K | 108.57 | 108.06 | 108.52 | **108.29** | 1.80x |
| 6x | 1536×1260 | 1935.4K | 81.99 | 81.99 | 77.53 | **79.76** | 1.33x |
| 8x | 2048×1680 | 3440.6K | 77.53 | 77.66 | 78.66 | **78.16** | 1.30x |

### 效能縮放分析

| 比較 | 基準 (2x) FPS | 目標 FPS | 像素比 | FPS 比 | 備註 |
|------|:------------:|:--------:|:------:|:------:|------|
| 2x → 2x | 116.52 | 116.52 | 1.0x | 1.00x | 基準 |
| 2x → 4x | 116.52 | 108.29 | 4.0x | 0.93x |  |
| 2x → 6x | 116.52 | 79.76 | 9.0x | 0.68x |  |
| 2x → 8x | 116.52 | 78.16 | 16.0x | 0.67x |  |

> **NES 即時 FPS**: 60.0988 FPS（NTSC）。平均 FPS ÷ 60.0988 = 即時倍率，≥ 1.0x 即可流暢運行。
