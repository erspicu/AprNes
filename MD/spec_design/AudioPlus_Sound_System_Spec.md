# AprNes AudioPlus 音訊系統設計規格書

> 本文件面向兩種讀者：對技術細節不熟悉的一般使用者，以及希望深入了解實作的技術人員。
> 前者可以聚焦於各模式的「設計用意」和「設定說明」；後者可以參考「技術細節」區塊中的信號流和增益計算公式。

---

## 一、設計理念：為什麼需要三種模式？

NES 誕生於 1983 年，五個聲道以 1,789,773 Hz 的速率運作。四十年後的今天，每個人對「NES 應該聽起來怎樣」有不同的期待——有人想要最輕量的標準模擬器體驗，有人想重溫小時候從電視喇叭傳出的聲音，有人則想用現代音響工程重新詮釋這些經典配樂。

AprNes 的 AudioPlus 引擎提供三種模式，不是好壞之分，而是**你想聽到什麼**的選擇。

---

## 二、三種模式總覽

| 模式 | 名稱 | 一句話描述 |
|------|------|-----------|
| Mode 0 | **Pure Digital** | 標準模擬器做法，最低 CPU 負擔，直覺乾脆 |
| Mode 1 | **Authentic** | 重現真實主機的類比電路特性，帶你回到那個年代 |
| Mode 2 | **Modern** | 五軌獨立混音 + 立體聲 + 空間效果，自由調音 |

```
             APU (1,789,773 Hz, 5 聲道 + 擴展音源)
                       │
          ┌────────────┼────────────┐
          │            │            │
     Mode 0        Mode 1       Mode 2
   Pure Digital   Authentic     Modern
          │            │            │
   查找表混音    3D DAC 查表    5+N 軌獨立歸一化
   DC Killer     90Hz HPF      per-ch gain
          │            │            │
   每~41cyc取1   256-Tap FIR   (5+N)×256-Tap FIR
   (簡單降頻)    (超採樣降頻)   (逐軌超採樣降頻)
          │            │            │
          │       主機型號 LPF   Triangle Bass Boost
          │       + Buzz + RF    + Stereo Pan
          │            │         + Haas + Reverb
          │            │            │
     Dual Mono    Dual Mono    True Stereo
          │            │            │
          └────────────┼────────────┘
                       │
                  Soft Limiter
                       │
              WaveOutPlayer / 音訊輸出
            44.1 kHz / 16-bit / Stereo
```

---

## 三、Mode 0：Pure Digital — 標準模擬器體驗

### 設計用意

這是大多數 NES 模擬器採用的方式：把五個聲道的數位輸出經過查找表混合，簡單處理後直接送到喇叭。沒有花俏的濾波，沒有額外的後處理。

選擇這個模式的理由很簡單：**它快、它直接、它就是很多人記憶中模擬器聽起來的樣子**——帶著一點粗糙的數位質感，但乾淨俐落。對於效能敏感的環境，或只是想專注玩遊戲不需要音效加工的場合，Pure Digital 是最合適的選擇。

### 信號處理流程

1. **非線性查表混音**：NES 的 DAC 不是線性加法。Pulse 1 + Pulse 2 合併查一張 `SQUARELOOKUP[31]` 表；Triangle + Noise + DMC 合併查另一張 `TNDLOOKUP[203]` 表（索引公式 `3×tri + 2×noise + dmc`）。這是 NESdev 社群記載的標準近似做法。
2. **擴展音源加總**：Mapper 擴展聲道（VRC6、VRC7、N163 等）的混合輸出直接以整數加到查表結果上。
3. **DC Killer**：一個簡單的高通濾波器消除直流偏移，避免喇叭振膜長期偏向一側。
4. **主音量縮放**：乘以使用者設定的總音量，clamp 至 16-bit signed 範圍。
5. **輸出**：Dual Mono（左右聲道相同）。

### 使用者可調設定

