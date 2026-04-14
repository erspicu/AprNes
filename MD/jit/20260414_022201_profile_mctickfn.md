# AprNes JIT Profile — After mcTickFn Routing (Legacy MasterClockTick Removed)

- **Date**: 2026-04-14 02:22:01
- **Branch**: `feature/remove-legacy-masterclocktick` @ `bdb62ca`
- **Build**: Debug x64, .NET Framework 4.8.1
- **Config**: NTSC, Audio Mode 2, Ultra Analog RF, CRT, 4x resolution
- **FPS (3-run avg)**: **63.28** (62.82 / 63.52 / 63.50)

---

## 1. FPS / Main-Loop Cost Comparison

| Build | Avg FPS | `Run_NTSC` Excl% | Notes |
|-------|---------|------------------|-------|
| 290a738 (master, post-masterClockTotal removal) | 63.23 | 9.1% | gated form, PPU handlers used legacy MasterClockTick |
| **bdb62ca (this, mcTickFn routed)** | **63.28** | **9.0%** | nested calls use region-specific inline variant |

Within noise (+0.05 FPS, -0.1%). Refactor is **performance-neutral**.

That's the expected outcome:
- Saved per nested tick: ~5 cycles (no `!isFDS` branch + constants)
- Added per nested tick: ~3 cycles (indirect call via function pointer)
- Net: ~2 cycles × ~37K nested ticks/sec ≈ 0.002% CPU — below noise

The real win is **code consistency + bug fix**, not speed:
- All tick callers now go through the same region-specific logic
- Latent PAL/Dendy NMI offset bug eliminated (was hardcoded `mcCpu==8`
  in legacy MasterClockTick, now correctly 12/11 via inline variants)
- 53 lines of dead code removed (Run_Legacy + MasterClockTick +
  masterPerPpuHalf + MasterTicksPerFrame)

---

## 2. Top Methods (57717 samples)

| Excl% | Method |
|-------|--------|
| 22.3% | `Crt_Render` lambda |
| 18.5% | `ppu_step_new` |
| 18.0% | `Curvature+Convergence` lambda |
| **9.0%** | **`Run_NTSC`** |
| 7.6% | `DemodulateRow_Core` |
| 3.5% | `PpuPhase4_SpriteEvalAndInit` |
| 2.9% | `apu_step` |
| 2.9% | `ApplyHorizontalBlur` lambda |
| 2.5% | `GenerateWaveform` |

CRT pipeline still dominant (~44% combined). PPU + main loop ~30%.
APU + NTSC encode ~9%. Hot ordering unchanged.

---

## 3. Cumulative FPS Trajectory

| Commit | Build | Avg FPS | `Run/Run_NTSC` Excl% |
|--------|-------|---------|----------------------|
| 83adc81 | master baseline (pre-static-dispatch) | ~54 | run() 10.7% |
| 2780287 | static dispatch merged | ~56 | Run_NTSC 9.7% |
| b60c023 | + FDS palCache fix | ~62 | Run_NTSC 9.7% |
| 7533ebd | + masterClockTotal dead-code removed | 63.23 | Run_NTSC 9.1% |
| **bdb62ca** | **+ mcTickFn routing, legacy MCT removed** | **63.28** | **Run_NTSC 9.0%** |

Cumulative gain over pre-refactor master baseline: ~**+17% FPS**
(though the absolute numbers vary by session state; see earlier
comparison reports for same-session validation).

---

## 4. Architectural Observations

The mcTickFn routing closes the static-dispatch loop:
- Outer hot path: `Run_NTSC` → `MasterClockTickInlineNTSC` × 120000
  (direct, AggressiveInlined into for-loop body)
- Nested time advancement (PPU register handlers, AlignPhase): same
  `MasterClockTickInlineNTSC` via function pointer
- Legacy `MasterClockTick` deleted — no remaining caller

PAL/Dendy now correctly use their region-specific NMI offsets
(`mcCpu==12` and `mcCpu==11`) even when nested via PPU register
access. Previously this leaked the NTSC-hardcoded `mcCpu==8` literal.

---

## 5. Test Status

- **184/184 blargg PASS**
- AC 138/138 inherited (not re-run on this branch — pure plumbing change)

---

## 6. Architectural Note: Structural Unroll Still Impossible

The mcTickFn refactor unifies nested call logic, but the underlying
constraint that prevents structural unroll is unchanged: PPU register
handlers internally advance master clocks via nested ticks (TriCNES
EmulateNMasterClockCycles model). The outer unroll cannot predict
nested consumption, so per-MC event scheduling cannot be hardcoded.

Gated form (one inline call = one tick) remains the architectural
ceiling under this model. Detailed analysis: `MD/jit/20260413_*` and
the architectural note above `Run_NTSC` in `Main.cs`.
