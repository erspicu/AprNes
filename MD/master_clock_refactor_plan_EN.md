# Master Clock Refactoring Plan

**Purpose**: Upgrade from CPU-driven tick to an M2 phase-aware architecture, resolving the remaining 17 FAILs in AccuracyCoin
**Date**: 2026-03-07
**Baseline**: blargg 174/174, AccuracyCoin 118/136 (87%)
**Status**: **Not adopted** — ultimately achieved 136/136 via Per-cycle CPU + DMA timing fixes (BUGFIX50-56) without requiring Master Clock refactoring

---

## Existing Infrastructure

MEM.cs already has a Master Clock foundation:
- `masterClock` / `cpuCycleCount` / `ppuClock` / `apuClock` counters
- `catchUpPPU()` / `catchUpAPU()` catch-up loops
- `tick_pre()` / `tick_post()` separation (`tick_post` is an empty placeholder)
- `MASTER_PER_CPU = 12`, `MASTER_PER_PPU = 4` constants

CPU.cs is 2395 lines (not the previously estimated 5000), so refactoring risk is lower than expected.

---

## Refactoring Phases

### Phase 1: M2 Phase Tracking (Low risk, high reward)

**Goal**: Track the M2 rise/fall phase within each CPU cycle so that DMA uses true GET/PUT determination

**Changes**:

1. **MEM.cs** — Add `m2Phase` field
   ```csharp
   enum M2Phase { Rise, Fall }  // Rise = GET (read), Fall = PUT (write)
   static M2Phase m2Phase = M2Phase.Rise;
   ```

2. **MEM.cs** — `Mem_r()` sets `m2Phase = M2Phase.Rise` (GET cycle)
3. **MEM.cs** — `Mem_w()` sets `m2Phase = M2Phase.Fall` (PUT cycle)
4. **APU.cs** — `dmcfillbuffer()` uses `m2Phase` instead of `cpuBusIsWrite` / `cpuCycleCount & 1`
   - Load DMA: `m2Phase == M2Phase.Fall ? 3 : 2`
   - Reload DMA: `m2Phase == M2Phase.Fall ? 2 : 3`

**Expected reward**: Infrastructure preparation, no behavioral change
**Regression risk**: None (Load DMA already uses the same parity; Reload DMA/OAM DMA retain original proxies)
**Verification**: blargg 174/174 ✓

**Actual test results** (2026-03-07):
- ✅ Completed: Added `m2PhaseIsWrite` field, used by Load DMA
- ⚠️ Reload DMA switched to m2PhaseIsWrite → 5 test regressions (dma_2007_read/write, dma_4016_read, sprdma×2)
- ⚠️ OAM DMA alignment switched to m2PhaseIsWrite → regression (same as above + cpu_interrupts)
- Conclusion: The existing proxies for Reload DMA and OAM DMA (`cpuBusIsWrite` / `apucycle` parity) cannot be simply replaced

---

### Phase 2: tick_pre/tick_post PPU Dot Split (Medium risk)

**Goal**: Split 3 PPU dots from tick_pre into 2+1 to simulate M2 rise behavior at master clock 3

**Changes**:

1. **MEM.cs** — `tick_pre()` advances master clock only to the M2 rise position (8 master clocks = 2 PPU dots)
   ```csharp
   static void tick_pre()
   {
       masterClock += 8;  // M2 rise at master clock 3 → 2 PPU dots
       cpuCycleCount++;
       if (nmi_delay) { nmi_pending = true; nmi_delay = false; }
       catchUpPPU();  // runs 2 PPU dots
       catchUpAPU();
       // IRQ tracking...
   }
   ```

2. **MEM.cs** — `tick_post()` advances the remaining 4 master clocks (1 PPU dot)
   ```csharp
   static void tick_post()
   {
       masterClock += 4;  // remaining 1 PPU dot
       catchUpPPU();  // runs 1 PPU dot
   }
   ```

3. **PPU.cs** — Verify whether `$2002` read timing needs adjustment (VBL/sprite flag clear timing may be affected)

**Expected reward**: Precise alignment of PPU register read/write with M2 phase; fixes sub-dot timing issues
**Regression risk**: Medium (relative order of PPU dot and bus access changes; may affect VBL/NMI timing)
**Verification**: Full blargg ppu_vbl_nmi suite + AccuracyCoin P17/P18

