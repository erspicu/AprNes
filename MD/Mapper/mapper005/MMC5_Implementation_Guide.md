# MMC5 (Mapper 005) 完整實作指南

**日期**: 2026-03-25
**目標讀者**: 正在開發 NES 模擬器、需要實作 MMC5 的開發者
**前提**: 讀者已有基本的 NES 模擬器（CPU/PPU/APU 已可運作），了解 mapper 基本概念

---

## 1. MMC5 概述

MMC5 (Nintendo ExROM) 是 NES 最複雜的官方 mapper，主要特性：

- **PRG banking**: 4 種模式（32KB / 16+16 / 16+8+8 / 8+8+8+8）
- **CHR banking**: 4 種模式（8KB / 4+4 / 2+2+2+2 / 1×8），帶 A/B set 切換
- **Nametable mapping**: 4 個 NT 各可映射到 CIRAM-A, CIRAM-B, ExRAM, Fill
- **Scanline IRQ**: 透過 3 次連續相同 NT 讀取偵測掃描線邊界
- **Expansion RAM**: 1KB（$5C00-$5FFF），4 種模式
- **8×8 硬體乘法器**: $5205 × $5206
- **擴展屬性模式**: ExRAM mode 1，每個 tile 獨立調色盤 + CHR bank

代表遊戲：Castlevania III、Gemfire、L'Empereur、Romance of the Three Kingdoms II

---

## 2. 暫存器一覽

| 地址 | 名稱 | 說明 |
|------|------|------|
| $5100 | PRG mode | bits[1:0] = PRG 模式 (0-3) |
| $5101 | CHR mode | bits[1:0] = CHR 模式 (0-3) |
| $5102/$5103 | PRG-RAM protect | $5102=2, $5103=1 時允許寫入 |
| $5104 | ExRAM mode | 0=NT extra, 1=Extended attr, 2=R/W RAM, 3=R-only |
| $5105 | NT mapping | 每 2 bits 一組: 0=CIRAM-A, 1=CIRAM-B, 2=ExRAM, 3=Fill |
| $5106/$5107 | Fill tile/color | Fill nametable 模式用 |
| $5113 | PRG-RAM bank | $6000-$7FFF 的 8KB PRG-RAM bank |
| $5114-$5117 | PRG-ROM banks | $8000-$FFFF 的 PRG 分頁 |
| $5120-$5127 | CHR A set | 8 個 A set CHR bank 暫存器 |
| $5128-$512B | CHR B set | 4 個 B set CHR bank 暫存器 |
| $5130 | CHR upper bits | 額外 2-bit CHR bank 高位 |
| $5203 | IRQ target | 目標掃描線號 |
| $5204 | IRQ control/status | W: bit7=enable; R: bit7=pending, bit6=inFrame |
| $5205/$5206 | Multiplier | W: 設定運算元; R: $5205=低8位, $5206=高8位 |

---

## 3. PRG Banking

### 3.1 模式說明

每個 bank 暫存器的 bit 7 決定 ROM/RAM：1=ROM, 0=RAM。

```
Mode 0 (32KB):  $5117 控制整個 $8000-$FFFF (忽略低 2 bits)
Mode 1 (16+16): $5115 → $8000-$BFFF; $5117 → $C000-$FFFF (忽略各自低 1 bit)
Mode 2 (16+8+8):$5115 → $8000-$BFFF; $5116 → $C000-$DFFF; $5117 → $E000-$FFFF
Mode 3 (8×4):   $5114-$5117 各控制一個 8KB window
```

### 3.2 實作要點

- `$5117` 的 `$E000-$FFFF` 永遠是 ROM（bit 7 被忽略），power-on 預設 = `0xFF`（最後一個 bank）
- PRG-RAM 總共 64KB（8 個 8KB bank），由 bank 號的 bits[2:0] 選擇
- $6000-$7FFF 由 $5113 控制，永遠映射到 PRG-RAM

### 3.3 NMI Vector 讀取 ($FFFA-$FFFB)

**重要**：CPU 讀取 NMI vector（$FFFA/$FFFB）時，必須重置 frame 狀態：

