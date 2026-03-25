# AudioPlus — AprNes 音訊引擎進化實作規劃

**日期**: 2026-03-25
**目標**: 將 AprNes 的音訊從基礎混音提升至雙模式（考究 / 現代）音訊引擎，涵蓋物理級 DAC 模擬、原生取樣率超採樣、立體聲擴展、空間感增強等功能。

---

## 1. 現狀分析

### 1.1 目前架構 (APU.cs)

```
apu_step() → 每 CPU cycle 更新 5 聲道 timer/sequencer
           → 累計 ~40.58 cycles 後呼叫 generateSample()

generateSample():
  1. Pulse 混音: SQUARELOOKUP[sq1*out + sq2*out]
  2. TND 混音:   TNDLOOKUP[3*tri + 2*noise + dmc]
  3. 加入 mapperExpansionAudio
  4. HPF ~90Hz (DC killer)
  5. HPF ~440Hz (RC filter)
  6. LPF ~14kHz (RC filter)
  7. 音量縮放 → 16-bit signed → AudioSampleReady callback
```

### 1.2 已具備的基礎

- 非線性 DAC 查找表（SQUARELOOKUP / TNDLOOKUP）
- 90Hz + 440Hz 雙重高通濾波
- 14kHz 低通濾波
- Expansion audio 混合（VRC6、Sunsoft 5B、Namco 163 等）
- RF 音訊干擾反饋（已整合至 Ntsc.cs）
- 44100Hz mono 輸出 → WaveOutPlayer

### 1.3 缺少的部分

- 無原生取樣率超採樣（直接從 ~40.58 cycle 降至 44.1kHz，有 aliasing）
- 無 per-channel 獨立處理（5 聲道在混音前就合併）
- 無立體聲輸出
- 無空間感（reverb、Haas effect）
- 無主機型號差異模擬
- 無 band-limited synthesis（方波邊緣有 aliasing）

---

## 2. 目標架構：雙模式音訊引擎

### 2.1 模式設計

| 模式 | 名稱 | 特色 | 輸出 |
|------|------|------|------|
| **Authentic** | 考究復古 | 物理級 DAC、主機型號濾波、RF 雜音 | Dual Mono → Stereo |
| **Modern** | 現代增強 | 5 軌獨立處理、立體聲配置、EQ、空間效果 | True Stereo |

使用者可在 UI 切換模式，也可選擇 **Pure Digital**（關閉所有後處理，最乾淨的原始輸出）。

### 2.2 總體架構圖

```
                    ┌─────────────────────────────────────┐
                    │         AudioDispatcher              │
                    │   PushApuClock(sq1,sq2,tri,noi,dmc) │
                    └──────────┬──────────┬───────────────┘
                               │          │
                    ┌──────────▼──┐  ┌────▼──────────────┐
                    │  Authentic  │  │     Modern         │
                    │  Pipeline   │  │     Pipeline       │
                    └──────┬──────┘  └────┬───────────────┘
                           │              │
                    ┌──────▼──────┐  ┌────▼───────────────┐
                    │ Dual Mono   │  │  True Stereo       │
                    │ Interleaved │  │  Interleaved       │
                    └──────┬──────┘  └────┬───────────────┘
                           └──────┬───────┘
                           ┌──────▼──────┐
                           │ WaveOut /   │
                           │ FFmpeg Pipe │
                           └─────────────┘
```

---

## 3. 模組設計與實作順序

### Phase 1: 基礎設施（最高優先）

#### 3.1 OversamplingEngine — 原生取樣率超採樣引擎

**目的**: 消除 aliasing，這是音質提升最大的單一改進。

**原理**: APU 以 1.789772 MHz 產生樣本，比 CD 品質高 40.58 倍。目前直接每 ~40.58 cycle 取一個 sample，會產生混疊失真。正確做法是在原生速率緩衝所有樣本，再用高品質 FIR 濾波器降至 44.1kHz。