**Actual test results** (2026-03-07):
- ❌ 2+1 split (tick_pre: MC+=8 runs 2 dots, tick_post: MC+=4 runs 1 dot) → **10 regressions**
  - ppu_vbl_nmi: 05-nmi_timing, 06-suppression, 08-nmi_off_timing
  - vbl_nmi_timing: 5-nmi_suppression, 6-nmi_disable, 7-nmi_timing
  - cpu_interrupts_v2: cpu_interrupts, 2-nmi_and_brk
  - sprite_overflow_tests: 5.Emulator
- Root cause: CPU sees PPU state 1 dot early; NMI edge detection timing shifted
- **Requires PPU event recalibration to adopt this split; not a simple change**

---

### Phase 3: Precise DMA Scheduling (Medium risk, highly targeted)

**Goal**: Replace DMA halt/alignment with precise M2 phase determination, eliminating parity proxies

**Changes**:

1. **APU.cs** — Rewrite `dmcfillbuffer()` halt/alignment logic
   - Halt: wait until the next GET cycle (`m2Phase == M2Phase.Rise`)
   - Alignment: if halt occurs in a PUT cycle, wait one extra cycle to align
   - Phantom reads: triggered every halt cycle, but only in GET phase

2. **PPU.cs** — `oamDmaExecute()` uses m2Phase to determine alignment
   - Replaces `cpuBusIsWrite` read/write cycle determination

3. **MEM.cs** — `tick()` (used by DMA) also sets m2Phase

**Expected reward**: +3~+6 (P10 SH* bus conflict, part of P14)
**Regression risk**: Medium (DMA cycle count changes may affect currently passing tests)
**Verification**: Full blargg dmc_dma suite + sprdma_and_dmc_dma + AccuracyCoin P10/P13/P14

---

### Phase 4: PPU Per-Dot Precision (Low risk, independent)

**Goal**: Change OAM evaluation to execute dot-by-dot instead of in batches

**Changes**:

1. **PPU.cs** — Change sprite evaluation from scanline-batch to a dot-by-dot state machine
   - Dot 1: Clear secondary OAM
   - Dot 65-256: Evaluate OAM entries one by one (one entry per 2 dots)
   - Dot 257-320: Sprite fetch

**Expected reward**: +3 (P19 BG serial / sprites SL0 / $2004 stress)
**Regression risk**: Low (sprite evaluation is an independent subsystem)
**Verification**: Full blargg sprite suite + AccuracyCoin P19

---

### Phase 5: Full Master Clock Implementation (High risk, final goal)

**Goal**: If Phase 1-4 still has unresolvable timing issues, convert the CPU to a pausable state machine

**Changes**:

1. **CPU.cs** — Convert giant switch to a microcode-driven state machine
2. **MEM.cs** — Convert main loop to master clock-driven
3. All subsystems scheduled independently

**Expected reward**: Theoretically 136/136
**Regression risk**: Extremely high (complete rewrite)
**Decision point**: Evaluate whether this is necessary after Phase 1-4 is complete

---

## Execution Order and Milestones

| Phase | Files Changed | Estimated Effort | Milestone |
|------|----------|---------|--------|
| Phase 1 | MEM.cs, APU.cs | Small | blargg 174 + AC P13 improvement |
| Phase 2 | MEM.cs | Small~Medium | blargg 174 + no VBL timing regression |
| Phase 3 | APU.cs, PPU.cs, MEM.cs | Medium | blargg 174 + AC P10/P14 improvement |
| Phase 4 | PPU.cs | Medium | blargg 174 + AC P19 improvement |
| Phase 5 | CPU.cs, MEM.cs, all | Large | Decide after evaluation |

**Strategy**: Run a full regression test after each Phase is complete; only proceed to the next phase after confirming no regressions. Phase 1 is the safest with the clearest reward; start there.

---

## Risk Management

1. **Each Phase is an independent commit** — can be reverted if problems arise
2. **blargg 174/174 is a hard baseline** — fix any regression immediately
3. **Phase 5 is optional** — only consider it if Phase 1-4 is insufficient
4. **AccuracyCoin page-level testing** — use `run_ac_test.sh` to quickly verify specific pages
