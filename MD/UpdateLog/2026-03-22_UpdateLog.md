# 2026-03-22 更新日誌

---

## 1. Bilinear Upscale 實作（Ntsc.cs）

- 新增 `UpscaleMode` 選項（0=nearest-neighbor, 1=bilinear，預設 bilinear）
- `ResampleH_Bilinear()`：水平 bilinear 重採樣，fixed-point 8-bit fraction
- `VerticalFillRows()`：垂直 bilinear 插值填充相鄰掃描線間的 gap rows
- CRT 開啟時走 Gaussian beam 路徑（不受影響），CRT 關閉時走 bilinear
- 適用於 non-CRT 模式的 Level 3 DemodulateRow / DemodulateRow_SVideo

## 2. 語系初始化（AprNesUI.cs）

- 新增 `GetDefaultLang()`：根據 `CultureInfo.CurrentUICulture` 自動判斷系統語系
- 繁體中文（zh-tw/hk/mo/hant）→ `zh-tw`，簡體中文 → `zh-cn`，其他 → `en-us`
- 首次啟動無 INI 時自動套用正確語系

## 3. 最近開啟 ROM（Recent ROMs）

- 右鍵選單新增「最近開啟」子選單，最多保留 10 筆不同 ROM 紀錄
- 顯示格式：檔名（不含路徑），滑鼠 hover 顯示完整路徑（ToolTipText）
- 儲存於 INI（pipe-delimited `RecentROMs` 欄位）
- 檔案不存在時提示並自動從清單移除
- 三語支援（en-us / zh-tw / zh-cn）

## 4. 鍵盤快捷鍵

| 快捷鍵 | 功能 |
|--------|------|
| `Ctrl+O` | 開啟遊戲 |
| `F11` | 全螢幕切換 |
| `Esc` | 退出全螢幕 |
| `Ctrl+R` | 重置（遊戲執行中） |

- 不影響既有 `Shift+P` 截圖功能

## 5. 全螢幕退出修復

- 修正退出全螢幕後 UI layout 跑掉的問題
- 改用 `initUIsize()` 統一還原，取代手動設定 panel/form 尺寸

## 6. INI 解析修正

- `Split('=')` → `Split('=', 2)`，修正路徑含 `=` 時解析錯誤

## 7. Renderer 重新命名

- `Render_ntsc_3x` → `Render_Analog`
- 更準確反映其角色：Analog 管線的直通 GDI 渲染器
- 影響檔案：InterfaceGraphic.cs、AprNesUI.cs、Main.cs

## 8. CRT 後處理效能優化（CrtScreen.cs）

`ApplyShadowMaskAndPhosphor()` 整數優化 + 3x 迴圈展開：

| 優化項目 | 說明 |
|---------|------|
| float → uint 整數乘法 | Phosphor decay 從 float multiply 改為 uint multiply + shift |
| 3x 迴圈展開 | 消除 shadow mask phase 0/1/2 的分支預測失敗 |
| 特化路徑分離 | mask+phosphor / mask-only / phosphor-only 三條獨立路徑 |
| SWAR R+B 合併乘法 | Phosphor-only 路徑 3 次乘法 → 2 次 |

**Benchmark 結果（Release, CRT+RF, 3-run 平均）：**

| AnalogSize | 優化前 FPS | 優化後 FPS | 提升 |
|:----------:|:----------:|:----------:|:----:|
| 2x (512×420) | 123.41 | 127.80 | +3.6% |
| 4x (1024×840) | 108.39 | 113.91 | +5.1% |
| 6x (1536×1260) | 74.13 | 81.81 | +10.4% |
| 8x (2048×1680) | 68.50 | 74.03 | +8.1% |

## 9. Benchmark 腳本改進（bench_analog_resolutions.sh）

- JIT 暖機從 20s 縮短為 10s，JIT 後不再冷卻
- 新增進度顯示 `[N/12]`（4 解析度 × 3 次 = 12 回合）

## 10. Save States 設計規劃文件

- 寫入 `MD/FunctionPlan/SaveStates/SaveStates_Plan.md`
- 完整涵蓋：Slot 設計、狀態清單（CPU/PPU/APU/MEM/IO/Mapper）、二進位格式、IMapper 介面擴展、3 階段實作優先順序、風險分析
- 僅規劃，未實作
