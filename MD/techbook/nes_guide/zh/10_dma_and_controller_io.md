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

**生活比喻**：手把不會「一次告訴你 8 個按鈕的狀態」。它像個**老式打卡機** —— 你拉一下拉桿（**strobe**），打卡機把當下的卡片載入；接著你按一次按鈕只能讀一個 bit，每按一次轉一格。讀完 8 次才知道全部按鈕的狀態。

NES controller 不是一次讀 8-bit button state。流程是：

```
1. CPU 寫 $4016 = 1   ←  strobe 高，shift register 持續載入當下按鍵
2. CPU 寫 $4016 = 0   ←  strobe 低，鎖定 register 內容
3. 讀 $4016 一次       ←  回傳 bit 0 (A 鈕的狀態)，下次讀回傳 B 的狀態
   讀 $4016 一次       ←  回傳 bit 0 (B 鈕)
   ...                       (內部 shift register 每次讀後右移)
   讀第 9 次以後        ←  回傳 1 (官方手把空狀態)
```

實際組語：

```assembly
read_controller:
    LDA  #$01           ; strobe = 1
    STA  $4016
    LDA  #$00
    STA  $4016          ; strobe = 0，鎖定當下按鍵
    LDX  #$08           ; 要讀 8 次
loop:
    LDA  $4016          ; 讀一個 bit (在 bit 0)
    LSR                 ; 把 bit 0 推到 carry
    ROL  buttons        ; 把 carry 推進 buttons 變數
    DEX
    BNE  loop
    RTS
```

Button order（讀回的順序）：

```text
讀第 1 次 → A
讀第 2 次 → B
讀第 3 次 → Select
讀第 4 次 → Start
讀第 5 次 → Up
讀第 6 次 → Down
讀第 7 次 → Left
讀第 8 次 → Right
讀第 9 次以後 → 1 (官方手把)；非官方手把 (Famicom 麥克風等) 可能不同
```

**為什麼這樣設計？** 1983 年的卡座只有 5 條腳位 (電源 / 地 / strobe / data1 / data2)，要在這麼少的線路上塞 8 個按鈕的狀態，只能用「序列傳輸」。串流方式硬體成本最低。**現代手把 (USB) 已經沒這個問題**，但 NES emulator 仍然要忠實模擬這個 shift-register 行為，否則某些遊戲會偵測不到輸入。

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
