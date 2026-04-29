# 05 6502 CPU Core

## 這章要解決什麼問題

CPU core 是 emulator 最容易開始、也最容易低估的部分。表面上是解 opcode，實際上還要處理 addressing mode、flag、dummy read、read-modify-write、interrupt polling、DMA 插入點。

本章以 AprNes 的 `CPU.cs` 為準，介紹 6502 core 的基本結構。

## NES 硬體觀念

NES CPU 是 Ricoh 2A03，核心接近 MOS 6502，但不支援正常 BCD decimal mode。

主要 register：

```text
A   accumulator
X   index X
Y   index Y
SP  stack pointer, stack page = $0100-$01FF
PC  program counter
P   status flags: N V - B D I Z C
```

狀態旗標：

- N：negative。
- V：overflow。
- D：decimal，2A03 不使用 decimal arithmetic。
- I：IRQ disable。
- Z：zero。
- C：carry。

CPU 指令不是一個函式瞬間做完。6502 每條指令由多個 bus cycle 組成，過程中可能讀 opcode、讀 operand、做 dummy read、寫回 memory。

## 初學者簡化模型

第一版可以先 instruction-level：

```text
fetch opcode
decode
execute whole instruction
return cycle count
PPU runs cycle count * 3
```

這樣容易寫，也能通過部分 CPU 測試。等需要更高精準度，再把每條指令拆成 cycle-by-cycle 狀態機。

建議先實作幾類代表指令：

- `LDA #imm`：立即值讀取與 N/Z flag。
- `STA abs`：寫記憶體。
- `ADC` / `SBC`：carry 與 overflow。
- `ASL` / `ROL`：read-modify-write。
- `BNE`：branch 與 page crossing。
- `BRK` / `RTI`：interrupt 流程。

## AprNes / NesCore 實作對照

AprNes 的 `CPU.cs` 是 per-cycle 模型。

重要欄位：

- `r_A`, `r_X`, `r_Y`, `r_SP`, `r_PC`：CPU registers。
- `flagN`, `flagV`, `flagD`, `flagI`, `flagZ`, `flagC`：拆開保存的 status flags。
- `opcode`：目前 opcode。
- `operationCycle`：目前指令進行到第幾個 cycle。
- `addressBus`：目前指令使用的地址。
- `dl`：data latch，中間資料暫存。
- `cpuIsRead`：目前 CPU bus cycle 是讀還是寫。

AprNes 的 opcode handler 不是回傳 cycle count，而是每次 CPU gate 只推進一個 cycle。`operationCycle` 決定該 cycle 要做哪件事。

例如 addressing mode helper：

- `GetImmediate()`。
- `GetAddressAbsolute()`。
- `GetAddressZeroPage()`。
- `GetAddressIndOffX()`。
- `GetAddressIndOffY()`。
- `GetAddressAbsOffX()`。
- `GetAddressAbsOffY()`。

指令完成時呼叫：

- `CompleteOperation()`：輪詢 interrupt，結束目前指令。
- `CompleteOperation_NoPoll()`：BRK 類特殊流程使用。

Opcode dispatch：

- `InitOpHandlers()` 建立 256-entry function pointer table。
- 每個 opcode 有對應 `Op_XX()` handler。
- AprNes 也實作許多 unofficial opcodes，讓測試與遊戲相容性更好。

## Interrupt 模型

AprNes 有：

- `NMILine`：PPU VBlank 相關的 NMI level。
- `IRQLine` / `irqLineCurrent`：IRQ line 的取樣與目前狀態。
- `doNMI`, `doIRQ`, `doReset`, `doBRK`：CPU 要處理的 interrupt 類型。

`PollInterrupts()` 在 instruction 邊界更新 NMI edge 與 IRQ 狀態。這比「PPU 一設 NMI 就立刻跳」更接近硬體。

## 常見錯誤

- 沒有處理 page crossing 額外 cycle。
- RMW 指令少做 dummy write 或 bus state。
- Branch timing 過度簡化。
- NMI 用 level 觸發，而不是 edge detection。
- Reset vector 讀取流程寫成直接設定 PC，忽略硬體 reset handler timing。

## 本章重點整理

1. CPU core 不只是 opcode 對函式表，還包含 bus cycle 與 interrupt 時序。
2. AprNes 用 `operationCycle` 把每條指令拆成 per-cycle state machine。
3. `CpuRead()` / `CpuWrite()` 是 CPU core 與整台硬體連接的入口。

## 下一章銜接

下一章會把 CPU 放回整台機器的時間線，介紹 master clock 如何同步 CPU、PPU、APU、DMA 與 Mapper。
