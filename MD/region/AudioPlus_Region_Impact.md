# AudioPlus 受 NTSC / PAL / Dendy Region 影響分析

> 2026-03-29

---

## 核心結論

AudioPlus 管線**不含任何 per-sample 或 per-tick 的 Region 判斷**。所有 region 差異透過 `AudioPlus_ApplyRegion()` 在初始化時一次設定完成，運行時只讀取已計算好的常數。

---

## Region-dependent 變數一覽

### `AudioPlus_ApplyRegion()` 設定的 5 個變數

| 變數 | NTSC | PAL | Dendy | 用途 |
|------|------|-----|-------|------|
| `AP_CPU_FREQ` | 1,789,773 Hz | 1,662,607 Hz | 1,773,447 Hz | 所有取樣率計算的基礎 |
| `AP_CLOCKS_PER_SAMPLE` | 40.584 | 37.700 | 40.214 | CPU clocks / audio sample (44100 Hz) |
| `OSE_CUTOFF_NORM` | 0.01117 | 0.01203 | 0.01128 | Oversampler 低通截止頻率 (20kHz / cpuFreq) |
| `OSE_CLOCKS_PER_SAMPLE_FP` | 2,660,824 | 2,471,019 | 2,636,590 | 定點數版 clocks/sample (×65536) |
| `CMF_RF_PHASE_INC` | 59.94/44100 | 50.0/44100 | 50.0/44100 | RF buzz 載波相位增量 |

### 各變數的影響分析

#### 1. `AP_CPU_FREQ` / `AP_CLOCKS_PER_SAMPLE` (取樣率轉換)

**影響模組**: BandlimitedSynthesizer (`blip_`)

```
blip_clockAccum += 1.0 (每 CPU tick)
當 blip_clockAccum >= AP_CLOCKS_PER_SAMPLE → 輸出一個 44100 Hz sample
```

| Region | CPU 頻率 | clocks/sample | 效果 |
|--------|---------|--------------|------|
| NTSC | 1.7898 MHz | ~40.58 | 每 ~40.6 個 CPU tick 產出 1 sample |
| PAL | 1.6626 MHz | ~37.70 | 每 ~37.7 個 CPU tick 產出 1 sample |
| Dendy | 1.7734 MHz | ~40.21 | 每 ~40.2 個 CPU tick 產出 1 sample |

PAL 的 CPU 較慢，每個 sample 的 tick 數較少。若不調整，PAL 會產出 44100×(40.58/37.70) ≈ 47,522 samples/s → 音高偏高、tempo 偏快。`AP_CLOCKS_PER_SAMPLE` 的更新確保 PAL 仍正確輸出 44100 Hz。

**路徑**: hot path (per CPU tick accumulate, per sample output)
**狀態**: ✅ 已由 `AudioPlus_ApplyRegion()` 正確處理

#### 2. `OSE_CUTOFF_NORM` / `OSE_CLOCKS_PER_SAMPLE_FP` (Oversampler 引擎)

**影響模組**: OversamplingEngine (`ose_`)

Oversampler 在 CPU 頻率上做多相 polyphase FIR 濾波，截止頻率設在 20 kHz：

```
OSE_CUTOFF_NORM = 20000.0 / AP_CPU_FREQ
```

| Region | 截止歸一化值 | 說明 |
|--------|------------|------|
| NTSC | 0.01117 | 20kHz / 1.79MHz |
| PAL | 0.01203 | 20kHz / 1.66MHz — 較高（CPU 較慢，Nyquist 較低） |
| Dendy | 0.01128 | 20kHz / 1.77MHz |

`ap_tablesInitialized = false` 強制下次 `ap_InitTables()` 重建 sinc kernel，反映新的截止頻率。

**路徑**: cold path (kernel 建表時)，hot path (卷積時使用已建好的 kernel)
**狀態**: ✅ 已由 `AudioPlus_ApplyRegion()` 正確處理

