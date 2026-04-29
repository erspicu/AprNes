# 05 6502 CPU Core

## What This Chapter Solves

The CPU core is both the easiest place to start an emulator and the easiest to underestimate. On the surface it's "decode opcode, execute"; in practice it requires addressing modes, flags, dummy reads, read-modify-write semantics, interrupt polling, and DMA insertion points.

This chapter introduces the 6502 core's structure based on AprNes's `CPU.cs`.

> **If you're not yet comfortable with concepts like "register vs RAM," "how the stack works," or "what the I and N flags do,"** start with [A1 Computer Organization Primer](A1_computer_organization_primer.md) — that one grounds these terms with kitchen analogies.
>
> **When implementing opcodes and unsure what a particular hex value should do**, see [A2 6502 Complete 256-Opcode Implementation Reference](A2_6502_opcode_reference.md) — every official + unofficial opcode with cycle counts, byte counts, flag effects, RMW rules, and page-cross penalties.

## NES Hardware Concepts

The NES CPU is the Ricoh 2A03 — close to the MOS 6502, except it has no working BCD decimal mode.

**Everyday analogy**: think of the 6502 as a chef with only two hands:
- **Left hand (A accumulator)**: the working hand for all arithmetic; addition / subtraction / logical results all land here.
- **Right hand 1 (X index)** / **right hand 2 (Y index)**: counters, used for "n-th position" indexing — e.g., `STA $1000,X` stores to address `$1000+X`.
- **Bookmark (PC program counter)**: which page of the recipe is being read.
- **Plate-stacker pointer (SP stack pointer)**: which level of the temporary stack is currently active.
- **Dashboard (P status flags)**: 7 independent indicator lights — "was the last result zero?", "did we carry?", "is the phone (IRQ) currently muted?".

Compared to modern CPUs, the 6502 is genuinely primitive — **no multiply or divide instructions, no floating point, no cache**. Every operation is 8-bit + 8-bit. But its instruction set is small (56 official instructions) and well-behaved, making it the best starting point for learning CPU emulation.

Main registers:

```text
A   accumulator       8-bit  ── primary arithmetic register
X   index X           8-bit  ── indexing / counter
Y   index Y           8-bit  ── indexing / counter
SP  stack pointer     8-bit  ── stack lives in $0100-$01FF (256 bytes);
                                 SP is the low byte; real addr = $100|SP
PC  program counter   16-bit ── points at next instruction to execute
P   status flags      8-bit  ── 7 independent flags
```

Status register P (high to low: `N V - B D I Z C`):

| Bit | Name | Meaning | Set when | Cleared when |
|---|---|---|---|---|
| 7 | **N** | Negative | result bit 7 = 1 | result bit 7 = 0 |
| 6 | **V** | Overflow | signed-arithmetic overflow (e.g. 127 + 1) | `CLV` or normal arithmetic |
| 5 | **-** | (unused) | always 1 (in P); also 1 when pushed | — |
| 4 | **B** | Break | 1 when pushed by `BRK`/`PHP`; 0 when pushed by IRQ/NMI | (no physical bit; only in pushed copy) |
| 3 | **D** | Decimal | `SED` | `CLD` |
| 2 | **I** | Interrupt Disable | `SEI` or entering an interrupt handler | `CLI` |
| 1 | **Z** | Zero | result == 0 | result != 0 |
| 0 | **C** | Carry | add carry / subtract no-borrow / shift bit-7-out | otherwise |

**Difference between NES and stock 6502**: the D (decimal) flag is readable/writable but **ADC/SBC do not go through the BCD path at all**. Ricoh removed the BCD logic gates from the 2A03. The emulator's ADC/SBC implementations don't need a BCD branch.

**Instruction categories** (rough):

