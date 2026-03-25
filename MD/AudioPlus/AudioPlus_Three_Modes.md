# AudioPlus 三種音訊模式技術文件

**日期**: 2026-03-26

---

## 總覽

AprNes 提供三種音訊處理模式，由 `NesCore.AudioMode` 控制（0 / 1 / 2）。三種模式共用相同的 APU 聲道模擬（Pulse1、Pulse2、Triangle、Noise、DMC），但在混音、降頻、濾波、空間效果等處理上各自不同。

| 模式 | 名稱 | 輸出 | 特色 |
|------|------|------|------|
| 0 | Pure Digital | Dual Mono | 直接查找表混音，最低延遲，經典 NES 聲音 |
| 1 | Authentic | Dual Mono | 物理級 DAC 非線性模擬 + 主機型號濾波 + 可選 RF 干擾 |
| 2 | Modern | True Stereo | 5 軌獨立超採樣 + 立體聲分離 + Bass Boost + 空間效果 |

最終輸出統一為 44,100 Hz / 16-bit / Stereo PCM，透過 WinMM `waveOut` API 播放。

---

## 共通基礎：APU 聲道產生

APU 在每個 CPU cycle 執行 `apu_step()`，更新 5 個聲道的 timer 與 sequencer，產生原始整數輸出：

| 聲道 | 範圍 | 說明 |
|------|------|------|
| Pulse 1 | 0–15 | 方波，可調占空比 (12.5%/25%/50%/75%) |
| Pulse 2 | 0–15 | 同上，獨立設定 |
| Triangle | 0–15 | 4-bit 步階三角波 |
| Noise | 0–15 | LFSR 雜音產生器 |
| DMC | 0–127 | Delta Modulation Channel，7-bit DPCM 取樣 |

此外還有 `mapperExpansionAudio`（來自 VRC6、Namco N163、VRC7 等卡帶擴展晶片）。

在 `apu_step()` 末段，依 `AudioMode` 值分流：

```
if (AudioMode > 0)
    AudioDispatcher.PushApuCycle(sq1, sq2, tri, noise, dmc, expansion)
else
    每 ~40.58 cycle 呼叫 generateSample()
```

---

## Mode 0：Pure Digital（純數位）

### 設計理念

最簡潔的混音路徑。使用預計算查找表模擬 NES DAC 的非線性混音，再以簡單的整數運算做 DC 消除與音量縮放。不經過任何浮點 FIR 降頻或額外效果處理，CPU 開銷最低。

### 資料處理路徑

```
APU 5 聲道原始值 (每 CPU cycle 更新)
│
├─ 累計 ~40.58 CPU cycles（1,789,773 Hz ÷ 44,100 Hz）
│  └─ 達到門檻 → 呼叫 generateSample()
│
▼
┌─────────────────────────────────────────────────┐
│ generateSample()  [APU.cs]                      │
│                                                 │
│ ① Pulse 混音:                                   │
│    sqIdx = sq1_out + sq2_out    (0–30)          │
│    SQUARELOOKUP[sqIdx]                          │
│    公式: (95.52 / (8128/n + 100)) × 49151      │
│                                                 │
│ ② TND 混音:                                     │
│    tndIdx = 3×tri + 2×noise + dmc  (0–202)     │
│    TNDLOOKUP[tndIdx]                            │
│    公式: (163.67 / (24329/n + 100)) × 49151    │
│                                                 │
│ ③ 混合:                                         │
│    mixed = SQUARELOOKUP + TNDLOOKUP  (0~98302) │
│    mixed += mapperExpansionAudio                │
│                                                 │
│ ④ 90 Hz 高通濾波 (DC Killer):                   │
│    mixed += dckiller                            │
│    dckiller -= mixed >> 8                       │
│    dckiller += (mixed > 0 ? -1 : 1)            │
│    ─→ 消除靜態 DC 偏移                          │
│                                                 │
│ ⑤ 音量縮放 + clamp:                             │
│    clamped = mixed × Volume / 100               │
│    clamp [-32768, +32767]                       │
│                                                 │
│ ⑥ 輸出:                                         │
│    AudioSampleReady(clamped, clamped)           │
│    ─→ Dual Mono（左右聲道相同）                  │
└─────────────────────────────────────────────────┘
│
▼
WaveOutPlayer → 16-bit stereo PCM → WinMM waveOut
```

