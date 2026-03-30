# 2026-03-31 更新日誌

---

## 1. Mapper 154 (Namco 129) 新增實作

### 問題
- Devil Man (J) 背景渲染異常。iNES header 錯誤標記為 mapper 088 + fourscreen mirroring，實際為 mapper 154 (Namco 129) + horizontal mirroring。

### 修復
- **Mapper088.cs**：新增 `IsMapper154` flag，支援 Namco 129 動態單屏鏡像（bit 6 控制 ScreenA/ScreenB）
- **RomDatabase.cs**：新增 Devil Man (J) CRC override（`0xD1691028 → MapperOverride=154, MirrorOverride=0`）
- **MapperRegistry.cs**：新增 Mapper 154 支援（Create、IsSupported、GetName）
- **PPU.cs**：新增四螢幕鏡像模式（`mirror == 4 → addr & 0x2FFF`）
- **Main.cs**：讀取 iNES header fourscreen flag 時設定 `*Vertical = 4`

### 驗證
- Devil Man (J) 人工驗證通過

---

## 2. Mapper 076 (Namco 109) 修復

### 問題
- Battle City Hack V4 進入遊戲後跳回選單。

### 根因分析與修復
1. **地址解碼錯誤**：原本 `address & 0x8001`，Namco 109 的 A15 直接接 /CE，$8000-$FFFF 皆有效，僅用 A0 區分 command/data
2. **Bank mask 陷阱**：bitmask (`& mask`) 僅適用 power-of-2 ROM 大小，hack ROM 非標準大小必須用 `%` modulo
3. **缺少線性開機狀態**：reg[2..5]=0,1,2,3（CHR）、reg[6]=0,reg[7]=1（PRG）
4. **新增 PRG 指標快取**：`UpdatePRGBanks()` 預計算 bank 指標，`MapperR_RPG` 變為純指標查表

### 驗證
- Battle City Hack V4 人工驗證通過

---

## 3. Mapper 064 (Tengen RAMBO-1) IRQ 重寫

### 問題
- Klax (Tengen) 下半部畫面快速上下震動（scroll jitter）。

### 修改內容
- 完整重寫 IRQ 邏輯以對齊 Mesen2 RAMBO-1 實作：
  - `ClockIrqCounter`：reload +1/+2 bias with always-decrement
  - `CpuCycle`：counter first（含 forceClock），然後 delay assertion
  - `forceClock`：CPU→PPU mode switch 時設 deferred flag（非立即 clock）
  - A12 watcher：accumulating cyclesDown with minDelay=30
  - `$E000`：僅清 IRQ source，不清 needIrqDelay
  - PRG Mode 1：$8000=R15, $A000=R6, $C000=R7（向下平移）

### 狀態
- Shinobi (Tengen) 正常運作
- **Klax scroll jitter 問題延後處理**（疑為更深層 timing 問題）

---

## 4. MAPPER_STATUS 更新

- Mapper 088：✅（Dragon Spirit, Quinty 正常）
- Mapper 154：新增 ✅（Devil Man 正常）
- Mapper 076：❌→ ✅（Battle City Hack V4 修復）
- Mapper 064：⚠️（Klax scroll jitter）
- **校驗摘要**：✅ 60 / ⚠️ 1 / ❓ 4 / 合計 65

---

## 統計

- **新增 Mapper**：154 (Namco 129)
- **修復 Mapper**：076 (Namco 109)、064 (RAMBO-1 部分)
- **測試基線**：174/174 blargg PASS、136/136 AccuracyCoin PASS（無回歸）
