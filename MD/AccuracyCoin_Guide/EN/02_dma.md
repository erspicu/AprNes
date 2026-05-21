# Part 2: DMA (Page 13)

> Maps to: **P13 APU Registers and DMA Tests** — DMA + Open Bus / $2002 / $2007 Read / $2007 Write / $4015 Read / $4016 Read, DMC DMA Bus Conflicts, DMC DMA + OAM DMA, Explicit DMA Abort, Implicit DMA Abort.
> Prerequisites: [`00_timing_model.md`](00_timing_model.md) (GET/PUT, master-clock), [`01_cpu.md`](01_cpu.md) (open bus / data bus).

DMA is the single page in all of AC most dependent on "cycle alignment." The NES has two kinds of DMA, both of which **steal CPU cycles**, and the tests specifically check "which cycle it steals, what that cycle leaves on the bus, and how it wraps up when interrupted." Without a per-cycle/master-clock model, you can't pass a single test on this page.

---

## 1. The two kinds of DMA

| | OAM DMA | DMC DMA |
|---|---------|---------|
| Trigger | write `$4014` (page) | DMC sample timer fires (automatic during DPCM playback) |
| Action | copy a whole 256-byte page into OAM (via `$2004`) | fetch 1 sample byte to feed the DMC shifter |
| Cycles stolen | 513 or 514 (depends on alignment) | 1–4 (depends on alignment + whether it collides with OAM DMA) |
| Our implementation | `OamDmaGet`/`OamDmaPut` (`MEM.cs:201/210`) | `DmcDmaGet` (`MEM.cs:231`) |