```
ppuInFrame = false
needInFrame = false
scanlineCounter = 0
irqPending = false
清除 mapper IRQ line
```

這是 MMC5 偵測 VBlank 開始的機制。Mesen2 在 `ReadRegister()` 中處理此邏輯。

---

## 4. CHR Banking — A/B Set 機制

### 4.1 核心概念

MMC5 的 CHR banking 是整個 mapper 中最精微的部分。在 8×16 sprite 模式下，CHR 空間分為兩組：

- **A set** ($5120-$5127)：用於 **sprite tile** 讀取
- **B set** ($5128-$512B)：用於 **BG tile** 讀取（4 個暫存器映射到 8 個 1KB slot，以鏡像方式填滿）

在 8×8 sprite 模式下，A set 用於所有讀取。

### 4.2 A/B Set 選擇邏輯（Mesen2 模型）

判斷使用 A set 還是 B set 的條件（每次 nametable fetch 時評估）：

```csharp
bool chrA = !largeSprites                              // 8x8 模式: 永遠 A
         || (splitTileNumber >= 32 && splitTileNumber < 40) // sprite fetch phase
         || (!ppuInFrame && lastChrReg <= 0x5127);      // VBlank + 最後寫入 A set reg
```

其中：
- `largeSprites` = PPU $2000 bit 5（8×16 sprite mode）
- `splitTileNumber` = 掃描線內的 tile 計數器（見 §4.3）
- `ppuInFrame` = 掃描線 IRQ 偵測的 "rendering 中" 狀態（見 §5）
- `lastChrReg` = 最後一次被寫入的 CHR 暫存器地址（$5120-$512B）

### 4.3 splitTileNumber 追蹤

PPU 每條掃描線的 VRAM fetch 結構：

```
dots   1-256: 32 個 BG tile (每個 8 dot = 4 fetches: NT, AT, CHR low, CHR high)
dots 257-320:  8 個 sprite tile (同上結構, garbage NT/AT + sprite CHR)
dots 321-336:  2 個 prefetch tile
dots 337-340:  2 個 garbage NT fetch (掃描線偵測用)
```

`splitTileNumber` 在每次 **nametable fetch** 時遞增：
- 0-31: BG tile phase → chrA 由 `lastChrReg` 和 `ppuInFrame` 決定
- 32-39: sprite tile phase → chrA = **true**（強制 A set）
- 40-41: prefetch phase

**在掃描線邊界偵測到時（3-consecutive-read），`splitTileNumber` 重置為 0。**

### 4.4 B Set 的鏡像填充（chrMode=3 為例）

B set 只有 4 個暫存器（$5128-$512B），但 CHR 空間有 8 個 1KB slot：

```
chrBankPtrs[0] = chrBankPtrs[4] = chrBanks[8]  ($5128)
chrBankPtrs[1] = chrBankPtrs[5] = chrBanks[9]  ($5129)
chrBankPtrs[2] = chrBankPtrs[6] = chrBanks[10] ($512A)
chrBankPtrs[3] = chrBankPtrs[7] = chrBanks[11] ($512B)
```

A set 的 8 個暫存器直接對應 8 個 slot，無鏡像。

### 4.5 lastChrReg 的作用

`lastChrReg` 記錄最後一次被遊戲寫入的 CHR bank 暫存器地址。

- 寫 $5120-$5127 → lastChrReg ≤ $5127 → BG 在非 rendering 時使用 A set
- 寫 $5128-$512B → lastChrReg > $5127 → BG 在非 rendering 時使用 B set

**8×8 模式下 lastChrReg 被重置為 0**（Mesen2 行為），確保永遠使用 A set。

### 4.6 CHR Upper Bits ($5130)

$5130 提供額外 2-bit 高位，與每次 $5120-$512B 寫入時的 value 組合：

```
chrBanks[i] = value | (chrUpperBits << 8)
```

這使得 CHR bank 號可達 10 bits（1024 個 1KB bank = 1MB CHR ROM）。

---

## 5. Scanline IRQ — 3-Consecutive-NT-Read 偵測

