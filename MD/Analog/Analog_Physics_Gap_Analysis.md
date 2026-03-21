# 類比模式物理模擬缺口分析

**日期**: 2026-03-21
**分析對象**: UltraAnalog + CRT + RF 全開狀態（Level 3 + Stage 2）
**目的**: 盤點目前實作相對真實 NTSC → CRT 硬體的簡化與缺失

---

## Stage 1: NTSC 訊號鏈 (Ntsc.cs)

### 已實作的物理模型

- 21.477 MHz 複合波形生成（4 samples/dot, 6-phase subcarrier）
- Blargg 電壓準位（loLevels/hiLevels, 4 luma levels）
- Hann 窗 FIR 相干解調（Y=6tap, I=18tap, Q=54tap）
- SlewRate IIR 頻寬限制 + ChromaBlur IIR 色度低通
- RF 雜訊 (xorshift PRNG) + 60Hz 音訊干擾 (AM buzz)
- Dot crawl（每條 scanline 相位 +2）
- ~~Emphasis bits 衰減（簡化版）~~ → ✅ Per-phase 方向性衰減（已完成）
- 三種端子參數組（RF / AV / S-Video）
- ✅ Color burst 相位抖動（RF 模式，~3% scanlines）
- ✅ 可調色溫（ColorTempR/G/B，YIQ→RGB 矩陣偏移）
- ✅ 可調 Gamma 係數（GammaCoeff）
- ✅ RF herringbone（4.5MHz per-sample 正弦振盪器，取代 per-line buzzRow）
- ✅ Ringing / 振鈴（damped spring 二階 IIR，RingStrength=0.3）
- ✅ 水平消隱區 HBI（IIR 初始值=blanking level 0.0）

### 簡化或缺少的部分

| # | 項目 | 現況 | 真實硬體行為 | 影響程度 |
|:-:|------|------|-------------|:--------:|
| 2 | **副載波頻率** | 精確 6:1 整數比（21.477/3.58=5.9966… 近似為 6） | 真實比值為無理數，長期相位緩慢漂移，產生幀間 beating 效果 | 低 — 差異極微 |
| 6 | **垂直色度濾波 (Comb filter)** | 純單行 1H 解調 | 較好的 CRT TV 用 2-line 或 3-line 梳狀濾波分離亮度/色度，減少 dot crawl 但引入垂直色度模糊 | 中 — 取決於模擬哪種等級的 TV |
| 7 | **RF 調諧/IF 鏈** | noise + buzz 直接加在複合訊號 | 真實 RF: 天線 → 調諧器 → IF 放大 → 包絡檢波 → 複合視訊，每級加入特定頻響和噪聲特性 | 低 — 視覺差異小 |
| 8 | **多路徑 / 鬼影 (Ghosting)** | 無 | RF 反射造成延遲副本疊加，出現「鬼影」和水平偏移的半透明副像 | 低 — 屬於接收環境瑕疵 |

---

## Stage 2: CRT 電視光學 (CrtScreen.cs)

### 已實作的物理模型

- 垂直高斯掃描線 beam profile (`exp(-dy²/2σ²)`)
- Brightness-dependent bloom（高光溢出到掃描線間隙）
- BrightnessBoost 補償掃描線黑溝亮度損失
- ~~Fast gamma 近似 (`v += 0.229f * v * (v-1)`)~~ → ✅ 可調 gamma 係數（GammaCoeff）
- 三種端子參數組（BeamSigma / BloomStrength / BrightnessBoost）
- ✅ 邊緣暗角 Vignette（VignetteStrength，拋物線垂直衰減）
- ✅ 隔行抖動 Interlace Jitter（±0.25px/frame，可開關）
- ✅ Shadow mask / Aperture grille（3px RGB 磷光條紋/點陣，Parallel.For 後處理）
- ✅ 螢幕曲率（barrel distortion，預計算 remap table + Parallel.For 後處理）

### 簡化或缺少的部分

（中成本項目已全部實作，無剩餘缺口）

---

## ✅ 已完成項目

### 零成本（2026-03-21）

