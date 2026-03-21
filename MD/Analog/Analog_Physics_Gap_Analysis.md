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
- Emphasis bits 衰減（簡化版）
- 三種端子參數組（RF / AV / S-Video）

### 簡化或缺少的部分

| # | 項目 | 現況 | 真實硬體行為 | 影響程度 |
|:-:|------|------|-------------|:--------:|
| 1 | **Emphasis bits** | `attenTab[popcount(bits)]` — 只算開幾個 bit，均勻衰減 | 3 個 emphasis bit 各自衰減特定副載波相位（R tint 衰減非紅相位、G/B 同理），是方向性的 | **高** — 使用 emphasis 的遊戲會出現錯誤色彩 |
| 2 | **副載波頻率** | 精確 6:1 整數比（21.477/3.58=5.9966… 近似為 6） | 真實比值為無理數，長期相位緩慢漂移，產生幀間 beating 效果 | 低 — 差異極微 |
| 3 | **Color burst** | 假設完美鎖相，無 burst 模擬 | 每行 back porch 有 ~9 週期 color burst 做相位參考，TV 解調器可能有相位抖動 | 低 |
| 4 | **水平消隱區 (HBI)** | 只模擬 256 dot 有效區域 | 完整 scanline 有 341 dot（含 sync tip, breezeway, burst, back porch），消隱區的 IIR 狀態會影響行首色彩 | 中 — 行首可能稍有偏差 |
| 5 | **Ringing / 振鈴** | 一階 IIR 低通（smooth） | 真實同軸線/RF 通道有帶限傳輸函數，尖銳色彩邊緣會出現 Gibbs 現象（過衝 + 阻尼振盪） | 中 — 影響銳利邊緣的「光暈」感 |
| 6 | **垂直色度濾波 (Comb filter)** | 純單行 1H 解調 | 較好的 CRT TV 用 2-line 或 3-line 梳狀濾波分離亮度/色度，減少 dot crawl 但引入垂直色度模糊 | 中 — 取決於模擬哪種等級的 TV |
| 7 | **RF 調諧/IF 鏈** | noise + buzz 直接加在複合訊號 | 真實 RF: 天線 → 調諧器 → IF 放大 → 包絡檢波 → 複合視訊，每級加入特定頻響和噪聲特性 | 低 — 視覺差異小 |
| 8 | **多路徑 / 鬼影 (Ghosting)** | 無 | RF 反射造成延遲副本疊加，出現「鬼影」和水平偏移的半透明副像 | 低 — 屬於接收環境瑕疵 |
| 9 | **RF 音訊干擾細節** | `buzzRow` = 每行一個常數值 | 真實 4.5 MHz 音訊載波與視訊載波拍差，在行內產生逐 sample 的 herringbone 干擾紋路 | 中 — 目前只有行級條紋，缺少行內紋理 |

---

## Stage 2: CRT 電視光學 (CrtScreen.cs)

### 已實作的物理模型

- 垂直高斯掃描線 beam profile (`exp(-dy²/2σ²)`)
- Brightness-dependent bloom（高光溢出到掃描線間隙）
- BrightnessBoost 補償掃描線黑溝亮度損失
- Fast gamma 近似 (`v += 0.229f * v * (v-1)`)
- 三種端子參數組（BeamSigma / BloomStrength / BrightnessBoost）

### 簡化或缺少的部分