#### 3. `CMF_RF_PHASE_INC` (RF buzz 頻率)

**影響模組**: ConsoleModelFilter (`cmf_`)

模擬不同主機型號的 RF 輸出 buzz（源自 NTSC/PAL 的場頻干擾）：

```
CMF_RF_PHASE_INC = (Region == NTSC ? 59.94 : 50.0) / 44100
```

| Region | 場頻 | 相位增量 | buzz 基頻 |
|--------|------|---------|----------|
| NTSC | 59.94 Hz | 0.001359 | ~60 Hz hum |
| PAL | 50.0 Hz | 0.001134 | ~50 Hz hum |
| Dendy | 50.0 Hz | 0.001134 | ~50 Hz hum（與 PAL 相同） |

**路徑**: per-sample (RF 模式啟用時)
**狀態**: ✅ 已由 `AudioPlus_ApplyRegion()` 正確處理

---

## 不受 Region 影響的模組

| 模組 | 說明 |
|------|------|
| ModernMixer (`mmix_`) | 5ch pan + Haas delay — 純音訊效果，不依賴硬體時脈 |
| MasterFX (`mfx_`) | Soft limiter + stereo widener — 純 DSP 後處理 |
| Mode2 expansion channel routing | per-channel 正規化常數為 hardcode，與 region 無關 |
| DAC lookup tables (`ap_lutPulse` 等) | NES 混音非線性表，三個 region 的 DAC 硬體相同 |

---

## 呼叫時機與初始化順序

```
ROM 載入 / HardReset
  └─ init()
       ├─ ApplyRegionProfile()      ← 設定 cpuFreq, FrameSeconds
       ├─ initAPU()                  ← APU 取 cpuFreq 算 _cycPerSample
       └─ AudioPlus_Init()
            ├─ AudioPlus_ApplyRegion()  ← 取 cpuFreq 算 5 個頻率常數
            ├─ ap_InitTables()          ← 用新 OSE_CUTOFF_NORM 建 kernel
            └─ ...
```

Runtime Region 切換（UI 選單）→ `HardReset()` → 重跑 `init()` → 完整重新初始化。

**注意**: `AudioPlus_ApplyRegion()` 目前**只在 `AudioPlus_Init()` 內呼叫**，不會被單獨呼叫。如果未來支援不經 HardReset 的 Region 熱切換，需要額外呼叫此函式。

---

## 基礎 APU (APU.cs) 的 Region 影響

為完整性一併列出：

| 項目 | NTSC | PAL | Dendy |
|------|------|-----|-------|
| `_cycPerSample` | 40.58 | 37.70 | 40.21 |
| Noise period 表 | NTSC 表 | **PAL 表** (不同週期) | **NTSC 表** |
| DMC rate 表 | NTSC 表 | **PAL 表** (不同週期) | **NTSC 表** |
| Frame counter IRQ | 啟用 | 啟用 | **禁用** (UMC bug) |
| Frame counter 步數/週期 | NTSC timing | PAL timing | **NTSC timing** |

PAL 使用獨立的 noise/DMC period 表（週期不同），Dendy 使用 NTSC 表。這些在 `APU.cs` 中已正確處理。

---

## 潛在風險

| 風險 | 嚴重度 | 說明 |
|------|--------|------|
| AP_CLOCKS_PER_SAMPLE 靜態初始值為 NTSC | 低 | `AudioPlus_ApplyRegion()` 在 init 時覆蓋，不影響運行。但如果 AudioPlus 在 ApplyRegion 之前被意外呼叫，會用錯誤的 NTSC 值 |
| OSE kernel 未 rebuild | 低 | `ap_tablesInitialized = false` 已設定，下次 `ap_InitTables()` 會重建。但若 `AudioPlus_Init()` 不被重新呼叫（如熱切換），kernel 不會更新 |
| RF buzz 二分法 (NTSC vs 非NTSC) | 無 | Dendy 實際場頻 50Hz，歸類為非 NTSC 正確 |
