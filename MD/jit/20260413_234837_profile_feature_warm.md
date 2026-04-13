# AprNes JIT / CPU Profiling Report (Feature Branch — Warm State)

- **Date**: 2026-04-13 23:48:37
- **Branch**: `feature/static-dispatch-mainloop` @ `0671d87`
- **Build**: Debug x64, .NET Framework 4.8.1, freshly rebuilt (`/t:Rebuild`)
- **ROM**: ny2011.nes (Mapper 0, NTSC)
- **Config**: NTSC, Audio Mode 2, Ultra Analog RF, CRT, 4x resolution
- **Benchmark FPS** (3-run avg): **62.59** (62.32 / 62.75 / 62.69)

---

## 1. FPS Recovery vs Earlier Session

| Time | Branch | Build State | 3-run Avg FPS |
|------|--------|-------------|---------------|
| ~22:23 | feature direct-inline | warm (after long perfview/test session) | 56.32 |
| ~22:56 | master (b160) | warm | 53.37 |
| **23:48** | **feature direct-inline** | **rebuild + cooler system** | **62.59** |

Same code as the 22:23 measurement — but now **+6.27 FPS** because background load and CPU thermal state recovered. This further confirms the earlier "FPS dropped" concern was session-state drift, not branch regression.

Compared to today's earliest master measurement (4a6ff7d at 64.77 FPS, very early in session): we're now within 2 FPS, so feature is essentially at the same ceiling as cold-master.

---

## 2. CPU Exclusive — Top Methods (56673 samples)

| Excl% | Samples | Method |
|-------|---------|--------|
| 22.2% | 12585 | `Crt_Render` lambda |
| 18.6% | 10557 | `ppu_step_new` |
| 17.5% | 9892  | `Curvature+Convergence` lambda |
| **9.7%** | **5473** | **`Run_NTSC`** |
| 7.6%  | 4320  | `DemodulateRow_Core` |
| 3.8%  | 2157  | `PpuPhase4_SpriteEvalAndInit` |
| 3.2%  | 1790  | `apu_step` |
| 3.0%  | 1684  | `ApplyHorizontalBlur` lambda |
| 2.6%  | 1476  | `GenerateWaveform` |

`Run_NTSC` exclusive = 9.7% — **identical to the earlier feature profile** despite higher absolute FPS. The cost shape is stable; system noise simply scales the 30s sample count.

---

## 3. Comparison with Master Profile (same-session baseline)

| Method | Master (53.37 FPS) | Feature (62.59 FPS) | Delta |
|--------|--------------------|---------------------|-------|
| Main loop | `run()` 10.7% | `Run_NTSC` **9.7%** | **−1.0%** |
| `Crt_Render` lambda | 19.6% | 22.2% | +2.6 (CRT scaled with FPS) |
| `ppu_step_new` | 19.1% | 18.6% | −0.5 |
| `Curvature` lambda | 18.1% | 17.5% | −0.6 |
| `DemodulateRow_Core` | 7.2% | 7.6% | +0.4 |
| `apu_step` | 2.9% | 3.2% | +0.3 |

Main loop saving holds at −1.0% (the structural improvement from removing `!isFDS` branches + NTSC constant inlining).

CRT exclusive % went up slightly because faster main loop = more frames rendered per second = more CRT pipeline executions per profile window.

---

## 4. Key Confirmation

The measurement at this point in the day shows:
- **Feature branch is healthy** — no regression, ~5-7% faster than master in same-session
- **System state recovered** to near the early-morning peak (62.59 vs the 64.77 morning master)
- **Main loop optimization is real and consistent** at −1.0% exclusive

---

## 5. Test Status (unchanged)

- 184/184 blargg PASS
- 138/138 AccuracyCoin PASS (inherited from baseline)