### 5.1 為什麼不用 A12

大多數 mapper（如 MMC3）靠 PPU address bus 的 A12 line 偵測掃描線。但 MMC5 使用完全不同的機制：**監視 PPU 的 VRAM read pattern**。

真實硬體中，MMC5 觀察 PPU 對 nametable 區域的讀取。PPU 在每條掃描線的末尾（dots 337, 339）和下一條掃描線的開頭（dot 1）會讀取**相同的 nametable 地址**。當 MMC5 偵測到 3 次連續相同的 NT 讀取，它知道一條新掃描線開始了。

### 5.2 PPU 向 MMC5 發送通知

**這是最關鍵的實作重點**。你的 PPU 必須在以下時機通知 MMC5：

```
BG tile fetch (dots 1-256, 每 8 dot 一組):
  phase 1 (NT byte):   notify(0x2000 | NT_addr)         ← nametable fetch
  phase 3 (AT byte):   notify(0x23C0 | AT_addr)         ← attribute fetch
  phase 5 (CHR low):   notify(pattern_table_addr)        ← CHR fetch
  phase 7 (CHR high):  notify(pattern_table_addr | 8)    ← CHR fetch

Sprite fetch (dots 257-320, 每 8 dot 一組):
  phase 1 (garbage NT): notify(0x2000)
  phase 3 (garbage AT): notify(0x23C0)
  phase 5 (CHR low):    notify(sprite_pattern_addr)
  phase 7 (CHR high):   notify(sprite_pattern_addr | 8)

Garbage NT fetch (dots 337, 339):
  notify(0x2000 | (vram_addr & 0x0FFF))                  ← 掃描線偵測的關鍵！
```

注意 dots 337 和 339 的地址包含 `vram_addr & 0x0FFF`，這使得它們與 dot 1 的 NT fetch 地址相同，形成 3 次連續相同讀取。

### 5.3 偵測演算法

```
NotifyVramRead(addr):
  1. 判斷是否為 NT fetch: addr ∈ [$2000, $2FFF] 且 (addr & 0x3FF) < 0x3C0
  2. 若是 NT fetch:
     a. splitTileNumber++
     b. 若 ppuInFrame=true → UpdateChrBanks()
     c. 若 needInFrame=true → promote: ppuInFrame=true, needInFrame=false, UpdateChrBanks()
  3. 呼叫 DetectScanlineStart(addr)
  4. ppuIdleCounter = 3
  5. 記錄 lastPpuReadAddr = addr

DetectScanlineStart(addr):
  if ntReadCounter >= 2:
    // 已偵測到 3+ 次相同 NT read → 掃描線邊界
    if !ppuInFrame && !needInFrame:
      needInFrame = true       ← 首次偵測: 標記等待晉升
      scanlineCounter = 0
    else:
      scanlineCounter++
      if scanlineCounter == irqCounterTarget:
        irqPending = true
        if irqEnabled: 觸發 mapper IRQ

  else if addr ∈ [$2000, $2FFF]:
    if addr == lastPpuReadAddr:
      ntReadCounter++
      if ntReadCounter >= 2:
        splitTileNumber = 0     ← 掃描線邊界: 重置 tile 計數器

  if addr != lastPpuReadAddr:
    ntReadCounter = 0            ← 地址改變: 重置連續計數
```

### 5.4 needInFrame 兩階段晉升

為什麼不直接設 `ppuInFrame = true`？

因為第一組 3-consecutive-read 發生在 pre-render line（scanline 261）的 dots 337-339 → scanline 0 的 dot 1。此時 rendering 才剛要開始。Mesen2 的設計是：

1. **第一次** 3-consecutive-read → `needInFrame = true`（但 `ppuInFrame` 仍為 false）
2. **第二次** 3-consecutive-read（下一掃描線）→ `ppuInFrame = true`，同時 `scanlineCounter` 開始計數

這確保 `scanlineCounter` 的計數起始點正確。

### 5.5 ppuIdleCounter — VBlank 偵測

