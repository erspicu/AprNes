# AprNes Architecture Evolution: From Initial Version to Cycle-Accurate

This document records the complete architectural evolution of the AprNes emulator from its initial version in 2016 to cycle-accurate precision in 2026,
including timing model changes at each stage, design decisions, and related NES hardware knowledge explanations.

---

## Table of Contents

1. [NES Hardware Timing Fundamentals](#1-nes-hardware-timing-fundamentals)
2. [Stage 1: Initial Version (2016)](#2-stage-1-initial-version-2016)
3. [Stage 2: PPU Cycle-Accurate Rewrite (2026-02-19)](#3-stage-2-ppu-cycle-accurate-rewrite-2026-02-19)
4. [Stage 3: VBL/NMI Timing Model (2026-02-21)](#4-stage-3-vblnmi-timing-model-2026-02-21)
5. [Stage 4: APU IRQ and CPU Interrupt Timing (2026-02-22)](#5-stage-4-apu-irq-and-cpu-interrupt-timing-2026-02-22)
6. [Stage 5: Per-Dot Sprite Accuracy (2026-02-22)](#6-stage-5-per-dot-sprite-accuracy-2026-02-22)
7. [Stage 6: DMC DMA Cycle Stealing (2026-02-22)](#7-stage-6-dmc-dma-cycle-stealing-2026-02-22)
8. [Stage 7: AccuracyCoin Challenge (2026-03-06~03-10)](#8-stage-7-accuracycoin-challenge-2026-0306-0310)
9. [Stage 8: Per-Cycle CPU Rewrite (2026-03-10)](#9-stage-8-per-cycle-cpu-rewrite-2026-03-10)
10. [Timing Model Overview](#10-timing-model-overview)
11. [Test Score Progress](#11-test-score-progress)

---

## 1. NES Hardware Timing Fundamentals

Understanding AprNes's evolution requires first understanding the basic timing architecture of the NES.

### 1.1 Master Clock and Subsystem Clocks

The core of the NES is a **21.477272 MHz** master clock, with each subsystem operating at different division ratios:

```
Master Clock: 21.477272 MHz
  ├── CPU (RP2A03): ÷12 = 1.789773 MHz  → 1 CPU cycle per 12 master clocks
  ├── PPU (RP2C02): ÷4  = 5.369318 MHz  → 1 PPU dot per 4 master clocks
  └── APU:          ÷24 = 894886.5 Hz   → 1 APU cycle per 2 CPU cycles
```

**Key ratio**: 1 CPU cycle = 3 PPU dots. This ratio is the cornerstone of emulator timing.

### 1.2 PPU Frame Structure

The PPU generates NTSC signal with the following scanline structure:

```
Scanline  0-239:  Visible picture (256 dots rendering + 85 dots HBlank = 341 dots/line)
Scanline  240:    Post-render (idle)
Scanline  241:    VBlank start (dot 1 sets VBL flag, can trigger NMI)
Scanline 242-260: VBlank period (CPU can freely access VRAM)
Scanline  261:    Pre-render (dot 2 clears VBL flag; prepares for next frame)
```

Each frame = 262 scanlines × 341 dots = **89,342 PPU dots** = **29,780.67 CPU cycles**.

### 1.3 CPU Instructions and Ticks

The 6502 CPU consumes 2–7 CPU cycles per instruction. Each cycle performs one memory access (read or write).
In the emulator, how PPU and APU are advanced between these accesses is the core problem of the "timing model."

### 1.4 Interrupt Mechanism

The NES has two types of interrupts:

- **NMI (Non-Maskable Interrupt)**: Issued by the PPU at the start of VBlank, notifying the CPU that the screen can be updated
- **IRQ (Interrupt Request)**: Issued by the APU frame counter or a mapper (such as MMC3), can be masked by the CPU

The precise trigger timing of interrupts is a key focus of many test ROMs.

---

## 2. Stage 1: Initial Version (2016)

**Git**: `e19d1b6` (2016-10-26)
**Architecture**: The simplest "per-frame" model

### 2.1 Initial Timing Model

```
┌─────────────────────────────────────┐
│          Initial Execution Model    │
│                                     │
│  while (running) {                  │
│      CPU.ExecuteOneInstruction();   │
│      PPU.CatchUp(cpu_cycles * 3);  │  ← PPU advanced after entire instruction
│      APU.Step();                    │
│  }                                  │
└─────────────────────────────────────┘
```

Characteristics:
- CPU executes one complete instruction at a time (2–7 cycles), then advances PPU in bulk
- PPU renders in bulk per scanline (not per dot)
- No interrupt timing simulation (NMI triggered directly at scanline 241)
- No DMC DMA simulation
- Supports Mapper 0/1/2/3/4

### 2.2 Limitations

This model is sufficient for most games, but cannot pass any timing-sensitive test ROMs.
Main problems:
- PPU register reads/writes cannot take effect at the correct dot position
- NMI/IRQ trigger timing is imprecise
- Mapper IRQ (e.g., MMC3 scanline counter) cannot count correctly

---

## 3. Stage 2: PPU Cycle-Accurate Rewrite (2026-02-19)

**Git**: `24687f0` ~ `be3f979`
**BUGFIX**: BUGFIX2 (Bug 10-14)
**Achievement**: Established per-dot PPU rendering pipeline

### 3.1 Motivation

In February 2026, the project shifted from "can run games" to "passing test ROMs."
The first step was to change the PPU from bulk rendering to per-dot rendering.

### 3.2 Tick-on-Access Model

Introduced AprNes's core timing mechanism — **advancing 3 PPU dots on every memory access**:

```
┌─────────────────────────────────────────────┐
│          Tick-on-Access Model               │
│                                             │
│  Mem_r(addr) {                              │
│      tick();              // advance 3 PPU dots │
│      return read(addr);   // perform read   │
│  }                                          │
│                                             │
│  tick() {                                   │
│      ppu_step_new();      // PPU dot 1      │
│      ppu_step_new();      // PPU dot 2      │
│      ppu_step_new();      // PPU dot 3      │
│      apu_step();          // APU half cycle │
│  }                                          │
└─────────────────────────────────────────────┘
```

Every cycle of each CPU instruction (Mem_r or Mem_w) calls `tick()`,
ensuring PPU advances precisely 3 dots between each CPU memory access.

### 3.3 PPU Rendering Pipeline

PPU tile fetching changed to an 8-cycle pipeline (8 dots per tile):

```
dot 0: Nametable byte fetch
dot 2: Attribute table byte fetch
dot 4: Pattern table low byte fetch
dot 6: Pattern table high byte fetch
dot 7: Load shift registers + render pixel
```

Introduced 16-bit shift registers (highshift/lowshift) and a 3-stage attribute pipeline,
replacing the original bulk rendering.

### 3.4 Explanation: Why Tick-on-Access?

In a real NES, the CPU, PPU, and APU operate in parallel simultaneously. But in a software emulator,
we have only one thread. Tick-on-Access is a "lazy synchronization" strategy:

> **Rather than proactively advancing PPU time, advance it opportunistically whenever the CPU accesses memory.**

Because the 6502 must have exactly one memory access (read or write) per cycle,
Mem_r/Mem_w is the most natural synchronization point.

Advantages: Simple to implement, high efficiency (no event scheduler needed).
Disadvantages: The observation granularity of the PPU is limited to "every 3 dots," unable to simulate sub-cycle behavior.

---

## 4. Stage 3: VBL/NMI Timing Model (2026-02-21)

**Git**: `7671455`
**BUGFIX**: BUGFIX5, BUGFIX9-11
**Achievement**: 154 PASS / 20 FAIL (+15)

### 4.1 1-Cycle NMI Delay Model

The NMI on real NES hardware is not triggered instantaneously. The PPU sets the VBL flag at scanline 241, dot 1,
but NMI is not detected by the CPU until **the next CPU cycle**.

```
┌──────────────────────────────────────────────────┐
│                NMI Timing Flow                   │
│                                                  │
│ PPU dot: ... → sl=241,cx=1 → ...                 │
│                    │                              │
│                    ▼                              │
│              Set VBL flag                         │
│              Set nmi_delay = true                 │
│                                                  │
│ At the start of the next tick():                 │
│              nmi_delay → nmi_pending              │
│              (promote: takes effect after 1 cycle delay) │
│                                                  │
│ CPU check: nmi_pending == true → trigger NMI     │
└──────────────────────────────────────────────────┘
```

### 4.2 $2002 Read Cancellation Mechanism

If the CPU reads $2002 in the same cycle the VBL flag was just set:
- Reads VBL flag = 1 (already set)
- **Clears nmi_delay** (NMI is cancelled, because it hasn't been promoted to nmi_pending yet)

But if nmi_delay has already been promoted to nmi_pending, it can no longer be cancelled.
This "1-cycle window" is the core test point of the `ppu_vbl_nmi` test suite.

### 4.3 Explanation: Why Is a Delay Needed?

On real hardware, the PPU and CPU run asynchronously. After the PPU pulls the NMI line low,
the CPU must wait until the next phi2 (CPU clock rising edge) to detect it. This is approximately 1 CPU cycle of delay.

Without implementing this delay, all 10 `ppu_vbl_nmi` tests fail.
After implementation, 15 tests pass at once.

---

## 5. Stage 4: APU IRQ and CPU Interrupt Timing (2026-02-22)

**Git**: `dd044d1` (APU IRQ), `1dd9024` (CPU interrupt)
**BUGFIX**: BUGFIX12-13 (APU), BUGFIX18 (CPU interrupt)
**Achievement**: 158 → 169 PASS

### 5.1 APU Frame Counter IRQ

The APU's frame counter generates an IRQ at step 4 in 4-step mode.
The key is the IRQ's "assert duration" and "clear timing":

```
Frame Counter 4-step Mode:
  Step 0: Envelope + Triangle linear counter
  Step 1: Envelope + Length counter + Sweep
  Step 2: Envelope + Triangle linear counter
  Step 3: Envelope + Length counter + Sweep + IRQ assert ← here!

IRQ assert lasts ~3 APU cycles (step 3 itself + 2 post cycles)
Writing $4017 or reading $4015 can clear the IRQ flag
```

### 5.2 CPU Interrupt Sampling: Penultimate Cycle

The 6502 CPU samples the IRQ/NMI line state on the **penultimate cycle** (second-to-last cycle) of each instruction.

```
┌──────────────────────────────────────────────┐
│  6502 Instruction Interrupt Sampling Timing  │
│                                              │
│  Assume instruction has N cycles:            │
│                                              │
│  Cycle 1: opcode fetch                       │
│  Cycle 2: operand fetch                      │
│  ...                                         │
│  Cycle N-1: ← IRQ/NMI sampled here ←        │
│  Cycle N: last cycle                         │
│                                              │
│  If IRQ line is low at Cycle N-1:            │
│  → Next instruction is replaced by IRQ handler│
└──────────────────────────────────────────────┘
```

**Implementation** (BUGFIX18):
- Record `irqLinePrev = irqLineCurrent` at the end of each `tick()`
- CPU checks `irqLinePrev` at the end of each instruction (naturally captures the state from the penultimate cycle)

### 5.3 NMI Deferral

If another NMI rising edge is detected while a BRK/IRQ/NMI handler is executing,
the NMI does not nest-trigger immediately, but is **deferred until after the next instruction**:

```
Normal:   NMI detected → immediately enter NMI handler
Deferred: BRK/IRQ in progress + NMI detected → set nmi_just_deferred
          → current handler completes → next instruction completes → NMI triggers
```

### 5.4 OAM DMA and IRQ Isolation

OAM DMA ($4014 write) steals 513–514 CPU cycles.
During DMA, changes in IRQ line state should not affect interrupt decisions after DMA completes:

```
Before DMA starts: save irqLinePrev
DMA executing: 513-514 tick() calls, irqLinePrev gets modified
After DMA ends: restore irqLinePrev (back to state before DMA)
```

---

## 6. Stage 5: Per-Dot Sprite Accuracy (2026-02-22)

**Git**: `5461fe7`
**BUGFIX**: BUGFIX17 (Sprite 0 Hit + Overflow)
**Achievement**: 165 PASS / 9 FAIL (+4)

### 6.1 Per-Pixel Sprite 0 Hit Detection

Sprite 0 Hit is an important PPU feature — when a non-transparent pixel of sprite 0 overlaps a non-transparent BG pixel,
$2002 bit 6 is set. Games use it for split-screen effects.

```
Before: bulk check entire scanline at dot 257 (imprecise)
After:  per-dot check in ppu_step_new() (dots 2-255)

bool CheckSprite0Hit(int dot) {
    if (dot < 2 || dot > 255) return false;
    if (sprite0_pixel[dot] != transparent && bg_pixel[dot] != transparent)
        return true;  // hit!
}
```

### 6.2 Sprite Overflow Hardware Bug

The NES's sprite overflow detection has a well-known hardware bug:
after 8 sprites are found, when evaluating the 9th sprite,
the byte offset `m` does not reset to zero but continues to increment (0→1→2→3).

```
Normal evaluation: compare OAM[n*4 + 0] (Y coordinate)
After bug triggers: compare OAM[n*4 + m], m increments each sprite
  sprite 9:  compare Y     (m=0, happens to be correct)
  sprite 10: compare tile  (m=1, wrong!)
  sprite 11: compare attr  (m=2, wrong!)
  sprite 12: compare X     (m=3, wrong!)
  sprite 13: compare Y     (m=0 wrap, correct again)
  ...
```

### 6.3 Follow-up: Secondary OAM FSM (BUGFIX47, 2026-03-08)

AccuracyCoin tests require more precise sprite evaluation:

```
dots  1-64:  clear secondary OAM (write $FF every 2 dots)
dots 65-256: per-dot evaluation of primary OAM
  odd dot:   read primary OAM[oamAddr] → oamCopyBuffer
  even dot:  write oamCopyBuffer → secondary OAM (if in-range)
dot  256:    finalize (set sprite 0 flag)
dot  257:    PrecomputePreRenderSprites() (prepare for next scanline)
```

$2004 reads during rendering return `oamCopyBuffer` (not primary OAM),
a detail that is a key test point in AccuracyCoin's SprSL0 and $2004 Stress Test.

---

## 7. Stage 6: DMC DMA Cycle Stealing (2026-02-22)

**Git**: `f3188b9`
**BUGFIX**: BUGFIX19
**Achievement**: 171 PASS / 3 FAIL (+2)

### 7.1 DMC DMA Mechanism

The APU's DMC channel needs to read sample data from memory.
This read is not performed by CPU instructions, but by a DMA unit that "steals" CPU cycles:

```
DMC sample fetch:
  Cycle 1: Halt (pause CPU)
  Cycle 2: Alignment (wait for correct read/write phase)
  Cycle 3: Dummy read (phantom read, using CPU's current bus address)
  Cycle 4: Sample read (read byte from DMC sample address)

3-4 cycles total, depending on whether the CPU is doing a read or write when DMA triggers
```

### 7.2 dmc_stolen_tick()

AprNes implements `dmc_stolen_tick()`, which is the same as `tick()` but bypasses the `in_tick` re-entrancy guard.
This is because DMC DMA occurs inside `tick()` (triggered by APU step),
requiring time to be advanced while already inside tick.

### 7.3 Phantom Read

The dummy cycle of DMC DMA produces a "ghost read" on the CPU's bus.
If the CPU's current bus address at that moment points to a PPU register (such as $2007),
this phantom read produces actual side effects!

```
Example: CPU is executing LDA $2007,X (page cross case)
  Cycle 3: Mem_r($2007) → PPU read buffer swap, vram_addr++
  DMC DMA triggers at this point:
  Stolen cycle 3: phantom read $2007 → another buffer swap!
  Cycle 4: Mem_r($2107) → mapped to $2007 → third read
```

This is the behavior verified by the `dmc_dma_during_read4` test suite.

---

## 8. Stage 7: AccuracyCoin Challenge (2026-03-06~03-10)

**Git**: `7c1a20b` ~ `5af6fdb`
**BUGFIX**: BUGFIX31-52
**Achievement**: 174/174 blargg + 132/136 AccuracyCoin

AccuracyCoin is an extremely strict emulator accuracy test, containing 136 sub-items,
covering various edge-case behaviors of CPU, PPU, and APU.

### 8.1 Load DMA Parity (BUGFIX31)

The delay when DMC DMA triggers depends on the APU's get/put cycle parity:

```
Before: fixed delay of 3 cycles
Fixed:  putCycle ? 2 : 3
        (delay 2 when APU cycle is odd, delay 3 when even)
```

This single fix brought blargg from 171 → 174, a full pass.

### 8.2 PPU Rendering Enable Delay (BUGFIX46)

The PPU's rendering enable ($2001 bit 3/4) does not take effect immediately;
it takes effect at the next PPU dot (1-dot delay):

```
ppuRenderingEnabled: updated at the end of each ppu_step_new()
tile fetch / shift register clocking: uses ppuRenderingEnabled (delayed value)
sprite 0 hit: uses the immediate ShowBackGround/ShowSprites (no delay)
```

### 8.3 $2002 Flag Clear Timing Stagger (BUGFIX45)

The timing at which the pre-render line (scanline 261) clears PPU flags is not identical:

```
dot 1: clear Sprite 0 Hit + Sprite Overflow
dot 2: clear VBlank flag
```

This 1-dot difference reflects the timing misalignment caused by the M2 duty cycle on real hardware.

### 8.4 NMI Delay Changed to Cycle-Based (BUGFIX35)

The original `nmi_delay` was a boolean; it was changed to a cycle count:

```
Before: nmi_delay = true → promote on next tick
After:  nmi_delay_cycle = cpuCycleCount → promote when cpuCycleCount > nmi_delay_cycle
```

More precisely simulates "exactly 1 CPU cycle" of delay.

---

## 9. Stage 8: Per-Cycle CPU Rewrite (2026-03-10)

**Git**: `533d1d4`
**BUGFIX**: BUGFIX50
**Achievement**: 174/174 blargg + 126/136 AC (+4)

### 9.1 Motivation

In the Tick-on-Access model, the CPU runs through a complete instruction at once,
and DMA can only be inserted at instruction boundaries. But on real hardware, DMA can be inserted at **any read cycle**.

```
Before (Per-Instruction):                  After (Per-Cycle):
┌─────────────────────┐                 ┌─────────────────────┐
│ Execute full instr   │                 │ cpu_step_one_cycle() │
│ Check DMA at end     │                 │   ├── CpuRead()     │
│ Execute full instr   │                 │   │   ├── tick()     │
│ ...                  │                 │   │   ├── CheckDMA() │ ← DMA insertable at every read
│                      │                 │   │   └── read data  │
│                      │                 │   └── operationCycle++│
│                      │                 │ cpu_step_one_cycle() │
│                      │                 │   ├── CpuWrite()    │
│                      │                 │   ...               │
└─────────────────────┘                 └─────────────────────┘
```

### 9.2 operationCycle State Machine

Each CPU instruction is broken down into multiple cycles, tracked with `operationCycle`:

```csharp
// Example: LDA abs,X (opcode 0xBD, 4-5 cycles)
case 0xBD:
    switch (operationCycle) {
        case 0: CpuRead(PC++); break;           // fetch opcode
        case 1: lo = CpuRead(PC++); break;      // fetch low byte
        case 2: hi = CpuRead(PC++); break;      // fetch high byte
        case 3:                                   // read from addr+X
            addr = (hi << 8) | lo;
            crossed = ((addr & 0xFF) + X) > 0xFF;
            CpuRead((addr & 0xFF00) | ((addr + X) & 0xFF)); // possible wrong page
            if (!crossed) { A = lastRead; SetNZ(A); done = true; }
            break;
        case 4:                                   // page cross: re-read correct addr
            A = CpuRead(addr + X);
            SetNZ(A);
            done = true;
            break;
    }
```

### 9.3 ProcessPendingDma

DMA checking now occurs inside every `CpuRead()` call:

```
CpuRead(addr):
    tick()                          // advance PPU/APU
    ProcessPendingDma()             // check for pending DMA
    return mem_read_fun[addr]()     // perform read
```

This allows DMC DMA to be inserted precisely at any read cycle **mid-instruction**,
greatly improving the pass rate of DMA-related tests.

### 9.4 Follow-up Optimizations (BUGFIX51-52)

- **SH\* Opcodes** (BUGFIX51): Unofficial opcodes (SHX/SHY/SHA) require special handling when DMA occurs
- **DMC Cooldown** (BUGFIX52): 2-cycle cooldown after DMC fetch completes, preventing immediate re-triggering

---

## 10. Timing Model Overview

### Architecture Evolution Comparison Table

| Aspect | Initial (2016) | Tick-on-Access (02-19) | VBL/NMI (02-21) | Per-Cycle (03-10) |
|--------|---------------|----------------------|----------------|------------------|
| **CPU Execution** | Per-instruction | Per-instruction | Per-instruction | Per-cycle |
| **PPU Advancement** | Bulk | Push 3 dots per Mem_r/Mem_w | Same | Same |
| **DMA Insertion** | None | Instruction boundary | Instruction boundary | Any read cycle |
| **NMI Timing** | Immediate | Immediate | 1-cycle delay | Cycle-based delay |
| **IRQ Sampling** | None | End of instruction | Penultimate cycle | Same |
| **Sprite 0 Hit** | Bulk | Bulk | Bulk | Per-dot |
| **Sprite Eval** | Bulk | Bulk | Bulk | Per-dot FSM |

### Tick Model Diagram

```
Current Model (Per-Cycle + Tick-on-Access):

CPU instruction: LDA $2007,X (page cross, 5 cycles)
                 ┌────────────────────────────────────────────────┐
Cycle 1 (read):  │ tick() → 3 PPU dots → CheckDMA → fetch opcode │
Cycle 2 (read):  │ tick() → 3 PPU dots → CheckDMA → fetch low    │
Cycle 3 (read):  │ tick() → 3 PPU dots → CheckDMA → fetch high   │
Cycle 4 (read):  │ tick() → 3 PPU dots → CheckDMA → dummy read   │
                 │                        ↑ DMC DMA can insert here! │
Cycle 5 (read):  │ tick() → 3 PPU dots → CheckDMA → real read    │
                 └────────────────────────────────────────────────┘
                  PPU dots: |•••|•••|•••|•••|•••| = 15 dots total
```

---

## 11. Test Score Progress

```
Date         blargg    AccuracyCoin    Key Fix
─────────────────────────────────────────────────────────────────
2016-10-26   ~20/124   —              Initial version, basic game running
2026-02-19   105/154   —              PPU cycle-accurate, APU audio
2026-02-20   130/154   —              APU init, DMC IRQ, test runner
2026-02-21   139/174   —              APU frame counter timing
2026-02-21   154/174   —              VBL/NMI 1-cycle delay model (+15!)
2026-02-22   156/174   —              MMC3 A12 phase alignment
2026-02-22   158/174   —              APU IRQ + CPU penultimate cycle
2026-02-22   165/174   —              Sprite per-pixel hit + overflow hw bug
2026-02-22   169/174   —              CPU interrupt timing + NMI deferral
2026-02-22   171/174   —              DMC DMA cycle stealing
2026-02-22   172/174   —              PPU $2007 read cooldown
2026-02-22   174/174   —              --pass-on-stable (ALL BLARGG PASS!)
2026-03-06   174/174   118/136        AccuracyCoin Phase 1
2026-03-07   174/174   120/136        PPU rendering enable delay
2026-03-08   174/174   122/136        Secondary OAM FSM
2026-03-10   174/174   126/136        Per-Cycle CPU rewrite
2026-03-10   174/174   131/136        SH* opcodes fix
2026-03-10   174/174   132/136        DMC DMA cooldown
2026-03-13   174/174   133/136        DMC Load DMA countdown timing (BUGFIX53)
2026-03-13   174/174   134/136        DMC DMA bus conflicts + deferred status (BUGFIX54)
2026-03-13   174/174   135/136        Explicit DMA abort (BUGFIX55)
2026-03-14   174/174   136/136        Implicit DMA abort (BUGFIX56) — PERFECT!
```

### All Complete

All 174 blargg tests + 136 AccuracyCoin tests pass.
The 4 failures in the P13 DMA tests were fixed one by one through BUGFIX53-56,
using TriCNES-style DMA timing (deferred status, bus conflicts,
explicit/implicit abort mechanisms), achieving this without changing the core tick model.

---

## Appendix: Related Document Index

| Document | Description |
|----------|-------------|
| `bugfix/` | Detailed records of all bug fixes (root cause analysis, changes made, verification results) |
| `MD/p13_fix_plan.md` | P13 DMA fix plan (Option A/B comparison) |
| `report/methodology.html` | Testing methodology document |
| `report/index.html` | Blargg test report (with screenshots) |
| `report/AccuracyCoin_report.html` | AccuracyCoin test report |
| `report/TriCNES_report.html` | TriCNES comparative test report |
| `CLAUDE.md` | Developer guide (compilation, testing, architecture description) |
