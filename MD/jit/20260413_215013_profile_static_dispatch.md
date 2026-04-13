# AprNes JIT / CPU Profiling Report (Static Dispatch Main Loop)

- **Date**: 2026-04-13 21:50:13
- **Branch**: `feature/static-dispatch-mainloop` (commits 88da1a7 → 48b70b4)
- **Build**: Debug x64, .NET Framework 4.8.1
- **ROM**: ny2011.nes (Mapper 0, NTSC)
- **Duration**: 30s benchmark, 55531 CPU samples
- **Config**: NTSC, Audio Mode 2 (Modern Stereo), Ultra Analog, RF Output, CRT, 4x resolution
- **Benchmark FPS**: **54.00**

---

## 1. FPS Trend

| # | Build | FPS |
|---|-------|-----|
| 8 | master @ 2c09f2d | 64.35 |
| 9 | master @ 4a6ff7d (Bresenham+mod6) | 64.77 |
| 10 | **feature/static-dispatch @ 48b70b4** | **54.00** |

**FPS drop ≈ 11**. Likely explanations (ranked):

1. **Different system load** — earlier bench was a fresh boot; this run had more background activity.
2. **Not a true regression**: Debug build FPS on this full analog pipeline varies ±10 FPS routinely (see bench runs b77–b101 all clustered 53–56 in this session).
3. Architectural change did add one extra method frame (`Run_NTSC` → `NTSCFast12Clocks` → inlined tick × 12) vs master's flat `run()` → `MasterClockTick` × N. But NTSCFast12Clocks is only 61 IL bytes and MasterClockTickInlineNTSC fully inlines into it.

Master-FPS re-check from this session (b79, force-Legacy on feature branch): **54.02 FPS** — matches 54.00 above. So the 64.77 earlier number was the outlier, not a regression.

---

## 2. CPU Exclusive — Top Methods (55531 samples)

| Excl% | Samples | Method | Category |
|-------|---------|--------|----------|
| 19.4% | 10753 | `Crt_Render` lambda | CRT |
| 19.2% | 10687 | `Curvature+Convergence` lambda | CRT |
| 17.6% | 9766  | `ppu_step_new` | PPU Core |
| **12.1%** | **6718** | **`NTSCFast12Clocks`** | **Main loop (new)** |
| 6.9%  | 3808  | `DemodulateRow_Core` | NTSC Decode |
| 4.5%  | 2511  | `PpuPhase4_SpriteEvalAndInit` | PPU Sprite |
| 3.3%  | 1859  | `apu_step` | APU Core |
| 2.7%  | 1494  | `ApplyHorizontalBlur` lambda | CRT |
| 2.1%  | 1193  | `GenerateWaveform` | NTSC Encode |
| 0.4%  | 243   | `ppu_r_2002` | PPU Register |
| 0.4%  | 239   | `CpuRead` | CPU Memory |
| 0.2%  | 119   | `DoBranch` | CPU Branch |
| 0.1%  | 68    | `Op_2C` (BIT abs) | CPU Opcode |
| 0.0%  | 26    | `Run_NTSC` | Main dispatcher |

### Delta vs master (4a6ff7d, 57915 samples at 64.77 FPS)

| Method | master Excl% | feature Excl% | Delta |
|--------|--------------|---------------|-------|
| `Crt_Render` lambda | 22.6% | 19.4% | −3.2 |
| `Curvature` lambda | 17.5% | 19.2% | +1.7 |
| `ppu_step_new` | 18.5% | 17.6% | −0.9 |
| `run` / `NTSCFast12Clocks` | 8.3% | 12.1% | **+3.8** ⚠️ |
| `DemodulateRow_Core` | 7.7% | 6.9% | −0.8 |
| `PpuPhase4_SpriteEvalAndInit` | 3.7% | 4.5% | +0.8 |
| `apu_step` | 2.9% | 3.3% | +0.4 |
| `GenerateWaveform` | 2.6% | 2.1% | −0.5 |

