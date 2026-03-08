# BUGFIX49: DMC Enable Delay Always Set

**Date**: 2026-03-08
**Baseline**: 174/174 blargg, 121/136 AccuracyCoin
**After**: 174/174 blargg, 122/136 AccuracyCoin (+1)
**Fixed**: P14 Delta Modulation Channel (err=21 → PASS)

## Root Cause

When `$4015` re-enables the DMC channel (with `dmcsamplesleft == 0`), our code only set
`dmcLoadDmaCountdown` if the buffer was already empty (`dmcBufferEmpty == true`).

Mesen2's `SetEnabled(true)` ALWAYS sets `_transferStartDelay` regardless of buffer state.

This matters when the DMC timer is about to fire shortly after the enable write:

1. `$4015` write enables DMC → `restartdmc()` sets `dmcsamplesleft`
2. Buffer NOT empty yet (shift register still consuming last byte)
3. **Old code**: No countdown set → buffer empties on next cycle → DMA fires immediately (too early)
4. **Fixed**: Countdown set → DMA delayed until countdown expires (correct timing)

AccuracyCoin Tests M and N specifically test this scenario: `$4015` write 1 or 0 cycles
before the DMC timer fires. The DMA should be delayed by 2-3 cycles after the buffer empties,
not fire immediately.

## Change

**APU.cs** `apu_4015()` — removed `if (dmcBufferEmpty)` condition:

```csharp
// Before:
if (dmcsamplesleft == 0)
{
    restartdmc();
    if (dmcBufferEmpty)
    {
        dmcLoadDmaCountdown = (apucycle & 1) != 0 ? 2 : 3;
    }
}

// After:
if (dmcsamplesleft == 0)
{
    restartdmc();
    // Always set countdown regardless of buffer state (Mesen2: _transferStartDelay)
    dmcLoadDmaCountdown = (apucycle & 1) != 0 ? 2 : 3;
}
```

The existing `dmcLoadDmaCountdown > 0` early-return in `apu_step_dmc()` (line 601-607)
already prevents the reload DMA path from triggering while the countdown is active, so
no other changes are needed.

## Verification

- blargg 174/174 (no regression)
- AccuracyCoin 122/136 (+1: P14 DMC PASS)
- All other P14 tests remain PASS (Controller Strobing, Frame Counter, APU Registers, etc.)
