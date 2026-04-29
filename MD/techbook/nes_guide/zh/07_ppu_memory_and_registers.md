# 07 PPU Memory 與 Register

## 這章要解決什麼問題

CPU 不能直接把像素畫到螢幕。NES 的畫面由 PPU 自己根據 VRAM、CHR、OAM、palette 與 register 狀態產生。CPU 只能透過 `$2000-$2007` 這些 memory-mapped register 間接控制 PPU。

本章介紹 PPU memory map、重要 register、scroll latch 與 AprNes 的實作。

## NES 硬體觀念

**先說一個重要觀念**：NES 的 PPU **沒有 framebuffer**。它不會把整個畫面存在某塊記憶體裡，再一次顯示出來。PPU 是「**邊掃邊算**」—— 電視射出電子束掃到第幾個 dot，PPU 就現場用 pattern table + name table + attribute + palette + sprite OAM 算出那個 dot 該是什麼顏色，立刻送出去。

**生活比喻**：背景畫面不是「一張完整的圖」，而是**馬賽克拼貼**：

- **Pattern table**（圖形樣版庫）：256 種 8×8 的 tile 樣板，像「貼紙簿」。
- **Name table**（畫面拼貼地圖）：32×30 = 960 格，每格寫一個「貼紙編號」。組合起來就是 256×240 的整個背景。
- **Attribute table**（顏色分區表）：每個 32×32 的區域指定要套用 4 組調色盤中的哪一組。
- **Palette RAM**：32 個 byte，存當前要用的調色盤（NES 全宇宙 64 種色，每幀只能挑 32 個出來用）。
- **OAM**（精靈表）：64 個 sprite × 4 byte = 256 byte，存「現在畫面上有哪些可移動角色」。

```
+------------+    +------------+
| Pattern    |    | Name       |     "去 pattern 庫第 42 號貼紙
| table      | ←  | table      | →   貼到第 (15, 8) 格"
| (CHR ROM)  |    | (1KB VRAM) |
+------------+    +------------+
                        ↓
                  +------------+    +------------+
                  | Attribute  | →  | Palette    |
                  | table      |    | RAM (32B)  |
                  +------------+    +------------+
                        ↓                ↓
                  電視第 (255, 127) 個 dot ⇒ 一個 NES 顏色 (0-63)
```

PPU address space（注意這是 PPU 自己的 14-bit 位址空間，跟 CPU 那個 16-bit 完全分開）：

```text
$0000-$0FFF  Pattern table 0  ┐
$1000-$1FFF  Pattern table 1  ┘ 通常從卡匣 CHR ROM/RAM 來，8 KB

$2000-$23FF  Nametable 0      ┐
$2400-$27FF  Nametable 1      │ 4 個邏輯 nametable，但
$2800-$2BFF  Nametable 2      │ 主機 VRAM 只有 2 KB 實體
$2C00-$2FFF  Nametable 3      ┘ → 必須兩兩鏡像 (mirror)

$3000-$3EFF  Nametable mirror

$3F00-$3F0F  Background palette (4 組 × 4 色)
$3F10-$3F1F  Sprite palette (4 組 × 4 色)
$3F20-$3FFF  Palette mirror
```

Pattern table 存 tile 圖形資料。每個 8x8 tile 使用 16 bytes，分成 low bitplane 與 high bitplane。**為什麼分兩個 bitplane？** 因為每個 pixel 是 2-bit (4 種顏色)，硬體把同一行 8 個 pixel 的低位 bit 集中放、高位 bit 集中放，PPU 渲染時並行抓兩個 byte 就拿到 8 個 pixel 的顏色 index：

```
tile byte $00-07: pattern low bitplane     (每行 8 個 pixel 的低位)
tile byte $08-0F: pattern high bitplane    (每行 8 個 pixel 的高位)

例：byte $00 = 01000010, byte $08 = 11000000
   pixel  0 1 2 3 4 5 6 7
   low    0 1 0 0 0 0 1 0
   high   1 1 0 0 0 0 0 0
   合併   2 3 0 0 0 0 1 0  ← 每個 pixel 的 2-bit 顏色 index
```

Name table 存畫面 tile index。**背景不是 bitmap**，而是由 tile index 組成。一張螢幕 = 32 × 30 = 960 個 byte 來指定要貼哪些 tile。

