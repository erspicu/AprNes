# 08 PPU Rendering Pipeline

## 這章要解決什麼問題

NES 背景不是一張 bitmap，sprite 也不是 CPU 即時畫到螢幕。PPU 依照固定時序抓取 tile、attribute、pattern、sprite 資料，經過 shift register 與 priority 規則，最後輸出 palette index。

本章說明 PPU rendering pipeline，以及 AprNes 如何用 `ppu_new.cs` 實作 dot-level 行為。

## NES 硬體觀念

**生活比喻**：把 PPU 想成餐廳裡的「**裝飾師**」，每秒畫 60 次餐桌擺盤。一張完整擺盤有 240 條橫向「**裝飾條**」(scanline)。每條裝飾條要花 341 個動作 (dot)：
- 動作 1–256：**真的擺盤** (可見像素)
- 動作 257–340：在準備下一條的材料 (預取下一條的 tile / sprite)

擺完 240 條後，餐廳暫時打烊 20 條時間 (VBlank) 不做事 —— 這時主廚 (CPU) 才能安全進場補貨 (改 PPU VRAM)。

```text
一幀 (frame) = 262 條 scanline (NTSC)：

  scanline   0 ─┐
              │ │ ← 240 條可見 scanline
              │ │   每條 341 dot，前 256 是可見像素
  scanline 239 ─┘
  scanline 240    post-render (PPU 不做事)
  scanline 241 ─┐ ← VBlank 開始
              │ │ ← VBlank 期間 (20 條 scanline)
              │ │   CPU 趁這時更新 PPU 內容
  scanline 260 ─┘   PPU 設 NMI 通知 CPU
  scanline 261    pre-render (PPU 預取第 0 條的資料)
  
  下一幀 scanline 0 從頭開始...
```

**為什麼遊戲程式都集中在 VBlank 才寫 PPU？** 因為 visible scanline 期間 PPU 正在用 VRAM bus 取 tile 資料。如果 CPU 同時試著寫 VRAM，會破壞當下的渲染。VBlank 是 PPU **暫停 VRAM 存取**的窗口，是遊戲改畫面的「空檔」。

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
byte 0  Y position - 1   (注意是「Y - 1」，因為 PPU 比較時序)
byte 1  Tile index
byte 2  Attributes        (palette / priority / horizontal flip / vertical flip)
byte 3  X position
```

OAM 共 256 byte = 64 個 sprite。**但每條 scanline 最多只能顯示 8 個 sprite** —— 這是 NES 硬體的鐵律。為什麼？因為 PPU 沒時間在每條 scanline 都掃完 64 個。

**生活比喻**：sprite 評估像「**搶鏡頭**」。每條 scanline 開始前，sprite 經紀人（PPU sprite evaluation hardware）有 192 個 dot 時間從 64 個演員 (OAM) 中挑出「下條 scanline 會出鏡的 8 位」。挑滿 8 位就停 —— 即使第 9 位也該出鏡。

```text
scanline N 進行中:
  dot   1- 64: 清空 secondary OAM (8 個 sprite slot 的暫存區)
  dot  65-256: 掃 OAM 找出 Y 落在 scanline N+1 的 sprite，
                填到 secondary OAM (最多 8 個)，
                第 9 個以後設定 sprite overflow flag
  dot 257-320: 從 CHR 取出 secondary OAM 內 sprite 的 pattern data，
                載入 sprite shifter

scanline N+1 開始 (dot 1-256):
  每個 dot 用 sprite shifter 輸出一個 sprite pixel，
  跟 background pixel 合成
```

**Sprite 0 hit**：OAM 第 0 號 sprite 是「特殊」的 —— 當它跟背景的非透明 pixel 重疊時，PPU 會設 `$2002` 的 bit 6。遊戲利用這個 flag 達成兩種神技：
1. **Split screen**：把 sprite 0 故意放在某條 scanline 上。當 CPU 看到 hit flag 設起來，就知道光柵掃到那條了 → 立刻寫 `$2005` 改變 scroll → 下半畫面用不同位置。例如《Super Mario Bros.》狀態列固定、下方關卡捲動就靠這招。
2. **時間量測**：知道光柵到哪了等同知道時間，可以做精確的計時。

模擬器要正確處理 sprite 0 hit 的精確 dot timing。差一個 dot，遊戲分屏會抖動。

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
