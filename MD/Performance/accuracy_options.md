# Accuracy Options

AprNes 提供可調整的精確度選項，允許在模擬準確性與執行效能之間取得平衡。
設定儲存於 `AprNes.ini`，由 GUI 與 headless 模式共用。

---

## 目前選項

### AccuracyOptA — Per-dot Secondary OAM Evaluation FSM

**INI 鍵**：`AccuracyOptA=1`（1 = 開啟，0 = 關閉）
**預設**：ON（1）
**CLI 覆蓋**：`--accuracy A`（ON）/ `--accuracy ""`（OFF）

#### 說明

NES PPU 在每條可見掃描線的 dots 1–256 執行 sprite evaluation（次要 OAM 清除與複製）：

- **Dots 1–64**：清除 secondary OAM（每 2 dots 寫一個 $FF）
- **Dot 65**：初始化 FSM，執行第一次 tick
- **Dots 66–256**：每 dot 執行 FSM tick，dot 256 結束

關閉此選項後，FSM 完全跳過，secondary OAM 不更新。渲染結果（sprite 顯示）不受影響，因為 `RenderSpritesLine()` 直接讀取 `spr_ram`，不依賴 secondary OAM。

#### 效能測試結果（Mega Man 5 USA, Mapper 4/MMC3, 20s, 90s 冷卻）

| 設定 | FPS | 差異 |
|------|-----|------|
| ON（預設） | 264.45 | baseline |
| OFF | 302.00 | **+37.55 FPS（+14.2%）** |

#### 正確性影響

| 測試集 | ON | OFF |
|--------|-----|-----|
| blargg 174 | 174/174 ✅ | 174/174 ✅ |
| AccuracyCoin 136 | 136/136 ✅ | 131/136 ❌ |

AccuracyCoin 失敗項目（OFF 時）：
- **P18**（$2002 Flag Clear Timing）：3 項失敗
- **P19**（BG Serial In）：2 項失敗

**結論**：關閉可獲得 ~14% 效能提升，但 AccuracyCoin P18/P19 共 5 項會失敗。預設維持 ON。

---

## 累積關閉效果（歷史測試，僅供參考）

以下為 2026-03-16 以 Mega Man 5 進行的完整累積測試（60s 冷卻，已移除 B–F 選項）：

| 設定 | FPS | vs ALL ON | blargg |
|------|-----|-----------|--------|
| A ON | 267.90 | baseline | 174/174 |
| A OFF | 302.35 | +12.9% | 174/174 |

---

## INI 設定格式

```ini
AccuracyOptA=1
```

寫入 `AprNes.exe` 同目錄的 `AprNes.ini`。GUI 模式於設定儲存時自動更新；headless 模式於啟動時讀取。

---

*最後更新：2026-03-16*
