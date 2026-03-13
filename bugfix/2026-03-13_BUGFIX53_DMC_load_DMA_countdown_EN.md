# BUGFIX53: DMC Load DMA Countdown Timing (TriCNES-style)

**Date**: 2026-03-13
**Baseline**: 174/174 blargg + 132/136 AC
**Result**: 174/174 blargg + 133/136 AC (+1)
**Fixed Test**: P13 "DMA + $2002 Read"

---

## Problem Description

AccuracyCoin P13 "DMA + $2002 Read" test was failing (err=2).
The test triggers a DMC DMA during VBL and expects the DMA halt cycle to read $2002,
clearing the VBL flag. However, AprNes's DMA was firing 1 cycle too early, causing the
halt cycle to read a ROM address instead of $2002.

## Root Cause Analysis

### APU/CPU Execution Order Difference: TriCNES vs AprNes

| | TriCNES | AprNes |
|--|---------|--------|
| APU/CPU order | CPU first, APU after (same tick) | APU first (StartCpuCycle), CPU after |
| DMCDMADelay | Fixed 2, decremented only on PUT cycle | 2 or 3 (parity-dependent), decremented every cycle |
| APU sets flag | Flag set by APU this tick, CPU sees it next tick | Flag set by APU this tick, CPU sees it immediately in same tick |

### Impact of Decrement Timing

TriCNES decrements DMCDMADelay on PUT cycles. Because APU runs after CPU, the flag takes
effect with a 1-tick delay:
- Write on GET cycle → actual delay 4 cycles
- Write on PUT cycle → actual delay 3 cycles

In AprNes, APU runs before CPU (StartCpuCycle), so flags take effect in the same tick.
If PUT-cycle decrement is used directly, the result is inverted (GET→3, PUT→4).

### Solution: Invert Decrement Parity

By using **GET cycle decrement** (instead of PUT) in AprNes, the APU/CPU order difference
is cancelled out:
- Write on GET cycle → next tick is PUT (no decrement) → tick after that is GET (decrement) → ... → 4 cycles ✓
- Write on PUT cycle → next tick is GET (decrement) → ... → 3 cycles ✓

## Changes

### APU.cs

1. **apu_4015()**: dmcLoadDmaCountdown changed to fixed 2 (TriCNES: `DMCDMADelay = 2`)
   ```csharp
   // Old: dmcLoadDmaCountdown = (apucycle & 1) != 0 ? 2 : 3;
   dmcLoadDmaCountdown = 2;
   ```

2. **apu_step()**: dmcLoadDmaCountdown decremented only on GET cycles
   ```csharp
   if (dmcLoadDmaCountdown > 0)
   {
       bool getCycle = (cpuCycleCount & 1) == 0;
       if (getCycle)
       {
           --dmcLoadDmaCountdown;
           if (dmcLoadDmaCountdown == 0 && dmcBufferEmpty && dmcsamplesleft > 0)
               dmcStartTransfer();
       }
       return;
   }
   ```

## Verification Results

| Test | Result |
|------|--------|
| blargg 174 | 174/174 PASS (no regression) |
| AC P13 DMA + $2002 Read | PASS (was FAIL err=2) |
| AC Total | 133/136 (+1) |

## References

- TriCNES: `Emulator.cs` line 9384 (`DMCDMADelay = 2`)
- TriCNES: `Emulator.cs` lines 971-980 (DMCDMADelay decrement in PUT branch)
- TriCNES: `Emulator.cs` lines 652-715 (CPU→APU execution order)