| 設定 | 說明 |
|------|------|
| 總音量 (Volume) | 主音量滑桿，影響最終輸出大小 |
| 聲道啟用 (Enable) | 可逐聲道 mute/unmute（NES 5ch + 擴展聲道） |

> Pure Digital 模式下，個別聲道的音量 TrackBar **不生效**——因為查表混音的比例由硬體公式決定，不額外干預。這是刻意的設計：保持標準模擬器的簡潔性。

---

## 四、Mode 1：Authentic — 重現真實主機

### 設計用意

真實的 NES 不是把數位信號直接送到耳朵的。信號經過 DAC 轉換成類比電壓，通過主機內部電路的濾波，透過 RF 線或 AV 端子輸出，最後從電視喇叭發出聲音。每一個環節都會改變音色。

Authentic 模式的目標是**完整重現這條類比路徑**。選擇不同的主機型號，你會聽到截然不同的音色——就像當年在朋友家和自己家聽到的 NES 不太一樣。

### 信號處理流程

1. **物理級 DAC 查表**：使用 NESdev 社群記錄的精確 DAC 公式，預計算了一張完整的 3D 查找表（`authMix_tndTable[]`，以 `tri<<11 | noise<<7 | dmc` 為索引），比 Pure Digital 的線性近似更精確，輸出為浮點電壓值。
2. **擴展音源混入**：擴展聲道以 `expansionAudio / 98302.0` 歸一化後加入總電壓。
3. **256-Tap FIR 超採樣降頻**：把 1.79 MHz 的完整波形用 128 組多相位 Blackman-windowed sinc FIR 濾波器精確降頻至 44.1 kHz，旁瓣衰減 > 58 dB，消除混疊失真。
4. **主機型號濾波 (CMF)**：根據所選主機型號施加對應的低通濾波特性。
5. **可選效果**：60 Hz Buzz（交流電哼聲）、RF 串擾（視訊信號竄入音訊）。
6. **輸出**：Dual Mono。

### 六款主機音色

不同版本的 NES 硬體，因為內部電路設計的差異，聲音聽起來截然不同：

| 主機型號 | 低通截止頻率 | 聲音特徵 |
|---------|------------|---------|
| Famicom (HVC-001) | ~14 kHz | 明亮清晰，經典紅白機音色 |
| Front-Loader (NES-001) | ~4.7 kHz | 溫暖厚實，許多西方玩家的童年記憶 |
| Top-Loader (NES-101) | ~20 kHz | 銳利通透，伴隨 60 Hz 交流電哼聲 |
| AV Famicom (HVC-101) | ~19 kHz | 乾淨直出，後期改良版 |
| Sharp Twin Famicom | ~12 kHz | 略暗於原版，Sharp 聯名機 |
| Sharp Famicom Titler | ~16 kHz | S-Video 等級音質 |
| **Custom（自訂）** | 1,000–22,000 Hz | 自由設定截止頻率、Buzz、RF 參數 |

### 使用者可調設定

| 設定 | 說明 | 範圍 |
|------|------|------|
| Console Model | 主機型號選擇 | 0–6（含 Custom） |
| RF Crosstalk | RF 音視串擾開關 | On/Off |
| Custom LPF Cutoff | 自訂低通截止頻率（僅 Custom 模式） | 1,000–22,000 Hz |
| Custom Buzz | 自訂交流電哼聲開關（僅 Custom 模式） | On/Off |
| Buzz Amplitude | 哼聲振幅（所有有 Buzz 的型號） | 0–100 |
| Buzz Freq | 哼聲頻率 | 50 Hz（歐規）/ 60 Hz（美規） |
| RF Volume | RF 串擾音量 | 0–200 |
| 聲道啟用 (Enable) | 逐聲道 mute/unmute | On/Off per channel |

> 與 Pure Digital 相同，Authentic 模式下個別聲道的音量 TrackBar **不生效**。NES 5ch 的音量比例由 DAC 查表決定，擴展聲道則使用統一的整體增益——這確保了類比模擬的一致性。

