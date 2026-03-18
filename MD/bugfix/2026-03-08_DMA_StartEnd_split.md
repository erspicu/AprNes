# DMA Start/End Split + IRQ in EndCpuCycle

**Date**: 2026-03-08
**Baseline**: 174/174 blargg, 121/136 AccuracyCoin (no change)
**Type**: Structural refactor (no functional change)

## Changes

### 1. IRQ sampling moved to EndCpuCycle (MEM.cs)

Moved `irqLinePrev/irqLineCurrent` tracking from StartCpuCycle to EndCpuCycle.
This matches Mesen2's model where IRQ is sampled in the second half of each CPU cycle (phi2 phase).

```
Before: StartCpuCycle() { CC++, NMI, PPU, APU, IRQ }  EndCpuCycle() { empty }
After:  StartCpuCycle() { CC++, NMI, PPU, APU }       EndCpuCycle() { IRQ }
```

### 2. DMA cycles split from tick() to Start/End (MEM.cs)

Each DMA cycle in ProcessPendingDma now uses `StartCpuCycle() -> bus op -> EndCpuCycle()`
instead of `tick()`. This is structurally correct: DMA reads/writes should have proper
bus timing with IRQ sampling after each operation.

Affected: halt cycle, DMC read, OAM read, OAM write, dummy/alignment reads.

## Investigation Notes (DMA timing for AccuracyCoin)

### Root cause of P13 DMA+$2002 failure

The DMC Load DMA countdown fires during the current Mem_r's StartCpuCycle (APU step),
and ProcessPendingDma picks it up in the SAME Mem_r. In Mesen2, ProcessPendingDma runs
BEFORE StartCpuCycle in the NEXT MemoryRead, so the DMA fires 1 cycle later.

This means our DMA phantom read hits the wrong bus address (previous instruction's fetch
instead of the target register like $2002).

### Why reordering ProcessPendingDma fails

Moving ProcessPendingDma before StartCpuCycle (matching Mesen2) changes the getCycle
parity inside DMA, which shifts OAM DMA stolen cycles by +/-1. This breaks irq_and_dma
(blargg test 4-irq_and_dma expects exactly 513 stolen cycles for the first OAM DMA).

The parity difference: our halt cycle's StartCpuCycle sees CC+2 (Mem_r Start + halt Start)
while Mesen2 sees CC+1 (only halt Start, no Mem_r Start yet). This inverts the alignment
decision.

### Blocked items

All remaining 14 FAIL + 1 SKIP in AccuracyCoin share the same root cause:
DMA fires 1 cycle too early in our model. Fixing this requires either:
1. Reordering + compensating irq_and_dma (deep investigation needed)
2. Deferring DMA trigger by 1 cycle (needs careful design to not affect reload DMA)
3. Full master clock scheduler with sub-cycle DMA precision
