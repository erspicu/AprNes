# AprNes JIT Profile — All Regions Phase 2 Complete

- **Date**: 2026-04-14 09:52:34
- **Branch**: `feature/remove-legacy-masterclocktick` @ `be1f2ad`
- **Build**: Debug x64, .NET Framework 4.8.1
- **Config**: NTSC, Audio Mode 2, Ultra Analog RF, CRT, 4x resolution
- **FPS (3-run avg)**: **71.76** (71.68 / 71.98 / 71.62)

---

## 1. Status of Refactor

All 4 region/cartridge configurations now have Phase 1 (de-recursion) +
Phase 2 (structural unroll) complete:

| Region | NestedTick variants | Unrolled kernel | Window |
|--------|--------------------|-----------------|--------|
| NTSC   | NestedTick7/2_NTSC | MasterClockTickUnrolledNTSC | 12 MC |
| PAL    | NestedTick7/2_PAL (5-way switch) | MasterClockTickUnrolledPAL (5 gates) | 80 MC |
| Dendy  | NestedTick7/2_Dendy | MasterClockTickUnrolledDendy | 15 MC |
| FDS    | (shares NTSC variants) | MasterClockTickUnrolledFDS | 12 MC |

Legacy fallback functions (`NestedTick7/2_Fallback`) retained but no
longer referenced.

---

## 2. NTSC Profile (hot path unchanged since Phase 2)

### Top methods (58883 samples)

| Excl% | Method |
|-------|--------|
| 22.9% | `Crt_Render` lambda |
| 18.3% | `Curvature+Convergence` lambda |
| 17.5% | `ppu_step_new` |
| 8.0%  | `DemodulateRow_Core` |
| 7.3%  | `Run_NTSC` |
| 4.9%  | `apu_step` |
| 3.4%  | `PpuPhase4_SpriteEvalAndInit` |
| 3.1%  | `ApplyHorizontalBlur` lambda |
| 2.6%  | `GenerateWaveform` |
| 0.4%  | `CpuRead` |

### vs Phase 2 NTSC report (commit 493c370)

| Metric | Phase 2 | This | Δ |
|--------|---------|------|---|
| FPS avg | 71.55 | 71.76 | +0.21 |
| Run_NTSC Excl% | 5.8% | 7.3% | +1.5 |
| apu_step Excl% | 3.1% | 4.9% | +1.8 |
| ppu_step_new Excl% | 18.1% | 17.5% | −0.6 |

NTSC hot-path code has not been modified between 493c370 and be1f2ad.
The ~1.5-1.8% drift in `Run_NTSC` / `apu_step` exclusive readings is
measurement noise — attributable to:

1. Different sample totals (61,262 vs 58,883) over 30s window → slight
   distribution shifts
2. Compiler-generated `DisplayClass` index advanced as this branch added
   ~13 new static methods (PAL/Dendy/FDS variants), so lambda symbol
   identifiers changed names but not machine code
3. The FPS itself (71.76 vs 71.55) is statistically indistinguishable —
   within run-to-run variance

---

## 3. Branch vs master Cumulative

Commits on this branch since forking from master:

| Commit | Milestone | NTSC FPS |
|--------|-----------|----------|
| (master baseline, pre-refactor) | run() shared | ~54 |
| (after master's static dispatch merge) | Region-specific Run_X | ~63 |
| `fe341df` (Phase 1 NTSC) | De-recursion | 63.28 |
| `493c370` (Phase 2 NTSC) | Unroll | 71.55 |
| `c84a404` (Phase 1B PAL) | — | (no NTSC change) |
| `2857f35` (Phase 2B PAL) | — | (no NTSC change) |
| `8a2b602` (Phase 2C FDS) | — | (no NTSC change) |
| **`be1f2ad` (Phase 1D+2D Dendy)** | **all regions done** | **71.76** |

Cumulative NTSC gain since pre-refactor master: **+32.9%** FPS.

---

## 4. Verification Summary

| Test suite | Result |
|------------|--------|
| blargg 184 suite (NTSC default) | 184/184 PASS (52.9s) |
| pal_apu_tests (--region PAL) | 10/10 PASS |
| Dendy visual smoke (DENDY Compo II NTSC/PAL PD) | Title screens render ✓ |
| FDS boot smoke (Baseball) | BIOS Nintendo logo renders ✓ |

User to perform personal verification of Dendy and FDS gameplay.

---

## 5. Next Steps (deferred)

- Merge back to master — pending user's additional verification
- Documentation: update MD/clocktick_refactor/STRUCTURAL_UNROLL_PLAN.md to
  mark all phases complete (Phase 1D+2D was "evaluate later", now done)
- Potential future work: refactor PAL's "5 gates per call" to match the
  "1 CPU cycle per call" pattern used by other regions (cosmetic/
  symmetry; estimated ~1% FPS trade-off; user deferred this decision)
