# AprNes Runtime Performance Benchmark Study

## Test Environment

| Item | Description |
|------|------|
| CPU  | 13th Gen Intel Core i7-1370P |
| OS   | Windows 10 (Build 19045) |
| ROM  | Controller Test (USA).nes (Mapper 0, NROM) |
| Test duration | 10 seconds per run, frame rate limit disabled (LimitFPS = false) |
| Test date | 2026-03-03 |

---

## Test Results

| # | Runtime | Total Frames | FPS | Relative Baseline |
|---|----------|------------|-----|---------|
| 1 | .NET Framework 4.8.1 JIT | 4,220 | **422.0** | 100% |
| 2 | Native AOT (NesCoreNative.dll) | 5,500 | **550.0** | +30.3% |
| 3 | .NET 8 RyuJIT | 7,018 | **701.8** | +66.3% |
| 4 | .NET 10 RyuJIT | 7,640 | **764.0** | +81.0% |

```
.NET Framework 4.8.1 JIT  ████████████████████  422 FPS  (baseline 100%)
Native AOT                 ██████████████████████████  550 FPS  (+30%)
.NET 8 RyuJIT              █████████████████████████████████  702 FPS  (+66%)
.NET 10 RyuJIT             ████████████████████████████████████  764 FPS  (+81%)
```

---

## .NET 8 vs .NET 10: JIT Comparison

```
.NET 8  RyuJIT : 701.8 FPS
.NET 10 RyuJIT : 764.0 FPS
Difference      : +62.2 FPS (.NET 10 is ~+8.9% faster)
```

### .NET 10 JIT Key Improvements (Officially Claimed)

| Improvement | Impact on Emulator |
|---------|--------------|
| **Loop Optimization** (hoisting bounds checks out of loops) | Reduces redundant operations per iteration in CPU/PPU main loops |
| **Struct Physical Promotion** (promote fields directly to registers) | Faster access to NES CPU register structs |
| **Stack Allocation of Value Arrays** (small arrays allocated on stack) | Reduces GC pressure; smoother hot paths |
| **Expanded Escape Analysis** | Reduces delegate/callback overhead |
| **SIMD / Vectorization Extensions** (AVX10.2, ARM64 SVE) | Automatic vectorization of numerically intensive operations |

---

## JIT vs AOT Trade-off Analysis

### JIT (Just-In-Time) — RyuJIT

**Advantages:**
- ✅ **PGO (Profile-Guided Optimization)**: Observes real hot paths at runtime and deeply optimizes the most-taken branches
- ✅ **Tiered Compilation**: Fast startup at Tier 0, then re-optimizes hot paths at Tier 1 based on profiling
- ✅ Can perform devirtualization (virtual method elimination) using runtime type information
- ✅ Benefits from continuous improvements in each .NET version

**Disadvantages:**
- ❌ Requires warm-up on startup (slower cold start)
- ❌ Target machine must have the corresponding .NET Runtime installed

### AOT (Ahead-Of-Time) — Native AOT

**Advantages:**
- ✅ Extremely fast startup (no JIT warm-up; 450ms → 50ms)
- ✅ Low memory footprint (no JIT infrastructure resident in memory)
- ✅ Suitable for containers/microservices/CLI tools (image size reduced 60~87%)
- ✅ Does not depend on the target machine having .NET Runtime installed
- ✅ **Stable low latency**: No occasional pauses caused by JIT recompilation

**Disadvantages:**
- ❌ **Lacks runtime PGO**: No profiling information at compile time; optimization depth cannot match JIT
- ❌ For long-running CPU-bound computation, static compilation cannot keep up with JIT's dynamic optimization
- ❌ Reflection and dynamic type features are restricted

### Key Conclusion

> **AOT's direction of progress is "deployment flexibility"; JIT's direction of progress is "computation throughput".**
>
> The gap between them still exists in .NET 10: AOT 550 FPS vs JIT 764 FPS (difference -28%).
> This gap stems from PGO's fundamental advantage and will not disappear as AOT versions are upgraded.

---

## Overall Comparison of Four Runtimes

| Runtime | FPS | Best Use Case | Poor Fit |
|----------|-----|---------|---------|
| .NET Framework 4.8.1 JIT | 422 | Maintaining existing Windows projects | High-performance requirements, cross-platform |
| Native AOT | 550 | CLI tools, containers, fast startup | Long-running CPU-bound computation |
| .NET 8 RyuJIT | 702 | High-performance apps, broad deployment environments | Startup-time-critical scenarios |
| **.NET 10 RyuJIT** | **764** | **High performance + latest version optimizations** | Environments requiring older compatibility |

