# AprNes PMU + JIT Profile — Post MEM Refactor (Ryzen 7 3700X)

- **Date**: 2026-04-23 01:55
- **Branch**: `master` @ dd0ac39 (post 8-page table + joypad atomic fix)
- **Build**: Debug x64, .NET Framework 4.8.1
- **CPU**: AMD Ryzen 7 3700X, 8-core Zen 2 (L1 I-cache 32 KB × 8)
- **Config**: NTSC, Audio Mode 2, Ultra Analog RF, CRT, 4× resolution (matches 04-14 baseline for apples-to-apples)
- **Duration**: 30 s benchmark, 77376 ms CPU time, 3.9 M PMU samples

Reproduction:

```
cmd /C tools\analyze\run_perfview_pmu.bat
dotnet run --project tools\analyze\PmuAnalyzer -c Release
dotnet run --project tools\analyze\EtlAnalyzer -c Release -- temp\aprnes_pmu.etl AprNes temp\jit_report.txt
```

---

## 1. L1 I-Cache Miss Overview

**Global miss rate: 1.73%** (13660 / 790853 fetches)

| Range | Tier | Current |
| --- | --- | --- |
| < 1 % | excellent | ← 04-14 baseline was here (0.52%) |
| **1–3 %** | **healthy** (L2 absorbs) | ← **current, 1.73%** |
| 3–10 % | concerning | |
| > 10 % | bad | |

Still inside "healthy" band but approaching the boundary — the 100+ perf commits between 04-14 and 04-23 (structural unroll, Phase4 cold extraction, SWAR, SIMD, FMA, etc.) grew the hot working set in exchange for FPS. Trade-off was +13% FPS across the period, so net positive.

### Per-method miss rate (hot methods only)

| Method | 04-14 | Current | Change |
| --- | ---:| ---:| ---:|
| `ppu_step_new` | 0.31% | **3.45%** | **11×↑** |
| `PpuPhase4_SpriteEvalAndInit` | 0.36% | **4.30%** | **12×↑** |
| `Run_NTSC` | 0.36% | **3.15%** | 9×↑ |
| `apu_step` | 0.47% | **3.10%** | 7×↑ |
| `PpuPhase4_SpriteFetch` (new) | — | **5.14%** | new hot |
| `CpuRead` (new) | — | **3.04%** | new hot |
| `DemodulateRow_Core` | 1.43% | 0.94% | 0.66×↓ |
| CRT `<Render>` lambda | 0.93% | 1.10% | ≈ |
| CRT `<Curvature>` lambda | 1.28% | 0.56% | 0.44×↓ |

**NesCore core degraded significantly in miss rate; CRT pipeline slightly improved** (FMA + Vector256 made CRT hot loop more compact).

### Root causes for NesCore degradation

1. **Structural unroll** (`NestedTickN` + `MasterClockTickUnrolled*`) — drove +13.1% FPS but added large amounts of region-kernel machine code
2. **PpuPhase4 cold extraction** (commit 330036a) — split `SpriteEvalAndInit` and `SpriteFetch` into two standalone methods. Reduced main function IL by 67%, but the two now have independent L1 footprints that evict each other under interleaved access
3. **APU `apuOutputFn` function pointer dispatch** (commit 671db3e) — shrank `apu_step` IL by 35% but split code paths that may no longer share a cache line

The 8-page MEM refactor (merged today) actually **reduces** I-cache pressure (1 MB dispatch table → 64 bytes). It's not the cause of the regression; its effect is offset by the larger forces above.

---

## 2. JIT — Top 30 by Exclusive CPU

NesCore accounts for **46.7% total CPU**; remainder is CRT lambdas (~26%), kernel (~12%), CLR runtime (~4%), and other (~11%).