---

## 五、Mode 2：Modern — 自由調音台

### 設計用意

Modern 模式跳脫了硬體模擬的框架。它的核心思路是：**把每個聲道當作錄音室裡的獨立軌道，讓使用者像混音師一樣自由調配**。

NES 原本是單聲道輸出，五個聲道混在一起就無法再分開。Modern 模式在降頻之前就把每個聲道隔離，各自獨立處理，所以才能做到逐軌音量控制、立體聲定位、只針對某個聲道加 EQ 等操作。

### 信號處理流程

1. **逐軌歸一化**：每個聲道乘以各自的 `mmix_chGain[]`，將原始整數值正規化至 0–1.0 浮點範圍。NES 5ch 使用 `normScale`（Pulse: 1/15, DMC: 1/127），擴展聲道使用 `Mode2ExpChNorm[]`（因每種晶片的輸出範圍不同）。
2. **用戶音量疊加**：`chGain = normScale × (ChannelVolume / 70)`。70% 對應 1.0× 校準基準，100% 對應 1.43× 增益，0% 靜音。
3. **(5+N) × 256-Tap FIR 超採樣降頻**：每個聲道各自擁有獨立的超採樣引擎，先降頻再混音。
4. **Triangle Bass Boost**：針對 Triangle 聲道的 Low-Shelf Biquad EQ（80–300 Hz, 0–12 dB），補償 4-bit 步階波天生的低頻不足。只動 Triangle，不影響其他聲道。
5. **Stereo Pan**：Pulse 1 偏左 (0.7)、Pulse 2 偏右 (0.7)、其餘置中，StereoWidth 可調分離程度。
6. **Haas Effect**：右聲道延遲 10–30 ms + crossfeed 回饋，利用心理聲學擴展音場寬度。
7. **Micro-Room Reverb**：四條互質延遲的平行 Comb Filter + 高頻阻尼，模擬小房間空間感。
8. **Soft Limiter**：漸進式壓縮，超過 80% 動態範圍的部分平滑壓限，避免硬 clip 爆音。
9. **輸出**：True Stereo。

### 使用者可調設定

| 設定 | 說明 | 範圍 | 預設 |
|------|------|------|------|
| Stereo Width | 立體聲寬度 | 0–100%（0=mono） | 50 |
| Haas Delay | 右聲道延遲 | 10–30 ms | 20 |
| Haas Crossfeed | 延遲信號回饋比例 | 0–80% | 40 |
| Reverb Wet | 殘響濕度 | 0–30%（0=Off） | 0 |
| Comb Feedback | 殘響長度 | 30–90% | 70 |
| Comb Damp | 高頻阻尼 | 10–70% | 30 |
| Bass Boost dB | Triangle 低音增強 | 0–12 dB（0=Off） | 0 |
| Bass Boost Freq | 增強中心頻率 | 80–300 Hz | 150 |
| 聲道音量 (Volume) | 逐軌獨立音量 | 0–100%（70%=校準基準） | 70 |
| 聲道啟用 (Enable) | 逐軌 mute/unmute | On/Off | On |

---

## 六、聲道音量與啟用控制 — 跨模式行為

這是整個音訊系統中最需要理解清楚的部分。不同的控制項在不同模式下的行為不同，這是刻意的設計。

### NES 內建 5 聲道（Pulse 1, Pulse 2, Triangle, Noise, DMC）

| 控制項 | Pure (0) | Authentic (1) | Modern (2) |
|--------|----------|---------------|------------|
| **Enable（啟用 checkbox）** | ✅ 生效 | ✅ 生效 | ✅ 生效 |
| **Volume（音量 trackbar）** | — 不生效 | — 不生效 | ✅ 逐軌獨立 |

**為什麼 Pure / Authentic 的音量不可調？**