### 查找表特性

- `SQUARELOOKUP[31]`：基於 NES Pulse 聲道 DAC 公式，sq1+sq2 的非線性混合。輸出已乘以 49151 放大至整數域。
- `TNDLOOKUP[203]`：Triangle、Noise、DMC 三聲道的非線性混合。使用簡化的單一加權索引 `3×tri + 2×noise + dmc`。
- 兩者輸出相加範圍約 0~98302。

### 可調參數

| 參數 | 範圍 | 說明 |
|------|------|------|
| Volume | 0–100 | 主音量 |

---

## Mode 1：Authentic（考究模式）

### 設計理念

精確重現真實 NES 硬體的音訊特性。使用物理級 DAC 非線性查找表（3D TND 表，128KB）在原生 1.789 MHz 取樣率下混音，再透過 256-tap polyphase FIR 做高品質降頻至 44.1 kHz，最後依使用者選擇的主機型號套用對應的類比電路特性濾波。

### 資料處理路徑

```
APU 5 聲道原始值 (每 CPU cycle)
│
▼
┌──────────────────────────────────────────────────────┐
│ AudioDispatcher.PushApuCycle()  [每 CPU cycle 呼叫]  │
│                                                      │
│  ┌──────────────────────────────────────────────┐    │
│  │ ① AuthenticAudioMixer.GetVoltage()           │    │
│  │                                              │    │
│  │  Pulse DAC 查找:                              │    │
│  │    pulseTable[sq1+sq2]   (31 entries)        │    │
│  │    公式: 95.88 / (8128/(sq1+sq2) + 100)     │    │
│  │                                              │    │
│  │  TND 3D DAC 查找:                            │    │
│  │    tndTable[(tri<<11)|(noise<<7)|dmc]        │    │
│  │    32768 entries (128KB), 完整 3D 交互作用    │    │
│  │    公式: 159.79 / (1/(t/8227+n/12241+d/22638) + 100)│
│  │                                              │    │
│  │  raw = pulseTable + tndTable                 │    │
│  │  raw += expansionAudio / 98302.0             │    │
│  │                                              │    │
│  │  90 Hz HPF (一階 IIR, alpha=0.99996844):     │    │
│  │    diff = raw - hpfPrev                      │    │
│  │    hpfState = alpha × (hpfState + diff)      │    │
│  │    ─→ 在 1.789MHz 原生速率下消除 DC          │    │
│  │                                              │    │
│  │  輸出: float 電壓值 (約 ±0.5)               │    │
│  └──────────────────────────────────────────────┘    │
│                          │                           │
│                          ▼                           │
│  ┌──────────────────────────────────────────────┐    │
│  │ ② OversamplingEngine (256-Tap FIR 降頻)      │    │
│  │                                              │    │
│  │  PushSample() → 寫入 64K ring buffer         │    │
│  │  累計 ~40.58 cycle → TryGetSample()          │    │
│  │                                              │    │
│  │  Polyphase FIR 卷積:                          │    │
│  │    128 個分相核心（處理非整數降頻比）          │    │
│  │    256 taps, Blackman-windowed Sinc           │    │
│  │    cutoff: 20 kHz (anti-aliasing)            │    │
│  │    SIMD Vector<float> 加速（AVX2 = 8-wide）  │    │
│  │                                              │    │
│  │  輸出: 1 個 44.1 kHz float 樣本              │    │
│  └──────────────────────────────────────────────┘    │
│                          │                           │
│                          ▼                           │
│  ┌──────────────────────────────────────────────┐    │
│  │ ③ ConsoleModelFilter (主機型號濾波)           │    │
│  │                                              │    │
│  │  一階 IIR LPF:                                │    │
│  │    y[n] = beta × x[n] + (1-beta) × y[n-1]   │    │
│  │    beta 由截止頻率決定                        │    │
│  │                                              │    │
│  │  （可選）60 Hz AC Buzz:                       │    │
│  │    output += sin(2π × buzzPhase) × amplitude │    │
│  │                                              │    │
│  │  （可選）RF Crosstalk:                        │    │
│  │    sawtooth = (rfPhase - 0.5) × 2            │    │
│  │    output += sawtooth × luminance × volume   │    │
│  └──────────────────────────────────────────────┘    │
│                          │                           │
│  OutputStereo(sample, sample)  → Dual Mono          │
└──────────────────────────────────────────────────────┘
│
▼
float → int16 (GAIN=40000, ×Volume/100, clamp)
│
▼
WaveOutPlayer → 16-bit stereo PCM → WinMM waveOut
```