Both can only steal when the **CPU is on a read cycle** (a write cycle can't be halted). That gate is in the main loop:

```csharp
// Main.cs MasterClockTickUnrolledNTSC — CPU gate
bool isDmcActive = dmcDmaRunning & (dmcStatusEnabled | dmcImplicitAbortActive);
if (cpuIsRead & (isDmcActive | spriteDmaTransfer)) DmaOneCycle();   // steal a cycle
else cpu_step_one_cycle();                                          // run the CPU normally
```

---

## 2. GET / PUT cycle parity (the alignment model)

On the bus, DMA alternates between **GET (read)** and **PUT (write)** cycles. Which cycle is GET and which is PUT depends on the **parity of the CPU cycle**. Get the alignment wrong and the whole DMA's cycle count and the addresses it hits are off.

We determine the GET/PUT phase from the parity of `cpuCycleCount` ([BUGFIX31/32](../../bugfix/2026-03-06_BUGFIX31.md), 171→174 blargg). OAM DMA may also need an **alignment cycle** (513→514) to align it to PUT.

> Key point: "how many cycles DMA steals" isn't a fixed value — it **depends on which parity it starts on**. The `if (OAMDMA_Aligned)` in `OamDmaPut` is handling exactly this — when unaligned, it first burns an alignment cycle via `DmaFetch(addressBus)`.

---

## 3. DMA + register reads (bus conflict) — the core of this page

When DMA steals a cycle it still does a bus access (GET is a read). If that read happens to hit a **register with side effects** (`$2002`/`$2007`/`$4015`/`$4016`), a "bus conflict" occurs — the register's side effects still happen, and the return value is synthesized per open-bus rules.

The tests check each one:
- **DMA + $2002 Read**: a DMA read of `$2002` clears the VBL flag and resets the address latch (side effects still happen).
- **DMA + $2007 Read**: advances the PPU address, updates the read buffer.
- **DMA + $4015 Read**: clears the frame interrupt flag — and **bit5 open bus comes from the external bus, not internal** (only a CPU read takes internal). This is the other half of the [dual data-bus](../../bugfix/2026-05-22_AC_InternalDataBus_DualDataBus.md) fix.
- **DMA + $4016 Read**: clocks the controller shift register.

We do this in `DmaFetch` (`MEM.cs:125`) — a DMA read doesn't bypass the register, it actually goes through the register logic; only, `$4015`'s bit5 takes `cpubus` (external) rather than `internalBus`:

```csharp
// DmaFetch: the bus-conflict path for a DMA read of $4015
if (reg == 0x15) {
    byte status = (byte)(val & 0x20);   // bit5 comes from the EXTERNAL bus (the DMA's own bus value)
    if (statusdmcint)   status |= 0x80;
    ...
    clearingFrameInterrupt = true;       // side effect: clears the frame IRQ flag as usual
    return status;
}
```

> ⚠️ This is exactly the source of the recent regression: we once changed this to `internalBus` too, and P14 APU Register Activation broke. **CPU read of $4015 → internal bus; DMA read of $4015 → external bus** — keep the two paths distinct.

---

## 4. DMC DMA cooldown

There's a minimum gap between two DMC DMAs; the cycle or two right after a DMA finishes can't immediately run another (the RDY line hasn't released yet in hardware). We use `dmcDmaCooldown` (TriCNES's `CannotRunDMCDMARightNow`, [BUGFIX52](../../bugfix/2026-03-10_BUGFIX52_DMC_DMA_cooldown.md), AC 131→132). `DmcDmaGet` sets `dmcDmaCooldown = 2` when it finishes.

---

## 5. Explicit DMA Abort (write $4015=$00)

**Test**: while a DMC DMA is in progress, write `$4015 = $00` (disable DMC), and the DMA should be "explicitly aborted." The hard part: the disable's **deferred status delay**, when it lands near a timer-fire boundary, must be extended — otherwise the timer fire (triggering a new DMA) and the disable (canceling the DMA) clash on the same cycle.

**Fix** ([BUGFIX55](../../bugfix/2026-03-13_BUGFIX55_Explicit_DMA_Abort.md), AC 134→135):
1. make the deferred status delay **parity-dependent**:
   ```csharp
   dmcStatusDelay = getCycle ? 4 : 3;   // avoid timer fire and deferred status clashing on the same cycle
   ```
2. make the explicit-abort detection cover a **2-cycle fire window** (`dmctimer == dmcrate` just fired, `dmctimer == 1` will fire next cycle), not just "just fired."

---

## 6. Implicit DMA Abort (write $4015=$10) — the phantom DMA

**Test**: when a **1-byte non-looping** DMC sample is about to end, writing `$4015 = $10` (enable) triggers a "phantom" **1-cycle DMA**; if that phantom DMA hits a write cycle it is **completely canceled**.

This is the most exotic behavior on the page. **Fix** ([BUGFIX56](../../bugfix/2026-03-14_BUGFIX56_Implicit_DMA_Abort.md), AC 135→**136 PERFECT** 🎉):

On the `$4015` write, detect that the timer is near firing and set `dmcImplicitAbortPending`; when the timer fires it becomes `dmcImplicitAbortActive`, triggering the 1-cycle phantom DMA:

```csharp
// detection conditions (corresponding to TriCNES timer==10/8; AprNes has a +3 position offset)
//   dmctimer == 8 && !getCycle   (TriCNES timer==10 && !PutCycle)
//   dmctimer == 9 &&  getCycle   (TriCNES timer==8  &&  PutCycle)
```

And the cancellation of the phantom DMA on hitting a write cycle is right there in the main loop's CPU gate:

```csharp
// Main.cs: phantom DMA meets a write cycle → cancel
if (dmcDmaRunning && dmcImplicitAbortActive) dmcImplicitAbortActive = false;
```

> **The timer-value mapping trap**: TriCNES's DMC timer decrements by **2** per GET cycle (always even), ours by **1** per cycle, and there's a **+3 position offset** for the pending→active transition. Copying TriCNES's constants verbatim will be wrong — you must first understand each side's timer cadence, then convert. This is the easiest place to crash when "porting a reference implementation": **align the semantics, not the numbers**.

---

## Summary

Whether you pass the DMA page comes down to three things:
1. **GET/PUT parity alignment** (how many cycles it steals, which address it hits).
2. **DMA reads actually go through register side effects** (bus conflict), with `$4015` bit5 from the external bus.
3. **Abort behavior** (explicit: disable delay is parity-dependent; implicit: phantom 1-cycle DMA canceled on a write cycle).

All three require "**DMA inserted at a precise parity/position in the CPU cycle sequence**." P13's last two items (explicit/implicit abort) were exactly the final two steps that clinched the **136/136 v1 perfect score** back then.

Next: [`03_apu.md`](03_apu.md) (APU: length counter, frame counter IRQ, DMC, register activation, controller).