**規格**:
- 輸入: 每個 APU cycle 一個 float（電壓值）
- Ring buffer 大小: ~30,000 samples（一幀 ≈ 29,780 APU cycles）
- FIR 降頻濾波器: 256-Tap, Blackman-windowed Sinc, 128 phase polyphase
- 輸出: 735 samples/frame @ 44.1kHz
- SIMD 加速: `Vector<float>` (AVX2 = 8 floats/instruction)

**關鍵 API**:
```csharp
class OversamplingEngine
{
    void PushSample(float voltage);              // 每 APU cycle 呼叫
    int Decimate(float[] output, int maxCount);  // 每 frame 呼叫, 回傳實際 sample 數
}
```

**效能預估**: <1ms/frame，CPU 負擔極低。

---

#### 3.2 AuthenticAudioMixer — 物理級 DAC 混音器

**目的**: 精確模擬 NES 硬體的非線性 DAC 混音特性。

**規格**:
- **3D DAC 查找表**:
  - Pulse table: 31 entries (sq1+sq2 = 0~30)
  - TND table: 16×16×128 = 32,768 entries (tri×noise×dmc)
  - 記憶體: ~131KB
- **90Hz HPF**: IIR 一階，在 1.79MHz 取樣率下計算 alpha
- **RF Crosstalk**: 59.94Hz 鋸齒波振盪器，振幅由 PPU 平均亮度調變

**與現有差異**: 目前的 SQUARELOOKUP/TNDLOOKUP 已有類似功能，但 TND 是簡化版（非完整 3D 查找）。升級為完整 3D 表可提升精確度。

**關鍵 API**:
```csharp
class AuthenticAudioMixer
{
    float GetVoltage(int sq1, int sq2, int tri, int noise, int dmc);
    void SetVideoLuminance(float avgLuma);  // RF buzz feedback
}
```

---

### Phase 2: 現代音訊管線

#### 3.3 ModernAudioMixer — 5 軌獨立混音器

**目的**: 保留 per-channel 控制權，實現立體聲配置和 EQ。

**規格**:
- 5 個獨立 OversamplingEngine（每聲道一個）
- 每聲道獨立歸一化（線性，不走 DAC 非線性曲線）
- **立體聲配置**:
  - Pulse 1: 左 30%
  - Pulse 2: 右 30%
  - Triangle: 中央
  - Noise: 中央（略寬）
  - DMC: 中央
- **Triangle Bass Boost**: Low-Shelf EQ, +3~6dB @ 150Hz 以下
- 可調式 per-channel 音量與 pan

**記憶體**: 5 個 ring buffer × ~30,000 floats ≈ 600KB

**關鍵 API**:
```csharp
class ModernAudioMixer
{
    void PushChannels(int sq1, int sq2, int tri, int noise, int dmc);
    void ProcessFrame(float[] stereoOut, int sampleCount);
}
```

---

#### 3.4 ModernAudioFX — 空間效果處理器

**目的**: 為單聲道的 NES 音源增加空間感和立體寬度。

**效果 1: Haas Effect（立體聲擴展）**
- 右聲道延遲 10~30ms（可調）
- Crossfeed: 延遲的右聲道以 40% 混入左聲道
- 消除耳機中聲音鎖在正中央的感覺

**效果 2: Micro-Room Reverb**
- 短殘響 0.3~0.8 秒（不是大廳 reverb，避免糊掉節奏）
- 4 條平行 Comb Filter（延遲長度: 1116, 1188, 1277, 1356 samples）
- 每條帶 feedback decay + 低通濾波
- Wet/Dry mix: 建議 15%（可調）

**關鍵 API**:
```csharp
class ModernAudioFX
{
    void Process(float[] stereoBuffer, int sampleCount);
    float HaasDelayMs { get; set; }   // 10-30ms
    float ReverbWet   { get; set; }   // 0.0-1.0
}
```

