# Master Clock Scheduler Refactoring Cost Assessment

**Date**: 2026-03-07
**Baseline**: blargg 174/174 (100%), AccuracyCoin 119/136 (87.5%)
**Goal**: AccuracyCoin 136/136 (100%)

---

## Current State Summary

Currently uses the CPU cycle as the minimum time unit (CPU-Driven Tick Model). Each `Mem_r()`/`Mem_w()` call invokes `tick_pre()` to advance 3 PPU dots + 1 APU step. Cannot distinguish M2 rise/fall edge; DMA timing uses an approximate model.

**Remaining 16 FAIL + 1 SKIP distribution**:

| Category | Tests | Root Cause | Required Precision |
|----------|-------|------------|-------------------|
| P13 DMA timing | 6 | DMA halt/alignment cycle count | M2 phase + DMA state machine |
| P10 SH* bus conflict | 5 | DMA-to-instruction RDY interaction | Internal bus arbitration |
| P14 DMC/APUReg/Controller | 3 | Internal address bus, DMA timing | Sub-cycle bus state |
| P19 SprSL0/$2004Stress | 2 | Pre-render sprite eval, per-dot OAM | Per-dot PPU evaluation |
| P12 IFlagLatency | 1 | DMC DMA accumulated timing offset | Master Clock scheduler |
| P20 ImpliedDummy | 1 | P13 prerequisite chain failure | Same as P13 |
| **Total** | **17** | | |

---

## Codebase Size