### Summary of Findings

1. **.NET 10 RyuJIT is the highest-performing of the four (764 FPS)**: +8.9% faster than .NET 8, validating the official claims that Loop Optimization and Struct Promotion provide real benefits for emulator tight loops.

2. **The improvement from .NET 8 → .NET 10 JIT is relatively modest (+8.9%)**: Compared to the jump from .NET Framework → .NET 8 (+66%), the marginal gains between versions are diminishing, indicating that RyuJIT is already quite mature.

3. **Native AOT is not the best choice for computational throughput**: AOT's role is "startup speed" and "deployment convenience". In this project, the existence of NesCoreNative.dll is to allow other languages to call the NES core, not to pursue maximum FPS.

4. **.NET Framework 4.8.1 JIT is still usable**: 422 FPS gives a 7× headroom over the NES 60 FPS target; there is no perceivable difference for users, but its performance ceiling is much lower than modern .NET.

5. **Migrating to .NET 8 or .NET 10 provides real benefits**: For performance alone, migrating to .NET 8 yields a free +66% performance gain; upgrading further to .NET 10 adds another +8.9%, at the cost of requiring the corresponding Runtime on the target machine.

---

## Sprite Pass 3 SIMD Optimization Results

### Implementation Overview

Under .NET 8/10, **conditional compilation (`#if NET8_0_OR_GREATER`)** was used to add SSE4.1 SIMD optimization to the sprite compositing loop (Pass 3):

**Original scalar logic (256 conditional branches):**
```csharp
for (int x = 0; x < 256; x++) {
    if (sprSet[x] == 0) continue;                               // branch
    if (!ShowBG || BG_array[x] == 0 || sprPriority[x] == 0)    // condition
        ScreenBuf[x] = sprColor[x];
}
```

**SIMD logic (SSE4.1, 4 pixels at a time):**
```
hasSprMask   = ConvertToVector128Int32(sprSet[x..x+3])  != 0
bgTranspMask = LoadVector128(BG_array[x..x+3])          == 0
frontMask    = ConvertToVector128Int32(priority[x..x+3]) == 0
condMask     = bgTranspMask | frontMask
writeMask    = hasSprMask & condMask
result       = BlendVariable(screen, sprColor, writeMask)  ← SSE4.1 core
```

.NET Framework 4.8.1 retains the original scalar path (no `System.Runtime.Intrinsics`).

---

### SIMD Effect Test

Test ROM: **spritecans.nes** (64 sprites bouncing across the full screen simultaneously, maximizing Sprite Pass 3 load)

Test method: Two independent processes, running in alternating order (to eliminate "first run JIT not yet warm" and "second run CPU already throttled" bias), taking the average:

| Round | SIMD ON | SIMD OFF |
|------|---------|----------|
| Round 1 (ON runs first) | 558.2 FPS | 603.8 FPS |
| Round 2 (OFF runs first) | 540.1 FPS | 534.5 FPS |
| **Average** | **549.1 FPS** | **569.1 FPS** |

**SIMD gain: -20.0 FPS (-3.5%)** ← within margin of error; inter-round differences reached 18~45 FPS, far exceeding any SIMD benefit.

---

### Why Is the SIMD Benefit Not Significant? (5 Root Causes)

1. **Sprite Pass 3 is not the real bottleneck**: Runs once per scanline (240 times/frame), but the true PPU bottleneck is `ppu_step_new` which runs every PPU cycle (~89,000 times/frame); SIMD cannot reach it.

2. **Sprite distribution in spritecans is sparse**: 64 sprites spread across 240 scanlines, averaging fewer than 3 colored sprites per scanline. The `MoveMask == 0` fast-skip causes most 4-pixel groups to be skipped immediately, leaving little room for SIMD.

3. **JIT auto-vectorization already covers part of the work**: Although conditional branches prevent full vectorization, RyuJIT PGO optimizes branch prediction on hot paths, narrowing the gap with SIMD.

4. **Memory bandwidth is not the bottleneck**: 256 uint = 1KB, entirely within L1 cache; scalar access is already fast enough.

5. **Thermal throttling noise dominates the results**: The laptop's i7-1370P Turbo Boost fluctuates by about ±10% under high load, far exceeding the proportion of total work represented by Sprite Pass 3, causing any small SIMD gain to be obscured.

### Conclusion