| # | 項目 | 現況 | 真實 CRT 行為 | 影響程度 |
|:-:|------|------|--------------|:--------:|
| 10 | **磷光體餘輝 (Phosphor persistence)** | 無 — 每幀獨立 | P22 磷光體衰減 ~數 ms，快速移動物體有尾跡殘影 | **高** — 快速動作場景的最大視覺差異 |
| 11 | **蔭罩 / 光柵孔 (Shadow mask / Aperture grille)** | 無 | Shadow mask: 六角磷光點三元組可見；Trinitron: 垂直磷光條紋可見。近距離觀看有明顯 subpixel 結構 | **高** — CRT「質感」的最大來源 |
| 12 | **水平 beam 擴散** | 僅有垂直高斯 profile | 電子束在水平方向也有寬度，造成水平像素間模糊（尤其高亮度下 spot size 增大） | 中 |
| 13 | **Beam convergence 偏差** | 無 | R/G/B 三槍 convergence 不完美 → 邊緣出現彩色邊紋 (color fringing)，螢幕中心最佳、四角最差 | 中 |
| 14 | **螢幕曲率 / 幾何畸變** | 無 — 完美平面 | CRT 為曲面，有桶形/枕形畸變、角落較暗、曲面反光特性 | 低～中 |
| 15 | **邊緣暗角 (Vignette)** | 無 | 邊角因偏轉角度大 + 蔭罩透射率降低而變暗 | 低 |
| 16 | **色溫 / 白點** | 固定 blargg -15° YIQ→RGB 矩陣 | 日本 CRT TV 常用 9300K（偏藍），NTSC 標準 6500K (D65)。不同機型色溫差異明顯 | 低～中 |
| 17 | **Gamma 精確性** | 快速二次近似 ≈ pow(v, 1/1.13) | 真實 CRT gamma 為 2.2~2.5，且非均勻（暗部偏高、亮部偏低），加上 NTSC 編碼 gamma 1/2.2 的系統聯合效果 | 低 |
| 18 | **隔行 / 場抖動** | 無 — 輸出逐行 240p | NES 輸出 240p，但某些 CRT 以 480i 方式處理，造成輕微垂直抖動 | 極低 |

---

## 優先推薦改善方向

以視覺影響和實作可行性排序：

### Tier 1：視覺影響最大

| 優先 | 項目 | 預估難度 | 效能影響 | 說明 |
|:----:|------|:--------:|:--------:|------|
| 1 | **Shadow mask / Aperture grille** (#11) | 中 | 低 | 預計算遮罩紋理，在 CrtScreen 後處理乘上即可。最能提升「CRT 質感」 |
| 2 | **Phosphor persistence** (#10) | 中 | 中 | 需要前一幀緩衝區，做指數衰減混合 `out = max(current, prev * decay)`。快速動作真實感 |
| 3 | **Emphasis bits 修正** (#1) | 中 | 無 | 改為 per-phase 方向性衰減。這是正確性修復，非視覺增強 |

### Tier 2：中等影響

| 優先 | 項目 | 預估難度 | 效能影響 | 說明 |
|:----:|------|:--------:|:--------:|------|
| 4 | **水平 beam 擴散** (#12) | 低 | 中 | 對 linearBuffer 每行做水平高斯模糊（σ≈0.5-1.0 pixel），配合已有的垂直 beam |
| 5 | **RF herringbone** (#9) | 低 | 低 | 將 buzzRow 改為 per-sample `sin(4.5MHz * t)`，產生行內斜紋干擾 |
| 6 | **Ringing / 振鈴** (#5) | 中 | 中 | 將一階 IIR 改為二階（或 4-tap FIR with overshoot），模擬帶限通道的 Gibbs 效應 |
| 7 | **色溫選擇** (#16) | 低 | 無 | 在 YIQ→RGB 矩陣後加色溫偏移矩陣，提供 9300K/6500K/自訂選項 |

### Tier 3：錦上添花

| 優先 | 項目 | 預估難度 | 效能影響 | 說明 |
|:----:|------|:--------:|:--------:|------|
| 8 | **螢幕曲率** (#14) | 中 | 低 | UV distortion map 後處理（barrel distortion） |
| 9 | **邊緣暗角** (#15) | 低 | 無 | 徑向 vignette 乘法遮罩 |
| 10 | **Beam convergence** (#13) | 高 | 中 | R/G/B 通道各自 sub-pixel 偏移，需 per-channel rendering |
| 11 | **Comb filter** (#6) | 高 | 高 | 需跨行緩衝，改變單行獨立的解調架構 |
| 12 | **HBI 模擬** (#4) | 中 | 中 | 擴展 waveBuf 到 341 dot，加入 sync/burst 區段的 IIR 狀態 |

---

## 備註

- **效能預算**: 目前 4x 解析度 107 FPS (1.79x 即時)，有約 44 FPS 的餘裕空間可用於新特效
- **Tier 1 的 shadow mask + phosphor 合計預估消耗**: ~10-15 FPS，仍可維持 >60 FPS 即時運行
- **Emphasis bits 修正**: 不影響效能，但需要修改 waveTable 生成邏輯和 GenerateSignal/GenerateWaveform，改為 per-phase attenuation 而非 uniform scalar