```
CpuCycle() (每 CPU cycle 呼叫一次):
  if ppuIdleCounter > 0:
    ppuIdleCounter--
    if ppuIdleCounter == 0:
      ppuInFrame = false     ← PPU 已停止 fetching → 進入 VBlank
      needInFrame = false
      UpdateChrBanks(true)
```

PPU 在 rendering 期間每個 dot 都會 fetch VRAM。當 MMC5 連續 3 個 CPU cycle（≈9 PPU dots）沒收到任何 VRAM read 通知，它知道 PPU 已進入 VBlank。

### 5.6 奇數幀跳過的處理

NES 在奇數幀、rendering 啟用時會跳過 pre-render line 的 dot 339。這會破壞 3-consecutive-read pattern（只有 2 次而非 3 次）。

**解法**：在跳過 dot 339 之前，先發送 MMC5 通知：

```csharp
// PPU odd-frame skip
if (scanline == 261 && cx == 339)
{
    oddSwap = !oddSwap;
    if (!oddSwap && renderingEnabled)
    {
        // 跳過 dot 339，但先通知 MMC5（否則 3-consecutive-read 會失敗）
        if (mmc5Ref != null)
            mmc5Ref.NotifyVramRead(0x2000 | (vram_addr & 0x0FFF));
        cx++;  // skip to dot 340
    }
}
```

---

## 6. Nametable Mapping ($5105)

### 6.1 機制

$5105 的每 2 bits 控制一個 nametable 的來源：

```
bits[1:0] = NT0 ($2000-$23FF)
bits[3:2] = NT1 ($2400-$27FF)
bits[5:4] = NT2 ($2800-$2BFF)
bits[7:6] = NT3 ($2C00-$2FFF)

值: 0=CIRAM page A, 1=CIRAM page B, 2=ExRAM, 3=Fill-mode
```

### 6.2 對 $2007 讀寫的影響

**重要陷阱**：CPU 透過 $2007 讀寫 nametable 區域時，也必須使用 MMC5 的 NT mapping，而不是標準的硬體鏡像。

許多模擬器的 $2007 讀寫路徑使用硬體 header 的 H/V mirroring 邏輯（如 `CIRAMAddr()`）。但 MMC5 的 $5105 mapping 完全覆蓋此邏輯。如果你的 $2007 讀寫不經過 NT bank 指標，則 CPU 寫入的 nametable 資料會到錯誤的位置。

**解法**：在 $2007 的 nametable 區域讀寫路徑中，檢查是否有 MMC5 NT override 啟用：

```csharp
// $2007 write to nametable region ($2000-$2FFF)
if (ntChrOverrideEnabled)
    ntBankPtrs[(addr >> 10) & 3][addr & 0x3FF] = value;
else
    ppu_ram[CIRAMAddr(addr)] = value;
```

同理，$2007 read 的 buffer 填充也需要走 `ntBankPtrs`。

### 6.3 標準鏡像的快速路徑

如果 $5105 的 4 個 NT 都映射到 CIRAM（不涉及 ExRAM 或 Fill），且組合等同於 H/V/1A/1B 之一，可以走快速路徑（使用原本的 mirroring flag），避免每次 NT 讀取都走指標陣列。

---

## 7. Pre-Sprite Render CHR Switch

### 7.1 問題

如果你的 PPU 在 dot 257 進行 sprite 批次渲染（batch render），而 CHR A/B 切換是由 NotifyVramRead 驅動的，會遇到以下時序問題：

```
dot 257: RenderSpritesLine() ← 使用 chrBankPtrs（此時仍是 B set！）
dot 258: NotifyVramRead(0x2000) ← 第一次 sprite phase NT fetch
         splitTileNumber 變為 32 → UpdateChrBanks 切換到 A set
```

sprite 渲染在 CHR bank 切換**之前**就已完成，使用了錯誤的 B set（BG 用）CHR bank。

### 7.2 解法

在 dot 257 的 `RenderSpritesLine()` 之前，強制切換到 A set：