---

### Phase 3: 主機型號模擬

#### 3.5 ConsoleModelFilter — 主機型號差異濾波

**目的**: 不同 NES/Famicom 硬體的音色差異顯著，考究派玩家在意這些細節。

| 型號 | 特色 | LPF 截止 |
|------|------|---------|
| **Famicom (HVC-001)** | 明亮清晰，支援擴展音效 | ~14kHz |
| **Front-Loader (NES-001)** | 溫暖厚實，高頻衰減明顯 | ~4.5-7kHz |
| **Top-Loader (NES-101)** | 銳利刺耳，幾乎無 LPF | ~20kHz (幾乎不濾) |

**實作**: 1-pole IIR LPF，依型號切換 cutoff 頻率。
- `alpha = dt / (rc + dt)`, rc = 1/(2π×fCutoff)

**附加效果**:
- Top-Loader: 60Hz AC buzz（PCB 走線不良導致）
- Front-Loader: 額外的高頻衰減（封閉式機殼）

---

### Phase 4: Band-Limited Synthesis（進階）

#### 3.6 BlipSynthesizer — 帶限步階合成

**目的**: 消除方波邊緣的 aliasing（Gibbs 現象），這是數位合成方波的根本問題。

**原理**: 不再記錄絕對電壓值，改為記錄**電壓變化量（delta）**。每次波形跳變時，注入一個 windowed sinc 脈衝（而非階梯跳變），再透過積分還原。

**規格**:
- 32 phases × 16 taps
- Blackman-windowed Sinc 步階表（預計算）
- 僅在振幅變化時注入脈衝（高效率）

**效益**: 主要影響 Pulse 和 Noise 聲道。Triangle 本身較平滑，影響較小。

**注意**: 這是最後實作的項目，因為 OversamplingEngine 的 256-Tap FIR 已能消除大部分 aliasing，BlipSynthesizer 是錦上添花。

---

## 4. AudioDispatcher — 總調度器

**目的**: 單一進入點，串接 APU → 管線 → 輸出。

```csharp
class AudioDispatcher
{
    enum AudioMode { PureDigital, Authentic, Modern }

    AudioMode CurrentMode;

    // 每 APU cycle 呼叫（由 apu_step 觸發）
    void PushApuClock(int sq1, int sq2, int tri, int noise, int dmc);

    // 每 frame 呼叫（產出 735 stereo samples）
    void ProcessFrame(float[] stereoInterleaved, int sampleCount);

    // RF buzz 亮度回饋（由 PPU 提供）
    void SetVideoLuminance(float avgLuma);
}
```

**模式行為**:

| 動作 | PureDigital | Authentic | Modern |
|------|------------|-----------|--------|
| PushApuClock | 直接混音 | DAC→1個超採樣器 | 歸一化→5個超採樣器 |
| ProcessFrame | 簡單降頻 | FIR降頻→型號濾波→dual mono | 5路FIR降頻→立體聲混音→空間效果 |
| 輸出格式 | Mono→Stereo | Dual Mono | True Stereo |

---

## 5. 整合計畫

### 5.1 與現有程式碼的接口

| 接口點 | 目前 | 改造後 |
|--------|------|--------|
| `apu_step()` 內 | 累計 cycle → `generateSample()` | 累計 cycle → `AudioDispatcher.PushApuClock()` |
| `generateSample()` | 混音+濾波+輸出 | 保留為 PureDigital 路徑的 fallback |
| `AudioSampleReady` | `Action<short>` (mono) | `Action<short, short>` (stereo L, R) 或 stereo interleaved buffer |
| WaveOutPlayer | 單聲道 735 samples | 立體聲 1470 samples (L,R,L,R,...) |
| FFmpeg pipe | 單聲道 PCM | 立體聲 PCM |
| Expansion audio | `mapperExpansionAudio` (int) | 同上，在 DAC 混合後加入 |

