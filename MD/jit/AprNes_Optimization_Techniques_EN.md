# AprNes Non-JIT Optimisation Techniques

> This document catalogues the performance changes landed on AprNes's master branch **since 2026-03-15**, focused on **below the language/runtime level**: bitwise ops, branchless code, SWAR, SIMD, lookup tables, magic numbers, loop unrolling, integer-for-float, redundancy elimination, function pointer dispatch, data layout, etc. Each section includes real before/after commits for reference.
>
> Complementary to `JIT_ICache_Tutorial.md` — that one covers the macro philosophy of JIT and I-Cache; this one lists hand-coded craft techniques.
>
> Suitable for readers wanting to squeeze the last drop of performance out of a C# / C++ hot path.

---

## Table of Contents

1. [Bitwise: Use `&` Instead of `%`](#1-bitwise-use--instead-of-)
2. [Branchless: Eliminating Branches](#2-branchless-eliminating-branches)
3. [Lookup Tables (LUT)](#3-lookup-tables-lut)
4. [Magic Numbers: Using Math to Replace Branches](#4-magic-numbers-using-math-to-replace-branches)
5. [SWAR: SIMD Within a Register](#5-swar-simd-within-a-register)
6. [SIMD: True Vectorisation](#6-simd-true-vectorisation)
7. [Integer for Float (Fixed-Point / Bresenham)](#7-integer-for-float-fixed-point--bresenham)
8. [Loop Optimisation (Unroll, ILP, Loopless)](#8-loop-optimisation-unroll-ilp-loopless)
9. [Function Pointer Dispatch (Static Dispatch)](#9-function-pointer-dispatch-static-dispatch)
10. [Data Layout and Memory Optimisation](#10-data-layout-and-memory-optimisation)
11. [Redundancy Elimination (DRY / Hoisting Invariants)](#11-redundancy-elimination-dry--hoisting-invariants)
12. [Techniques Summary Table](#12-techniques-summary-table)

---

## 1. Bitwise: Use `&` Instead of `%`

**Core idea**: when the divisor `N` is a power of 2, `x % N` is bit-for-bit equivalent to `x & (N - 1)`. The latter is a single AND instruction (~1 cycle); the former requires the hardware divider (10+ cycles, depending on CPU).

### AprNes Case: Mapper ROM Bank Indexing

Commit `06a35ac perf(mapper): replace % N with & mask in hot read paths (pow2 ROMs)`

**Before:**
```csharp
public byte MapperR_RPG(ushort address)
{
    int bank = /* ... */;
    return PRG_ROM[bank % PRG_ROM_count * 0x4000 + (address & 0x3FFF)];
}
```

**After:**
```csharp
// Validate + precompute mask at MapperInit
if ((_PRG_ROM_count & (_PRG_ROM_count - 1)) != 0)
    throw new Exception("PRG_ROM_count must be power of 2");
prgCountMask = _PRG_ROM_count - 1;

// Hot path uses AND instead of %
return PRG_ROM[(bank & prgCountMask) * 0x4000 + (address & 0x3FFF)];
```

### When It Applies

| Applicable | Not applicable |
| --- | --- |
| Divisor known to be a power of 2 (ROM size, cache-line alignment, buffer capacity) | Divisor is arbitrary (e.g. `% 3`, `% 6`) |
| Can be validated at init time (throw if non-pow2) | Divisor varies at runtime |

**Note**: Mapper005 (MMC5) explicitly supports arbitrary CHR/PRG sizes, so `%` is kept there; Mapper019's audio phase `waveLength` is runtime-variable and can't be changed either.

### Advanced: Non-Power-of-2 Modulo

When the divisor isn't pow2 (e.g. `% 6`), use an "add + sign-bit" wrap:

Commit `57119fb perf: eliminate 3 hot-path modulo-6 ops in NTSC demodulator`

**Before:**
```csharp
int tModQ = ((phase0 - wQ_half + 2) % 6 + 6) % 6;  // double %
```

**After:**
```csharp
int tModQ = phase0 + 5;                          // subtract first (equivalent -wQ_half+2 ≡ 5 mod 6)
tModQ += ((5 - tModQ) >> 31) & -6;               // branchless wrap: if > 5, subtract 6
```

Principle: `(5 - tModQ) >> 31` in 32-bit signed arithmetic yields `0` (no overflow) or `-1` (overflow); `& -6` conditionally subtracts. Pure ALU, no branches, no div.

---

## 2. Branchless: Eliminating Branches

**Core idea**: modern CPUs have deep pipelines and branch prediction. A mispredicted branch flushes 10+ cycles. Regular, stable branches are fine, but **data-dependent branches** (different result each time) carry heavy cost. Branchless techniques convert "if/else" into arithmetic, letting the compiler emit CMOV or pure ALU.

### 2.1 Branchless Y-Flip (XOR mask)

Commit `7baf6a0 perf: branchless ComputeSpritePatternAddr + FlipByte LUT`

**Before:**
```csharp
int r = flipY ? ((7 - row) & 7) : (row & 7);
```

**After:**
```csharp
// -(sprAttr >> 7) = 0 (no flip) or -1 (flip)
// & 7 → 0 or 7
// row ^= 7 → 7 - row (in 3-bit range)
row ^= -(sprAttr >> 7) & 7;
```

Same trick for 8×16 sprites:

```csharp
row ^= -(sprAttr >> 7) & 15;
return ((sprTile & 1) << 12) | ((sprTile & 0xFE) << 4) | ((row & 8) << 1) | (row & 7);
// Pure bitwise ops replace a 4-way if/else for tile-half selection
```

### 2.2 Branchless Clamp / Saturate

C#'s `Math.Max / Math.Min` typically emits CMOV (conditional move), already branchless; but more complex range clamps sometimes benefit from explicit forms:

```csharp
// Clamp x to [0, maxIdx]
int rxR = Math.Max(0, Math.Min(srcTx + ioff, maxIdx));  // RyuJIT emits CMOV
```

Fully manual:
```csharp
int v = srcTx + ioff;
v -= (v - maxIdx) & ((v - maxIdx) >> 31 ^ -1);  // min(v, maxIdx)
v &= ~(v >> 31);                                 // max(v, 0)
```

But **in most cases `Math.Max/Min` is already good enough**; hand-coded bit versions are only worth writing when profiling identifies them as a bottleneck.

### 2.3 Branch → Mask Select

```csharp
// Original:
int result = condition ? a : b;

// Branchless (C-style):
int mask = -(condition ? 1 : 0);   // or:
int mask = condition ? -1 : 0;
int result = (a & mask) | (b & ~mask);
```

Again, RyuJIT often autogen CMOV — **disassemble first** before deciding to hand-write.

### 2.4 Key Judgement: When Is Branchless Worth It?

| Situation | Worth it? |
| --- | --- |
| Branch result is **highly predictable** (e.g. 99% go the same way) | **No** — branch predictor handles it |
| Different result every time (pixel-level data dependency) | **Yes** — mispredict cost is too high |
| Branches contain "long" side-effect work | **No** — branchless would force both sides to execute every time |
| Both sides are symmetric and short | **Yes** |

---

## 3. Lookup Tables (LUT)

**Core idea**: if a function's input space is small (e.g. ≤ 256 values), precompute all outputs into a table. Runtime becomes a single memory load — cost goes from N ALU ops down to one L1 hit (~4 cycles).

### AprNes Case: 256-Byte FlipByte LUT

Commit `7baf6a0`

**Before (12 ALU ops):**
```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
static byte FlipByte(byte b)
{
    b = (byte)(((b & 0xF0) >> 4) | ((b & 0x0F) << 4));
    b = (byte)(((b & 0xCC) >> 2) | ((b & 0x33) << 2));
    b = (byte)(((b & 0xAA) >> 1) | ((b & 0x55) << 1));
    return b;
}
```

**After (1 table read):**
```csharp
static readonly byte[] FlipTable = GenerateFlipTable();

static byte[] GenerateFlipTable()
{
    byte[] t = new byte[256];
    for (int i = 0; i < 256; i++) {
        int v = i;
        v = ((v & 0xF0) >> 4) | ((v & 0x0F) << 4);
        v = ((v & 0xCC) >> 2) | ((v & 0x33) << 2);
        v = ((v & 0xAA) >> 1) | ((v & 0x55) << 1);
        t[i] = (byte)v;
    }
    return t;
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
static byte FlipByte(byte b) => FlipTable[b];
```

256 bytes is a permanently L1-resident size, never evicted.

### Advanced: byte[] → byte* to Remove Bounds Checks

Commit `ad162c7 perf: FlipTable from managed byte[] to unmanaged byte*`

C# `byte[]` incurs a bounds check on every index. For known-safe indices, switch to `Marshal.AllocHGlobal` / `NativeMemory.AlignedAlloc` + `byte*`:

```csharp
static byte* FlipTablePtr;  // Marshal.AllocHGlobal(256) at init

static byte FlipByte(byte b) => FlipTablePtr[b];   // no bounds check
```

### When to Use LUT

| Condition | Good fit for LUT? |
| --- | --- |
| Input space ≤ 256 (1 byte) | **Very** — only 256 bytes, permanently L1-resident |
| Input space ~ 64 KB (16-bit) | Depends on access frequency. May evict other hot data |
| Input space > 1 MB | **No** — doesn't fit L2/L3, miss cost > compute cost |
| Computation itself is very cheap (1–2 instructions) | **No** — computing beats lookup |

Rule: **LUT capacity must be so small that D-Cache miss is virtually impossible**, otherwise you're just trading I-Cache pressure for D-Cache pressure.

---

## 4. Magic Numbers: Using Math to Replace Branches

**Core idea**: use clever integer / bitwise identities to compress multi-level branches into a single ALU chain. Classic techniques: de-Bruijn sequences, modular inverses, fast reciprocals.

### AprNes Case: de-Bruijn Style Bit Position Decode

Commit `8cd97cf perf(ppu): branchless sprite-index decode via magic-multiply`

**Problem**: given a 64-bit value `lowest` with exactly one bit set at position `8k+7` (k=0..7), find k.

**Before (3-level binary search):**
```csharp
uint lo32 = (uint)lowest;
int i;
if (lo32 != 0) {
    if ((lo32 & 0xFFFFu) != 0) i = (lo32 & 0x80u) != 0 ? 0 : 1;
    else                       i = (lo32 & 0x800000u) != 0 ? 2 : 3;
} else {
    uint hi32 = (uint)(lowest >> 32);
    if ((hi32 & 0xFFFFu) != 0) i = (hi32 & 0x80u) != 0 ? 4 : 5;
    else                       i = (hi32 & 0x800000u) != 0 ? 6 : 7;
}
```
2–3 mispredict-prone branches.

**After (1 multiplication):**
```csharp
int i = (int)((0x0001020304050607UL * (lowest >> 7)) >> 56);
```

**Principle**:
- `(lowest >> 7)` shifts the sole set bit from `8k+7` to `8k`, becoming `1 << (8k)`.
- Multiplying the magic constant `0x0001020304050607` by `1 << (8k)` shifts byte k of the magic left by `8k` bits.
- Byte k of the magic holds value k (byte 0 = 0x07, byte 1 = 0x06, …, byte 7 = 0x00).
- `>> 56` takes the top byte — the answer.

Cost: 1 SHR + 1 IMUL + 1 SHR, ~3–5 cycles, zero branches.

### When Can You Use Magic Numbers?

- **Input has strong structure** (e.g. "exactly one bit set", "must be power of 2", "narrow value range").
- **Data-dependent branches** will trash the branch predictor.
- **Correctness provable mathematically** (not via trial-and-error).

Magic number tricks are immensely satisfying once found, but **always leave a comment explaining the principle** — otherwise future-you won't understand it three months later.

---

## 5. SWAR: SIMD Within a Register

**Core idea**: SWAR (SIMD Within A Register) uses **regular integer registers** (`long` / `ulong`) to process multiple smaller values simultaneously (e.g. 8 bytes, 4 shorts). No SIMD instruction set required — it's the "poor man's" parallelisation.

### AprNes Case: Loopless OAM Multiplexer

Commit `5ad35c4 perf(ppu): loopless SWAR OAM multiplexer (+5% FPS)`

NES PPU must pick the lowest-index sprite with "X counter == 0 and (H|L) high bit set" from 8 sprite slots per pixel. Originally `for (int i = 0; i < 8; i++) { if (...) break; }`; now a pure SWAR pipeline:

```csharp
ulong xc = *(ulong*)sprXCounter;   // 8 bytes loaded at once

// Is each byte == 0? Carry-based trick
ulong has_bits = ((xc & 0x7F7F7F7F7F7F7F7FUL) + 0x7F7F7F7F7F7F7F7FUL) | xc;
ulong active_mask = (~has_bits & 0x8080808080808080UL);  // 0x80 per byte where byte == 0

// H | L high bit per byte
ulong pixel_mask = (*(ulong*)sprShiftH | *(ulong*)sprShiftL)
                   & 0x8080808080808080UL;

ulong valid = active_mask & pixel_mask;

if (valid != 0)
{
    // Isolate lowest set bit → identifies lowest-index sprite
    ulong lowest = valid & (ulong)(-(long)valid);
    // ... decode index via magic multiply
}
```

**Wins:**
- 8-iter for loop → single ulong pipeline.
- Common case (no sprite hit) exits early at `valid != 0`, skipping the whole block.
- Measured +5% FPS.

### Common SWAR Idioms

| Goal | Expression |
| --- | --- |
| Per-byte test for == 0 | `(x & 0x7F...) + 0x7F... \| x`; invert and mask 0x80... to decide per byte |
| Per-byte test for < N | `(x - 0x01...N) & ~x & 0x80...` |
| Per-byte add (no carry crossing byte) | `(a + b - ((a ^ b) & 0x80...)) ^ ((a ^ b) & 0x80...)` |
| Broadcast byte to all 8 lanes | `x * 0x0101010101010101` |
| Isolate lowest set bit | `x & -x` (on ulong) |

### Another AprNes Case: Batching APU Halt Flag Reads

Commit `0cd963d perf(apu): SWAR-batch the 4 per-cycle lenctrHalt register reads`

Originally each APU cycle read 4 halt bits (scattered across `$4000 / $4004 / $4008 / $400C`); now one 8-byte load, then mask:

```csharp
ulong rH = *(ulong*)(regs + 0);
lenctrHalt0 = (rH & 0x0000_0000_0000_0020UL) != 0;  // byte 0 bit 5
lenctrHalt1 = (rH & 0x0000_0000_0020_0000UL) != 0;  // byte 4 bit 5
lenctrHalt2 = (rH & 0x0000_0000_0000_0080UL) != 0;  // byte 8 bit 7
lenctrHalt3 = (rH & 0x0000_0020_0000_0000UL) != 0;  // byte C bit 5
```

One load + four bit tests, replacing four loads.

---

## 6. SIMD: True Vectorisation

**Core idea**: SIMD (Single Instruction, Multiple Data) uses CPU-specific vector registers and instruction sets (SSE2 / AVX2 / AVX-512 / NEON) to process 128-bit (4 × int32 / 8 × int16 / 16 × int8), 256-bit, or even 512-bit data in one instruction.

.NET offers them in `System.Runtime.Intrinsics`:
- `Vector128<T>`: cross-platform abstraction (automatically emits SSE2 on x86 / NEON on ARM).
- `Vector256<T>`: x86 AVX2 only (no ARM equivalent width).
- `Avx2.GatherVector256`, `Sse41.Dot`, `Fma.MultiplyAdd`: platform-specific intrinsics.

### 6.1 Vector256: Batched CRT Pixel Processing

Commit `6e7c350 perf(crt/simd): Vector256<uint> SIMD for all 3 ProcessRow*_SWAR variants`

Upgraded scalar SWAR to `Vector256<uint>`, **processing 8 pixels at a time**:

```csharp
// Before: per-pixel scalar
for (int x = 0; x < width; x++) {
    dst[x] = (src[x] & 0xFEFEFEFE) >> 1 + ...;  // decay math
}

// After: 8 pixels per iteration
for (int x = 0; x < width; x += 8)
{
    Vector256<uint> v = Avx2.LoadVector256((uint*)(src + x));
    Vector256<uint> decayed = Avx2.ShiftRightLogical(
        Avx2.And(v, Vector256.Create(0xFEFEFEFEu)), 1);
    // ... more ops
    Avx2.Store((uint*)(dst + x), result);
}
```

### 6.2 Gather: Non-Contiguous Loads

Commit `87bb1b4 perf(crt/simd): Avx2.GatherVector256 in ApplyFullFrameCurvatureAndConvergence`

CRT curvature / convergence correction requires indirect pixel reads via `map[dstIdx]`. Hardware supports `GatherVector256`:

```csharp
Vector256<int> indices = /* 8 indirect indices */;
Vector256<uint> gathered = Avx2.GatherVector256((uint*)srcPtr, indices, 4);
```

### 6.3 No Hardware Gather? Software Scalar Gather

Commit `06bef96 perf(crt/simd): replace Avx2.GatherVector256 with manual scalar gather`

Later measurements showed that on some CPUs (or cross-platform cases), hardware gather is actually **slower** than 8 scalar loads. Switched to manual gather:

```csharp
var v = Vector256.Create(
    srcPtr[i0], srcPtr[i1], srcPtr[i2], srcPtr[i3],
    srcPtr[i4], srcPtr[i5], srcPtr[i6], srcPtr[i7]);
```

Even more important on NEON — ARM has no hardware gather equivalent, so software is the only option.

### 6.4 Cross-Platform Strategy: Runtime Dispatch

Pattern used by AprNes / EnigmaBenchmark:

```csharp
if (Avx2.IsSupported)
    CrackImplAvx2();
else if (AdvSimd.IsSupported)    // ARM64 NEON
    CrackImplNeon();
else
    CrackImplScalar();
```

Detect once at startup, cache into a function pointer, call directly afterward. The static `Vector128<T>` API auto-emits NEON on ARM64, so in most cases **using Vector128 alone gives you cross-platform for free**. True dispatch is only needed for:
- Vector256 / Vector512 (AVX-only).
- Platform-specific intrinsics (Gather, Shuffle variants, FMA specifics).

### 6.5 FMA: Fused Multiply-Add

Commit `351e790 perf(Ntsc): FMA YIQ→RGB matrix + gamma curve (.NET 10 conditional)`

`Fma.MultiplyAdd(a, b, c)` performs `a * b + c` in one hardware instruction — better precision than separate `mul` + `add` (intermediate result isn't truncated), lower latency. Applies to matrix ops, convolution, filters.

```csharp
// Without FMA
Vector256<float> y = Avx.Add(Avx.Multiply(a, b), c);

// With FMA
Vector256<float> y = Fma.MultiplyAdd(a, b, c);
```

---

## 7. Integer for Float (Fixed-Point / Bresenham)

**Core idea**: float is fast, but `float → int` cast (`cvttss2si`), `fmod`, `round` are all more expensive than pure integer ops. If precision requirements are limited, fixed-point integers or Bresenham-style accumulators give a big speedup.

### 7.1 16.16 Fixed-Point Accumulator

Commit `fc8be3f perf: CRT Convergence fixed-point accumulator`

**Before:**
```csharp
float baseOffset = -halfW * step + 1024.5f;
for (int tx = 0; tx < dstW; tx++)
{
    int ioff = (int)(tx * step + baseOffset) - 1024;  // float→int cast per pixel
    // ...
}
```

**After:**
```csharp
int stepFx = (int)(step * 65536f);          // 16.16 fixed-point
int baseFx = (int)((-halfW * step + 0.5f) * 65536f);

int iFx = baseFx;
for (int tx = 0; tx < dstW; tx++)
{
    int ioff = iFx >> 16;                   // extract integer part
    // ...
    iFx += stepFx;                          // pure int add
}
```

Whole loop is integer-only, easier for JIT to vectorise, avoids `cvttss2si` latency on x86.

### 7.2 Bresenham-Style Sample Accumulator

Commit `4a6ff7d perf: APU Bresenham + NTSC mod-6 single-line merge + RfBuzz fmod removal`

APU decides at each CPU cycle whether to emit an audio sample (sample rate 44100, CPU freq 1.79 MHz). Originally used double accumulator:

**Before:**
```csharp
static double _sampleAccum  = 0.0;
static double _cycPerSample = 1789773.0 / 44100;

_sampleAccum += 1.0;
if (_sampleAccum >= _cycPerSample) {
    _sampleAccum -= _cycPerSample;
    EmitSample();
}
```

**After (pure int Bresenham):**
```csharp
static int _sampleAccum = 0;
static int _cpuFreqInt  = 1789773;

_sampleAccum += 44100;                   // per cycle: add sample_rate
if (_sampleAccum >= _cpuFreqInt) {       // threshold: CPU freq
    _sampleAccum -= _cpuFreqInt;
    EmitSample();
}
```

Mathematically equivalent to "emit a sample every `cpu/rate` cycles", but each cycle is pure int add + compare — eliminates ~5.4M FPU ops/sec.

### 7.3 `fmod` Replaced by Compare+Subtract

Same commit, AudioPlus RfBuzzPhase:

**Before:**
```csharp
phase = phase + dt;
phase = phase % 1.0f;    // ~50 cycles on x86 (microcoded fmod)
```

**After:**
```csharp
phase += dt;
if (phase >= 1.0f) phase -= 1.0f;   // 1 cycle, well-predicted branch
```

As long as `dt < 1.0` (always true here), compare+subtract is equivalent. **fmod on x86 is extremely slow** — usually always worth replacing.

### 7.4 div → mul

Commit `b667bc7 perf(audio): authMix_GetVoltage — drop dead clamp, avoid double round-trip, div→mul`

```csharp
// Before
float y = x / k;

// After — precompute 1/k
static readonly float invK = 1.0f / k;
float y = x * invK;
```

Float division on x86 is ~15–30 cycles; mul is 4–5. If divisor is constant or unchanging, precomputing the reciprocal is always worth it.

---

## 8. Loop Optimisation (Unroll, ILP, Loopless)

### 8.1 Structural Unroll

Commit `2857f35 feat(phase2b): PAL outer unroll — MasterClockTickUnrolledPAL`

NES PAL region's master clock period is 80 MC = 5 CPU cycles × 16 MC/cycle. Originally `for (int mc = 0; mc < 80; mc++)` + internal state machine. Refactored into 5 hand-unrolled gate functions, each handling one 16 MC chunk:

```csharp
// Before
while (mcCpu > 0) { MasterClockTick(); }

// Unrolled
MasterClockTickUnrolledPAL() {
    PalGate1();   // events: APU + 4 PPU-full + 3 PPU-half + NMI + IRQ
    PalGate2();   // events: APU + 3 PPU-full + 3 PPU-half + NMI + IRQ
    PalGate3();   // ...
    PalGate4();
    PalGate5();
}
```

**Wins:**
- Eliminates loop-control overhead (counter increment + compare + branch).
- Event order in each gate is fixed at compile time → JIT can inline and reorder more aggressively.
- Measured +13.1% FPS (NTSC equivalent).

**Costs:**
- Code bloat — watch I-Cache. AprNes's PAL gates total ~10–15 KB IL, still within L1 I-Cache range.

### 8.2 ILP: Instruction-Level Parallelism

Commit `ca59cb1 perf: RunWaveformLoop ILP — 4-step herringbone lookahead + xorshift chunking`

Modern CPUs (3+ integer execution pipelines) can execute independent instructions in parallel. The key is to **break dependency chains**, letting the compiler reorder.

**Before (serial dependency):**
```csharp
for (int s = 0; s < 4; s++) {
    // each s depends on previous s's hRl/hIl
    x = hRl * hC - hIl * hS;
    float t = hRl * hS + hIl * hC;
    hRl = x;
    hIl = t;
}
```

**After (4-step lookahead):**
```csharp
// Precompute rotation matrices for steps 1..4 (constant)
float c1 = hC, s1 = hS;
float c2 = c1*hC - s1*hS, s2 = s1*hC + c1*hS;
float c3 = c2*hC - s2*hS, s3 = s2*hC + c2*hS;
float c4 = c3*hC - s3*hS, s4 = s3*hC + c3*hS;

// 4 samples computed in parallel, no data dependency
float h0 = hIl;
float h1 = hRl * s1 + hIl * c1;
float h2 = hRl * s2 + hIl * c2;
float h3 = hRl * s3 + hIl * c3;
float tR = hRl * c4 - hIl * s4;
hIl = hRl * s4 + hIl * c4;
hRl = tR;
```

The 4 multiplications now run on parallel pipelines, throughput nearly linear.

### 8.3 xorshift Chunking and Reuse

Same commit: one xorshift produces 32-bit noise, **split into 4 bytes** across 4 samples:

```csharp
// Before: one full xorshift per sample
ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5;
x += (ns & 0xFF) * nScale - nOff;   // sample 0
ns ^= ns << 13; ...                  // sample 1
...

// After: one xorshift feeds 4 samples
ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5;
n0 = (ns & 0xFF) * nScale - nOff;
n1 = ((ns >>  8) & 0xFF) * nScale - nOff;
n2 = ((ns >> 16) & 0xFF) * nScale - nOff;
n3 = ((ns >> 24) & 0xFF) * nScale - nOff;
```

12 bitops/dot → 3.

### 8.4 Loopless: Eliminate the Loop Entirely

See §5.1 — SWAR OAM multiplexer. The original `for (int i = 0; i < 8; i++)` was replaced by a SWAR pipeline. The mindset shift: **from "iterate over each element" to "use a vector to process all elements in parallel"**.

---

## 9. Function Pointer Dispatch (Static Dispatch)

**Core idea**: replace per-cycle `if (mode == X) DoX(); else DoY();` branches with a function pointer (C# `delegate*` or delegate field) that's **set once and reused**. Set when the mode changes; per cycle you only pay the indirect-call cost.

### AprNes Case: APU Audio Output Dispatch

Commit `671db3e perf(apu): function-pointer dispatch for audio output (+1.9% FPS)`

**Before:**
```csharp
void apu_step() {
    // ... channel updates ...
    if (AudioMode > 0) {
        // per-cycle push to AudioPlus (per-cycle precision)
        if (expansionChannelCount > 0) {
            float gain = ap_mode01ExpGain;
            int sum = 0;
            for (int i = 0; i < expansionChannelCount; i++) { /* ... */ }
            // ...
        }
    } else {
        // Catchup: only compute at sample rate
        _sampleAccum += APU_SAMPLE_RATE;
        if (_sampleAccum >= _cpuFreqInt) { /* ... */ }
    }
}
```

**After:**
```csharp
static delegate*<void> apuOutputFn = &ApuOutputCatchup;

public static void ApuRefreshOutputFn() {
    apuOutputFn = AudioMode > 0 ? &ApuOutputPushPlus : &ApuOutputCatchup;
}

void apu_step() {
    // ... channel updates ...
    apuOutputFn();   // single indirect call
}

static void ApuOutputPushPlus() { /* ... */ }
static void ApuOutputCatchup()  { /* ... */ }
```

**Wins:**
- Removes a branch from every cycle.
- **Bonus win** (the actual big deal): `apu_step` IL shrinks from 1212 → 784 bytes (−35%), making the whole function more I-Cache-friendly. **+1.9% FPS**.

**Detail**: `ApuRefreshOutputFn()` is called at init and when `AudioMode` changes — never in the hot path.

### Other Applications of the Same Technique

- Memory dispatch: `NesCore.MEM.cs` uses `delegate*<ushort, byte>[]` indexed over `$0000-$FFFF` to route reads/writes, each mapper registers its handlers at init.
- Region (NTSC / PAL / Dendy / FDS) selection: `mcTickFn` points to the corresponding `MasterClockTickUnrolled*`.

---

## 10. Data Layout and Memory Optimisation

### 10.1 byte[] → byte* (Remove Bounds Checks)

Commit `ed4ef6e perf: ntscScanBuf byte[] → byte* + palBuf signatures to byte*`

C# array indexing bounds-checks every time. For hot-path fixed-size buffers, use `Marshal.AllocHGlobal` / `NativeMemory.AlignedAlloc` to get a `byte*` and save the bounds check:

```csharp
// Before
byte[] ntscScanBuf = new byte[width];

// After
byte* ntscScanBuf = (byte*)Marshal.AllocHGlobal(width);
```

**Risk**: loss of GC memory safety; you must manage lifecycle yourself (alloc at init, free at cleanup). Bugs become native memory corruption — expensive to debug.

### 10.2 stackalloc → static unmanaged

Commit `829b9dc perf(ntsc): replace stackalloc-per-scanline with static unmanaged buffers`

Every `stackalloc` moves the stack pointer (cheap but non-zero cost) and scope is limited to the current function. For buffers used repeatedly, switch to a **one-time-allocate, permanent** static unmanaged pointer:

```csharp
// Before
void DecodeScanline() {
    byte* temp = stackalloc byte[256];  // alloc per scanline
    // ...
}

// After
static byte* scanlineTemp;   // allocate once in initNTSC

void initNTSC() {
    if (scanlineTemp == null)
        scanlineTemp = (byte*)Marshal.AllocHGlobal(256);
}
```

Benefits: zero stack movement; buffer's L1/L2 residence is stable (prefetcher-friendlier).

### 10.3 Alignment (AlignedAlloc)

Commit `0f47dea perf(mem): NativeMemory.AlignedAlloc via conditional helpers on .NET 10`

SIMD load/store require aligned addresses (SSE: 16 bytes, AVX2: 32, AVX-512: 64). Misaligned access on most modern CPUs silently works but incurs a penalty.

```csharp
#if NET10_0_OR_GREATER
    byte* buf = (byte*)NativeMemory.AlignedAlloc((nuint)size, 32);
#else
    byte* buf = (byte*)Marshal.AllocHGlobal(size);  // no alignment guarantee
#endif
```

Cache-line alignment (64 bytes) further avoids split-line access.

### 10.4 Unmanaged Migration

Commit `9e7e494 perf(core): unmanaged memory migration + PPU $2007 SR simplify`

AprNes migrated all NES memory (RAM, VRAM, OAM, palette) to unmanaged. Reasons:

| Reason | Notes |
| --- | --- |
| **Eliminate GC pressure** | The whole emulator core allocates no managed objects → GC almost never intervenes |
| **Stable memory position** | Managed arrays may be moved by GC, forcing frequent `fixed` blocks to pin |
| **Predictable L1/L2 locality** | Stable addresses → higher prefetcher hit rate |
| **Easy to pass across methods** | Just pass `byte*` instead of `Span<T> + fixed` |

The cost is manual lifetime management and buffer-overflow risk — but for emulator cores whose hot paths run millions of times, it's worth it.

---

## 11. Redundancy Elimination (DRY / Hoisting Invariants)

Many gains come not from clever algorithms but from **simply removing redundant or unnecessary computation**.

### 11.1 DRY: Don't Compute the Same Value Twice

Commit `1eda716 perf(ppu): branchless flip LUT + sprite range hack + $2001 DRY`

PPU `$2001` mask register is read by multiple flags (`showBG`, `showSpr`, `ShowBGLeft8`, `ShowSprLeft8`). Originally each place did `(mask & 0x08) != 0` / `(mask & 0x10) != 0` independently. Now compute **once at write time**, store as bool fields:

```csharp
// Before (in hot loop)
if ((mask & 0x08) != 0 && (mask & 0x10) != 0) { /* ... */ }

// After — compute once at $2001 write
static bool showBG, showSpr, ShowBGLeft8, ShowSprLeft8;

void ppu_w_2001(byte v) {
    showBG       = (v & 0x08) != 0;
    showSpr      = (v & 0x10) != 0;
    ShowBGLeft8  = (v & 0x02) != 0;
    ShowSprLeft8 = (v & 0x04) != 0;
}
```

### 11.2 Hoist Invariants: Move Constants Out of the Loop

Commit `4a6ff7d perf: APU Bresenham + NTSC mod-6 single-line merge + RfBuzz fmod removal`

**Before:**
```csharp
for (int d = 0; d < kDots; d++) {
    float cosH = MathF.Cos(1.31683f);   // constant but computed each time
    float sinH = MathF.Sin(1.31683f);
    // ...
}
```

**After:**
```csharp
static readonly float CosHerring = MathF.Cos(1.31683f);
static readonly float SinHerring = MathF.Sin(1.31683f);

for (int d = 0; d < kDots; d++) {
    // just read statics
}
```

Compilers usually auto-hoist pure constant expressions, but **as soon as there's any perceived side-effect** (e.g. `MathF.Cos` might throw), they won't touch it. Declaring `static readonly` explicitly is safest.

### 11.3 Dead-Code Cleanup

Same commit:

```csharp
// Before
float y = Math.Max(0, Math.Min(1, x));  // clamp [0,1]
y = Math.Max(0, y);                      // clamp again! dead
return (int)Math.Round(y);
```

`Math.Max(0, y)` is redundant when y is already ≥ 0. Profile showed the method at 0.x% CPU — cheap win on deletion.

### 11.4 Double Round-Trip

Same commit's `authMix_GetVoltage`:

```csharp
// Before
float v = ComputeSomething();   // float
double d = v;                    // extend to double
// ... double computation
return (float)d;                // narrow back — waste

// After
float v = ComputeSomething();
// ... all float
return v;
```

`float ↔ double` round-trip emits `cvtss2sd` + `cvtsd2ss` on x86, ~4 cycles each. Don't widen unless precision demands it.

---

## 12. Techniques Summary Table

| Technique | Typical Gain | Representative Commit | Prerequisite |
| --- | --- | --- | --- |
| `% N` → `& mask` | 1 inst vs 10+ cycles | `06a35ac` | N is power of 2 |
| `% N` → sign-ext wrap | 3 ALU vs div | `57119fb` | Small fixed N, known range |
| Branchless Y-flip (XOR mask) | Eliminate mispredict | `7baf6a0` | Data-dependent branch |
| 256-byte LUT | 1 load vs N ALU | `7baf6a0` | Input ≤ 256 |
| LUT byte[] → byte* | Save bounds check | `ad162c7` | Index always valid |
| Magic-multiply de-Bruijn | 1 mul vs 3-level branch | `8cd97cf` | Input has strong structure (e.g. single-bit set) |
| SWAR OAM mux | Eliminate 8-iter loop | `5ad35c4` | Data packed as 8 × byte |
| SWAR lenctrHalt batch | 1 load vs 4 loads | `0cd963d` | Related fields contiguous |
| `Vector256<uint>` | 8× parallel | `6e7c350` | AVX2 available, aligned |
| `Avx2.GatherVector256` | Hardware gather | `87bb1b4` | HW supported, faster than scalar |
| Software scalar gather | Cross-platform fallback | `06bef96` | No HW gather (NEON or slow HW) |
| FMA | 1 inst vs mul+add | `351e790` | Matrices / convolution / filters |
| 16.16 fixed-point | Remove float→int cast | `fc8be3f` | Limited precision need |
| Bresenham int accumulator | Remove FPU | `4a6ff7d` | Fixed ratio |
| `fmod` → compare+subtract | Avoid microcoded fmod | `4a6ff7d` | Increment < modulus |
| div → mul(1/k) | 15-30 cycle → 4-5 cycle | `b667bc7` | k constant or invariant |
| Structural Loop Unroll | Remove loop control | `2857f35` | Iteration count fixed, small |
| ILP 4-step lookahead | Break data dependency | `ca59cb1` | Next step's matrix precomputable |
| xorshift chunking | 4 samples share 1 rand | `ca59cb1` | Precision-tolerant |
| `delegate*` dispatch | Remove hot-path branch, shrink IL | `671db3e` | Mode changes ≪ cycle freq |
| byte[] → byte* | Save bounds check | `ed4ef6e` | Fixed size, safety verified |
| stackalloc → static unmanaged | Stable cache position | `829b9dc` | Fixed buffer size |
| `NativeMemory.AlignedAlloc` | SIMD alignment | `0f47dea` | .NET 6+, SIMD path |
| DRY `$2001` flags | Avoid recomputing same value | `1eda716` | Cache-able derivation |
| Hoist cos/sin constants | Move const out of loop | `4a6ff7d` | JIT won't auto-hoist (possible side effect) |

---

## Closing Thoughts

Every technique listed here has been empirically validated on AprNes's master branch between **2026-03-15 and 2026-04-19**, totalling ~170 commits. Aggregate effect:

- NES core on Debug build went from ~106 FPS to ~120 FPS (+13%+).
- Avalonia .NET 10 + SIMD CRT pipeline stays above 60 FPS at 4× internal resolution.
- AccuracyCoin v2 138/138 + blargg 184/184 held **without regression** throughout.

Each technique in isolation might buy only a few cycles, but a hot path that's hit millions of times per second adds up:

> **Optimisation isn't finding one miracle trick — it's stacking a hundred 0.5% wins.**

Before each edit:
1. Profile first (see `JIT_ICache_Tutorial_EN.md` §11 or `profiling_workflow.md`).
2. Pick a technique, apply it, measure.
3. Confirm regression tests all pass (here: blargg 184/184 + AccuracyCoin 138/138).
4. Commit with a clear message: which technique, how much gain.
5. Return to step 1.

That's the loop this project has been running for weeks. This document is a side effect of that loop — hopefully useful to anyone doing the same.