| # | Excl % | Method | IL bytes | Inlined? |
| ---:| ---:| --- | ---:| --- |
| 1 | **10.6** | CrtScreen lambda `<Render>b__0` | 1364 | NO (standalone) |
| 2 | **9.1** | `ppu_step_new` | **2331** | NO (standalone) |
| 3 | **7.9** | CrtScreen lambda `<ApplyCurvature>b__1` | 309 | NO (standalone) |
| 4 | **5.6** | `DemodulateRow_Core` | **2283** | **YES** (into NTSC worker lambda) |
| 5 | 4.1 | `Run_NTSC` | — | NO (outer loop) |
| 6 | 3.5 | `PpuPhase4_SpriteEvalAndInit` | 612 | NO |
| 7 | 1.8 | `apu_step` | 680 | NO |
| 8 | 1.5 | `GenerateWaveform` | 565 | NO |
| 9 | 0.5 | CrtScreen lambda `<ApplyHorizontalBlur>b__0` | 385 | NO |
| 10 | 0.5 | `PpuPhase4_SpriteFetch` | 660 | NO |
| 11 | 0.4 | `CpuRead` | — | NO |
| 12 | 0.2 | `NestedTick7_NTSC` | 153 | NO |
| 13 | 0.2 | `DoBranch` | 240 | NO |
| 14 | 0.1 | `ApuOutputCatchup` | 223 | NO |
| 15 | 0.1 | `Op_2C` (BIT abs) | — | NO |
| 16 | 0.1 | `Wrap_MapperR_RPG` | — | NO |
| 17 | 0.1 | `Mapper000.MapperR_CHR` | — | NO |
| 18 | 0.0 | `GetAddressAbsolute` | — | **YES** |
| 19 | 0.0 | `Mapper000.MapperR_RPG` | — | NO |
| 20 | 0.0 | `Op_CD` (CMP abs) | — | NO |
| 21 | 0.0 | `clockdmc` | — | NO |
| 22 | 0.0 | `Op_50` (BVC) | — | NO |
| 23 | 0.0 | `PpuPhase4_DummyNTFetch` | 319 | NO |
| 24 | 0.0 | `PpuPhase4_VisibleScanlineDot1Init` | 135 | NO |
| 25 | 0.0 | `GetAddressAbsOffX` | 541 | NO |
| 26 | 0.0 | `ppu_r_2002` | — | NO |
| 27 | 0.0 | `Op_CA` (DEX) | — | NO |
| 28 | 0.0 | `Op_D0` (BNE) | — | NO |
| 29 | 0.0 | `PpuPhase3_Events` | — | NO |
| 30 | 0.0 | `Op_BD` (LDA abs,X) | — | NO |

---

## 3. JIT Inlining Status

- **Successful inline events**: **1611**
- **Failed inline events**: **0** (JIT accepted every hint)

Inline decisions are healthy overall:

- Every method > ~300 IL bytes on the hot list stays **standalone** (correct — avoids code duplication that would blow I-cache)
- Small helpers (`GetAddressAbsolute`, bit-twiddles, etc.) auto-inline as expected
- **One notable exception**: `DemodulateRow_Core` (2283 IL) got **inlined** into its single caller `Ntsc_FlushPendingRows` worker lambda. This forms a > 2 KB composite lambda, which partly explains why the NTSC worker lambda dominates Excl% ranks #1 and #3 at 10.6% + 7.9% = **18.5%** combined

---

## 4. IL Size Ranking (code-bloat risk tracking)

Top 15 methods by IL size:

| # | IL bytes | Method | Path |
| ---:| ---:| --- | --- |
| 1 | 5296 | `InitOpHandlers` | cctor (one-shot) |
| 2 | 4363 | `TestRunnerCore.Run` | test-mode only |
| 3 | 3195 | `initAPU` | one-shot |
| 4 | 2978 | `NesCore..cctor` | one-shot |
| **5** | **2331** | **`ppu_step_new`** | **hot** |
| **6** | **2283** | **`DemodulateRow_Core`** | **hot (NTSC)** |
| 7 | 2203 | `init` | one-shot |
| 8 | 2185 | `MapperRegistry.Create` | one-shot |
| 9 | 2001 | `Ntsc_Init` | one-shot |
| 10 | 1726 | TestRunnerCore lambda | test-mode only |
| **11** | **1364** | **CrtScreen `<Render>` lambda** | **hot (10.6% CPU)** |
| 12 | 1121 | `initPalette` | one-shot |
| 13 | 921 | `RomDatabase..cctor` | one-shot |
| 14 | 795 | `HardResetState` | reset-only |
| **15** | **680** | **`apu_step`** | **hot** |

---

## 5. Key Observations and Optimisation Directions

### Healthy aspects ✓

- All large methods (IL > 300) stay standalone — JIT's heuristics correctly resist inlining them
- Small helpers get auto-inlined — no wasted call overhead
- 100% inline success rate — no dropped hints

### Degradation watchlist

1. **`ppu_step_new` at 2331 IL + 9.1% CPU + 3.45% miss** — the single largest hot method. Next-level optimisation direction: push rarely-hit scanline-boundary / special-dot branches into `NoInlining` cold helpers, shrinking the resident main path
2. **`DemodulateRow_Core` inlined into NTSC worker lambda** — resulting composite > 2 KB; candidate for `[MethodImpl(NoInlining)]` to keep lambda lean, but only worth doing if a direct A/B shows Flush-Pending-Rows benefits
3. **`CrtScreen <Render>` lambda at 10.6% CPU + 1364 IL** — CRT pipeline is half the cost after NesCore; aprnesava's GPU move via SkSL is the long-term plan for this
4. **`PpuPhase4_SpriteEvalAndInit` + `SpriteFetch` (4.0% CPU combined + 4.30%/5.14% miss each)** — cold extraction made the main function smaller but split the footprint into two competing L1-resident methods that evict each other. Fusing them under conditional predicates might claw back miss rate at the cost of one big method

### What to do now

Nothing urgent. We're still in the "healthy" I-cache band, blargg 184/184 and AccuracyCoin 138/138 are intact, and FPS has been moving up steadily. The numbers in this snapshot document the current cost structure so future changes can be measured against it.

Next natural target for further gains: cold extraction on `ppu_step_new` to carve out the rarely-executed boundary logic — historically that pattern has won 1-3% FPS per commit.