### 元件詳解

#### AuthenticAudioMixer — 物理級 DAC 混音

與 Mode 0 的查找表不同，Authentic 使用 NESdev Wiki 的精確 DAC 公式：

- **Pulse Table** (31 entries): `95.88 / (8128/n + 100)`，浮點輸出
- **TND 3D Table** (32768 entries, 128KB): 索引 `(tri<<11)|(noise<<7)|dmc`，完整保留 Triangle×Noise×DMC 三聲道的非線性交互作用，不做加權簡化

另含 90 Hz 一階 IIR 高通濾波器，在 1.789 MHz 原生取樣率下運作（alpha=0.99996844），使用 double 精度避免長時間累積誤差。

#### OversamplingEngine — FIR 超採樣降頻

NES APU 以 1,789,773 Hz 產生樣本，比 CD 品質（44,100 Hz）高 40.58 倍。直接每 ~41 cycle 取一個 sample 會產生混疊失真。OversamplingEngine 在原生速率緩衝所有樣本，用 anti-aliasing FIR 正確降頻：

- 256-tap Blackman-windowed Sinc，cutoff 20 kHz
- 128 個 polyphase 分相核心處理非整數降頻比（40.584...）的小數部分
- 64K float ring buffer（可容納約 2 frame 的輸入）
- `System.Numerics.Vector<float>` SIMD 加速卷積（AVX2 下一次處理 8 個 float，256 taps 僅需 32 次迭代）

#### ConsoleModelFilter — 主機型號差異

不同版本的 NES/Famicom 硬體有不同的類比電路特性，影響音色：

| 編號 | 主機型號 | LPF 截止頻率 | 60 Hz Buzz | 音色特徵 |
|------|----------|-------------|-----------|----------|
| 0 | Famicom (HVC-001) | 14 kHz | 無 | 明亮清晰，RF 輸出 |
| 1 | Front-Loader (NES-001) | 4.7 kHz | 無 | 溫暖厚實，低通明顯 |
| 2 | Top-Loader (NES-101) | 20 kHz | 有 | 接近無濾波，但有 AC buzz |
| 3 | AV Famicom (HVC-101) | 19 kHz | 無 | AV 直出乾淨 |
| 4 | Sharp Twin Famicom | 12 kHz | 無 | 略暗於原版 |
| 5 | Sharp Famicom Titler | 16 kHz | 無 | S-Video 乾淨 |
| 6 | Custom | 1000–22000 Hz | 可調 | 完全自訂 |

**RF Crosstalk**：模擬 RF 線材中視訊信號串入音訊的干擾。以 59.94 Hz 鋸齒波乘以畫面平均亮度，產生隨畫面變化的微弱雜音。

### 可調參數

| 參數 | 欄位 | 範圍 | 說明 |
|------|------|------|------|
| 主機型號 | ConsoleModel | 0–6 | 選擇預設或自訂 |
| 自訂 LPF | CustomLpfCutoff | 1000–22000 Hz | Custom 模式截止頻率 |
| 自訂 Buzz | CustomBuzz | on/off | Custom 模式 buzz 開關 |
| Buzz 頻率 | BuzzFreq | 50/60 Hz | 歐規/美規市電頻率 |
| Buzz 振幅 | BuzzAmplitude | 0–100 | 映射至 0.000–0.010 |
| RF 串擾 | RfCrosstalk | on/off | 全域開關 |
| RF 音量 | RfVolume | 0–200 | 映射至 0.00–0.20 |

