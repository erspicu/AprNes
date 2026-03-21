# 類比模式可選項化效果清單

**日期**: 2026-03-21
**狀態**: 待實作（等其他物理項目完成後統一加入 UI）

---

## 已實作、適合選項化的效果

| # | 效果 | 欄位 | 預設值 | 類型 | 所在檔案 | 說明 |
|:-:|------|------|:------:|------|----------|------|
| 1 | **Interlace Jitter** | `CrtScreen.InterlaceJitter` | `true` | bool | CrtScreen.cs | 隔行垂直抖動 ±0.25px/frame。NES 原生 240p 不做隔行，且缺少 phosphor persistence 配套會放大跳動感。**建議預設 false 或提供開關** |
| 2 | **Vignette** | `CrtScreen.VignetteStrength` | `0.15` | float (0~1) | CrtScreen.cs | 邊緣暗角。0=關閉，越大邊角越暗。拋物線垂直衰減 |
| 3 | **Color Burst Jitter** | 硬編碼 ~3% 機率 | RF only | — | Ntsc.cs:469 | RF 模式色相抖動。目前無開關，需抽出為 bool 參數 |
| 4 | **Color Temperature** | `Ntsc.ColorTempR/G/B` | `1.0/1.0/1.0` | float×3 | Ntsc.cs | YIQ→RGB 矩陣色溫偏移。可提供預設組合（9300K 偏藍 / 6500K D65 / 自訂） |
| 5 | **Gamma** | `Ntsc.GammaCoeff` | `0.229` | float | Ntsc.cs | CRT gamma 近似係數。0.229 ≈ pow(v,1/1.13)，增大則 gamma 校正更強 |

---

## 低成本項目（已實作，預設開啟）

| # | 效果 | 欄位 | 預設值 | 類型 | 所在檔案 | 說明 |
|:-:|------|------|:------:|------|----------|------|
| 6 | **Shadow Mask / Aperture Grille** | `CrtScreen.ShadowMaskMode` | `ApertureGrille` | enum (None/ApertureGrille/ShadowMask) | CrtScreen.cs | 蔭罩類型。ApertureGrille=Trinitron 垂直條紋，ShadowMask=點陣三角排列（偶奇行偏移），None=關閉 |
|   |  | `CrtScreen.ShadowMaskStrength` | `0.3` | float (0~1) | CrtScreen.cs | 非主色通道衰減比例。0=無效果，0.3=微妙，0.6=明顯 |
| 7 | **RF Herringbone** | 由 `RfAudioLevel` 控制 | RF only | — | Ntsc.cs | 4.5MHz 音訊載波 per-sample 正弦波（取代原 per-line 常數 buzzRow），產生斜紋干擾。振幅受 60Hz 包絡調制 |
| 8 | **Screen Curvature** | `CrtScreen.CurvatureStrength` | `0.12` | float (0~0.5) | CrtScreen.cs | 桶形畸變強度。0=平面，0.12=微妙弧度，0.3=明顯彎曲。預計算 remap table，Parallel.For 後處理 |

---

## 尚未實作、未來完成後也適合選項化的效果

| # | 效果 | Gap Analysis 編號 | 優先級 | 說明 |
|:-:|------|:-----------------:|:------:|------|
| 9 | **Phosphor Persistence** | #10 | Tier 1 | 餘輝衰減係數（0=關，0.5~0.8 典型值）。實作後 Interlace Jitter 才有意義 |
| 10 | **Horizontal Beam Spread** | #12 | Tier 2 | 水平模糊 σ 值（0=關） |
| 11 | **Ringing / Gibbs** | #5 | Tier 2 | 振鈴強度（0=純 IIR，>0 加入過衝） |
| 12 | **Beam Convergence** | #13 | Tier 3 | RGB 匯聚偏差量 |

---

## UI 設計備註

- 所有效果應在 ConfigureUI 的類比設定區域集中管理
- bool 型用 CheckBox，float 型用 TrackBar + 數值顯示
- 色溫建議提供下拉預設組合 + 自訂 RGB 滑桿
- 所有參數存入 AprNes.ini，啟動時讀取
- **Interlace Jitter 需等 Phosphor Persistence (#10) 實作後再決定預設值**
  - 有餘輝配套 → 預設 true 合理
  - 無餘輝配套 → 預設 false（跳動感太強）
