# 05 6502 CPU Core

## 這章要解決什麼問題

CPU core 是 emulator 最容易開始、也最容易低估的部分。表面上是解 opcode，實際上還要處理 addressing mode、flag、dummy read、read-modify-write、interrupt polling、DMA 插入點。

本章以 AprNes 的 `CPU.cs` 為準，介紹 6502 core 的基本結構。

> **如果你對「register 跟 RAM 差在哪」「stack 怎麼運作」「I 跟 N 旗標是什麼」這類觀念還不熟**，先看 [A1 計算機組織小複習](A1_computer_organization_primer.md) —— 那篇用廚房比喻把這些術語接地氣了。
>
> **遇到實作 opcode 時不確定某個 hex 該做什麼**，翻 [A2 6502 完整 256 Opcode 實作參考](A2_6502_opcode_reference.md) —— 含全部官方 + 非官方 opcode 的 cycle、bytes、flags、RMW 規則跟 page-cross penalty。

## NES 硬體觀念

NES CPU 是 Ricoh 2A03，核心接近 MOS 6502，但不支援正常 BCD decimal mode。

**生活比喻**：把 6502 想成一個只有兩隻手的主廚：
- **左手 (A 累加器)**：所有運算的工作手，加減/邏輯結果都進這裡。
- **右手 1 (X 索引)** / **右手 2 (Y 索引)**：拿來當「第幾個位置」的計數器，例如「`STA $1000,X` 表示寫到 `$1000+X` 那格」。
- **書籤 (PC program counter)**：食譜目前讀到第幾頁。
- **疊盤指標 (SP stack pointer)**：暫時放東西的盤子堆放在哪一層。
- **儀表板 (P 狀態旗標)**：7 個獨立小燈號，表示「上次運算結果是 0 嗎？」「進位了嗎？」「現在能不能接電話 (IRQ)」。

跟現代 CPU 比起來 6502 真的很簡陋 —— **沒有乘除法指令、沒有浮點、沒有 cache**。所有運算都是 8-bit + 8-bit。但它指令集小（official 56 條指令）、行為規律，是學 CPU 模擬最好的起點。

主要 register：

```text
A   accumulator       8-bit  ── 主要運算暫存器
X   index X           8-bit  ── 索引/計數
Y   index Y           8-bit  ── 索引/計數
SP  stack pointer     8-bit  ── stack 在 $0100-$01FF (256 byte)，
                                 SP 是 low byte，real addr = $100|SP
PC  program counter   16-bit ── 指向下一條要執行的指令
P   status flags      8-bit  ── 7 個獨立 flag
```

狀態旗標 P (從高位到低位 `N V - B D I Z C`)：

| Bit | 名稱 | 中文 | 何時設定 | 何時清除 |
|---|---|---|---|---|
| 7 | **N** | Negative | 結果 bit 7 = 1 | 結果 bit 7 = 0 |
| 6 | **V** | Overflow | 簽名運算溢位 (例: 127 + 1) | `CLV` 或正常運算 |
| 5 | **-** | (unused) | 永遠 1 (在 P 中)；push 時也是 1 | — |
| 4 | **B** | Break | `BRK`/`PHP` push 時 = 1；IRQ/NMI push 時 = 0 | (沒實體 bit；只在 push 出去的副本) |
| 3 | **D** | Decimal | `SED` | `CLD` |
| 2 | **I** | Interrupt Disable | `SEI` 或進入中斷 handler | `CLI` |
| 1 | **Z** | Zero | 結果 = 0 | 結果 ≠ 0 |
| 0 | **C** | Carry | 加法進位 / 減法不借位 / shift 從 bit 7 出來 | 反之 |

**NES 跟標準 6502 的差別**：D（decimal）旗標可以讀寫，**但 ADC/SBC 完全不走 BCD 路徑**。Ricoh 2A03 把 BCD 邏輯閘移除了。模擬器寫 ADC/SBC 時不需要做 BCD 模式判斷。

**指令分類** (大致)：

```
Load/Store     LDA LDX LDY STA STX STY      ── 進出 register
Transfer       TAX TAY TXA TYA TSX TXS      ── register 之間搬
Stack          PHA PHP PLA PLP              ── push/pull
Arithmetic     ADC SBC                      ── 加減 (含 carry)
Logical        AND ORA EOR                  ── 位元邏輯
Bit op         BIT                          ── 測試 bit
Compare        CMP CPX CPY                  ── 比較 (設旗標)
Inc/Dec        INC DEC INX DEX INY DEY      ── ±1
Shift/Rotate   ASL LSR ROL ROR              ── 移位
Branch         BCC BCS BEQ BNE BMI BPL...   ── 條件分支 (8 種)
Jump           JMP JSR RTS RTI              ── 無條件跳/呼叫/返回
Status         CLC SEC CLI SEI CLV CLD SED  ── 改 P 旗標
System         BRK NOP                      ── 中斷/不做事
```

完整 256 個 opcode（含 illegal）的詳細規則見 [A2 6502 完整 256 Opcode 實作參考](A2_6502_opcode_reference.md)。
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