---

## Mode 2：Modern（現代模式）

### 設計理念

跳脫硬體模擬的限制，以現代音訊工程手法重新處理 NES 音訊。核心差異在於 **先降頻、再混音**（Authentic 是先混音再降頻），保留各聲道的獨立性，使立體聲配置與針對性效果成為可能。提供 Haas Effect 空間感、Comb Filter 殘響、Triangle 低音增強等效果。

### 資料處理路徑

```
APU 5 聲道原始值 (每 CPU cycle)
│
▼
┌───────────────────────────────────────────────────────────┐
│ AudioDispatcher.PushApuCycle()  [每 CPU cycle 呼叫]       │
│                                                           │
│  ┌────────────────────────────────────────────────────┐   │
│  │ ① ModernAudioMixer.PushChannels()                  │   │
│  │                                                    │   │
│  │  5 聲道各自歸一化:                                  │   │
│  │    Pulse1:   sq1 / 15    ─→ Engine[0]              │   │
│  │    Pulse2:   sq2 / 15    ─→ Engine[1]              │   │
│  │    Triangle: tri / 15    ─→ Engine[2]              │   │
│  │    Noise:    noise / 15  ─→ Engine[3]              │   │
│  │    DMC:      dmc / 127   ─→ Engine[4]              │   │
│  │                                                    │   │
│  │  每聲道各有獨立的 OversamplingEngine (256-Tap FIR)  │   │
│  │  各自從 1.789 MHz 降頻至 44.1 kHz                  │   │
│  └────────────────────────────────────────────────────┘   │
│                          │                                │
│  累計 ~40.58 cycle → TryGetStereoSample()                │
│                          │                                │
│                          ▼                                │
│  ┌────────────────────────────────────────────────────┐   │
│  │ ② Triangle Bass Boost (Low-Shelf Biquad)           │   │
│  │                                                    │   │
│  │  僅作用於 Triangle 聲道                             │   │
│  │  NES Triangle 為 4-bit 步階波，低頻豐滿感不足      │   │
│  │                                                    │   │
│  │  Biquad 係數 (Audio EQ Cookbook):                   │   │
│  │    A = 10^(gainDb/40)                              │   │
│  │    w0 = 2π × freq / 44100                          │   │
│  │    Q = 0.707 (Butterworth)                         │   │
│  │                                                    │   │
│  │  triOut = b0×triIn + b1×x1 + b2×x2                │   │
│  │         - a1×y1 - a2×y2                            │   │
│  └────────────────────────────────────────────────────┘   │
│                          │                                │
│                          ▼                                │
│  ┌────────────────────────────────────────────────────┐   │
│  │ ③ Stereo Pan 配置                                  │   │
│  │                                                    │   │
│  │  基礎配置 (StereoWidth=100%):                      │   │
│  │    Pulse1:   L=0.7, R=0.3  (偏左)                 │   │
│  │    Pulse2:   L=0.3, R=0.7  (偏右)                 │   │
│  │    Triangle: L=0.5, R=0.5  (中央)                 │   │
│  │    Noise:    L=0.5, R=0.5  (中央)                 │   │
│  │    DMC:      L=0.5, R=0.5  (中央)                 │   │
│  │                                                    │   │
│  │  StereoWidth 線性插值:                              │   │
│  │    pan = 0.5 + (basePan - 0.5) × width/100        │   │
│  │    0%=全 mono, 50%=適度分離, 100%=最大分離         │   │
│  │                                                    │   │
│  │  混合:                                              │   │
│  │    L = Σ channel[i] × pan[i].L                     │   │
│  │    R = Σ channel[i] × pan[i].R                     │   │
│  └────────────────────────────────────────────────────┘   │
│                          │                                │
│                          ▼                                │
│  ┌────────────────────────────────────────────────────┐   │
│  │ ④ ModernAudioFX — 空間效果                         │   │
│  │                                                    │   │
│  │  ┌──── Micro-Room Reverb ────────────────────┐     │   │
│  │  │                                           │     │   │
│  │  │ mono = (L + R) × 0.5                      │     │   │
│  │  │                                           │     │   │
│  │  │ 4 條平行 Comb Filter:                      │     │   │
│  │  │   延遲: 1116/1188/1277/1356 samples       │     │   │
│  │  │        (25.3/26.9/28.9/30.7 ms, 互質)     │     │   │
│  │  │                                           │     │   │
│  │  │ 每條 Comb:                                 │     │   │
│  │  │   delayed = combBuf[pos]                  │     │   │
│  │  │   lpfState += damp × (lpfState - delayed) │     │   │
│  │  │   combBuf[pos] = mono + lpfState × feedback│    │   │
│  │  │   reverbOut += delayed                    │     │   │
│  │  │                                           │     │   │
│  │  │ reverbOut /= 4                            │     │   │
│  │  │ L += reverbOut × wet                      │     │   │
│  │  │ R += reverbOut × wet                      │     │   │
│  │  └───────────────────────────────────────────┘     │   │
│  │                                                    │   │
│  │  ┌──── Haas Effect ──────────────────────────┐     │   │
│  │  │                                           │     │   │
│  │  │ R 寫入 1543-sample 環形延遲線             │     │   │
│  │  │ delayedR = 讀取 N samples 前的 R 值       │     │   │
│  │  │                                           │     │   │
│  │  │ L += delayedR × crossfeed  (增加豐滿度)   │     │   │
│  │  │ R = delayedR               (產生時間差)    │     │   │
│  │  │                                           │     │   │
│  │  │ 人耳對 <40ms 時間差感知為                  │     │   │
│  │  │ 「同一聲源但更寬」→ 空間感                 │     │   │
│  │  └───────────────────────────────────────────┘     │   │
│  └────────────────────────────────────────────────────┘   │
│                          │                                │
│  OutputStereo(L, R)  → True Stereo（左右聲道不同）       │
└───────────────────────────────────────────────────────────┘
│
▼
float → int16 (GAIN=40000, ×Volume/100, clamp)
│
▼
WaveOutPlayer → 16-bit stereo PCM → WinMM waveOut
```

