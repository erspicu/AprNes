# Analog Mode Performance Benchmark

**日期**: 2026-03-21
**測試目的**: 驗證經過 NTSC 類比模擬 + CRT 電子束光學模擬完整流程後的效能狀況

---

## 測試條件

| 項目 | 設定 |
|------|------|
| 組態 | Release (x64) |
| AccuracyOptA | ON |
| AnalogMode | 1 (Enabled) |
| UltraAnalog | 1 (Level 3 物理路徑) |
| CRT | 1 (Stage 2 電子束光學) |
| AnalogSize | 4 (1024×840) |
| AnalogOutput | RF |
| 音效輸出 | OFF (headless) |
| 畫面顯示 | OFF (headless, 無 GPU rendering) |
| 測試時長 | 20 秒 / 回合 |
| 測試 ROM | Mega Man 5 (USA).nes (Mapper 004, MMC3) |
| 冷卻時間 | 每回合前 30 秒 |

**完整管線**:
```
PPU per-scanline → Ntsc.DecodeScanline (21.477 MHz waveform + coherent demodulation + RF AM modulation)
→ linearBuffer → CrtScreen.Render (Gaussian scanline bloom) → AnalogScreenBuf
```

## 測試協議

- **3 次法**: 第 1 次為 JIT/TieredPGO 暖機不採計 → sleep 30s → 第 2 次（有效）→ sleep 30s → 第 3 次（有效）→ 取 Run 2、Run 3 平均
- 原因: .NET TieredPGO 第 1 次以 Tier-0 跑並收集 PGO，第 2 次起才用 Tier-1 最佳化程式碼

## 測試指令

```bash
AprNes/bin/Release/AprNes.exe --rom "etc/Mega Man 5 (USA).nes" \
    --benchmark 20 --ultra-analog --analog-output RF --analog-size 4 --crt --accuracy A
```

## 測試結果

| 回合 | Frames | 時長 (s) | FPS | 備註 |
|:----:|:------:|:--------:|:---:|------|
| Run 1 | 2081 | 20.01 | 104.01 | JIT 暖機，不採計 |
| **Run 2** | **2029** | **20.01** | **101.41** | 有效 |
| **Run 3** | **2105** | **20.01** | **105.22** | 有效 |

### 基準 FPS

| 指標 | 數值 |
|------|------|
| **Run 2 + Run 3 平均** | **103.32 FPS** |
| NES 原始 FPS (NTSC) | 60.0988 FPS |
| 相對即時速度 | **1.72x** (即時播放的 172%) |

## 效能對比

| 模式 | FPS | 相對基準 |
|------|:---:|:--------:|
| 無類比 (AccuracyOptA=ON, Release) | 264.45 | 100% |
| **UltraAnalog + CRT + RF (本次)** | **103.32** | **39.1%** |

> 完整 NTSC+CRT 類比模擬管線（Level 3 + Stage 2）在 RF 模式下約消耗 61% 的效能，
> 但仍維持約 1.72 倍即時速度，足以流暢運行。
