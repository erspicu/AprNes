# 15 Mapper004 / MMC3

## 這章要解決什麼問題

MMC3 是 NES 上非常重要的 mapper。它支援細緻的 PRG/CHR bank switching、mirroring control、scanline IRQ。理解 MMC3，也能理解為什麼 PPU timing 會影響卡匣硬體。

本章以 AprNes 的 `Mapper004.cs`、`Mapper004RevA.cs`、`Mapper004MMC6.cs` 為準。

## NES 硬體觀念

**生活比喻**：MMC3 是任天堂自家的「**全能 mapper**」，幾乎是 NES 後半生命週期的標準裝備。它一次提供：
- **PRG 切換更細**：UNROM 一次切 16 KB，MMC3 一次切 8 KB（4 個槽位獨立切）。
- **CHR 切換更彈性**：CNROM 一次切 8 KB，MMC3 可以切兩個 2 KB + 四個 1 KB 共 8 個槽位。
- **內建 IRQ 計時器**：可以在「PPU 掃到第 X 條 scanline」自動通知 CPU。

最後一個是革命性 —— **scanline IRQ** 讓遊戲不用「狂查 sprite 0 hit」就能在精確的 raster line 觸發事件，達成複雜的 split scrolling、status bar、特效。**代表作品**：《Super Mario Bros. 3》、《Mega Man 3-6》、《Kirby's Adventure》、《Crystalis》。

MMC3 功能：

- 8KB PRG bank switching。
- 1KB / 2KB CHR bank switching。
- mirroring control。
- IRQ latch / reload / enable。
- **觀察 PPU A12 rising edge** 產生 scanline counter clock。

### PRG bank mode

MMC3 的 CPU `$8000-$FFFF` 分成四個 8KB 區間：

```text
$8000-$9FFF
$A000-$BFFF
$C000-$DFFF
$E000-$FFFF
```

最後一個 8KB 通常固定到最後 bank。PRG mode 決定 `$8000-$9FFF` 與 `$C000-$DFFF` 哪個是固定 second-last bank，哪個是可切換 bank。

### CHR bank mode

PPU `$0000-$1FFF` 分成 8 個 1KB slot。MMC3 有：

- 兩個 2KB CHR bank。
- 四個 1KB CHR bank。

CHR mode 決定 2KB bank 在低半部還是高半部。

### IRQ

MMC3 scanline IRQ 不是單純每條 scanline 加 1。**硬體實際上是觀察 PPU 位址 bus 的 A12 線**：

- PPU 讀 `$0000-$0FFF` 時 A12 = 0（pattern table 0）
- PPU 讀 `$1000-$1FFF` 時 A12 = 1（pattern table 1）
- 背景 / sprite pattern fetch 在不同時機讀不同 pattern table，造成 A12 訊號上升下降
- 每次 A12 從 0 → 1（rising edge）時 mapper 內部 counter 減 1
- counter 減到 0 觸發 IRQ

**生活比喻**：MMC3 不戴錶，是看「**廚房瓦斯爐什麼時候轉到大火**」來計時。背景跟 sprite 用不同 pattern table 的時候，A12 線會在掃描線中段「轉大火」一次。MMC3 卡這個訊號計算過了多少 scanline。

```text
標準 NES PPU 一條 scanline 的 pattern fetch：
  dot 1-256    背景 fetch (用 BG pattern table)
  dot 257-320  sprite fetch (用 sprite pattern table)
  dot 321-336  下一條 scanline 的背景前 2 個 tile

如果 BG 用 $0000 (A12=0), sprite 用 $1000 (A12=1)：
  scanline 進行中：A12 在 dot 256→257 切換時上升 ★ ← MMC3 看到這個 edge
  之後再切回 0
  下條 scanline 又一個 edge ★

每條可見 scanline 大約產生 1 次 A12 rising edge → MMC3 counter -1
```

**為什麼這個設計這麼複雜？** 因為任天堂想讓 MMC3 能精確計算 scanline，但又不想多拉訊號線到 mapper。**反正 PPU 本來就會在每條 scanline 對 pattern table 做特定模式的存取，A12 邊緣訊號就剛好是「免費的 scanline 時鐘」**。