Pure 和 Authentic 模式使用硬體公式的查表混音（非線性 DAC 查找表），五個聲道之間的音量比例由 NES 硬體電路決定。如果允許使用者任意調整個別聲道音量，就等於破壞了這個硬體特性——Pulse 1 輸出 7 加 Pulse 2 輸出 8 在非線性 DAC 中的結果，跟各自乘以不同倍率後再查表是完全不同的。

Modern 模式則完全不同：它把每個聲道獨立歸一化後線性混音，音量調整是在歸一化之後施加的，不存在非線性交互的問題。

**Enable 為什麼全模式都能用？**

Mute（靜音）是一個工具性功能——等同於物理上斷開某個聲道。它不改變其他聲道的混音行為，只是讓被 mute 的聲道輸出零值。這在任何模式下都是安全且有意義的操作（例如想單獨聽某個聲道、或排除某個聲道的干擾）。

### Mapper 擴展聲道（VRC6, VRC7, N163, Sunsoft 5B, MMC5, FDS）

| 控制項 | Pure (0) | Authentic (1) | Modern (2) |
|--------|----------|---------------|------------|
| **Enable（啟用 checkbox）** | ✅ 逐軌生效 | ✅ 逐軌生效 | ✅ 逐軌生效 |
| **Volume（音量 trackbar）** | ⚠️ 整體增益 | ⚠️ 整體增益 | ✅ 逐軌獨立 |

**Pure / Authentic 的「整體增益」是什麼意思？**

在 Mode 0 和 Mode 1 中，所有啟用的擴展聲道共用一個統一的增益值 `ap_mode01ExpGain`。這個值是從所有啟用聲道的 Volume 設定取平均後，再乘以該晶片的基礎增益 (`DefaultChipGain`) 計算出來的。

這意味著：如果你把 VRC6 的三個聲道分別設為 100%、50%、20%，在 Mode 0/1 下它們會被平均成 (100+50+20)/3 ≈ 57% 的統一增益——三個聲道的比例不會改變，只是整體變小聲。

在 Mode 2 下則是真正的逐軌獨立：100% 的聲道明顯比 20% 的大聲。

**為什麼擴展聲道在 Pure / Authentic 也允許調音量（雖然是整體的）？**

真實硬體中，擴展音源的音量取決於卡帶電路設計，不同遊戲、不同卡帶版本的擴展聲道大小聲差異很大。這不像 NES 內建 5ch 有精確的 DAC 公式可循，所以即使在強調硬體模擬的模式下，提供整體音量調整也是合理的。

### UI 標籤對照

設定介面中的灰色提示文字對應以下行為：

| 區域 | 提示文字 | 含義 |
|------|---------|------|
| NES 5 Channel 下方 | 啟用：所有模式 ｜ 音量：僅 Modern 模式 | checkbox 三模式都生效，trackbar 只在 Modern 生效 |
| Expansion Channel 上方 | 啟用：所有模式 ｜ 音量：Modern 逐軌，其餘整體 | checkbox 三模式都生效，trackbar 在 Modern 逐軌獨立、Pure/Authentic 為整體平均增益 |

---

## 七、70% 校準基準的設計

Modern 模式的聲道音量 TrackBar 範圍是 0–100%，但**預設值是 70%，而非 100%**。

這是刻意的設計：

- **70% = 1.0× 校準增益**：這是我們實際校調各晶片音量平衡後的基準點，代表「經過校準的舒適音量」。
- **100% = 1.43× 增益**：提供約 43% 的上調空間，讓偏好某個聲道更突出的使用者有餘裕。
- **0% = 靜音**。

為什麼不直接把校準點設在 100%？因為這樣使用者就只能往下調、不能往上調。70% 的基準讓音量控制是**雙向的**——可以減弱也可以增強，更符合實際使用需求。

超過 1.0× 增益可能導致峰值超過 16-bit 範圍，這由 Soft Limiter 處理：低於 80% 動態範圍的信號完全線性通過，超過部分漸進壓縮，避免硬 clip 爆音。

