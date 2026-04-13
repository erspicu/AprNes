# AprNes JIT / CPU Profiling Report (Direct-Inline Fix)

- **Date**: 2026-04-13 22:27:21
- **Branch**: `feature/static-dispatch-mainloop`
- **Build**: Debug x64, .NET Framework 4.8.1
- **ROM**: ny2011.nes (Mapper 0, NTSC)
- **Config**: NTSC, Audio Mode 2, Ultra Analog RF, CRT, 4x resolution
- **Purpose**: Explain the FPS regression in Fast v1 (wrapper-based) and validate the Fast v2 fix.

---

## 1. Three-Way Comparison

All benchmarks on same system state, 3 runs averaged, 30s each:

| Version | Run 1 | Run 2 | Run 3 | Average | Main-loop Excl% | vs Legacy |
|---------|-------|-------|-------|---------|-----------------|-----------|
| **Legacy** (master `run()` unchanged) | 55.64 | 55.13 | 55.74 | **55.50** | `Run_Legacy` 10.3% | baseline |
| **Fast v1** (with `NTSCFast12Clocks` wrapper) | 51.71 | 54.05 | 51.45 | **52.40** | `NTSCFast12Clocks` 12.1% | **−5.6%** ❌ |
| **Fast v2** (direct-inline, this commit) | 57.39 | 57.94 | 57.79 | **57.71** | `Run_NTSC` 9.7% | **+4.0%** ✅ |

---

## 2. Root Cause Analysis — Why Fast v1 Was Slower

### Legacy structure

```csharp
static void Run_Legacy() {
    while (!exit)
        for (int batch = 0; batch < MasterTicksPerFrame; batch++)
            MasterClockTick();   // AggressiveInlining
}
```

JIT sees `MasterClockTick` has `[AggressiveInlining]` and **inlines it directly into `Run_Legacy`'s for loop body**. Result: `Run_Legacy` is one big method containing 1 inlined tick body, and the tight loop has **zero function-call overhead**. JIT can do full register allocation / flattening across the entire hot loop.

### Fast v1 structure (the wrapper)

```csharp
static void Run_NTSC() {
    for (int i = 0; i < 10000; i++)
        NTSCFast12Clocks();      // NO [AggressiveInlining]
}

static void NTSCFast12Clocks() {   // NOT inlined into Run_NTSC
    MasterClockTickInlineNTSC();   // x12, all AggressiveInlined here
    // ...
}
```

`NTSCFast12Clocks` was a **regular standalone method** (no `[AggressiveInlining]` — it couldn't have one because 12 inlined kernel bodies would blow past JIT's inline budget anyway). Every 12 master clocks, `Run_NTSC`'s inner loop makes a **function call** to `NTSCFast12Clocks`.

**Cost per frame**:
- 357,368 MC per NTSC frame / 12 MC per call = 29,780 calls per frame
- × 60 FPS = ~1.8M function calls/sec
- × ~4 cycles per call (prologue/epilogue) = ~7M cycles/sec
- On a 2 GHz core at Debug build (inefficient baseline): material cost

The wrapper also **broke the branch-predictor pattern** — Legacy has 357K identical tick iterations per frame (one big predictable pattern); Fast v1 has 30K calls + 12 tick bodies per call, a different access pattern that doesn't tile as cleanly into I-cache.

### Fast v2 structure (the fix)

```csharp
static void Run_NTSC() {
    while (!exit)
        for (int i = 0; i < 120000; i++)
            MasterClockTickInlineNTSC();  // AggressiveInlining
}
```

JIT sees `MasterClockTickInlineNTSC` with `[AggressiveInlining]` and **inlines it directly into `Run_NTSC`'s for loop body** — **same structure as Legacy**, no wrapper, no call boundary. The outer loop now ticks 120,000 times (= 12 × 10,000) per exit check, matching the previous iteration count.

**Result**:
- Main-loop exclusive CPU: 9.7% (vs Legacy 10.3%, Fast v1 12.1%)
- The 0.6% under Legacy comes from two micro-optimizations:
  1. Removed `if (!isFDS)` branches (×2 per tick; non-FDS is guaranteed)
  2. Hardcoded NTSC constants (`masterPerCpu = 12`, `masterPerPpu = 4`, `masterPerPpuHalf = 2`) instead of static-field loads

---

## 3. Key Takeaway for Future Optimizers

**In .NET Framework 4.8.1 (no TieredPGO), wrapping an AggressiveInlined kernel in a standalone method cancels out the inlining benefit.** The method boundary becomes a real function call, and the wrapper itself becomes standalone-JIT'd code that cannot be inlined into its caller (because its body post-inline is too large for the JIT inline budget).

The correct pattern:
- **Do**: inline the kernel at **one** level (either into the final outer loop, or as a manually-unrolled block within a method the JIT can flatten)
- **Don't**: create a kernel-group wrapper (X-times-the-kernel-per-call) unless it has `[AggressiveInlining]` AND the post-inline size stays within JIT budget (~1KB IL for .NET Framework)

The originally-intended "fully unrolled" approach (inline 12 kernels textually into NTSCFast12Clocks, skip the gating checks manually) **was the correct target** — but we abandoned it because skipping the `mcCpu==X` gate checks broke PPU timing tests. The alternative of keeping the gates but inlining 12 copies into a wrapper method created this regression.

**Resolution**: Drop the wrapper entirely, call the inlined kernel directly from the outer loop. This is structurally equivalent to Legacy but with NTSC-constant hardcoding and `!isFDS` removal.

---

## 4. Test + Profile Status

- blargg: **184 / 184 PASS** (unchanged)
- Profile: `MasterClockTickInlineNTSC` shows as inlined into `Run_NTSC` (0 standalone codegen), exactly as Legacy's `MasterClockTick` inlines into `Run_Legacy`

---

## 5. Files Changed (Stage 1A post-fix)

`Main.cs`:
- `Run_NTSC` / `Run_FDS` / `Run_Dendy` / `Run_PAL` — direct inline call to `MasterClockTickInline<Region>` in outer for loop (ExitCheckInterval = 120000, matching Legacy's 357368-per-frame batching)
- Removed dead wrapper methods: `NTSCFast12Clocks`, `FDSFast12Clocks`, `DendyFast15Clocks`, `PALFast80Clocks`

Net line change: cleanup (−30 lines).
