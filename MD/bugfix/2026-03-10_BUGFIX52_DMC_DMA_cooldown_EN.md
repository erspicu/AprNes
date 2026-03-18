# BUGFIX52: DMC DMA Cooldown (131→132/136 AC, +1)

**Date**: 2026-03-10 (commit 5af6fdb)
**Impact**: AccuracyCoin 131→132 (+1), blargg 174/174 unchanged

## Problem

After a DMC DMA completes, the next DMC DMA was allowed to trigger immediately.
However, real hardware has a cooldown period that prevents back-to-back DMA transfers.
Without this mechanism, the stolen cycle count in the P13 DMC DMA + OAM DMA test was incorrect.

## Fix

Based on TriCNES's `CannotRunDMCDMARightNow` mechanism:

1. After DMC fetch completes in `ProcessPendingDma`, set `dmcDmaCooldown = 2`
2. Decrement cooldown every APU cycle in `clockdmc()`
3. Add `dmcDmaCooldown != 2` check to the DMA trigger condition

```csharp
// ProcessPendingDma: after DMC fetch completes
dmcDmaCooldown = 2;

// clockdmc(): decrement
if (dmcDmaCooldown > 0) dmcDmaCooldown--;

// DMA trigger condition
if (dmcBufferEmpty && dmcsamplesleft > 0)
{
    if (dmcDmaCooldown != 2) // block if just finished DMA
        dmcStartTransfer();
}
```

## AccuracyCoin Tests Fixed

- P13 DMC DMA + OAM DMA: stolen cycle count now correct

## Files Modified

- `AprNes/NesCore/APU.cs` — dmcDmaCooldown field, clockdmc() decrement logic
- `AprNes/NesCore/MEM.cs` — ProcessPendingDma sets cooldown