```
Load/Store     LDA LDX LDY STA STX STY      ── register in/out
Transfer       TAX TAY TXA TYA TSX TXS      ── move between registers
Stack          PHA PHP PLA PLP              ── push/pull
Arithmetic     ADC SBC                      ── add/subtract with carry
Logical        AND ORA EOR                  ── bitwise logic
Bit op         BIT                          ── test bits
Compare        CMP CPX CPY                  ── set flags by comparison
Inc/Dec        INC DEC INX DEX INY DEY      ── ±1
Shift/Rotate   ASL LSR ROL ROR              ── shift / rotate
Branch         BCC BCS BEQ BNE BMI BPL...   ── conditional branches (8 of them)
Jump           JMP JSR RTS RTI              ── unconditional jump / call / return
Status         CLC SEC CLI SEI CLV CLD SED  ── modify P flags
System         BRK NOP                      ── interrupt / no-op
```

For all 256 opcodes (including illegal) and exact rules, see [A2 6502 Complete 256-Opcode Implementation Reference](A2_6502_opcode_reference.md).

CPU instructions don't execute as a single function call. Each 6502 instruction is composed of multiple bus cycles: opcode fetch, operand fetch, dummy reads, memory writes, etc.

## Beginner-Friendly Simplification

A first version can use instruction-level dispatch:

```text
fetch opcode
decode
execute the whole instruction
return cycle count
PPU advances cycle count * 3
```

This is simpler to write and passes a portion of the CPU tests. When you need higher fidelity, split each instruction into a cycle-by-cycle state machine.

Recommended initial implementation order:

- `LDA #imm`: immediate load and N/Z flag handling.
- `STA abs`: memory write.
- `ADC` / `SBC`: carry and overflow.
- `ASL` / `ROL`: read-modify-write.
- `BNE`: branch with page-crossing penalty.
- `BRK` / `RTI`: interrupt flow.

## AprNes / NesCore Implementation Mapping

AprNes's `CPU.cs` is a per-cycle model.

Important fields:

- `r_A`, `r_X`, `r_Y`, `r_SP`, `r_PC`: CPU registers.
- `flagN`, `flagV`, `flagD`, `flagI`, `flagZ`, `flagC`: status flags stored separately.
- `opcode`: current opcode.
- `operationCycle`: which cycle of the current instruction is in progress.
- `addressBus`: address used by the current instruction.
- `dl`: data latch — temporary intermediate value.
- `cpuIsRead`: whether the current CPU bus cycle is a read or write.

AprNes opcode handlers don't return a cycle count. Instead, each CPU gate advances one cycle, and `operationCycle` decides what that cycle does.

Examples of addressing-mode helpers:

- `GetImmediate()`.
- `GetAddressAbsolute()`.
- `GetAddressZeroPage()`.
- `GetAddressIndOffX()`.
- `GetAddressIndOffY()`.
- `GetAddressAbsOffX()`.
- `GetAddressAbsOffY()`.

When an instruction completes:

- `CompleteOperation()`: poll interrupts, end the current instruction.
- `CompleteOperation_NoPoll()`: special path used by BRK and friends.

Opcode dispatch:

- `InitOpHandlers()` builds a 256-entry function-pointer table.
- Each opcode has a corresponding `Op_XX()` handler.
- AprNes also implements many unofficial opcodes for better test/game compatibility.

## Interrupt Model

AprNes carries:

- `NMILine`: PPU-VBlank-related NMI level.
- `IRQLine` / `irqLineCurrent`: IRQ line state and its sampled value.
- `doNMI`, `doIRQ`, `doReset`, `doBRK`: which interrupts the CPU is processing.

`PollInterrupts()` updates NMI edge detection and IRQ state at instruction boundaries. This is closer to hardware than "PPU sets NMI; CPU jumps immediately."

## Common Mistakes

- Missing the page-crossing extra cycle.
- Skipping the dummy write or bus state in RMW instructions.
- Oversimplifying branch timing.
- Treating NMI as level-triggered rather than edge-detected.
- Setting PC directly for reset, ignoring the timing of the hardware reset handler.

## Chapter Recap

1. The CPU core is more than an opcode → function table — it includes bus cycles and interrupt timing.
2. AprNes uses `operationCycle` to turn each instruction into a per-cycle state machine.
3. `CpuRead()` / `CpuWrite()` are how the CPU core connects to the rest of the hardware.

## Bridge to the Next Chapter

The next chapter places the CPU back on the system timeline, covering how the master clock synchronises CPU, PPU, APU, DMA, and the mapper.
