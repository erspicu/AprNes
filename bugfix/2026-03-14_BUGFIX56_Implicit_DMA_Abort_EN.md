# BUGFIX56: P13 Implicit DMA Abort (2026-03-14)

## Problem
AccuracyCoin P13 "Implicit DMA Abort" test FAIL (err=2).
The test verifies that when writing $4015=$10 (enable) just as a 1-byte non-looping DMC
sample is about to end, a "phantom" 1-cycle DMA is triggered. This DMA is completely
cancelled if it encounters a write cycle.

## Root Cause Analysis

### Implicit Abort Mechanism (TriCNES Reference)

TriCNES (line 9403) detects timer proximity to fire on $4015 write:
- `APU_ChannelTimer_DMC == 10 && !APU_PutCycle`
- `APU_ChannelTimer_DMC == 8 && APU_PutCycle`

Sets `APU_SetImplicitAbortDMC4015 = true`. On timer fire, transitions to
`APU_ImplicitAbortDMC4015 = true`, triggering a 1-cycle phantom DMA.

### Timer Value Mapping

TriCNES DMC timer decrements by 2 every GET cycle (values always even); AprNes decrements
by 1 every cycle. Testing confirmed a +3 position offset (pending→active transition delay,
bits counter fires 8 times).

Corresponding detection conditions in AprNes:
- `dmctimer == 8 && !getCycle` (corresponds to TriCNES timer==10 && !PutCycle)
- `dmctimer == 9 && getCycle` (corresponds to TriCNES timer==8 && PutCycle)

### 1-cycle Phantom DMA

TriCNES (lines 8758-8761) clears the implicit abort flag after each CPU cycle:
```
if (DoDMCDMA && APU_ImplicitAbortDMC4015)
    APU_ImplicitAbortDMC4015 = false;
```
The next cycle's DMA gate fails because the flag is already cleared, resulting in a
phantom DMA lasting only 1 cycle (halt only).

### Write Cycle Cancellation

TriCNES (line 3974) DMA gate requires `CPU_Read` (GET cycle):
```
DoDMCDMA && (APU_Status_DMC || APU_ImplicitAbortDMC4015) && CPU_Read
```
If the CPU is executing a write cycle (e.g., STA), the implicit abort DMA simply does not execute.

## Fix

### Change 1: APU.cs — Implicit abort detection condition

```csharp
// Detect timer proximity to fire on $4015 write
if ((dmctimer == 8 && !getCycle) || (dmctimer == 9 && getCycle))
{
    dmcImplicitAbortPending = true;
}
```

### Change 2: APU.cs — pending→active transition on timer fire

In clockdmc when buffer empties and bits counter reaches zero:
```csharp
if (dmcImplicitAbortPending)
{
    dmcImplicitAbortActive = true;
    dmcImplicitAbortPending = false;
}
```

### Change 3: MEM.cs — Post-halt 1-cycle phantom DMA cancellation

In ProcessPendingDma after the halt cycle executes:
```csharp
if (dmcImplicitAbortActive)
{
    dmcImplicitAbortActive = false;
    if (dmcDmaRunning && dmcsamplesleft == 0)
    {
        dmcDmaRunning = false;
        dmcNeedDummyRead = false;
        if (!spriteDmaTransfer) return;
    }
}
```
`dmcsamplesleft == 0` ensures cancellation only for pure implicit abort DMA, leaving
normal refill DMA unaffected.

### Change 4: CPU.cs — Write cycle cancels implicit abort DMA

In CpuWrite, if implicit abort is active and DMA is waiting for halt:
```csharp
if (dmcImplicitAbortActive && dmaNeedHalt)
{
    dmcImplicitAbortActive = false;
    dmcDmaRunning = false;
    dmcNeedDummyRead = false;
    dmaNeedHalt = false;
}
```

## Test Results
- Implicit DMA Abort pre-1990 answer key fully matched
- blargg: 174/174 PASS (no regression)
- AccuracyCoin: 135→136/136 (+1, PERFECT)

## Baseline
- **174 PASS / 0 FAIL / 174 TOTAL**
- **AccuracyCoin: 136/136 PASS** (all tests passing)
