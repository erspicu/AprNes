# AprNes Bug 修復紀錄

本文件記錄 push 至 GitHub 後的所有 bug 修復，包含問題現象、根本原因、影響位置及修復方式。

---

## 目錄

1. [MMC3 IRQ 錯誤計時條件](#1-mmc3-irq-錯誤計時條件)
2. [MMC3 $C001 IRQ Reload 寫入邏輯錯誤](#2-mmc3-c001-irq-reload-寫入邏輯錯誤)
3. [MMC3 $E000 IRQ Disable 寫入邏輯錯誤](#3-mmc3-e000-irq-disable-寫入邏輯錯誤)
4. [8x16 精靈垂直翻轉 tile 交換遺漏](#4-8x16-精靈垂直翻轉-tile-交換遺漏)
5. [$2001 左 8 像素遮罩未實作](#5-2001-左-8-像素遮罩未實作)
6. [每條掃描線精靈上限 8 個未實作](#6-每條掃描線精靈上限-8-個未實作)
7. [精靈掃描範圍與渲染範圍 off-by-1](#7-精靈掃描範圍與渲染範圍-off-by-1)
8. [背景 Fine Y 使用 scrol_y 而非 vram_addr](#8-背景-fine-y-使用-scrol_y-而非-vram_addr)
9. [MMC3 IRQ 未在 pre-render scanline 261 計時](#9-mmc3-irq-未在-pre-render-scanline-261-計時)

---

## 1. MMC3 IRQ 錯誤計時條件

### 現象
Super Mario Bros. 3 進入關卡後，背景圖形出現水平錯位、花屏或分割線失效。

### 根本原因
`PPU.cs` 中，MMC3 IRQ 計時器的觸發條件限制過嚴：只有同時滿足「精靈 pattern table 在 $1000」**且**「背景 pattern table 在 $0000」時才計時。這只是兩種 pattern table 配置中的一種，其他配置下 IRQ 永遠不會被觸發。

NES 硬體的 MMC3 IRQ 是由 PPU 地址匯流排的 A12 訊號上升沿觸發（從低電位 → 高電位），每條可見掃描線必定發生一次，與 pattern table 的配置無關。

### 影響位置
`PPU.cs`：`ppu_step()` 內的 IRQ 計時判斷。

### 修復方式
移除多餘的 pattern table 條件，改為只要渲染啟用（`ShowBackGround || ShowSprites`）且 mapper 為 4，就在每條掃描線的 cycle 260 計時一次：

```csharp
// 修復前（條件過嚴）
if (SpPatternTableAddr == 0x1000 && BgPatternTableAddr == 0 && mapper == 4)
    (MapperObj as Mapper004).Mapper04step_IRQ();

// 修復後
if ((ShowBackGround || ShowSprites) && mapper == 4)
    (MapperObj as Mapper004).Mapper04step_IRQ();
```

---

## 2. MMC3 $C001 IRQ Reload 寫入邏輯錯誤

### 現象
同上（MMC3 遊戲畫面分割線位置不正確）。

### 根本原因
`Mapper004.cs` 中，CPU 寫入 `$C001`（奇數位址）應設定「下次 A12 上升沿時重新載入計數器」的旗標（`IRQReset = true`）。但原始實作誤寫為直接對計數器做 OR 運算（`IRQCounter |= 0x80`），完全無效。

NES 硬體規格：`$C001` 寫入的作用是使計數器在**下一個** A12 上升沿到來時從 latch 值重新載入，而非立即修改計數器。

### 影響位置
`Mapper004.cs`：`MapperW_PRG()` 中 `address < 0xe000` 的奇數位址分支。

### 修復方式
```csharp
// 修復前（錯誤，直接修改計數器）
else if (address < 0xe000)
{
    IRQCounter |= 0x80;
    // ...
}

// 修復後（正確，設定重新載入旗標）
else if (address < 0xe000)
{
    IRQReset = true;
}
```

---

## 3. MMC3 $E000 IRQ Disable 寫入邏輯錯誤

### 現象
同上（MMC3 遊戲 IRQ 節奏混亂）。

### 根本原因
`Mapper004.cs` 中，CPU 寫入 `$E000`（偶數位址）應停用 IRQ（`IRQ_enable = false`），原始實作除停用外還多做了 `IRQCounter = IRQlatchVal`（重新載入計數器），這是錯誤的副作用。

NES 硬體規格：`$E000` 寫入的唯一作用是將 IRQ 停用旗標清除，計數器狀態不應被改變。

### 影響位置
`Mapper004.cs`：`MapperW_PRG()` 中 `address >= 0xe000` 的偶數位址分支。

### 修復方式
```csharp
// 修復前（多餘的計數器重載）
else
{
    IRQ_enable = false;
    IRQCounter = IRQlatchVal; // ← 錯誤
}

// 修復後
else
{
    IRQ_enable = false;
}
```

---

## 4. 8x16 精靈垂直翻轉 tile 交換遺漏

### 現象
MMC3 遊戲中，8x16 模式的精靈在垂直翻轉時，上下兩個 tile 的圖案顛倒（tile 位置沒有跟著翻）。

### 根本原因
8x16 精靈由兩個 8x8 tile 組成（上 tile 和下 tile）。垂直翻轉時，除了每個 tile 內部的像素列要顛倒外，上下兩個 tile 本身也需要互換（上 tile 顯示在下、下 tile 顯示在上）。原始程式碼只做了列翻轉，沒有交換 tile 編號。

### 影響位置
`PPU.cs`：`RenderSpritesLine()` 中 8x16 垂直翻轉判斷。

### 修復方式
```csharp
if ((sprite_attr & 0x80) != 0)
{
    line_t = 7 - line;
    if (Spritesize8x16) tile_th_t ^= 1; // ← 新增：交換上下 tile（bit0 切換奇偶）
}
```

---

## 5. $2001 左 8 像素遮罩未實作

### 現象
部分遊戲左邊緣 8 像素應顯示背景色（被遮罩），卻顯示出背景 tile 或精靈圖案，造成邊緣閃爍。

### 根本原因
NES PPU `$2001` 暫存器的 bit 1（背景）和 bit 2（精靈）控制最左側 8 像素是否顯示。遊戲通常在捲動時關閉這兩個 bit，避免左邊緣 tile 部分顯示出來。原始程式碼完全沒有讀取這兩個 bit 的邏輯。

### 影響位置
- `PPU.cs`：`ppu_w_2001()`、`RenderBackGroundLine()`、`RenderSpritesLine()`。

### 修復方式

新增兩個靜態欄位：
```csharp
static bool ShowBgLeft8 = true, ShowSprLeft8 = true;
```

在 `ppu_w_2001()` 中讀取：
```csharp
static void ppu_w_2001(byte value)
{
    ShowBgLeft8  = (value & 0x02) != 0;
    ShowSprLeft8 = (value & 0x04) != 0;
    ShowBackGround = (value & 0x08) != 0;
    ShowSprites    = (value & 0x10) != 0;
}
```

背景渲染：當 `ShowBgLeft8 = false` 且 pixel X < 8 時，輸出背景色（`ppu_ram[0x3f00]`）並將 `Buffer_BG_array` 設為 0（透明）。

精靈渲染：當 `ShowSprLeft8 = false` 且 `screenX < 8` 時跳過該像素。

---

## 6. 每條掃描線精靈上限 8 個未實作

### 現象
部分精靈在 NES 硬體上因超出每條掃描線 8 個上限而被隱藏，但模擬器卻全部渲染出來，導致原本應消失的精靈（如敵人、遮罩精靈）顯示出來。

### 根本原因
NES 硬體的次要 OAM（Secondary OAM）每條掃描線最多只能容納 8 個精靈；OAM 掃描時按 sprite 0 → sprite 63 的順序，先選到的優先，第 9 個以後的精靈不被渲染。原始程式碼將全部 64 個精靈都渲染，無上限限制。

遊戲設計師依賴這個限制：透過「刻意放置超過 8 個精靈」使特定精靈超出上限而不渲染，達到遮罩或隱藏效果。

### 影響位置
`PPU.cs`：`RenderSpritesLine()`。

### 修復方式
將渲染改為兩階段設計：

```csharp
static void RenderSpritesLine()
{
    // Pass 1：按 OAM 順序掃描，選出前 8 個可見精靈，第 9 個起只設溢出旗標
    int* sel = stackalloc int[8];
    int selCount = 0, spriteCount = 0;
    int height = Spritesize8x16 ? 15 : 7;

    for (int oam_th = 0; oam_th < 64; oam_th++)
    {
        int raw_y = spr_ram[oam_th << 2];
        if (scanline <= raw_y || scanline - raw_y > height + 1) continue;
        if (++spriteCount == 9) isSpriteOverflow = true;
        if (selCount < 8) sel[selCount++] = oam_th;
    }

    if (!ShowSprites) return;

    // Pass 2：以反向 OAM 順序渲染（讓較低編號的精靈覆蓋較高編號）
    for (int si = selCount - 1; si >= 0; si--)
    {
        // ... 渲染 sel[si] 號精靈
    }
}
```

使用 `stackalloc int[8]` 分配於 stack，避免 GC 壓力。

---

## 7. 精靈掃描範圍與渲染範圍 off-by-1

### 現象
實作第 6 項修復後，精靈渲染出現破損：部分精靈頂端多出一行雜訊（讀取 tile 第 -1 列），且精靈底部最後一行遺失。

### 根本原因
NES OAM 的 Y 欄位儲存的值為「顯示頂端掃描線 − 1」，即 `y_loc = raw_y + 1`。

Pass 1 掃描時使用了以 `raw_y` 為基準的範圍 `[raw_y, raw_y+height]`，但 Pass 2 渲染時用 `y_loc = raw_y + 1` 計算 `line = scanline - y_loc`。兩者基準點相差 1，導致：

| 情況 | 結果 |
|------|------|
| `scanline == raw_y` 時被選入 | `line = -1` → 讀取 CHR 資料第 -1 列（記憶體越界，出現雜訊） |
| `scanline == raw_y + height + 1` 時未被選入 | 精靈底部最後一列永遠不渲染 |

### 影響位置
`PPU.cs`：`RenderSpritesLine()` 的 Pass 1 掃描條件。

### 修復方式
將掃描條件改為以「顯示基準點」為範圍，與 Pass 2 的 `y_loc` 一致：

```csharp
// 修復前（基準為 raw_y）
if (raw_y > scanline || scanline - raw_y > height) continue;

// 修復後（基準為 raw_y+1，與 y_loc 一致）
// OAM Y = display_top - 1，顯示範圍為 [raw_y+1, raw_y+1+height]
if (scanline <= raw_y || scanline - raw_y > height + 1) continue;
```

修正後：
- `scanline == raw_y + 1` 時：`line = 0`（正確的第 0 列）
- `scanline == raw_y + height + 1` 時：`line = height`（正確的最後一列）

---

## 8. 背景 Fine Y 使用 scrol_y 而非 vram_addr

### 現象
MMC3 遊戲（如 Super Mario Bros. 3）進入關卡後，背景分割線以下的畫面出現水平線條移位或 tile 圖案錯行（「畫面線跳到其他位置」）。

### 根本原因
`GetAttr()` 函式在讀取背景 tile 的 pattern table 時，使用下列公式計算 fine Y（tile 列內的像素列偏移）：

```csharp
// 錯誤
array_loc = (ppu_ram[tileAddr] << 4) + BgPatternTableAddr + ((scanline + scrol_y) & 7);
```

`scrol_y` 只在 CPU 寫入 `$2005`（第二次）時更新。SMB3 的捲軸分割（split scroll）是在 MMC3 IRQ handler 內透過寫入 `$2006` 直接更新 `vram_addr`，這條路徑完全不觸碰 `scrol_y`。

結果：分割線以下的畫面，`scrol_y` 是舊值，fine Y 算錯，每個 tile 從錯誤的像素列讀取 CHR 資料 → 背景圖案每格往上或往下偏移。

NES PPU 的正確 fine Y 永遠是 `vram_addr` 的 bits 12–14，不論捲軸是透過 `$2005` 還是 `$2006` 設定都保證正確。

### 影響位置
`PPU.cs`：`GetAttr()` 中的 `array_loc` 計算。

### 修復方式
```csharp
// 修復前（scrol_y 不受 $2006 更新）
array_loc = (ppu_ram[tileAddr] << 4) + BgPatternTableAddr + ((scanline + scrol_y) & 7);

// 修復後（直接從 vram_addr bits 12-14 讀取 fine Y）
array_loc = (ppu_ram[tileAddr] << 4) + BgPatternTableAddr + ((vram_addr >> 12) & 7);
```

**與現有遊戲的相容性**：無捲軸分割的普通遊戲，`(scanline + scrol_y) & 7` 和 `(vram_addr >> 12) & 7` 數學等價，修復不影響現有遊戲行為。

**連帶修復**：背景 `Buffer_BG_array` 數值正確後，後景精靈（priority=1）的遮蔽判斷也恢復正常，同步解決「精靈該隱蔽卻顯示」的問題。

---

## 9. MMC3 IRQ 未在 pre-render scanline 261 計時

### 現象
MMC3 遊戲的畫面分割效果每幀偏移一條掃描線，或分割線高度與預期差 1（輕微但累積後可見）。

### 根本原因
MMC3 IRQ 計數器由 PPU A12 上升沿觸發。NES 硬體在**所有渲染掃描線**觸發，包含：

- 可見掃描線：0–239（共 240 次/幀）
- Pre-render 掃描線：261（共 1 次/幀，合計 241 次/幀）

原始程式碼的條件 `if (scanline < 240)` 只計時 0–239，少了 scanline 261 這一次。每幀少計一次，等於 IRQ latch 的有效值比遊戲期望的多 1，導致分割線在錯誤的掃描線觸發。

### 影響位置
`PPU.cs`：`ppu_step()` 中的 IRQ 計時區塊（原本只在 `if (scanline < 240)` 內）。

### 修復方式
在 `if (scanline < 240)` 的 `else if` 鏈中，為 scanline 261 額外加入計時：

```csharp
if (scanline < 240)
{
    ...
    else if (ppu_cycles_x == 260)
    {
        if ((ShowBackGround || ShowSprites) && mapper == 4)
            (MapperObj as Mapper004).Mapper04step_IRQ();
    }
}
else if (scanline == 261 && ppu_cycles_x == 260)
{
    // pre-render scanline 也要計時（硬體同樣在此產生 A12 上升沿）
    if ((ShowBackGround || ShowSprites) && mapper == 4)
        (MapperObj as Mapper004).Mapper04step_IRQ();
}
```

---

## 修復對應檔案彙整

| # | 檔案 | 函式/位置 | 修復內容 |
|---|------|-----------|----------|
| 1 | `PPU.cs` | `ppu_step()` cycle 260 | 移除 pattern table 條件限制 |
| 2 | `Mapper004.cs` | `MapperW_PRG()` $C001 odd | 改為 `IRQReset = true` |
| 3 | `Mapper004.cs` | `MapperW_PRG()` $E000 even | 移除 `IRQCounter = IRQlatchVal` |
| 4 | `PPU.cs` | `RenderSpritesLine()` vflip | 加入 8x16 tile 交換 `tile_th_t ^= 1` |
| 5 | `PPU.cs` | `ppu_w_2001()`、渲染函式 | 新增 `ShowBgLeft8`/`ShowSprLeft8` 遮罩 |
| 6 | `PPU.cs` | `RenderSpritesLine()` | 兩階段選取 + 最多 8 精靈限制 |
| 7 | `PPU.cs` | `RenderSpritesLine()` Pass 1 | 掃描條件 `scanline <= raw_y \|\| ... > height + 1` |
| 8 | `PPU.cs` | `GetAttr()` | `(vram_addr >> 12) & 7` 取代 `(scanline + scrol_y) & 7` |
| 9 | `PPU.cs` | `ppu_step()` scanline 261 | 補上 pre-render scanline 的 IRQ 計時 |
