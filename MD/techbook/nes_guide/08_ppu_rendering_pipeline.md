# 08 PPU Rendering Pipeline

## 這章要解決什麼問題

NES 背景不是一張 bitmap，sprite 也不是 CPU 即時畫到螢幕。PPU 依照固定時序抓取 tile、attribute、pattern、sprite 資料，經過 shift register 與 priority 規則，最後輸出 palette index。

本章說明 PPU rendering pipeline，以及 AprNes 如何用 `ppu_new.cs` 實作 dot-level 行為。

## NES 硬體觀念

一幀大致由：

- visible scanline 0-239。
- post-render line。
- VBlank。
- pre-render line。

每條 scanline 有 341 dots。可見像素是前 256 dots，但 PPU 在後面的 dots 會準備下一條 scanline 的資料。

### 背景 pipeline

背景每 8 dots 抓一個 tile：

```text
Name table fetch
Attribute table fetch
Pattern low fetch
Pattern high fetch
Load shift registers
```

Shift register 每個 dot 移位，配合 `FineX` 取出目前 pixel 的 low/high bit，再加上 attribute bits 得到 palette index。

### Sprite pipeline

Sprite 資料在 OAM 中，每個 sprite 4 bytes：

```text
Y position
Tile index
Attributes
X position
```

PPU 每條 visible scanline 會：

- dots 1-64：清 secondary OAM。
- dots 65-256：evaluate sprites for next scanline。
- dots 257-320：fetch sprite pattern data。
- 下一條 scanline dots 1-256：使用 sprite shifter 輸出 sprite pixel。

每條 scanline 最多顯示 8 個 sprites。sprite overflow 與 sprite 0 hit 都有硬體細節，不能只用直覺實作。

### Pixel compose

每個 dot 會得到：

- background pixel。
- sprite pixel。

若 sprite pixel 非透明，且 background pixel 非透明，可能觸發 sprite 0 hit。Sprite attribute 的 priority bit 決定 sprite 在 background 前或後。

## 初學者簡化模型

第一版可以先 scanline-based：

1. 根據 scroll 找出目前 scanline 需要的背景 tile。
2. 解 pattern table 得到背景像素。
3. 掃 OAM 找出本 scanline 的 sprites。
4. 疊 sprite。
5. 產生 framebuffer。

這能讓遊戲顯示畫面。等需要支援 split scroll、sprite 0 hit 精準 timing、MMC3 IRQ，再改 dot-level。

## AprNes / NesCore 實作對照

`ppu_new.cs` 是 AprNes 的 PPU 主實作。

主要入口：

- `ppu_step_new()`：依 visible / vblank / pre-render 選 dispatch table。
- `ppu_half_step_new()`：處理 background shift、fetch commit、VBlank latch、sprite0 pipeline、`$2007` 第二階段。

背景相關欄位：

- `renderLow`, `renderHigh`。
- `renderAttrLow`, `renderAttrHigh`。
- `NTVal`, `ATVal`。
- `pendingTileLow`, `pendingTileHigh`。
- `pendingAttrLatch`。

Sprite 相關欄位：

- `spr_ram`：primary OAM。
- `secondaryOAM`：本 scanline 選出的 sprite。
- `sprShiftL`, `sprShiftH`。
- `sprXCounter`。
- `sprFetchAttr`。
- `sprSlotCount`。
- `sprZeroInSlots`。

重要函式：

- `SpriteEvalTick()`：per-dot sprite evaluation。
- `SpriteEvalEnd()`：結束 evaluation，計算 sprite count。
- `PpuPhase4_SpriteFetch()`：dots 257-320 抓 sprite pattern。
- `PpuPhase4_VisibleScanlineDot1Init()`：每條 visible scanline 開始前初始化 palette index buffer。
- `PpuPhase_FrameRender()`：frame 結束時轉換 palette index 並送出畫面。

AprNes 的畫面管線先寫 palette index 到 `ntsc_rowPalettes`。若不是 analog mode，frame end 時呼叫 `Convert_PalIdxFrameToRGB(digitalFrameRgb)`，再由 render path 輸出。

## 常見錯誤

- 背景以 pixel array 儲存，忽略 tile/attribute 結構。
- Sprite evaluation 直接掃完 64 sprites，沒有模擬 8 sprite limit 與 overflow 行為。
- sprite 0 hit 用整張圖後處理，導致 timing 錯誤。
- 忽略 pre-render line 對 scroll 與狀態旗標的重置。
- MMC3 IRQ 用 scanline 計數，而不是 PPU A12 行為。

## 本章重點整理

1. PPU 是固定時序的資料管線，不是 CPU 呼叫的繪圖函式。
2. 背景與 sprite 都透過 fetch、shift、compose 產生像素。
3. AprNes 用 dot dispatch 與 half step 表達 PPU 內部 pipeline timing。

## 下一章銜接

下一章會介紹 APU，並聚焦在 AprNes 的 `AudioMode = 0` Pure Digital 輸出路徑。
