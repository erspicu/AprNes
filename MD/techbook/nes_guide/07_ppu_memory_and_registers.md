# 07 PPU Memory 與 Register

## 這章要解決什麼問題

CPU 不能直接把像素畫到螢幕。NES 的畫面由 PPU 自己根據 VRAM、CHR、OAM、palette 與 register 狀態產生。CPU 只能透過 `$2000-$2007` 這些 memory-mapped register 間接控制 PPU。

本章介紹 PPU memory map、重要 register、scroll latch 與 AprNes 的實作。

## NES 硬體觀念

PPU address space：

```text
$0000-$1FFF  Pattern tables, usually CHR ROM/RAM from cartridge
$2000-$2FFF  Name tables
$3000-$3EFF  Mirrors
$3F00-$3F1F  Palette RAM
$3F20-$3FFF  Palette mirrors
```

Pattern table 存 tile 圖形資料。每個 8x8 tile 使用 16 bytes，分成 low bitplane 與 high bitplane。

Name table 存畫面 tile index。背景不是 bitmap，而是由 tile index 組成。

Attribute table 決定每個區域使用哪組背景 palette。

Palette RAM 把 2-bit pattern 與 attribute 組合後的 palette index 對應到 NES 64 色表中的顏色。

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
