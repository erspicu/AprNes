# 2026-03-25 更新日誌

---

## 1. MMC5 完整重寫 — Mesen2 風格 VRAM Read Notification（Mapper005.cs, PPU.cs, MEM.cs, Main.cs）

### 核心變更

- **chrABAutoSwitch=false**：不再由 PPU 自動在 cx=257/320 切換 CHR A/B set
- **NotifyVramRead()**：PPU 每次 VRAM fetch（BG tile、sprite tile、prefetch garbage NT）都通知 MMC5
- **3-consecutive-NT-read 掃描線偵測**：取代舊版 A12 IRQ，改用 dots 337, 339, 1 的 3 次相同 NT address 偵測掃描線邊界
- **needInFrame 兩階段晉升**：第一組 3-identical-read 設 needInFrame=true → 下一掃描線 NT fetch 晉升 ppuInFrame=true
- **splitTileNumber 追蹤**：0-31=BG tiles, 32-39=sprite tiles, 40-41=prefetch；決定 CHR A/B set 選擇
- **mmc5Ref 直接呼叫**：Main.cs 新增 `static Mapper005 mmc5Ref`，PPU 直接呼叫 mapper 方法

### $2007 Nametable 修正（MEM.cs）

- CPU 透過 $2007 讀寫 nametable 區域時，若 `ntChrOverrideEnabled=true`（MMC5 $5105 mapping 啟用），使用 `ntBankPtrs[]` 而非 `CIRAMAddr()` 鏡像
- 修復 mmc5test split screen 下半部 nametable 內容全零的問題

### Odd-Frame Skip 修正（PPU.cs）

- 奇數幀 pre-render line cx=339 跳過前，先觸發 `mmc5Ref.NotifyVramRead()`
- 確保 3-consecutive-NT-read 偵測在奇偶幀都穩定

### Pre-Sprite Render CHR 修正（PPU.cs, Mapper005.cs）

- 新增 `Mapper005.PreSpriteRender()` 方法
- cx=257 的 `RenderSpritesLine()` 之前呼叫，強制切換 chrBankPtrs 到 A set（sprites）
- 修復 Castlevania III 標題畫面十字架游標亂碼問題（`NotifyVramRead` 的 sprite phase 在 cx=258 才觸發，晚一個 dot）

---

## 驗證結果

- mmc5test: split screen 正確渲染 ✅
- Castlevania III: 標題畫面十字架游標正常、遊戲關卡正常 ✅
- blargg: **174/174 PASS** ✅
- AccuracyCoin: **136/136 PASS** ✅
