# AprNes PMU L1 I-Cache Analysis (AMD Ryzen 7 3700X)

- **Date**: 2026-04-14 00:50
- **Branch**: `master` @ 47f7876
- **Build**: Debug x64, .NET Framework 4.8.1
- **CPU**: AMD Ryzen 7 3700X, 8-core Zen 2 (L1 I-cache: 32 KB × 8)
- **Config**: NTSC, Audio Mode 2, Ultra Analog RF, CRT, 4x resolution
- **Duration**: 30s benchmark, 59,562 ms CPU time

---

## 1. How to Reproduce

PerfView supports sampling on hardware PMU counters via `/CpuCounters`. AMD
Ryzen 7 3700X exposes (via `temp/PerfView.exe ListCpuCounters`):

- `IcacheMisses` (id 9) — L1 I-cache miss events
- `IcacheIssues` (id 20) — L1 I-cache fetch events (denominator)
- `TotalCycles` (id 19)
- `Timer` (id 0) — conventional sampled profile

PMU hardware has a fixed number of programmable slots (4-6 on Zen 2), so
we collect at most 4 counters at once. See `temp/run_perfview_pmu.bat`.

Analysis tool: `temp/PmuAnalyzer/` (stand-alone .NET 10 console app that
parses PMCSample events from the ETL and groups by JIT'd method name).

```
cmd /C temp\run_perfview_pmu.bat           # collect ~3M samples over 30s
dotnet run --project temp/PmuAnalyzer -c Release
# output: temp/pmu_report.txt
```

---

## 2. Global L1 I-Cache Miss Rate

**0.52%** (3,143 misses / 603,569 fetches)

Industry health thresholds:
- **< 1%**: excellent — working set fits comfortably in L1
- **1-3%**: healthy — minor evictions, L2 absorbs cost
- **3-10%**: concerning — significant L2 traffic
- **> 10%**: bad — visible stall-related FPS loss

AprNes is firmly in the "excellent" tier.

---

## 3. Per-Method Miss Rates (top hot methods)

| Method | IcacheMisses | IcacheIssues | **Miss %** |
|--------|--------------|--------------|-----------|
| `ppu_step_new` | 343 | 108,996 | **0.31%** |
| `Run_NTSC` | 203 | 56,778 | **0.36%** |
| `PpuPhase4_SpriteEvalAndInit` | 83 | 23,200 | **0.36%** |
| `apu_step` | 85 | 18,057 | **0.47%** |
| `Crt_Render` lambda | 60 | 6,444 | **0.93%** |
| `Curvature+Convergence` lambda | 50 | 3,905 | **1.28%** |
| `DemodulateRow_Core` | 57 | 3,991 | **1.43%** |
| `ApplyHorizontalBlur` lambda | 9 | ~1500 | ~0.6% |

### Observations

1. **Emulation core hot path (ppu_step_new / Run_NTSC / apu_step) = 0.3-0.5%** — excellent locality, the ~20 KB core fits well within L1 even with its callers.

2. **CRT pipeline = 0.9-1.4%** — slightly elevated but still healthy. The `DemodulateRow_Core` method (2283 IL bytes, largest in the pipeline) tops out at 1.43%. Consistent with the static estimate that analog+CRT working set approaches the 32 KB L1 ceiling but does not breach it catastrophically.

3. **Top miss rates in the report are all `ntoskrnl` (Windows kernel) addresses** — kernel code has ~2-6% miss rates because it's shared between all processes and has much larger working set than any one application. Our code is consistently better-localized than the kernel it runs on.

---

## 4. Why the Static 47 KB Estimate Overshot

Earlier static analysis (IL × 4 heuristic for Debug machine code) suggested
~47 KB total hot working set. Real measurement shows very little L1 pressure.
Reasons:

1. **Running window is narrow**. Within any 12-MC window only 2-3 methods are
   actively executing (`Run_NTSC` + one of `ppu_step_new`/`apu_step`/etc.).
   Combined working code = ~15-20 KB, easy L1 fit.

2. **CRT + emulation don't really interleave at 1-cycle granularity**. The CRT
   pipeline runs in chunks (per-row Parallel.For), so while its working set is
   hot the emulation pipeline is paused. The two don't simultaneously compete
   for the same L1.

3. **Zen 2 has 8-way associative 32 KB L1 I with an op cache in front**. Repeated
   loops often serve directly from the op cache (micro-ops already decoded),
   skipping L1 I-cache lookups entirely. This is why `ppu_step_new` — a 2627 IL
   byte method — can sustain 0.31% miss rate even though it exceeds any naive
   "how big is my function" calculation.

4. **Prefetcher reach**. AMD Zen 2's L1 I-fetcher stays ahead of the execution
   pointer, streaming code into L1 before it's needed.

---

## 5. Implications for Static Dispatch Refactor

The 4 region-specific kernels (`MasterClockTickInlineNTSC/FDS/Dendy/PAL`)
were a concern for potential I-cache bloat:

- Static code size: ~4 KB total across all 4 variants (well under 32 KB L1)
- **Runtime**: only 1 variant is hot per session (the others are cold code
  and get pushed to L2/L3 or just not fetched)
- **Measurement**: `Run_NTSC` miss rate 0.36% — one of the lowest in the
  entire profile

**Conclusion**: the static-dispatch refactor does not create I-cache pressure
and the `+3.9-5.5%` FPS improvement is entirely explained by the per-tick
branch and field-load savings (confirmed via cycle-count attribution in the
TotalCycles counter: `Run_NTSC` 7.9% of total cycles vs master's `run()`
10.7% — delta ≈ 2.8% of total budget).

---

## 6. Kernel Samples Leak Into Profile

About 10-15% of samples fall in `ntoskrnl` / `ntdll` / `clr` addresses that
PerfView couldn't symbolize (because we don't ship with kernel PDBs and the
PerfView cache doesn't auto-fetch them in the headless `run` mode). These
are genuine CPU time — context switches, scheduler, interrupt handlers,
page fault services for JIT'd code — not something we can optimize away,
and not bloating our L1 footprint (they're in separate address space).

---

## 7. Bottom Line

The Debug x64 AprNes build has **no measurable L1 I-cache pressure** under
the full analog+CRT pipeline. Main-loop optimizations beyond this point
need to target:

1. **CPU cycle count per tick** — but already near irreducible (the gated
   form can't be shortened without breaking PPU register `& 3` timing)
2. **CRT kernel inner loops** — 0.9-1.4% miss rate suggests minor wins
   possible from SIMD / data layout, but < 2% gain upper bound
3. **Parallel.For overhead** for CRT — visible but small

I-cache is **not** a bottleneck. The real bottleneck is the raw FLOP count
in the analog NTSC/CRT pipeline (`DemodulateRow_Core` alone = 6.7% of total
cycles).
