# Part 1: CPU behavior (Page 1 / 2–11 / 12 / 20)

> Maps to: **P1 CPU Behavior**, **P2–P11 Unofficial Opcodes**, **P12 CPU Interrupts**, **P20 CPU Behavior 2**.
> Skeleton: ① what the test checks → ② real hardware behavior → ③ the pitfalls we hit → ④ how to fix it (with code) → ⑤ commit/file:line.
> Prerequisite: read [`00_timing_model.md`](00_timing_model.md) first (per-cycle / master-clock is the premise for all of this).

CPU pages are mostly "logic puzzles" — they don't need the PPU's dot precision, but they **demand precise within-cycle bus and interrupt sampling timing**. Pass the CPU pages and your CPU core is basically solid.

---

## 1. Open Bus (the data-bus model) — the first concept to nail

**Tests (P1 Open Bus, codes 1–9)**:
- `1: reading open bus should not be all zeros`
- `2: LDA Absolute reading open bus should return the operand high byte`
- `6: the upper 3 bits when reading a controller should be open bus`
- `9: $4015 bit5 should be open bus`…

**Hardware behavior**: the NES data bus is "**the last value someone drove onto it**." When nothing drives it (reading an unmapped address, or an unimplemented register bit), what you read is that latch's residual value — that's open bus.

**Our implementation**: a single `cpubus` latch (`PPU.cs:1032`), updated on every CPU read/write; reading an open-bus region (e.g. `$4020–$5FFF`, the upper 3 bits of `$4016/$4017`) returns `cpubus`.

```csharp
// JoyPad.cs: reading a controller — low bit from the shift register, upper 3 bits are open bus
return (byte)((P1_ShiftRegister >> 7) | (cpubus & 0xE0));
```

**Pitfalls we hit** ([BUGFIX29](../../bugfix/2026-03-04_BUGFIX29.md)):
- `$4020–$5FFF` was originally routed to the mapper's `ExpansionROM` (returns 0) → open bus code 1 FAIL. Changed to return `cpubus` across the whole range.
- ZP read/write didn't update `cpubus` initially → code 4 FAIL. Added it.

> **Advanced**: open bus actually has "**internal vs external**" forms. The source of `$4015` bit5 differs from ordinary open bus — a CPU read of `$4015` takes internal, a DMA fetch takes external. This is the latest pitfall, written up separately: [dual data-bus](../../bugfix/2026-05-22_AC_InternalDataBus_DualDataBus.md). Open bus is the hidden thread of the CPU pages, running from the first page to the last.

---

## 2. Dummy read / write cycles

In certain addressing modes the 6502 does a "**redundant bus access**" — it's not a harmless no-op, because that access **updates the data bus** and may hit an address with side effects (like `$2002`, `$4015`).

**Tests (P1 Dummy read / Dummy write, P20 Branch/Implied Dummy Reads)** focus on:
- the dummy read on indexed addressing crossing a page boundary (one read at the wrong high address).
- read-modify-write instructions writing to `$2006` twice (first the old value, then the new value).
- the extra dummy read on a taken branch.

**Pitfall we hit** (BUGFIX29): the dummy read on a taken branch was originally just an empty `tick()` with no actual memory read → the data bus wasn't updated → P20 Branch Dummy Reads FAIL 4/5. The fix was to replace the empty tick with a **real read**:
- taken, no page cross: dummy read from `PC+2`.
- page cross: dummy read from the "wrong-page address" `(dest_hi mangled into the PC high byte) | dest_lo`.

> Under the current per-cycle model, these dummy accesses are just a `CpuRead` in the instruction's cycle sequence and naturally update `cpubus` — no special "should we update the bus?" handling, because **every bus cycle is a real access**. That's the dividend of switching to the per-cycle model (see [timing model](00_timing_model.md) §2).

---

## 3. Decimal flag / B flag (easy to pass, but understand the quirk)

- **Decimal flag (P1)**: the NES's 2A03 strips out the 6502's BCD, so `ADC`/`SBC` are **unaffected by the D flag**. But the `D` flag itself still exists and is still pushed onto the stack by `PHP`/`BRK`. So in code: ADC/SBC ignore D entirely, but PHP/BRK push the P register (including the D bit) as usual.
- **B flag (P1)**: the 6502 has no real "B register" — bits 4/5 are decided only **when pushing to the stack**, by source:
  - P pushed by `PHP` / `BRK`: bit 4 = 1, bit 5 = 1.
  - P pushed by `IRQ` / `NMI`: bit 4 = 0, bit 5 = 1.
  The test checks all 9 combinations (codes 1–9). Implementation: when pushing P to the stack, decide bit 4 by "is this PHP/BRK or an interrupt."

These two aren't hard, but they teach one thing: **some bits of the P register are "synthesized at push time," not actually stored in a register.**

---

## 4. Unofficial opcodes (P2–P11)

