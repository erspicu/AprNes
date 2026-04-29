# 10 DMA 與 Controller I/O

## 這章要解決什麼問題

OAM DMA、DMC DMA 與 Controller I/O 都透過 CPU bus 運作。它們看似是周邊功能，但會直接影響 CPU cycle 與 bus value。

本章說明 DMA 與 JoyPad 的硬體行為，並對照 AprNes 的 `MEM.cs` 與 `JoyPad.cs`。

## NES 硬體觀念

### OAM DMA

CPU 寫 `$4014` 會啟動 OAM DMA。寫入值代表 source page：

```text
write $4014 = XX
source = $XX00-$XXFF
destination = PPU OAM 256 bytes
```

DMA 期間 CPU 會被 halt，DMA 交替執行讀 source 與寫 OAM 的 cycle。

### DMC DMA

DMC 需要 sample byte 時，會從 CPU memory 讀一個 byte。這也會插入 CPU bus timing，甚至會與 OAM DMA 互動。

### Controller serial read

NES controller 不是一次讀 8-bit button state。流程是：

1. CPU 寫 `$4016` bit 0 設定 strobe。
2. strobe 高時 controller shift register 載入目前按鍵。
3. strobe 低後，每次讀 `$4016` 或 `$4017` 取出一個 bit。
4. 讀完後通常回傳 1。

Button order 通常是：

```text
A, B, Select, Start, Up, Down, Left, Right
```

## 初學者簡化模型

OAM DMA 初版可以：

- 寫 `$4014` 時立刻複製 256 bytes。
- 同時讓 CPU 增加 513 或 514 cycles。

Controller 初版可以：

- 用一個 byte 保存 buttons。
- strobe 從 1 變 0 後，每次 read shift 一 bit。

之後再加入 AprNes 類似的 per-cycle DMA 與 controller shift delay。

## AprNes / NesCore 實作對照

### DMA

`MEM.cs` 的狀態：

- `spriteDmaTransfer`：OAM DMA 進行中。
- `spriteDmaOffset`：source page。
- `dmaOamHalt`。
- `dmaOamAligned`。
- `dmaOamAddr`。
- `dmcDmaRunning`。
- `dmcDmaHalt`。

`DmaOneCycle()` 每次只做一個 DMA cycle，並依 GET/PUT phase 決定：

- OAM DMA get。
- OAM DMA put。
- DMC DMA get。
- halted fetch。

重要函式：

- `DmaFetch()`：DMA bus read，包含 open bus 與 `$4015/$4016/$4017` 特例。
- `OamDmaGet()`：讀 source byte。
- `OamDmaPut()`：寫到 OAM。
- `DmcDmaGet()`：讀 DMC sample byte。

`PPU.cs` 中的 `ppu_w_4014()` 只設定 DMA 狀態，真正搬運由 master clock CPU gate 逐 cycle 處理。

### Controller

`JoyPad.cs`：

- `P1_Port`, `P2_Port`：目前按鍵狀態。
- `P1_ShiftRegister`, `P2_ShiftRegister`：serial read 用 shift register。
- `P1_ShiftCounter`, `P2_ShiftCounter`：讀取後延遲 shift。
- `controllerStrobing`, `controllerStrobed`。

讀取：

- `gamepad_r_4016()` 回傳 player 1 目前 bit。
- `gamepad_r_4017()` 回傳 player 2 目前 bit。
- 高位保留 `cpubus & 0xE0`。

寫入：

- `gamepad_w_4016()` 設定 strobe flag。

Shift 與 strobe reload 不是在 read function 立即完成，而是在 `apu_step()` 中呼叫：

- `ProcessControllerShift()`。
- `ProcessControllerStrobe()`。

## 常見錯誤

- OAM DMA 只複製資料，不阻塞 CPU。
- DMC DMA 完全忽略 CPU bus 影響。
- Controller 每次 read 都直接查目前鍵盤狀態，而不是 shift register。
- strobe 高時沒有持續 reload button state。
- 忽略 `$4016/$4017` 高位 open bus。

## 本章重點整理

1. DMA 是硬體接管 CPU bus，不是普通記憶體複製。
2. Controller 是 serial shift read，不是一次讀完整 button byte。
3. AprNes 把 DMA 與 controller 都放進 cycle timing，避免周邊功能破壞硬體時序。

## 下一章銜接

下一章開始 Mapper 系列，先從最簡單的 Mapper000 / NROM 介紹卡匣硬體如何接到 CPU 與 PPU bus。