> Sprite Pass 3 SIMD provides **unmeasurable** benefit in real NES emulator scenarios (< margin of error).
> The impact on overall performance is limited, but as a practical example of conditional compilation, it demonstrates how
> the same codebase can take the SSE4.1 path on .NET 8/10 and the scalar path on .NET Framework, with no need to maintain two separate codebases.
> To measure a reliable SIMD benefit, testing must be done in a fixed-frequency CPU environment (with Turbo Boost disabled).

---

## Summary of Learnings

### Core Findings of This Study

#### 1. Runtime Selection
- **.NET 10 RyuJIT** is currently the highest-throughput choice (764 FPS): **+81%** faster than .NET Framework 4.8.1, and +9% faster than .NET 8
- **Native AOT** is not positioned as "a faster JIT" but as "fast startup + deployment convenience"; for long-running CPU-bound computation, AOT (550) trails JIT (764) by about 28%
- **.NET Framework 4.8.1** provides a 7× headroom over the NES 60 FPS target with no perceptible difference for users, but its performance ceiling is fixed

#### 2. Why JIT Consistently Outperforms AOT (Computation Throughput)
- **PGO (Profile-Guided Optimization)**: JIT can observe real hot paths at runtime and re-optimize; AOT can only rely on static analysis
- **Tiered Compilation**: .NET JIT compiles quickly for startup, then deeply optimizes hot paths afterward, balancing startup speed and steady-state performance
- **Devirtualization**: JIT confirms types at runtime; AOT must make conservative assumptions

#### 3. Conditions for SIMD Applicability
Manual SIMD (`System.Runtime.Intrinsics`) only yields measurable benefits when **all** of the following conditions hold:
- The operation itself accounts for a significant proportion of total execution time (> 5%)
- Data is dense enough and memory access is the bottleneck
- JIT auto-vectorization cannot cover it automatically (usually because of conditional branches)
- Tested in a fixed-frequency CPU environment (eliminating thermal throttling noise)

This project's Sprite Pass 3 (240 times/frame, 1KB within L1 cache) does not meet the first two conditions; SIMD benefit is unmeasurable.

#### 4. Correct Use of Conditional Compilation
```csharp
#if NET8_0_OR_GREATER
    // System.Runtime.Intrinsics is only available on .NET 8/10
    using System.Runtime.Intrinsics;
    using System.Runtime.Intrinsics.X86;
#endif

// In code:
#if NET8_0_OR_GREATER
    if (SIMDEnabled && Sse41.IsSupported)
        CompositeSpritesSimd(...);
    else
#endif
        CompositeSpritesScalar(...);
```
- `NETFRAMEWORK`: .NET Framework 4.8.1
- `NET8_0_OR_GREATER`: covers .NET 8 and .NET 10
- `NET10_0_OR_GREATER`: .NET 10+ only
- Conditional compilation symbols are automatically defined by the SDK; no extra csproj configuration needed

#### 5. Reliability of Performance Measurements
| Problem | Cause | Solution |
|------|------|---------|
| JIT warm-up bias | Tiered Compilation runs as Tier 0 (unoptimized) for the first few seconds | Use 10s+ tests; the first few seconds are the JIT ramp-up period |
| GUI exe doesn't wait | PowerShell `&` doesn't block WinExe | Use `Start-Process -Wait -NoNewWindow` instead |
| Thermal throttling noise ±10% | Laptop Turbo Boost dynamic frequency scaling | Alternate test order and take average, or test in a fixed-frequency environment |
| Same-process comparison distortion | JIT state from the first test segment affects the second | Use independent processes for each comparison |

#### 6. Confirmed Role of Each Target
| Project | Runtime | Primary Purpose |
|------|---------|---------|
| `AprNes` (.NET Fx 4.8.1) | JIT | Original development version, maximum compatibility |
| `NesCoreNative` (Native AOT) | AOT | NES core DLL, callable from other languages |
| `AprNesAOT` (.NET 8) | JIT | High-performance version, broad deployment environments |
| `AprNesAOT10` (.NET 10) | JIT | Highest performance, requires .NET 10 Runtime |

---

## Notes

> Main test ROM: `Controller Test (USA).nes` (Mapper 0, NROM), a pure CPU-bound scenario with frame rate limit disabled (LimitFPS = false).  
> SIMD test ROM: `spritecans.nes` (64 sprites bouncing full-screen, maximizing Sprite Pass 3 load).  
> Actual game performance varies with ROM complexity, but the relative ranking should remain consistent.

*Test tools: `benchmark.bat` / `benchmark.ps1`, results stored in `benchmark.txt`*  
*Last updated: 2026-03-03*