```csharp
// PPU.cs, cx == 257
if (mmc5Ref != null) mmc5Ref.PreSpriteRender();
RenderSpritesLine();

// Mapper005.cs
public void PreSpriteRender()
{
    if (Spritesize8x16 && chrRomSize > 0)
    {
        prevChrA = true;
        FillCHRBankPtrs(chrBankPtrs, true);  // 強制 A set
    }
}
```

### 7.3 為什麼不用 chrABAutoSwitch

另一種方法是在 PPU 中維護 `chrBankPtrsA[]` 和 `chrBankPtrsB[]` 的拷貝，在 dot 257/320 由 PPU 自動切換（`chrABAutoSwitch=true`）。這對簡單的 A/B 切換場景有效，但無法處理 MMC5 需要的 **per-tile** CHR bank 動態切換（如 Castlevania III 的 mid-frame IRQ 改變 CHR bank）。

NotifyVramRead 模型讓 MMC5 在每個 NT fetch 時都重新評估 CHR bank，是更正確的做法。代價是需要手動處理 dot 257 的時序問題（即上述 PreSpriteRender）。

---

## 8. Expansion RAM ($5C00-$5FFF)

### 8.1 四種模式

| Mode | 寫入行為 | 讀取行為 |
|------|---------|---------|
| 0 | ppuInFrame 時可寫，否則寫 0 | 返回 open bus |
| 1 | 同 mode 0 | Extended attribute（PPU 渲染時使用）|
| 2 | 任何時候都可寫 | 正常讀取 |
| 3 | 唯讀 | 正常讀取 |

### 8.2 Extended Attribute Mode (mode 1)

**目前為實作中較進階的功能**。當 ExRAM mode=1 時，每個 BG tile 的 nametable fetch 會同時查閱 ExRAM：

```
ExRAM byte layout:
  bits[5:0] = CHR bank number (combined with chrUpperBits)
  bits[7:6] = palette index (取代 attribute table)
```

每個 8×8 tile 都有獨立的調色盤選擇（而非標準的 16×16 像素共享），大幅提升畫面色彩豐富度。

---

## 9. 其他功能

### 9.1 硬體乘法器

```
W $5205 = a;  W $5206 = b;
R $5205 = (a×b) & 0xFF;  R $5206 = (a×b) >> 8;
```

8-bit × 8-bit = 16-bit 無號乘法，結果立即可讀。

### 9.2 PRG-RAM Protect

只有 $5102=2 且 $5103=1 時，$8000-$DFFF 區域的 RAM bank 才允許寫入。

### 9.3 Fill Mode

$5105 中使用 value=3 的 NT slot 會映射到 fill nametable：

- $5106 = fill tile number（960 bytes 全部填充此值）
- $5107 bits[1:0] = fill attribute（64 bytes 全部填充此值，4 組重複）

---

## 10. 與 PPU 的介面設計

### 10.1 需要從 PPU 暴露的資訊

MMC5 需要知道：
- `Spritesize8x16` — 目前的 sprite 大小模式
- `chrBankPtrs[]` — CHR bank 指標陣列（MMC5 直接修改）
- `ntBankPtrs[]` — Nametable bank 指標陣列（MMC5 直接設定）
- `ntChrOverrideEnabled` — 是否啟用 NT override（影響 $2007 路徑）

### 10.2 PPU 需要呼叫 MMC5 的時機

| 時機 | 呼叫 | 說明 |
|------|------|------|
| BG tile fetch phase 1,3,5,7 | `NotifyVramRead(addr)` | VRAM 讀取通知 |
| Sprite fetch phase 1,3,5,7 (dots 258-320) | `NotifyVramRead(addr)` | sprite 的 garbage NT/AT + CHR |
| Garbage NT fetch (dots 337, 339) | `NotifyVramRead(0x2000 \| (v & 0xFFF))` | 掃描線偵測的關鍵 |
| Dot 257, before sprite render | `PreSpriteRender()` | 強制 A set |
| Odd-frame skip (dot 339) | `NotifyVramRead(...)` before skip | 確保偵測穩定 |
| Every CPU cycle | `CpuCycle()` | ppuIdleCounter 遞減 |
| NMI vector read ($FFFA/B) | 重置 frame 狀態 | 在 PRG read 中處理 |