| # | 項目 | 實作方式 | 所在檔案 |
|:-:|------|----------|----------|
| 1 | **Emphasis bits 修正** | Per-phase 方向性衰減（emphAtten table + Fourier 預計算 yBaseE/iBaseE/qBaseE） | Ntsc.cs |
| 3 | **Color burst 相位抖動** | RF 模式 ~3% scanlines ±1 phase（xorshift PRNG） | Ntsc.cs |
| 15 | **邊緣暗角 (Vignette)** | 拋物線垂直衰減，VignetteStrength=0.15 | CrtScreen.cs |
| 16 | **色溫 / 白點** | ColorTempR/G/B 乘入 YIQ→RGB 矩陣，支援自訂 | Ntsc.cs |
| 17 | **Gamma 精確性** | GammaCoeff 可調，預設 0.229 ≈ pow(v,1/1.13) | Ntsc.cs + CrtScreen.cs |
| 18 | **隔行 / 場抖動** | InterlaceJitter ±0.25px/frame（需 Phosphor persistence 配套才適合預設開啟） | CrtScreen.cs |

### 低成本（2026-03-21）

| # | 項目 | 實作方式 | 所在檔案 |
|:-:|------|----------|----------|
| 11 | **Shadow mask / Aperture grille** | 3px RGB 磷光條紋（ApertureGrille）或偏移點陣（ShadowMask），Parallel.For 後處理，預設強度 0.3 | CrtScreen.cs |
| 9 | **RF herringbone** | 4.5MHz per-sample 正弦振盪器（recursive oscillator），60Hz 包絡調制，Level 2 + Level 3 | Ntsc.cs |
| 14 | **螢幕曲率** | Barrel distortion，預計算 remap table，Parallel.For 後處理，預設強度 0.12 | CrtScreen.cs |

### 中成本（2026-03-21）

| # | 項目 | 實作方式 | 所在檔案 |
|:-:|------|----------|----------|
| 10 | **Phosphor persistence** | 前一幀緩衝區 + per-channel `max(current, prev * decay)`，PhosphorDecay=0.6，Parallel.For 後處理 | CrtScreen.cs |
| 12 | **水平 beam 擴散** | linearBuffer 3-tap FIR 模糊（[α, 1-2α, α]），HBeamSpread=0.4，Parallel.For per-plane | CrtScreen.cs |
| 5 | **Ringing / 振鈴** | Damped spring 二階 IIR（`vVel = vVel * ringDamp + err * SlewRate`），RingStrength=0.3，Level 2 + Level 3 全路徑 | Ntsc.cs |
| 4 | **HBI 模擬** | IIR 初始值改為 blanking level（0.0f），HbiSimulation=true，Level 2 + Level 3 全路徑 | Ntsc.cs |
| 13 | **Beam convergence** | R/B 通道水平偏移（邊緣遞增），ConvergenceStrength=2.0px，Parallel.For 後處理 | CrtScreen.cs |

---

## 未完成項目 — 依效能成本分群

### 🔴 高成本（預估 >15 FPS）

| 優先 | 項目 | 預估難度 | 預估消耗 | 說明 |
|:----:|------|:--------:|:--------:|------|
| 6 | **Comb filter** (#6) | 高 | ~15-20 FPS | 需跨行緩衝，改變單行獨立的解調架構 |

### ⚪ 不建議實作

| # | 項目 | 理由 |
|:-:|------|------|
| 2 | **副載波頻率** | 差異極微（6:1 vs 5.9966:1），視覺不可辨 |
| 7 | **RF 調諧/IF 鏈** | 視覺差異小，複雜度高 |
| 8 | **多路徑 / 鬼影** | 屬接收環境瑕疵，非 CRT 本質特性 |

---

## 備註

- **效能預算**: 目前 4x 解析度 107 FPS (1.79x 即時)，有約 44 FPS 餘裕
- **零成本 + 低成本 + 中成本群已全部實作**: 實際消耗待測
- 剩餘僅 Comb filter（高成本）和 3 項不建議實作的項目