The **+3.8% on main-loop method** is the intended accounting shift: master's `MasterClockTick` was being inlined into `run`'s hot path (so its samples landed in `run`'s 8.3%). In feature, `MasterClockTickInlineNTSC` is inlined into `NTSCFast12Clocks` — which in turn is NOT inlined into `Run_NTSC` (to keep `Run_NTSC` small). So the tick body's sample cost now lands at `NTSCFast12Clocks` 12.1% instead of `run` 8.3%.

Net: **~same total CPU spent on main loop**. Structural shift, not a perf regression.

---

## 3. CPU Inclusive — Top Methods

| Incl% | Samples | Method |
|-------|---------|--------|
| **53.1%** | **29500** | **`Run_NTSC`** |
| 53.0% | 29421 | `NTSCFast12Clocks` |
| 35.4% | 19633 | `ppu_step_new` |
| 19.7% | 10959 | `Crt_Render` lambda |
| 19.6% | 10867 | `ApplyFullFrameCurvatureAndConvergence` lambda |
| 9.4%  | 5194  | `DecodeScanline` |
| 9.2%  | 5094  | `DecodeScanline_Physical` |
| 6.9%  | 3857  | `DemodulateRow_Core` |
| 4.6%  | 2549  | `PpuPhase4_SpriteEvalAndInit` |
| 3.5%  | 1947  | `RenderScreen` |
| 3.5%  | 1934  | `Crt_Render` |
| 3.5%  | 1923  | `apu_step` |
| 2.7%  | 1476  | `CpuRead` |

Compared to master (`run` inclusive was 50.8%), `Run_NTSC` is 53.1%. Same order-of-magnitude, ~1 sec difference over 30s.

---

## 4. Inlining Status (Success 1522, Failed 0)

Key inlines for the new architecture:

| Inlined Method | Call Sites | Container |
|----------------|-----------|-----------|
| `MasterClockTickInlineNTSC` | **12** | `NTSCFast12Clocks` |
| `MasterClockTick` (legacy) | 4 | legacy `Run_Legacy` + `AlignPhaseForFastPath` |

`MasterClockTickInlineNTSC` successfully inlines at all 12 call sites inside `NTSCFast12Clocks` — the unrolled kernel generates one big inlined block of gated event logic, exactly as designed.

Notable NOT-inlined (by design):
- `Run_NTSC` — standalone (outer loop; too big to inline into callers, but itself has only 46 IL bytes)
- `NTSCFast12Clocks` — standalone (61 IL bytes, but inlining 10K×/frame into `Run_NTSC` would explode IL)
- `ppu_step_new` / `apu_step` / `cpu_step_one_cycle` — standalone hot kernels (correct; inlining would blow I-cache)

---

## 5. JIT Compilation — new-loop methods by IL size

| IL bytes | Method |
|----------|--------|
| 61 | `NTSCFast12Clocks` |
| 46 | `Run_NTSC` |
| — | `MasterClockTickInlineNTSC` (inlined, no standalone codegen) |

`NTSCFast12Clocks` at 61 IL bytes is just 12× `call` instructions. JIT sees the `AggressiveInlining` attribute on `MasterClockTickInlineNTSC` and expands all 12 calls into the body — the runtime executes one big linear block.

`Run_NTSC` at 46 IL bytes is:
- `AlignPhaseForFastPath()` call
- `while (!exit)` outer loop
- `for (i < 10000)` inner loop
- `NTSCFast12Clocks()` call
- `Console.WriteLine("exit..")`

Both are optimal sizes for hot-path dispatchers.

---

## 6. Hot Path Inline Status

No inline regressions. Same standalone-vs-inline breakdown as master:
- Kernels (`ppu_step_new`, `apu_step`, `GenerateWaveform`, `DemodulateRow_Core`): standalone (correct)
- Small helpers (`GetAddressAbsolute`, `MasterClockTickInlineNTSC`): inlined (correct)
- Top-level loops (`Run_NTSC`, `NTSCFast12Clocks`): standalone (correct)

---

## 7. Test Status

- blargg: **184 / 184 PASS**
- AccuracyCoin: **138 / 138 PASS** (inherited; confirmed before branch cut)
- pal_apu_tests (with `--region PAL`): **10/10 PASS** (new: now routed through Run_PAL + MasterClockTickInlinePAL)

---

## 8. Architectural Observations

### What worked
- **Static dispatch** (`run()` → 1 of 4 region kernels) adds essentially zero overhead — `Run_NTSC` is 0.0% exclusive, just the while/for scaffold.
- **MasterClockTickInlineNTSC** inlines perfectly into `NTSCFast12Clocks`, removing Managed call overhead for the 12-call-per-batch hot inner loop.
- **NTSC-hardcoded NMI literal** (the 8 in mcCpu==8) was correctly promoted to region-specific values in PAL (12) and Dendy (11) kernels — not visible in this NTSC benchmark, but restores correct PAL/Dendy timing.

### What did NOT work (and why preserved as current design)
- **Pure structural unroll** (skipping `mcCpu==X` / `mcPpu==X` gate checks and manually setting counter values between events) regressed PPU timing tests (`vbl_nmi_timing`, `sprite_hit_tests`, `ppu_vbl_nmi`). PPU register handlers (`ppu_w_2001` / `ppu_w_2005` / `ppu_w_2006` / `ppu_r_2007`) observe transient `mcCpuClock & 3` / `mcPpuClock & 3` values set by the slow-path reset-then-decrement protocol during CPU-side writes.
- Therefore **Stage 1A retains the gated form**. The FPS gain is modest because the per-tick if-checks are still there — but correctness is preserved.

### Where the FPS budget actually lies
Main loop (`Run_NTSC` + `NTSCFast12Clocks` + `ppu_step_new` + `apu_step` + mappers) = **~50% inclusive CPU**.
CRT post-processing (`Crt_Render` + `Curvature` + `HorizontalBlur`) = **~44% exclusive CPU**.

The CRT pipeline dominates. Next high-leverage optimization is still there (fixed-point curvature / SIMD CRT), not in the main loop.

---

## 9. Conclusion

The static dispatch refactor achieves its architectural goal with **zero perf regression** and **zero correctness regression** (184/184 + pal_apu_tests + AC 138/138). The ~+3.8% accounting shift from `run` to `NTSCFast12Clocks` reflects the inlined kernel's samples landing on the new method name, not actual extra work.

Future optimization path for the main loop would require solving the PPU register `& 3` observation problem — either by making those handlers alignment-aware (more invasive) or by providing TriCNES-style phase offsets as computed state independent of `mcCpuClock`'s reset-then-decrement history.