### 10.3 推薦的介面模式

使用直接引用而非介面虛擬呼叫，因為 `NotifyVramRead` 每秒被呼叫數百萬次：

```csharp
// 在 NesCore 中
static Mapper005 mmc5Ref = null;  // null = 非 MMC5 遊戲

// 在 PPU 的 rendering loop 中
if (mmc5Ref != null) mmc5Ref.NotifyVramRead(addr);
```

`[MethodImpl(MethodImplOptions.AggressiveInlining)]` 標記 `NotifyVramRead` 以確保 JIT 內聯。

---

## 11. 實作順序建議

建議的實作順序（由易到難）：

1. **PRG banking** — 先讓遊戲能啟動
2. **CHR banking (無 A/B)** — 8×8 模式下的基本 CHR 切換
3. **Nametable mapping ($5105)** — 含 CIRAM/ExRAM/Fill
4. **乘法器 + PRG-RAM** — 簡單功能
5. **Scanline IRQ** — 實作 NotifyVramRead + 3-consecutive-read 偵測
6. **CHR A/B set** — 8×16 模式的 per-tile bank 切換
7. **Pre-sprite render fix** — 修正 dot 257 時序
8. **Extended attribute mode** — 進階功能

### 11.1 測試用 ROM

- **mmc5test** (homebrew): 驗證 split screen 的 mid-frame IRQ + CHR bank 切換
- **Castlevania III (US)**: 標題畫面的十字架游標（A/B set），2F 的 mid-frame IRQ（HUD ↔ gameplay CHR 切換）
- **Gemfire**: 基本 PRG/CHR banking
- **L'Empereur**: nametable mapping

---

## 12. 常見陷阱

### 12.1 $2007 不走 NT override

**症狀**: split screen 下半部全黑或內容錯誤
**原因**: CPU 透過 $2007 寫入 nametable 的資料沒經過 `ntBankPtrs`，而是走了硬體 mirroring
**修正**: $2007 的 NT 區域讀寫必須檢查 `ntChrOverrideEnabled`

### 12.2 奇數幀 scanline 計數器不穩定

**症狀**: scanline IRQ 每隔一幀觸發在錯誤的掃描線
**原因**: 奇數幀跳過 dot 339，導致 3-consecutive-read 模式變成只有 2 次
**修正**: 在跳過 dot 339 之前，發送 NotifyVramRead 通知

### 12.3 Sprite 使用錯誤的 CHR bank

**症狀**: 8×16 sprite 的 tile 圖案錯誤（使用了 BG 的 CHR bank）
**原因**: 批次 sprite 渲染在 CHR bank 切換到 A set 之前執行
**修正**: dot 257 呼叫 PreSpriteRender() 強制切換

### 12.4 ppuInFrame 在 VBlank 被設為 true

**症狀**: VBlank 期間的 CHR bank 選擇錯誤
**原因**: VBlank 期間的 PPU $2006/$2007 操作被當成 rendering 中的 VRAM access
**修正**: 只在 rendering scanline 期間發送 NotifyVramRead，或在 mapper 端正確處理 ppuIdleCounter

### 12.5 CHR mode 變更後沒有重新填充 bank pointers

**症狀**: $5101 寫入後 CHR bank 不更新
**原因**: 改變 CHR mode 後沒有呼叫 `UpdateChrBanks(true)`
**修正**: $5101 寫入時 force update

---

## 13. 參考實作

- **Mesen2**: `Core/NES/Mappers/Nintendo/MMC5.h` — 最完整的參考，含所有邊界情況
  - `MapperReadVram()` = 我們的 `NotifyVramRead()`
  - `UpdateChrBanks()` = CHR A/B 判定邏輯
  - `DetectScanlineStart()` = 3-consecutive-read 偵測
  - `ProcessCpuClock()` = ppuIdleCounter 遞減
- **NESdev Wiki**: https://wiki.nesdev.com/w/index.php/MMC5 — 暫存器定義與行為說明
- **TriCNES**: `ref/TriCNES-main/` — 另一個 136/136 AccuracyCoin 的參考實作
