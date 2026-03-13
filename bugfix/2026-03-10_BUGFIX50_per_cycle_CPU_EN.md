# BUGFIX50: Per-cycle CPU Rewrite (122→126/136 AC, +4)

**Date**: 2026-03-10 (commit 533d1d4)
**Impact**: AccuracyCoin 122→126 (+4), blargg 174/174 unchanged

## Problem

The old CPU model executed all cycles of an instruction at once, and DMA could only be
inserted at instruction boundaries. This caused imprecise DMA stolen cycle timing and
failures in multiple AccuracyCoin tests.

## Fix

Rewrote the CPU from "execute entire instruction at once" to a "step one cycle at a time" model:

- Added `cpu_step_one_cycle()` function, executing exactly one CPU cycle per call
- Each cycle has its own independent `StartCpuCycle → bus op → EndCpuCycle`
- DMA (`ProcessPendingDma`) can be inserted at any read cycle boundary
- Uses `operationCycle` state machine to track instruction execution progress
- `CpuRead`/`CpuWrite` replace the original `Mem_r`/`Mem_w`

## AccuracyCoin Tests Fixed

- P12 IFlagLatency: Test E no longer hangs (DMC DMA cycle drift eliminated)
- P20 Instruction Timing + Implied Dummy Reads
- P13 DMA + $4015 Read / DMC DMA + OAM DMA

## Files Modified

- `AprNes/NesCore/CPU.cs` — complete rewrite (per-cycle state machine)
- `AprNes/NesCore/MEM.cs` — StartCpuCycle/EndCpuCycle/ProcessPendingDma
- `AprNes/NesCore/Main.cs` — run() loop updated to use cpu_step()
