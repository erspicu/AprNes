# 音訊 DSP 效能評估 — 無頭模式整合規劃

**日期**: 2026-03-26

---

## 目標

讓無頭模式（`--wait-result` / benchmark）可以跑完整的音訊 DSP 管線，但**不經過 WinMM waveOut 實際播放**，用來量測各模式的 DSP 開銷。

## 現狀問題

`TestRunner.cs` 設定 `NesCore.AudioEnabled = false`，導致：
- Mode 0: `generateSample()` 第一行 `if (!AudioEnabled) return;` 直接跳出
- Mode 1/2: `AudioDispatcher.PushApuCycle()` 第一行 `if (!NesCore.AudioEnabled || !initialized) return;` 直接跳出

APU 的 timer/sequencer 仍在運作（`apu_step()` 不檢查 AudioEnabled），但**混音、FIR 降頻、濾波、空間效果全部被跳過**。效能測試完全不包含音訊處理開銷。

## 命令列參數

| 參數 | 說明 |
|------|------|
| `--audio-dsp` | 啟用音訊 DSP 處理（不播放），預設 off |
| `--audio-mode <0\|1\|2>` | 指定音訊模式：0=Pure Digital, 1=Authentic, 2=Modern |

## 各模式的評估策略

### Mode 0 (Pure Digital)
- 查找表混音 + DC Killer
- 開銷最低，作為 DSP baseline

### Mode 1 (Authentic) — 最大開銷配置
- 主機型號：任選一個（filter 參數不同但運算量相同，都是一階 IIR）
- **RF 干擾：強制開啟** → 多一組鋸齒波 × 亮度調變
- **Buzz：強制開啟** → 多一組 sin() 計算
- 完整管線：DAC 3D LUT → 256-tap FIR → IIR LPF → Buzz → RF

### Mode 2 (Modern) — 最大開銷配置
- 所有效果全開：StereoWidth=100, BassBoost=12dB, Haas=ON, Reverb=ON
- 完整管線：5×256-tap FIR → Biquad EQ → Stereo Pan → Comb×4 → Haas delay

## 實作方式

### TestRunner.cs 修改
1. 新增 `--audio-dsp` 和 `--audio-mode` 參數解析
2. 當 `--audio-dsp` 啟用時：
   - 設定 `AudioEnabled = true`
   - 設定 `AudioMode = <指定值>`
   - **不註冊 `AudioSampleReady` handler** — 樣本產出後丟棄，不進 WaveOutPlayer
   - 呼叫 `AudioDispatcher.Init()` 初始化管線實例
   - 依模式套用最大開銷參數：
     - Mode 1: `RfCrosstalk = true`, `CustomBuzz = true`, `BuzzAmplitude = 30`
     - Mode 2: `StereoWidth = 100`, `BassBoostDb = 12`, `HaasDelay = 20`, `HaasCrossfeed = 40`, `ReverbWet = 15`, `CombFeedback = 70`, `CombDamp = 30`
   - 呼叫 `AudioDispatcher.ApplySettings()` 同步設定

### 不需修改的部分
- `APU.cs`: `apu_step()` 內的模式分流已依 `AudioEnabled` + `AudioMode` 正確分派
- `AudioDispatcher.cs`: `OutputStereo()` 透過 `AudioSampleReady?.Invoke()` 發送，無 handler 時自動丟棄
- `WaveOutPlayer.cs`: 不需建立實例，因為不呼叫 `openAudio()`

## 用法範例

```bash
# Baseline（無 DSP）
AprNes.exe --rom test.nes --wait-result --max-wait 30

# Pure Digital DSP
AprNes.exe --rom test.nes --wait-result --max-wait 30 --audio-dsp --audio-mode 0

# Authentic 最大開銷
AprNes.exe --rom test.nes --wait-result --max-wait 30 --audio-dsp --audio-mode 1

# Modern 最大開銷
AprNes.exe --rom test.nes --wait-result --max-wait 30 --audio-dsp --audio-mode 2
```

## 預期產出

分別量測：無 DSP baseline → Mode 0 增量 → Mode 1 增量 → Mode 2 增量，算出每種模式的確切 cost。