The whole batch of unofficial opcodes must be implemented correctly — most (`NOP` in various addressing modes, `LAX`/`SAX`/`DCP`/`ISC`/`SLO`/`RLA`/`SRE`/`RRA`) are just combinations of official instructions; match the cycle count and dummy reads and you're done.

**The truly hard ones are the SH\* family** (`SHA $93/$9F`, `SHX $9E`, `SHY $9C`, `SHS $9B`).

**Hardware behavior**: SH* writes the value `register & (address high byte + 1)`. But there's an infamous quirk: **when DMA/an interrupt cuts in before the write cycle, that `& (H+1)` high-byte masking is "canceled"** (becomes `& 0xFF`).

**How to fix it** ([BUGFIX51](../../bugfix/2026-03-10_BUGFIX51_SH_opcodes.md), commit `3a3d728`, AC 126→131 +5): detect whether SH* was interrupted by DMA at the critical cycle and set an `ignoreH` flag; when true, the write uses `H = 0xFF`:

```csharp
// At the DMA insertion point, detect SH*'s critical cycle (cf. TriCNES IgnoreH)
if ((opcode == 0x93 && operationCycle == 4) ||
    (opcode == 0x9B && operationCycle == 3) ||
    (opcode == 0x9C && operationCycle == 3) ||
    (opcode == 0x9E && operationCycle == 3) ||
    (opcode == 0x9F && operationCycle == 3))
{
    ignoreH = true;   // SH* write uses H = 0xFF, removing the high-byte masking
}
```

> This one is a litmus test for the "per-cycle model": you have to know **exactly which cycle of the instruction** DMA was inserted at to decide ignoreH. An instruction-level model simply can't express it — another reason the model switch was unavoidable. `ignoreH` is cleared on hard reset (`Main.cs:285`).

---

## 5. Interrupt timing (P12) — penultimate-cycle sampling + no NMI polling during the interrupt sequence

This is the most delicate part of the CPU pages. Three key hardware facts:

1. **IRQ/NMI are sampled on the instruction's "penultimate cycle"** (penultimate-cycle polling). That is, the line state *before* the instruction's last cycle is what counts.
2. **The interrupt sequence (the 7 cycles of BRK/IRQ/NMI) itself does no NMI polling** — NMI can only fire on the handler's first instruction. Otherwise you get "NMI cutting in before the IRQ handler's first instruction (SEC)."
3. **NMI is edge-triggered + 1-cycle delay** (see [timing model](00_timing_model.md) §4); IRQ is level-triggered.

**Pitfall we hit** ([BUGFIX18](../../bugfix/2026-02-22_BUGFIX18.md), 165→169): early on, `irqLineAtFetch` sampled after the opcode fetch — correct for 2-cycle instructions, but too early for a 3-cycle JMP and OAM DMA (500+ cycles) → IRQ fired on the wrong instruction.

**Current approach (master-clock model)**: the line state is sampled at precise master-clock positions, naturally covering all instruction lengths — **NMI sampled at MC 4, IRQ at MC 7** (`Main.cs`):

```csharp
// MasterClockTickUnrolledNTSC: one CPU cycle = 12 master clocks
mcCpuClock = 8; mcPpuClock = 0;
NMILine |= NMIable && isVblank;                       // ← MC 4: NMI sample
if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
...
mcCpuClock = 5;
IRQLine = irqLineCurrent;                              // ← MC 7: IRQ sample
if (statusframeint && !apuintflag) irqLineCurrent = true;
```

P12 also has **NMI Overlap BRK / IRQ** (the preemption behavior when another interrupt arrives during the interrupt sequence, a.k.a. interrupt hijacking) and **Interrupt flag latency** (`SEI`/`CLI`/`PLP` changing the I flag take effect one cycle late). These all hinge on "cycle-precise line sampling + no NMI polling inside the interrupt sequence."

---

## 6. P20 CPU Behavior 2 (synthesis)

P20 is a synthesis check of everything above: Instruction Timing, Implied Dummy Reads, Branch Dummy Reads (§2), JSR Edge Cases, **Internal Data Bus** (the advanced form of §1, [dual data-bus](../../bugfix/2026-05-22_AC_InternalDataBus_DualDataBus.md)). Pass P20 and the CPU core has basically graduated.

---

## Summary

The CPU pages really come down to two hidden threads:
1. **The data bus** (open bus / dummy accesses updating the bus / internal vs external) — running from P1 to P20.
2. **Within-cycle sampling timing** (penultimate IRQ, NMI 1-cycle delay, no polling during the interrupt sequence, ignoreH depending on which cycle DMA hit).

Both require "**being able to locate yourself to a cycle / master clock inside an instruction**." So the real bar for the CPU pages isn't any single opcode — it's whether the [timing model](00_timing_model.md) is right.

Next: [`02_dma.md`](02_dma.md) (DMA: OAM/DMC timing, bus conflicts, abort).
