# BUGFIX51: SH* Unofficial Opcodes Fix (126→131/136 AC, +5)

**Date**: 2026-03-10 (commit 3a3d728)
**Impact**: AccuracyCoin 126→131 (+5), blargg 174/174 unchanged

## Problem

The SH* unofficial opcodes SHA ($93), SHX ($9E), SHY ($9C), SHS ($9B) should suppress
the H (high byte) AND masking when a DMA occurs before the write cycle.
The original implementation was missing this behavior, causing all 6 SH*-related tests
in P10 to fail.

## Fix

Detect the critical cycle of SH* opcodes at the entry of `ProcessPendingDma`:

```csharp
if ((opcode == 0x93 && operationCycle == 4) ||
    (opcode == 0x9B && operationCycle == 3) ||
    (opcode == 0x9C && operationCycle == 3) ||
    (opcode == 0x9E && operationCycle == 3) ||
    (opcode == 0x9F && operationCycle == 3))
{
    ignoreH = true;
}
```

When `ignoreH = true`, the SH* write uses `H = 0xFF` (equivalent to suppressing H masking).
Based on TriCNES's `IgnoreH` mechanism.

## AccuracyCoin Tests Fixed

- P10 all 6 SH* tests (SHA, SHX, SHY, SHS variants)

## Files Modified

- `AprNes/NesCore/CPU.cs` — ignoreH flag, SH* opcode write logic
- `AprNes/NesCore/MEM.cs` — ProcessPendingDma entry detection
