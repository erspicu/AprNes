# 02 寫模擬器前必懂的硬體觀念

## 這章要解決什麼問題

NES 模擬器的程式碼會大量出現遮罩、移位、鏡像、open bus、latch、DMA、IRQ、NMI、cycle 等字眼。這些不是實作細節，而是硬體本來的運作方式。

本章整理後續會反覆使用的硬體觀念。

## NES 硬體觀念

### Bit field

NES register 常常是一個 byte 裡每個 bit 都有不同意義。例如 PPU `$2000`：

```text
bit 7  NMI enable
bit 5  sprite size
bit 4  background pattern table
bit 3  sprite pattern table
bit 2  VRAM increment
bit 1  base nametable bit 1
bit 0  base nametable bit 0
```

所以 emulator 會常看到：

```csharp
NMIable = (value & 0x80) != 0;
VramaddrIncrement = (value & 0x04) != 0 ? 32 : 1;
```

### Address bus 與 data bus

CPU 對外溝通時，通常是：

1. address bus 放出地址。
2. read/write pin 表示讀或寫。
3. data bus 傳送一個 byte。

如果地址對應 RAM，就讀寫 RAM。如果地址對應 PPU register，就觸發 PPU register 的行為。

### Memory-mapped I/O

NES 沒有獨立的 I/O 指令。CPU 用一般 memory read/write 控制硬體：

```text
$2000-$2007  PPU registers
$4000-$4013  APU channel registers
$4014        OAM DMA
$4015        APU status
$4016        Controller strobe / read
$4017        APU frame counter / controller 2 read
```

讀寫這些地址會有副作用。例如讀 `$2002` 會影響 VBlank 與 write latch，寫 `$4014` 會啟動 OAM DMA。

### Mirroring

Mirroring 是多個地址對應同一份實體硬體。

CPU 內建 RAM 只有 2KB，卻出現在 `$0000-$1FFF`：

```text
$0000-$07FF  actual RAM
$0800-$0FFF  mirror
$1000-$17FF  mirror
$1800-$1FFF  mirror
```

因此讀寫 CPU RAM 時常用 `addr & 0x7FF`。

PPU register 也每 8 bytes 鏡像一次，因此 `$2008` 等同 `$2000`，`$3FFF` 以前都會反覆映射到 `$2000-$2007`。

### Latch

Latch 是硬體裡暫存狀態的概念。CPU 寫入 register 後，結果不一定是普通變數立即被完整更新。

PPU `$2005` 和 `$2006` 就依賴 latch：

- 第一次寫 `$2005` 是 horizontal scroll。
- 第二次寫 `$2005` 是 vertical scroll。
- 第一次寫 `$2006` 是 VRAM address high byte。
- 第二次寫 `$2006` 是 low byte 並排程更新 address。

### Open bus

Open bus 指 data bus 沒有被新的硬體值主動驅動時，讀到殘留值。這會影響一些測試 ROM 與特殊遊戲行為。

AprNes 中可以看到 `openbus` 與 `cpubus`：

- `openbus`：PPU 相關的 bus 殘留。
- `cpubus`：CPU data bus 最近值。

### Clock 與 cycle

不要把所有「cycle」混在一起。

- master clock：整台機器的基準時脈。
- CPU cycle：CPU 一次 bus cycle 或內部步進。
- PPU dot：PPU 畫面管線的一個像素時序。
- APU step：音訊硬體更新一次。

NTSC NES 中，PPU 大約每 CPU cycle 前進 3 dots。AprNes 更進一步用 master clock gate 描述各硬體在哪個 phase 動作。

### IRQ 與 NMI

Interrupt 是硬體請 CPU 暫停目前流程，跳去執行 interrupt handler。

- NMI：不可遮罩中斷，NES 常由 PPU VBlank 觸發。
- IRQ：可遮罩中斷，可能來自 APU frame counter、DMC、Mapper。

CPU 不是任意瞬間都跳中斷，而是在特定 instruction boundary 輪詢中斷狀態。

### DMA

DMA 是硬體接管 bus 搬資料。OAM DMA 會把 CPU memory 的 256 bytes 搬到 PPU OAM。DMC DMA 會讀取 sample byte。

DMA 不是普通 `Array.Copy`，因為它會消耗 CPU bus cycle，並與 CPU read/write phase 互動。

## 初學者簡化模型

第一版可以這樣處理：

- RAM mirroring 用 `addr & mask`。
- PPU/APU/IO 先做最常用 register。
- open bus 先回傳上一個 bus value。
- DMA 先用 cycle count 阻塞 CPU。
- IRQ/NMI 先在 instruction 結束時檢查。

等遊戲能跑，再逐步逼近 AprNes 的 per-cycle 行為。

## AprNes / NesCore 實作對照

- `CPU.cs`
  - `CpuRead()` / `CpuWrite()` 設定 `cpuBusAddr`, `cpuIsRead`, `cpubus`。
  - `PollInterrupts()` 在 instruction 完成前輪詢 NMI/IRQ。
- `MEM.cs`
  - `Read_NesRam()` 用 `addr & 0x7FF` 處理 RAM mirror。
  - `DmaOneCycle()` 每次只執行一個 DMA cycle。
  - `DmaFetch()` 處理 DMA 讀取、open bus 與 APU/joypad bus conflict。
- `PPU.cs`
  - `vram_latch`, `ppu_2007_buffer`, `openbus`。
  - `$2005/$2006/$2007` 都有延遲或 pipeline 行為。
- `IO.cs`
  - 把 CPU 對 `$2000-$4017` 的讀寫導向 PPU/APU/JoyPad。

## 常見錯誤

- 把 PPU register 當普通陣列。
- 忽略 mirror，導致遊戲讀寫錯位址。
- 在 CPU 寫 `$2006` 後立即更新所有 PPU 內部狀態，忽略延遲。
- 用簡單布林值代表 IRQ，卻沒有區分 IRQ line current 與 CPU 已取樣狀態。
- 把 DMA 寫成瞬間複製，完全不影響 CPU timing。

## 本章重點整理

1. NES 透過 memory-mapped I/O 控制硬體。
2. Bus、latch、open bus、DMA 都會產生可觀察行為。
3. AprNes 的許多複雜度都是為了讓這些硬體細節出現在正確時序。

## 下一章銜接

下一章進入 ROM 載入，介紹 `.nes` 檔案、iNES header、PRG ROM、CHR ROM、Mapper 編號與 AprNes 的初始化流程。