### 元件詳解

#### ModernAudioMixer — 5 軌獨立超採樣

**關鍵差異**: Authentic 是「先混音、再降頻」（模擬 DAC 混合後的類比信號），Modern 是「先降頻、再混音」（保留聲道獨立性）。

每聲道各有一個 OversamplingEngine 實例（共 5 個），規格與 Authentic 相同（256-tap polyphase FIR, SIMD 加速），但獨立運作。降頻後才進行 pan 混合，使左右聲道可以有不同的聲道組成。

#### Triangle Bass Boost — Low-Shelf Biquad EQ

NES 的 Triangle 聲道是 4-bit 步階三角波（只有 16 級），低頻豐滿感天生不足。Bass Boost 使用 Low-Shelf Biquad 濾波器提升指定頻率以下的頻段，僅作用於 Triangle 聲道，不影響其他聲道。

Biquad 係數依 Audio EQ Cookbook 標準公式計算，Q=0.707（Butterworth 特性，無共振峰值）。

#### ModernAudioFX — 空間效果

處理順序為 **Reverb → Haas**，使殘響也受到 Haas 空間化處理。

**Micro-Room Reverb**:
- 4 條平行 Comb Filter，延遲長度互質（避免金屬腔體共振）
- 每條 Comb 的 feedback 路徑含一階 LPF（模擬牆壁高頻吸收）
- Mono sum 輸入 → wet/dry mix 回原 L/R

**Haas Effect**（優先效應）:
- 右聲道經過 10–30 ms 延遲
- 延遲信號以 0–80% 的比例 crossfeed 回左聲道
- 人耳對 <40 ms 的左右時間差感知為「同一聲源但音場更寬」，而非回音

### 可調參數

