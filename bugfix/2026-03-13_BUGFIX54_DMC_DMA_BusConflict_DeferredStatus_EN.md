# BUGFIX54: DMC DMA Bus Conflict + Deferred $4015 Status

**Date**: 2026-03-13
**Baseline**: 174/174 blargg + 133/136 AC
**Result**: 174/174 blargg + 134/136 AC (+1)
**Fixed Test**: P13 "DMC DMA Bus Conflicts"

---

## Problem Description

AccuracyCoin P13 "DMC DMA Bus Conflicts" test was failing (err=2).
The test verifies that when the CPU address bus points to APU register space ($4000-$401F)
during a DMC DMA read, a bus conflict should occur: both the APU register and ROM respond
simultaneously, and a $4015 read must not affect the data bus.

## Root Cause Analysis

### 1. $4016/$4017 Bus Conflict Order Incorrect

The old implementation read the controller first, then ROM, then ANDed them together.
However, TriCNES's approach is: read ROM first to set the data bus, then the controller
read naturally uses cpubus as the open bus bits.

### 2. $4015 Bus Conflict Updates cpubus

TriCNES explicitly states "$4015 read can not affect the databus" (line 9084).
The old implementation stored the $4015 read result into cpubus, causing subsequent
DMA reads to see the wrong bus value.

### 3. Missing Deferred $4015 Status Update

TriCNES uses an `APU_DelayedDMC4015` countdown to delay the DMC enable/disable effect
from a $4015 write. The bit 4 of the $4015 read is also based on the delayed status
(`APU_Status_DelayedDMC`). The old AprNes implementation applied changes immediately
without using a deferred mechanism.

## Changes

### MEM.cs — ProcessDmaRead()

1. **$4016/$4017**: Changed to read ROM first → set cpubus → then read controller (controller naturally uses cpubus as open bus)
2. **$4015**: Added `dmaReadSkipBusUpdate` flag; reading $4015 does not update cpubus
3. **Caller**: `if (!dmaReadSkipBusUpdate) cpubus = val;`

### APU.cs — Deferred Status

1. **dmcStatusDelay / dmcDelayedEnable**: New fields replacing the old dmcDisableDelay
2. **apu_4015()**: On $4015 write, set dmcStatusDelay (getCycle?3:2); disable no longer takes effect immediately
3. **clockdmc()**: dmcStatusDelay decremented every cycle (including during DMA); applies enable/disable when reaching 0
4. **apu_r_4015()**: Bit 4 uses `dmcsamplesleft > 0 && dmcDelayedEnable`

## Verification Results

| Test | Result |
|------|--------|
| blargg 174 | 174/174 PASS (no regression) |
| AC P13 DMC DMA Bus Conflicts | PASS (was FAIL err=2) |
| AC Total | 134/136 (+1) |

## References

- TriCNES: `Emulator.cs` lines 9058-9113 (bus conflict handling in Fetch())
- TriCNES: `Emulator.cs` line 9084 ("$4015 read can not affect the databus")
- TriCNES: `Emulator.cs` lines 983-993 (APU_DelayedDMC4015 decrement every cycle)
- TriCNES: `Emulator.cs` lines 9375 (normal delay: PUT?3:4)