### 技術細節：增益計算公式

```
NES 聲道:
  mmix_chGain[i] = ChannelEnabled[i]
    ? normScale[i] × (ChannelVolume[i] / 70.0)
    : 0

擴展聲道 (Modern):
  mmix_chGain[5+i] = ChannelEnabled[5+i]
    ? Mode2ExpChNorm[chipType] × (ChannelVolume[5+i] / 70.0)
    : 0

擴展聲道 (Pure / Authentic):
  avgVol = Σ(啟用聲道的 ChannelVolume) / 聲道總數 / 70.0
  ap_mode01ExpGain = DefaultChipGain[chipType] × avgVol
```

### 各晶片正規化參數

**Mode 2 (Modern) — `Mode2ExpChNorm[]`**

| 晶片 | 正規化值 | 說明 |
|------|---------|------|
| VRC6 | 1/15 | Pulse 0–15 → 1.0，Saw 0–31 超出部分由 Soft Limiter 處理 |
| VRC7 | 1/2000 | OPLL 混合輸出 ±12285，保守正規化 |
| N163 | 0.25/15 | Mapper 已除以 activeCh，per-ch 約 0–15，再 ×0.25 |
| Sunsoft 5B | 1/32 | AY 對數 DAC，遊戲常用 vol 10–13，volumeLut[10]=31 ≈ 1.0 |
| MMC5 | 1/15 | 同 NES Pulse |
| FDS | 1/63 | 6-bit wavetable 0–63 |

**Mode 0/1 (Pure / Authentic) — `DefaultChipGain[]`**

| 晶片 | 增益 | 目標 |
|------|------|------|
| VRC6 | 740 | 讓 max 輸出 ≈ APU 範圍的 1/2 (~45000) |
| VRC7 | 3 | OPLL raw ±12285 × 3 → ~37000 |
| N163 | 500 | Mapper 已除以 (numCh+1)，×500 → ~60000 |
| Sunsoft 5B | 120 | sum×120 → ~64000 |
| MMC5 | 43 | — |
| FDS | 20 | — |

---

## 八、Mapper 擴展音源晶片對照

在 Channel Volume 設定介面中，Mapper Sound Chip 下拉選單對應以下 Mapper 編號：

| 晶片名稱 | 對應 Mapper | 聲道數 | 聲道名稱 |
|----------|------------|--------|---------|
| VRC6 | 024 (VRC6a), 026 (VRC6b) | 3 | Pulse 1, Pulse 2, Saw |
| VRC7 | 085 | 1 (混合輸出) | FM |
| Namco 163 | 019 | 最多 8 | Ch1–Ch8 |
| Sunsoft 5B | 069 (FME-7/5B) | 3 | Ch A, Ch B, Ch C |
| MMC5 | 005 | 2 | Pulse 1, Pulse 2 |
| FDS | Famicom Disk System | 1 | Wave |

選擇不同的晶片，下方會自動顯示對應數量的聲道控制項。每種晶片的音量和啟用設定**獨立儲存**，切換晶片時不會互相覆蓋。

設定會存入 `AprNesAudioPlus.ini`，以 per-chip prefix 區分（如 `ChVol_VRC6_0=70`、`ChVol_N163_3=70`）。載入遊戲時，會自動偵測遊戲使用的擴展晶片並套用對應的設定。

---

## 九、UI 設定介面結構

AudioPlus Settings 視窗分為四個區塊：

### 1. Authentic 設定群組
Console Model 選擇、RF Crosstalk 開關、Custom 模式專用參數（LPF Cutoff、Buzz）、通用微調（Buzz Amplitude/Freq、RF Volume）。僅影響 Mode 1。

### 2. Modern 設定群組
Stereo Width、Haas Effect（Delay + Crossfeed）、Reverb（Wet + Feedback + Damping）、Bass Boost（dB + Freq）。僅影響 Mode 2。