Attribute table 決定每個 32×32 像素區域使用哪組 palette（NES 一個 attribute byte 控制 4 個 16×16 區塊各 2 bit）。**這是 NES 顏色限制的主因** —— 為什麼老遊戲 sprite 邊緣有時會出現「色塊撕裂」？因為角色跨越了 attribute boundary。

Palette RAM 把 2-bit pattern + 2-bit attribute = 4-bit palette index，對應到 NES 64 色表中的顏色。

## PPU Registers

CPU 透過 `$2000-$2007` 控制 PPU：

```text
$2000 PPUCTRL
$2001 PPUMASK
$2002 PPUSTATUS
$2003 OAMADDR
$2004 OAMDATA
$2005 PPUSCROLL
$2006 PPUADDR
$2007 PPUDATA
```

### `$2000` PPUCTRL

控制：

- base nametable。
- VRAM increment 是 1 或 32。
- sprite/background pattern table。
- sprite size。
- NMI enable。

### `$2001` PPUMASK

控制：

- background enable。
- sprite enable。
- left 8 pixel mask。
- greyscale。
- emphasis bits。

### `$2002` PPUSTATUS

讀取：

- bit 7：VBlank。
- bit 6：sprite 0 hit。
- bit 5：sprite overflow。

讀 `$2002` 也會重置 `$2005/$2006` write latch。

### `$2005` 與 `$2006`

這兩個 register 不是普通寫入。

`$2005`：

- 第一次寫 horizontal scroll。
- 第二次寫 vertical scroll。

`$2006`：

- 第一次寫 VRAM address high byte。
- 第二次寫 low byte，排程更新 `v`。

### `$2007`

`$2007` 讀寫 PPU memory。非 palette 讀取有 read buffer delay。每次讀寫後，VRAM address 依 `$2000` bit 2 加 1 或 32。

## 初學者簡化模型

可以先建立 Loopy scrolling 變數：

- `v`：current VRAM address。
- `t`：temporary VRAM address。
- `x`：fine X scroll。
- `w`：write toggle。

先把 `$2005/$2006/$2007` 做成近似行為，再逐步補延遲、open bus、palette read 特例。

## AprNes / NesCore 實作對照

`PPU.cs` 相關欄位：

- `vram_addr`：目前 VRAM address。
- `vram_addr_internal`：temporary address。
- `FineX`：fine X。
- `vram_latch`：write toggle。
- `ppu_2007_buffer`：PPUDATA read buffer。
- `openbus`：PPU bus 殘留值。

Register handler：

- `ppu_w_2000()`：處理 PPUCTRL，包含 TriCNES 模型的 2 master-clock push。
- `ppu_w_2001()`：設定 rendering enable、mask、emphasis 的延遲更新。
- `ppu_r_2002()`：讀 status，處理 deferred VBlank clear 與 latch reset。
- `ppu_w_2005()`：排程 scroll update。
- `ppu_w_2006()`：排程 t 到 v 的 copy。
- `ppu_r_2007()` / `ppu_w_2007()`：透過 `$2007` SR latch pipeline。

`ppu_new.cs` 中：

- `PpuPhase2_DeferredUpdates()` 處理 `$2005/$2006` 延遲。
- `PPU_DATA_Pipeline_Step1()` 與 `PPU_DATA_Pipeline_Step3()` 處理 `$2007` read/write pipeline。

## 常見錯誤

- 把 `$2007` 讀取寫成直接回傳 VRAM，忽略 read buffer。
- 讀 `$2002` 後沒有重置 `$2005/$2006` latch。
- 忽略 palette mirror，例如 `$3F10` 與 `$3F00` 的特殊鏡像。
- Rendering 開啟中仍用簡單 VRAM read/write 模型。
- `$2005/$2006` 寫入立即改變所有內部狀態，導致 split scroll timing 錯誤。

## 本章重點整理

1. CPU 透過 PPU register 間接操作畫面晶片。
2. `$2005/$2006/$2007` 的 latch、buffer、延遲是 PPU 模擬的核心難點。
3. AprNes 把 PPU register 行為放進 dot-level pipeline 中，而不是只做普通變數更新。

## 下一章銜接

下一章會介紹 PPU 實際如何在每個 scanline 與 dot 產生背景、精靈與最後的 palette index。