| File | Lines | Description |
|------|-------|-------------|
| CPU.cs | 2,395 | Giant switch for 241 opcodes + interrupt handling |
| PPU.cs | 1,040 | Scanline rendering + VBL/NMI + sprite eval |
| APU.cs | 1,002 | 5 channels + frame counter + DMC DMA |
| MEM.cs | 345 | Tick/memory dispatch + DMA |
| Main.cs | 313 | ROM loader + main loop |
| IO.cs | 93 | $2000-$4017 register dispatch |
| JoyPad.cs | 58 | Gamepad strobe/read |
| Mapper/*.cs | 952 (13 files) | MMC3, UxROM, CNROM, etc. |
| **Total** | **6,198** | |

**Mesen2 comparison** (C++, equivalent scope):
- NesCpu.cpp: 621 lines + NesCpu.h: 858 lines (total 1,479)
- But Mesen2 CPU uses opcode table + addressing mode separation, not a giant switch

---

## Option Comparison

### Option A: Full Master Clock + CPU State Machine

**Scope of Changes**:

| File | Change Volume | Description |
|------|--------------|-------------|
| CPU.cs | **Rewrite ~2,000 lines** | 241 opcodes converted from inline to per-cycle state machine. Each opcode needs to be split into 2~8 cycle states. Estimated ~3,000 new lines. |
| MEM.cs | **Rewrite ~200 lines** | tick()/tick_pre()/tick_post() → master clock scheduler. Mem_r/Mem_w changed to catch-up + bus access. |
| APU.cs | **Rewrite ~300 lines** | dmcfillbuffer() completely rewritten as DMA state machine (halt/dummy/read states). OAM DMA merged into unified DMA scheduler. |
| PPU.cs | **Rewrite ~150 lines** | ppu_step_new() call timing changed to master clock scheduling (logic unchanged, but caller changes). Sprite eval needs a true per-dot FSM. |
| IO.cs | **Minor ~20 lines** | Register dispatch unchanged, but write timing needs alignment to M2 phase |
| JoyPad.cs | **Minor ~10 lines** | Strobe timing naturally accurate |
| Main.cs | **Rewrite ~50 lines** | Main loop changed to master clock driven |
| Mapper/*.cs | **No changes needed** | Interface unchanged (MapperR/W still called by memory dispatch) |

**Effort Estimate**:

| Phase | Description | Estimated Effort |
|-------|-------------|-----------------|
| 1. CPU State Machine | 241 opcodes × cycle decomposition → ~500 state transitions | **Very Large** — the single most time-consuming task |
| 2. Master Clock Core | MEM.cs scheduler + catch-up mechanism | **Medium** — structure is clear |
| 3. DMA State Machine | DMC DMA + OAM DMA unified scheduling | **Large** — the hardest part to debug |
| 4. PPU Per-Dot Eval | Sprite evaluation FSM (including pre-render) | **Medium** — logic partially exists |
| 5. Regression Testing | Full rerun of 174 blargg + 136 AC, debug one by one | **Very Large** — unpredictable |

**Regression Risk**: **Extremely High**. Rewriting the CPU giant switch is equivalent to re-validating all 174 blargg tests from scratch. Historical experience shows every timing change triggers cascading regressions.

**Expected Gain**: Theoretically reaches 136/136, but depends on debugging quality in practice.

---

### Option B: Mesen2-Style Catch-Up Architecture (Recommended Compromise)

**Core Idea**: Do not change the CPU giant switch; keep the `Mem_r()`/`Mem_w()` triggered timing model. But change the internals of `StartCpuCycle()` to precise master clock catch-up, plus M2 phase tracking.

This is exactly Mesen2's approach: `NesCpu.cpp:317`'s `StartCpuCycle()` simply advances the master clock and catch-ups the PPU; the CPU itself remains a synchronous opcode function.

**Scope of Changes**:

| File | Change Volume | Description |
|------|--------------|-------------|
| CPU.cs | **No changes needed** | Giant switch fully preserved! |
| MEM.cs | **Rewrite ~100 lines** | tick_pre() adds precise M2 phase calculation (different master clock offsets for read vs write cycles). Partially already implemented (masterClock, catchUpPPU/APU). |
| APU.cs | **Rewrite ~200 lines** | DMA rewritten to Mesen2's `ProcessPendingDma()` model: halt → dummy → read/write cycles, each cycle with precise StartCpuCycle/EndCpuCycle. |
| PPU.cs | **Rewrite ~100 lines** | Sprite evaluation changed to per-dot FSM (required for P19). $2004 read behavior refined. |
| IO.cs | **No changes needed** | |
| JoyPad.cs | **No changes needed** | |

**Effort Estimate**:

| Phase | Description | Estimated Effort |
|-------|-------------|-----------------|
| 1. M2 Phase Precision | tick_pre() masterClock offset distinguishes read/write | **Small** — catch-up architecture already exists |
| 2. DMA Rewrite | Translate ProcessPendingDma() to C# | **Large** — Mesen2 has ~150 lines of DMA logic |
| 3. PPU Sprite Eval | Per-dot FSM + pre-render line | **Medium** |
| 4. Regression Testing | Primarily affects DMA-related tests | **Medium** — CPU unchanged, smaller regression scope |

**Regression Risk**: **Medium**. CPU opcodes completely untouched, PPU rendering core untouched; risk concentrated in DMA and sprite eval.

**Expected Gain**: +12~15 items (130~134/136). P10 SH* may still require internal bus arbitration to pass.

---

### Option C: DMA State Machine Only (Minimal Changes)

Only rewrite the DMA portion; do not change tick_pre/tick_post structure.

| File | Change Volume |
|------|--------------|
| APU.cs | Rewrite dmcfillbuffer() + oamDmaExecute() (~150 lines) |
| MEM.cs | Minor changes (~30 lines), precise DMA stolen cycle count |

**Expected Gain**: +6~8 items (P13 DMA series); lowest risk but also lowest ceiling.

---

## Cost Comparison Table

| | Option A (Full Rewrite) | Option B (Catch-Up) | Option C (DMA Only) |
|---|---|---|---|
| **Lines Changed** | ~3,500+ lines | ~400 lines | ~180 lines |
| **Files** | 7 | 3 | 2 |
| **CPU.cs** | Rewrite | Unchanged | Unchanged |
| **Regression Risk** | Extremely High | Medium | Low |
| **Expected Gain** | 136/136 (theoretical) | 130~134/136 | 125~127/136 |
| **Hardest Part** | CPU state machine (241 opcodes) | DMA state machine | DMA state machine |

---

## Recommendation

**Recommended Option B (Mesen2-Style Catch-Up)**, rationale:

1. **CPU.cs completely untouched** — This is the biggest risk point (2,395-line giant switch); Option B avoids it entirely
2. **Existing catch-up foundation** — MEM.cs already implements masterClock + catchUpPPU/catchUpAPU; only needs precision improvement
3. **Best return on investment** — ~400 lines of changes for +11~15 AccuracyCoin items
4. **Incremental feasibility** — Can validate DMA first (Option C subset), then extend to sprite eval

The remaining items that cannot be resolved (5 P10 SH* items) require 6502 internal bus arbitration simulation, which is the deepest change in any option and can be treated as the "last 2%" problem.

---

## Recommended Execution Order (Option B)

```
Phase 1: DMA State Machine          → estimated +6~8 (P13 series)
Phase 2: M2 Read/Write Offset       → estimated +1~2 (P12 IFlagLatency, P20 ImplDummy)
Phase 3: PPU Per-Dot Sprite Eval    → estimated +2 (P19 SprSL0, $2004Stress)
Phase 4: APU Internal Bus           → estimated +1~2 (P14 APUReg, DMC)
Phase 5: SH* Bus Arbitration        → estimated +0~5 (P10, highest difficulty)
```