### 5.2 檔案規劃

```
AprNes/NesCore/AudioPlus/
  ├── AudioDispatcher.cs       — 總調度器
  ├── OversamplingEngine.cs    — 超採樣 + FIR 降頻
  ├── AuthenticAudioMixer.cs   — 物理 DAC + HPF + RF buzz
  ├── ModernAudioMixer.cs      — 5 軌獨立 + 立體聲 + EQ
  ├── ModernAudioFX.cs         — Haas + Reverb
  ├── ConsoleModelFilter.cs    — 主機型號濾波
  └── BlipSynthesizer.cs       — 帶限步階合成（Phase 4）
```

### 5.3 UI 設定

```
Settings → Audio:
  ├── Engine Mode: [Pure Digital] [Authentic] [Modern]
  ├── Console Model: [Famicom] [Front-Loader] [Top-Loader] (Authentic only)
  ├── Stereo Width: 0-100% (Modern only)
  ├── Reverb: Off / Light / Medium (Modern only)
  ├── Bass Boost: Off / +3dB / +6dB (Modern only)
  └── RF Crosstalk: On/Off (Authentic only)
```

---

## 6. 實作順序與里程碑

| Phase | 項目 | 預估工作量 | 依賴 | 效益 |
|:-----:|------|:--------:|------|------|
| **1a** | OversamplingEngine | 中 | 無 | 消除 aliasing，音質大幅提升 |
| **1b** | AuthenticAudioMixer (3D DAC) | 小 | 無 | 更精確的混音 |
| **1c** | AudioDispatcher (Authentic path) | 中 | 1a, 1b | 串接管線，替換 generateSample |
| **1d** | ConsoleModelFilter | 小 | 1c | 主機型號音色差異 |
| **2a** | ModernAudioMixer (5-track) | 中 | 1a | 立體聲 + per-channel EQ |
| **2b** | ModernAudioFX (Haas + Reverb) | 中 | 2a | 空間感增強 |
| **2c** | AudioDispatcher (Modern path) | 小 | 2a, 2b | 完整雙模式 |
| **3** | WaveOutPlayer 立體聲改造 | 小 | 1c or 2c | 立體聲輸出 |
| **4** | BlipSynthesizer | 大 | 1a | 帶限合成（錦上添花） |
| **5** | UI 設定介面 | 小 | 全部 | 使用者可調參數 |

**建議**: Phase 1 完成後即可替換現有 `generateSample()`，立即獲得音質提升。Phase 2 可獨立開發，不影響 Authentic 路徑。

---

## 7. 效能預估

| 項目 | CPU 負擔 | 記憶體 |
|------|---------|--------|
| OversamplingEngine ×1 | <0.5ms/frame | ~120KB (ring buffer + FIR kernel) |
| OversamplingEngine ×5 | <2.5ms/frame | ~600KB |
| FIR Decimation (SIMD) | <1ms/frame | 含在上面 |
| ModernAudioFX | <0.2ms/frame | ~30KB (delay lines) |
| AuthenticAudioMixer | <0.1ms/frame | ~131KB (3D LUT) |
| **合計 (Modern mode)** | **<4ms/frame** | **<900KB** |

以 60fps 計算，一幀有 16.67ms，音訊處理佔不到 25%。在多核系統上可移至獨立執行緒，幾乎零影響。

---

## 8. 參考資料來源

所有技術細節來自 `ref/聲音優化MD/` 目錄內的 17 份文件，涵蓋：
- DAC 非線性混音公式與查找表
- 超採樣引擎與 FIR 降頻設計
- Haas effect 與 Comb Filter reverb 參數
- 主機型號濾波特性（Famicom / Front-Loader / Top-Loader）
- Band-limited step synthesis（Blip Buffer）原理
- AudioDispatcher 雙管線架構
- 算力可行性分析（<1% 單核 CPU）