副作用：**遊戲必須讓 BG 跟 sprite 用不同的 pattern table**（例如 BG 用 `$0000`、sprite 用 `$1000`），否則 A12 永遠不變化，scanline IRQ 不會運作。`$2000` register 的 bit 3、bit 4 控制這兩個選擇。

為了避免短暫 pulse 被誤判，需要 A12 low 持續一段時間後的 rising edge 才算。實作模擬器時通常用一個 counter 計算 A12 維持 low 多少 PPU cycle，超過閾值（一般是 8 cycle 左右）才認可下次 rising edge。MMC3 還有不同 revision（Sharp 版 vs IRQ A vs IRQ B），各家行為微妙不同。

## 初學者簡化模型

可以先把 MMC3 拆成三階段：

1. 實作 PRG bank mapping。
2. 實作 CHR bank mapping。
3. 實作 IRQ counter。

IRQ 初版可先 scanline-based，但要知道這不是最終正確模型。要接近 AprNes，必須讓 mapper 看到 PPU address bus 或 A12 edge。

## AprNes / NesCore 實作對照

### Register write

`Mapper004.cs` 的 `MapperW_PRG()` 依 address 與 odd/even 分派：

```text
$8000 even  bank select, PRG mode, CHR mode
$8001 odd   bank data
$A000 even  mirroring
$A001 odd   PRG RAM protect, base Mapper004 忽略
$C000 even  IRQ latch
$C001 odd   IRQ reload
$E000 even  IRQ disable and acknowledge
$E001 odd   IRQ enable
```

`BankReg` 決定 `$8001` 寫入要更新哪個 bank register。

### PRG read

`MapperR_RPG()` 依 `PRG_Bankmode`：

- mode 0：`$8000` 可切、`$C000` 固定 second-last。
- mode 1：`$8000` 固定 second-last、`$C000` 可切。
- `$A000` 使用 `PRG1_Bankselect`。
- `$E000` 固定最後 bank。

### CHR bank pointer

`UpdateCHRBanks()` 把 MMC3 的 CHR register 轉成 `NesCore.chrBankPtrs[0..7]`。

mode 0：

- 兩個 2KB bank 在 `$0000-$0FFF`。
- 四個 1KB bank 在 `$1000-$1FFF`。

mode 1：

- 四個 1KB bank 在 `$0000-$0FFF`。
- 兩個 2KB bank 在 `$1000-$1FFF`。

### A12 與 IRQ

`PpuClock()`：

- 讀 `NesCore.ppuAddressBus` 的 bit 12。
- 若 A12 low，累積 `m2Filter`。
- 若從 low 到 high 且 filter 達門檻，呼叫 `Mapper04step_IRQ()`。

`Mapper04step_IRQ()`：

- 處理 `IRQReset`。
- decrement 或 reload `IRQCounter`。
- counter 到 0 且 `IRQ_enable` 時設定 `NesCore.statusmapperint`。
- 呼叫 `NesCore.UpdateIRQLine()`。

### Rev A 與 MMC6

`Mapper004RevA.cs`：

- 繼承 `Mapper004`。
- 只覆寫 IRQ step。
- Rev A 的 counter 到 0 觸發條件不同。

`Mapper004MMC6.cs`：

- IRQ 行為同 Rev A。
- 額外支援 `$A001` PRG-RAM protect。
- `MapperR_RAM()` 與 `MapperW_RAM()` 依 bit 控制 lower/upper 1KB RAM read/write enable。

## 常見錯誤

- 用 CPU cycle 或 scanline number 直接 clock MMC3 IRQ。
- 忽略 PPU A12 filter。
- CHR 2KB bank 沒把低 bit forced even。
- PRG mode 0/1 的固定 bank 位置寫反。
- `$E000` disable 時沒有 acknowledge IRQ line。
- 忽略 MMC3 revision 差異，導致特定 test ROM 不通。

## 本章重點整理

1. MMC3 是 PRG、CHR、mirroring、IRQ 都具備的進階 mapper。
2. MMC3 IRQ 來自 PPU A12 edge，不是單純 scanline counter。
3. AprNes 讓 mapper 讀 PPU address bus，讓卡匣硬體能跟 PPU timing 互動。

## 下一章銜接

下一章整理從零寫 NES 模擬器的建議實作順序，把前面章節轉成可執行的開發路線。
