# AprNes JIT Profile — Phase 2 Structural Unroll (+13% FPS)

- **Date**: 2026-04-14 08:32:24
- **Branch**: `feature/remove-legacy-masterclocktick` (pending commit)
- **Build**: Debug x64, .NET Framework 4.8.1
- **Config**: NTSC, Audio Mode 2, Ultra Analog RF, CRT, 4x resolution
- **FPS (3-run avg)**: **71.55** (71.60 / 71.72 / 71.33)

---

## 1. Major Milestone

**Phase 2 of STRUCTURAL_UNROLL_PLAN shipped successfully.** The outer
`MasterClockTickUnrolledNTSC` processes 12 master clocks per call via
a 3-way switch on post-cpu_step counter state. Gains dramatically exceed
the conservative +2-5% estimate.

| Build | Avg FPS | Run_NTSC Excl% | Notes |
|-------|---------|----------------|-------|
| b505da4 (mcTickFn routing, master) | 63.28 | 9.0% | gated form, PPU handlers via mcTickFn |
| fe341df (Phase 1: NestedTickN) | 63.28 | 9.0% | de-recursion, neutral perf |
| **this commit (Phase 2: Unroll)** | **71.55** | **5.8%** | **+13.1% FPS, −3.2% main loop** |

---

## 2. Top Methods (61262 samples)

| Excl% | Method |
|-------|--------|
| 24.1% | `Crt_Render` lambda |
| 18.8% | `Curvature+Convergence` lambda |
| 18.1% | `ppu_step_new` |
| 8.3%  | `DemodulateRow_Core` |
| **5.8%** | **`Run_NTSC`** (was 9.0%) |
| 3.6%  | `PpuPhase4_SpriteEvalAndInit` |
| 3.2%  | `ApplyHorizontalBlur` lambda |
| 3.1%  | `apu_step` |
| 2.6%  | `GenerateWaveform` |
| 0.4%  | `CpuRead` |
| 0.3%  | `NestedTick7_NTSC` (new) |

`Run_NTSC` inclusive also dropped (main loop cost now smaller → CRT
pipeline gets a larger relative slice, e.g. Crt_Render 22% → 24%).

---

## 3. Why the Gain Is So Large

Predicted gain was +2-5% from eliminating gate checks + decrements.
Actual was +13%. Explanations:

1. **Direct saves (predicted)**:
   - ~70 gate checks per 12-MC window × ~30K batches/sec = 2.1M ops/sec
   - ~22 decrements per 12-MC × 30K = 660K ops/sec
   - ~11 function call boundaries eliminated (12 inline calls → 1)
   - Total: ~3M ops/sec × ~2 cycles = **~6M cycles/sec ≈ 0.2% CPU**

2. **Secondary effects (underestimated)**:
   - **JIT emits much tighter code**: no more per-tick branch spread.
     The 3-way switch + linear event sequence lets JIT fold constants
     and eliminate redundant loads.
   - **Branch predictor efficiency**: 3-way `state` switch is learned
     extremely well (most CPU instructions don't touch PPU regs →
     state==12 overwhelmingly dominant).
   - **I-cache locality**: the unrolled body stays hot in L1 as one
     block; gated form's 12 separate call invocations spread branch
     targets.
   - **cpu_step_one_cycle call once per 12 MC** (not 12 times through
     the same gate machinery), so its code stays in L1 during the
     outer event sequence.

---

## 4. Verification

Per user direction — critical timing/interrupt ROMs first, then full suite.

**Critical suites (33/33 PASS)**:
- `vbl_nmi_timing` 7/7
- `sprite_hit_tests_2005.10.05` 11/11
- `cpu_interrupts_v2` 5/5
- `ppu_vbl_nmi` 10/10

**Full blargg**: **184/184 PASS** (test time dropped from 61s → 52s,
consistent with overall FPS gain).

---

## 5. 3-Way Switch Dispatch Breakdown

After `cpu_step_one_cycle()` returns, `mcCpuClock` reveals what happened:

| state | Meaning | Frequency (est.) |
|-------|---------|------------------|
| 12 | No PPU register access during this CPU cycle | ~99% |
| 10 | `$2000` write (NestedTick2 fired) | ~0.1% |
| 5 | `$2002/$2007/$2004` read or `$2007` write (NestedTick7 fired) | ~1% |

Hot path (state==12) is the dominant case — JIT and CPU branch
predictor both heavily optimize this. The other two branches are
correct but rarely executed.

---

## 6. Cumulative Trajectory

| Phase | Commit | FPS | Main Loop Excl% |
|-------|--------|-----|-----------------|
| Pre-static-dispatch | 83adc81 | ~54 | run() 10.7% |
| Static dispatch merged | 2780287 | ~56 | Run_NTSC 9.7% |
| + FDS palCache fix | b60c023 | ~62 | 9.7% |
| + masterClockTotal removed | 7533ebd | 63.23 | 9.1% |
| + mcTickFn routing | bdb62ca | 63.28 | 9.0% |
| + NestedTickN (Phase 1) | fe341df | 63.28 | 9.0% |
| **+ Structural unroll (Phase 2)** | **this** | **71.55** | **5.8%** |

Cumulative gain since pre-refactor: **+17 FPS (+31.4%)**.

---

## 7. What's Next

- **Phase 1B**: extend NestedTickN variants to PAL/Dendy (current fallback
  still uses mcTickFn loop for non-NTSC — works correctly but doesn't
  unlock Phase 2 there).
- **Phase 2B**: port unroll to Run_PAL (LCM 80 MC → more complex), Run_Dendy
  (LCM 15), Run_FDS (same as NTSC but fds_CpuCycle).
- Phase 3 (removal of gated Inline variants) deferred until all regions
  have unrolled paths.

Debug FPS ceiling now ~71 FPS. Release build likely much higher (JIT
tier-1 optimizations kick in).