### 3. Channel Volume 群組

左側為 NES 5 內建聲道，右側為 Mapper 擴展聲道。每個聲道包含：

- **CheckBox**（啟用/停用）：控制該聲道是否輸出聲音
- **Label**（聲道名稱）：顯示聲道識別名稱
- **TrackBar**（音量 0–100%）：控制該聲道的音量大小
- **Value Label**（數值顯示）：拖動 TrackBar 時即時顯示目前設定的百分比

擴展聲道區上方顯示灰色提示，說明所選晶片對應的 Mapper 編號以及各控制項的生效模式。

### 4. OK / Cancel 按鈕
OK 會將所有設定寫回 NesCore 並立即套用（更新增益、重建音訊管線），同時存入 INI 檔案。Cancel 則不儲存直接關閉。

### 多語支援

介面支援英文 (en-us)、繁體中文 (zh-tw)、簡體中文 (zh-cn) 三種語言，所有 UI 文字（標題、群組名、標籤、提示文字、按鈕）皆透過 `AprNesLang.ini` 載入，可在不修改程式碼的情況下擴充新語言。

---

## 十、設定檔持久化

所有 AudioPlus 相關設定存放於 `configure/AprNesAudioPlus.ini`：

- **Authentic 參數**：ConsoleModel, RfCrosstalk, CustomLpfCutoff, CustomBuzz, BuzzAmplitude, BuzzFreq, RfVolume
- **Modern 參數**：StereoWidth, HaasDelay, HaasCrossfeed, ReverbWet, CombFeedback, CombDamp, BassBoostDb, BassBoostFreq
- **NES 聲道**：ChVol_Pulse1/Pulse2/Triangle/Noise/DMC, ChEn_同
- **擴展聲道**：以晶片名稱為前綴，ChVol_VRC6_0~7, ChEn_VRC6_0~7, ... ChVol_FDS_0~7, ChEn_FDS_0~7

若 INI 檔案不存在，系統會使用程式碼內建的預設值正常運作（所有音量預設 70、所有聲道預設啟用），並在使用者首次儲存設定時自動建立完整的 INI 檔案。

AudioMode（Pure/Authentic/Modern 的選擇）存放於主設定檔 `AprNes.ini` 中。

---

## 十一、技術特色摘要

- **256-Tap Blackman-windowed sinc FIR 降頻**：旁瓣衰減 > 58 dB，128 組多相位核心處理非整數降頻比 (1,789,773 / 44,100 ≈ 40.584)，消除混疊失真
- **逐軌獨立超採樣 (Modern)**：每個聲道各自降頻後再混音，實現真正的 per-channel 處理能力
- **物理級 3D DAC 查表 (Authentic)**：完整的 Triangle × Noise × DMC 三維組合查表，重現非線性電阻梯級網路的交互作用
- **六款真實主機濾波特性**：基於實測數據的 IIR 低通濾波，加上可選的 60 Hz Buzz 和 RF 串擾
- **Soft Limiter**：漸進式壓縮取代硬 clip，±80% 以下線性通過，超出部分平滑壓限至 ±32767
- **70% 校準基準**：雙向音量調整空間（可減弱至靜音、可增強至 1.43×），搭配 Soft Limiter 避免爆音
- **per-chip INI 持久化**：切換不同 Mapper 晶片時各自保留獨立設定，不互相覆蓋

---

## 十二、邀請回饋

這套音訊系統的設計經過多輪迭代與校調，但我們深知在信號處理、心理聲學、硬體模擬等領域，仍可能存在設計上的盲區、理解上的偏差、或值得精進之處。

如果你在使用過程中發現：
- 某個模式的聽感不符預期
- 某種 Mapper 晶片的音量平衡不理想
- 技術實作上有可以改善的地方
- 或者只是覺得「這裡應該可以做得更好」

都非常歡迎提出意見與建議。