| 參數 | 欄位 | 範圍 | 說明 |
|------|------|------|------|
| 立體聲寬度 | StereoWidth | 0–100% | 0=mono, 100=最大分離 |
| Bass Boost | BassBoostDb | 0–12 dB | Triangle 低音增強量 |
| Bass 頻率 | BassBoostFreq | 80–300 Hz | Low-shelf 中心頻率 |
| Haas 延遲 | HaasDelay | 10–30 ms | 右聲道延遲時間 |
| Haas Crossfeed | HaasCrossfeed | 0–80% | 延遲 R 混入 L 的比例 |
| 殘響濕度 | ReverbWet | 0–30% | Comb reverb 濕信號量 |
| 殘響回饋 | CombFeedback | 30–90% | 殘響衰減時間 |
| 殘響阻尼 | CombDamp | 10–70% | 高頻吸收程度 |

---

## 三模式比較

| 特性 | Mode 0 Pure Digital | Mode 1 Authentic | Mode 2 Modern |
|------|:---:|:---:|:---:|
| 混音方式 | 整數查找表 | 3D DAC 浮點查找表 | 線性歸一化 |
| DAC 非線性 | 簡化公式 | 精確 NESdev 公式 | 無（線性） |
| 降頻方式 | 每 ~41 cycle 取 1 sample | 256-tap FIR (×1) | 256-tap FIR (×5) |
| Anti-aliasing | 無 | 20 kHz Blackman sinc | 20 kHz Blackman sinc |
| 濾波 | 90 Hz HPF (整數) | 90 Hz HPF (double) + 主機 LPF | 無 |
| 立體聲 | Dual Mono | Dual Mono | True Stereo |
| 空間效果 | 無 | 無 | Haas + Comb Reverb |
| 主機模擬 | 無 | 6 預設 + 自訂 | 無 |
| RF 干擾 | 無 | 可選 | 無 |
| Bass Boost | 無 | 無 | Triangle 專用 |
| CPU 開銷 | 最低 | 中（1 個 FIR） | 最高（5 個 FIR + FX） |
| OversamplingEngine | 0 個 | 1 個 | 5 個 |

---

## 最終輸出：WaveOutPlayer

三種模式最終都透過 `NesCore.AudioSampleReady(short left, short right)` 事件發送樣本給 `WaveOutPlayer`：

```
WaveOutPlayer 配置:
  取樣率:   44,100 Hz
  聲道數:   2 (stereo, L/R interleaved)
  位元深度: 16-bit signed PCM
  緩衝區:   4 × 735 samples 環形緩衝（1 frame = 735 stereo pairs）
  API:      WinMM waveOut* (winmm.dll)

流程:
  AudioSampleReady(L, R)
    → OnSampleReady(L, R): 寫入當前緩衝區
    → 緩衝區滿 735 pairs → SubmitBuffer()
    → waveOutPrepareHeader + waveOutWrite → 系統音訊佇列
    → 切換至下一個環形緩衝區
```

---

## 原始碼對應

| 檔案 | 職責 |
|------|------|
| `NesCore/APU.cs` | APU 聲道模擬、Mode 0 generateSample()、模式分流 |
| `NesCore/AudioPlus/AudioDispatcher.cs` | 總調度器，Mode 1/2 分流 + 最終輸出 |
| `NesCore/AudioPlus/AuthenticAudioMixer.cs` | Mode 1: 3D DAC 非線性混音 + 90 Hz HPF |
| `NesCore/AudioPlus/OversamplingEngine.cs` | Mode 1/2: 256-tap polyphase FIR 降頻 |
| `NesCore/AudioPlus/ConsoleModelFilter.cs` | Mode 1: 主機型號 LPF + Buzz + RF |
| `NesCore/AudioPlus/ModernAudioMixer.cs` | Mode 2: 5 軌獨立超採樣 + stereo pan + Bass Boost |
| `NesCore/AudioPlus/ModernAudioFX.cs` | Mode 2: Haas Effect + Comb Reverb |
| `tool/WaveOutPlayer.cs` | WinMM waveOut PCM 輸出 |
| `UI/AprNes_AudioPlusConfigureUI.cs` | 設定 UI (Authentic 面板 + Modern 面板) |
