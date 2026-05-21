# The timing model: why sub-cycle is mandatory, and AprNes's master-clock architecture

> Maps to: the underlying premise of every page. This chapter is about the "foundation" — without a correct timing model, every PPU/APU/DMA fix afterward just stacks compensation on a wrong base.

---

## 1. Why frame/scanline level can't pass AC

Emulators come in tiers of precision:

| Precision tier | Advances per step | Runs commercial games? | Can pass AC? |
|----------------|-------------------|------------------------|--------------|
| frame level | a whole frame | mostly yes | ❌ |
| scanline level | one scanline | almost always | ❌ |
| **cycle / dot level** | 1 CPU cycle / 1 PPU dot | yes | mostly |
| **sub-cycle / master-clock level** | 1 master clock (1/12 of a CPU cycle) | yes | ✅ required |

Many AccuracyCoin tests check things like "within the *same* CPU cycle, at which PPU dot does some flag change" — e.g. the set/clear timing of the `$2002` VBL flag, the NMI edge, the exact dot of a sprite 0 hit. These **simply can't be expressed by a model that only settles at CPU-cycle boundaries**; you have to subdivide the CPU cycle further.

---

## 2. AprNes's evolution (three generations of timing model)

This is the condensed version of the [fix chronicle](00_fix_history.md), seen from the "model" angle:

1. **Gen 1: tick-on-access** (early). Every `Mem_r`/`Mem_w` calls `tick()`, advancing 3 PPU dots + 1 APU cycle. Simple, fast enough, no problem getting blargg to 174. But **DMA can only be inserted at instruction boundaries** → DMC stolen-cycle timing is off, and AC stalled at 122/136.
2. **Gen 2: per-cycle CPU** ([BUGFIX50](../../bugfix/2026-03-10_BUGFIX50_per_cycle_CPU.md)). The CPU becomes "step one cycle at a time" (`cpu_step_one_cycle()`), so DMA can be inserted at **any read-cycle boundary**. AC 122→136 perfect (v1).
3. **Gen 3: per-master-clock** (ported from TriCNES, current). Even the inside of a CPU cycle is sliced into 12 master clocks, and the PPU advances at several sub-points within the cycle. Only this can align the sub-cycle flag behaviors of `$2002`/`$2005`/`$2001`, pushing AC to v2 138 / 20260521's 139.

> **Core lesson**: every wall's root cause was "model granularity too coarse," not "some value computed wrong." Switching to the right model beats patching.

---

## 3. How the current master-clock model runs

NTSC clock relationships: **master clock 21.477 MHz**, CPU = master / 12, PPU = master / 4. So **1 CPU cycle = 12 master clocks = 3 PPU dots**.

The main loop `MasterClockTickUnrolledNTSC()` (`Main.cs:712`) unrolls those 12 master clocks flat, with events landing at precise MC positions:

```
MC 0 :  CPU gate (cpu_step_one_cycle or DmaOneCycle) + MapperObj.CpuCycle()
        apu_step() + APU put/get toggle
        ppu_step_new()        ← PPU dot #1 (full step)
MC 2 :  ppu_half_step_new()   ← half-step (sub-dot precision)
MC 4 :  NMI line sample + ppu_step_new()  ← PPU dot #2
MC 6 :  ppu_half_step_new()   ← half-step
MC 7 :  IRQ line sample + MapperObj.CpuClockRise()
MC 8 :  ppu_step_new()        ← PPU dot #3
MC 10:  ppu_half_step_new()   ← half-step
```

Key design points:
- **The CPU runs one cycle at MC 0**, but its effect on the bus and its position relative to the PPU are aligned precisely by the PPU (half-)steps that follow at MC 2/4/6/8/10.
- **NMI is sampled at MC 4, IRQ at MC 7** (`Main.cs:736, 745`) — interrupt behaviors like "penultimate-cycle sampling" are expressed exactly by these sub-cycle positions.
- **half-step** (MC 2/6/10) gives flags that need "half a dot" precision (like `$2002`'s VBL/sprite 0/overflow staggered clearing within a dot) a place to land.
- **DMA gate**: `if (cpuIsRead & (isDmcActive | spriteDmaTransfer)) DmaOneCycle();` (`Main.cs:717`) — DMA only steals a cycle when the CPU is on a read cycle, aligned precisely to GET/PUT.

> The CPU itself is `cpu_step_one_cycle()` (`CPU.cs:593`) stepping cycle by cycle; bus access goes through `CpuRead`/`CpuWrite` (`CPU.cs:77+`), no longer the old `Mem_r/tick`.

---

## 4. Two sub-models you must understand

### VBL/NMI 1-cycle delay
NMI doesn't fire the instant the flag changes; instead: **rising edge → set `nmi_delay` → next tick promotes to `nmi_pending` → CPU checks `nmi_pending`**. Reading `$2002` clears `nmi_delay` (cancellable) but not `nmi_pending` (irreversible). This 1-cycle delay model is the ticket to passing the whole NMI control/timing/suppression batch (it's what took blargg from 139→154 way back, see fix chronicle Phase 0).

### address bus vs data bus (and internal vs external)
NES bus behavior is the disaster zone of the later AC pages:
- **address bus**: the address the CPU currently drives (PC or access target).
- **data bus / open bus**: the last byte that was on the bus; when nothing drives it, that residual value is what you read (open bus).
- finer still: **internal data bus vs external data bus** — the open-bus source for `$4015` bit5 (a CPU read takes internal, a DMA read takes external); this is the subject of the latest [dual data-bus fix](../../bugfix/2026-05-22_AC_InternalDataBus_DualDataBus.md).

All of these require updating the right bus latch at the right timing point; too coarse a model and you can't tell them apart.

---

## 5. catch-up vs global tick (the trade-off)

We use **global tick** (the master clock drives all subsystems uniformly), not catch-up (each subsystem keeps its own time and catches up on demand).

- **global tick**: every master clock advances everything, so subsystems are always in sync → highest precision, easiest to reason about, but every tick touches all subsystems, costing performance.
- **catch-up**: the PPU/APU sit idle and only "catch up" to the current time when the CPU reads them → saves performance, but precise cross-subsystem interactions (DMA, bus conflicts, flag stagger) are very hard to get right.

A perfect AC score needs too many precise interactions; the mental overhead of catch-up would explode, so we chose global tick and clawed performance back with .NET 10's JIT/SIMD/PGO.

> For a deeper dive on timing tiers and the catch-up concept, see the existing long-form pieces in [`MD/techbook/`](../../techbook/) (NES Emulator Timing Models, Catch-Up Concept, AprNes Catch-Up and Structural Optimization).

---

Next, we go into the subsystems: [`01_cpu.md`](01_cpu.md) (CPU: open bus / dummy cycles / unofficial opcodes / interrupt timing).
