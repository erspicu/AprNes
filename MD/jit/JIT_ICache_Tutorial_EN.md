# C# JIT and I-Cache Optimisation Tutorial

> Starting from the Game Loop, this tutorial walks through CPU cache hierarchy, hot/cold path splitting, multi-core pipelining, thread affinity, and concludes with the actual PMU / ETW analysis workflow used in this project (AprNes NES emulator).
>
> The content was reorganised from multiple rounds of Q&A discussion into a structured tutorial. Target audience: C# developers writing games, emulators, or high-performance services who want to understand performance from the JIT behaviour and CPU micro-architecture level.

---

## Table of Contents

1. [Game Loop: Where It All Begins](#1-game-loop-where-it-all-begins)
2. [The Tug-of-War Between Inlining and I-Cache](#2-the-tug-of-war-between-inlining-and-i-cache)
3. [Finding the Optimum: Quantitative Tools and Laddered Strategy](#3-finding-the-optimum-quantitative-tools-and-laddered-strategy)
4. [Hot-Path Overflow: When Core Logic Alone Exceeds L1](#4-hot-path-overflow-when-core-logic-alone-exceeds-l1)
5. [Multi-Core Pipelining: Sharing the Work Across I-Caches](#5-multi-core-pipelining-sharing-the-work-across-i-caches)
6. [Which Cache Level Is "I-Cache" Exactly?](#6-which-cache-level-is-i-cache-exactly)
7. [The Cost of Inter-Core Communication](#7-the-cost-of-inter-core-communication)
8. [Ensuring Threads Actually Land on Different Cores in C#](#8-ensuring-threads-actually-land-on-different-cores-in-c)
9. [Do These Ideas Generalise to Other Languages?](#9-do-these-ideas-generalise-to-other-languages)
10. [Extending to High-Concurrency Web Services](#10-extending-to-high-concurrency-web-services)
11. [Field Appendix: AprNes's JIT / I-Cache Analysis Workflow](#11-field-appendix-aprness-jit--i-cache-analysis-workflow)

---

## 1. Game Loop: Where It All Begins

### Q: When writing games or emulators in C#, why does there always have to be a core loop called the "Game Loop"? What is its role?

The Game Loop is the soul of every real-time interactive program. A console app or web form is **passive** — it only reacts when the user does something. A game or emulator is **active** — the world must keep running even when the player does nothing (leaves rustle, NPCs patrol, the emulated CPU keeps advancing cycles). This requires a perpetually spinning gear.

### The Three-Step Core

```text
while (isRunning) {
    1. Process Input   ── keyboard / gamepad / mouse
    2. Update State    ── physics, AI, state-machine transitions
    3. Render          ── paint the result to the screen
}
```

Minimal skeleton:

```csharp
bool isRunning = true;
while (isRunning)
{
    var input = GetPlayerInput();
    UpdateGameLogic(input);
    DrawToScreen();
    // Optionally pace the loop here: Thread.Sleep / vsync / custom timer
}
```

### How Different Frameworks Express It

| Framework | What you write | Who provides the main loop |
| --- | --- | --- |
| **Unity** | Just `Update()` / `FixedUpdate()` / `LateUpdate()` | Engine |
| **MonoGame / XNA** | Override `Update(GameTime)` + `Draw(GameTime)` | Framework skeleton |
| **Raw C# (emulator)** | Hand-rolled `while` loop, with strict cycle accounting | You |

An emulator's loop is stricter than a game's: the number of instructions executed per second must match original hardware (e.g. NES's ~1.79 MHz CPU + PPU), otherwise visuals and audio will drift.

### A Key Concept: Delta Time

Different machines run at different speeds — a fast machine runs 200 loops/sec, a slow one 30. To keep the **perceived** movement speed consistent, multiply displacement by the delta between frames:

```text
NewPosition = CurrentPosition + Speed × DeltaTime
```

Emulators usually don't use Delta Time; they use a fixed cycle counter instead — because accuracy matters more than visual smoothness.

---

## 2. The Tug-of-War Between Inlining and I-Cache

### Q: From the C# JIT's perspective, if we aggressively inline many methods into the Game Loop, could we blow out the L1 I-Cache and cause cache misses? But frequent method calls also have overhead — how do we balance hot/cold path separation? Does a CPU with larger I-Cache (e.g. the X3D series) change the equation?

This is a classic "space-for-time" trade-off and memory-hierarchy game. It's not purely technical — it's genuinely an art of balance.

### 2.1 Costs at Both Extremes

| Choice | Main cost |
| --- | --- |
| **Method call (no inline)** | With good branch prediction, call itself is a few cycles. Real cost: it **blocks register allocation and pipeline scheduling across the call** |
| **Forced inline everywhere** | Hot path bloats; once it exceeds L1 I-Cache (~32–64 KB per core), every fetch goes to L2 or L3. Stall penalty can be **tens of times** the call cost |

Conclusion: if your Game Loop core is already large (physics + AI + render submission), forcing full inlining is **absolutely** counter-productive.

### 2.2 Hot / Cold Path Splitting

Core idea: **keep the most frequently executed instructions as compact as possible in memory**.

- **Hot path**: logic that runs every frame (coordinate updates, input dispatch). Keep small; let JIT inline aggressively.
- **Cold path**: rarely-triggered but bulky branches (error handling, initialisation, special events). Manually mark `NoInlining` so JIT places the machine code "far away".

```csharp
void GameLoop()
{
    // Hot: keep small, let JIT freely inline
    UpdatePhysics();

    if (unlikelyEvent)
        HandleComplexEvent();  // cold path
}

[MethodImpl(MethodImplOptions.NoInlining)]
void HandleComplexEvent()
{
    // hundreds of lines that rarely execute
}
```

### 2.3 Three Principles for Balancing

| Principle | Notes |
| --- | --- |
| **Small-method principle** | Keep methods small (< 16 bytes IL). JIT has a strong bias toward auto-inlining these, and their machine code fits I-Cache easily |
| **Use `AggressiveInlining` sparingly** | Reserve it for "tiny and extremely hot" methods (vector add, property getters, bit-twiddles). Overuse blows out I-Cache |
| **Prioritise D-Cache before I-Cache** | Most C# programs are bottlenecked by **D-Cache misses** caused by GC / reference types, not I-Cache. Making data contiguous (`struct` arrays, `Span<T>`) usually pays off more than inlining tweaks |

### 2.4 Hardware Variation

AMD's 3D V-Cache (e.g. Ryzen 7800X3D) stacks L3 beyond 96 MB, dramatically raising tolerance for "code bloat". Even after an L1 miss, fetching from L3 is still far faster than RAM. This means:

- **Desktop / high-end X3D**: more aggressive inlining and loop unrolling is viable.
- **Mobile / low-end CPUs**: I-Cache is precious; excessive inlining is almost always a performance killer.

Best practice is to optimise for the **lowest common denominator** deployment target, not for your own dev machine.

---

## 3. Finding the Optimum: Quantitative Tools and Laddered Strategy

### Q: Is there a systematic way to find the optimal inlining strategy? Which tools offer quantitative analysis?

Eyeballing code almost never finds the optimum. The only viable path is the **scientific method**: change, measure, compare, change again.

### 3.1 Micro-benchmarking: `BenchmarkDotNet`

The industry standard for .NET performance work. Accurate to nanoseconds, supports hardware counters.

```csharp
[HardwareCounters(
    HardwareCounter.InstructionCacheMisses,
    HardwareCounter.BranchMispredictions)]
public class GameLoopBenchmark
{
    [Benchmark] public void InlineVersion()  { /* ... */ }
    [Benchmark] public void CallVersion()    { /* ... */ }
}
```

Strengths:
- Automatic JIT warm-up (first Tier-0 + PGO pass, then Tier-1 measurements).
- Can dump JIT-produced assembly to verify `[MethodImpl]` took effect.
- Reads CPU PMU (Performance Monitoring Unit) and reports I-Cache miss counts directly.

### 3.2 System-Level Profilers

| Tool | Strengths | When to use |
| --- | --- | --- |
| **Intel VTune Profiler** | Top-Down Microarchitecture Analysis; identifies Front-End Bound (typically I-Cache); maps back to C# and JIT machine code | Strongest micro-arch analysis on Intel |
| **AMD uProf** | Per-line L1/L2/L3 hit-rate; especially useful for X3D's large cache | AMD CPUs |
| **PerfView (Microsoft)** | Ugly and steep learning curve, but the authoritative tool for .NET Runtime events (JIT / GC / ETW); free | Diagnose JIT compile events, inlining decisions, GC behaviour |

### 3.3 Laddered Optimisation Strategy

Don't start by hacking inlining. Check in this order:

```text
1. Data-Oriented Design (D-Cache first)
   ├─ struct arrays instead of class lists
   ├─ avoid boxing
   └─ reduce GC pressure

2. Keep small methods small — let JIT auto-inline
   └─ < 16 bytes IL is the sweet spot

3. Manually annotate cold paths
   ├─ throw / WriteLine / error handling → [MethodImpl(NoInlining)]
   └─ keep hot-path machine code contiguous

4. Targeted experiments
   └─ Only when Profiler points to elevated I-Cache Miss, split out previously inlined methods
```

> Donald Knuth: **"Premature optimisation is the root of all evil."**
> Preserve readability first; only intervene manually when a Profiler highlights a genuine hotspot.

---

## 4. Hot-Path Overflow: When Core Logic Alone Exceeds L1

### Q: If the hot path itself is already huge — the whole loop won't fit in L1 I-Cache no matter what — is this the hardest category of optimisation problem?

Yes. This is the **ceiling problem** of high-performance work. When core logic already exceeds L1 capacity, simple code tweaks can't save you — this is an **architectural-level refactor**.

Below are several advanced counter-strategies.

### 4.1 Split Phases: Break One Big Loop Into Multiple Passes

If a loop does A, B, C — all heavy — and their combined code exceeds L1:

- Don't let the CPU thrash I-Cache inside each iteration.
- Instead, run a loop that does only A and stashes results; run another loop that does only B; etc.

Cost: increased D-Cache traffic. Benefit: **I-Cache hit rate approaches 100%**. When I-Cache miss is pipeline-stalling the CPU, this often recovers integer-multiple speedups.

### 4.2 Instruction Alignment and Profile-Guided Optimisation (PGO)

C# has less control over code placement than C++, but JIT features still help:

- **Dynamic PGO (.NET 6/7/8+)**: JIT first runs Tier-0 collecting branch data, then recompiles hot methods with basic blocks rearranged contiguously in memory.
- **Enable**: `DOTNET_TieredPGO=1` (default on in .NET 6+).
- Especially effective for emulators, which hammer the same hotspots continuously.

### 4.3 SIMD: Fewer Instructions for the Same Work

If the loop is fundamentally vector math, introducing SIMD (`Vector128<T>` / `Vector256<T>` / `Vector512<T>`) usually compresses 100 instructions of work into 10:

- Fewer instructions → **I-Cache pressure evaporates**.
- For NES PPU background compositing, audio mixing, NTSC demodulation — pixel/sample-level work — SIMD is the most effective weight-loss drug.
- AprNes uses SWAR (SIMD Within A Register) + `Vector256<uint>` extensively across scanlines; a single commit can yield 10%+ FPS.

### 4.4 Thread Affinity

Lock the hot path to a specific CPU core:

- Avoid the OS migrating the thread (Context Switch).
- Prevent other programs from "polluting" that core's L1 I-Cache.

### 4.5 Indicator: CPI (Cycles Per Instruction)

| Symptom | Cause | Direction |
| --- | --- | --- |
| CPI high, CPU at 100% | I-Cache / D-Cache misses; CPU stalling waiting for instructions or data | Shrink code, split loop phases, prefetch data |
| CPI low, still too slow | Instructions well-optimised; pure compute load is too high | Change algorithm, go SIMD, or parallelise |

### 4.6 Summary: Deconstruct + Pipeline

When the hot path doesn't fit, the most professional approach is:

> **Don't try to do everything at once.**

Deconstruct complex logic into small modules that each individually fit in L1, then chain them like a factory assembly line. This increases memory bandwidth cost, but modern CPUs have far stronger D-Cache prefetchers than I-Cache prefetchers, so the trade-off usually pays.

---

## 5. Multi-Core Pipelining: Sharing the Work Across I-Caches

### Q: If a single core's L1 can't hold the whole hot path, can we split the logic across multiple cores — each with its own private L1 I-Cache — so the aggregate instruction capacity goes up?

This is a correct instinct. In high-performance circles this is called **core-level instruction pipelining**.

### 5.1 Factory Analogy

Think of the Game Loop as a factory:

- **Traditional**: one small workshop that keeps swapping tools (I-Cache thrashing).
- **Multi-core pipeline**: three workshops, each with its own toolset; parts (data) flow between them.

Each core only handles one segment of logic → that segment fully resides in its private L1 I-Cache → **zero-latency instruction fetch**.

### 5.2 Benefits and Risks

**Benefits:**
1. Each core's hot path fits perfectly in its private L1 I-Cache.
2. Aggregate throughput rises significantly, though single-item latency may grow due to inter-core transfer.

**Risks:**
1. Inter-core data transfer goes over L3 or Infinity Fabric / Ring Bus.
2. Cache coherence protocol (MESI) introduces synchronisation overhead.
3. If sync frequency is too high, saved I-Cache misses < added communication latency → **net negative**.

### 5.3 When Is It Worth Splitting?

Look at **compute density**:

| Compute density | Example | Recommendation |
| --- | --- | --- |
| **Low** (big logic, simple compute) | Raw memcpy, simple sums | Don't split — single core is better |
| **High** (big logic, complex compute) | NES PPU render (palette + sprite collision + background compositing), AAA physics | Split to dedicated cores |

For emulators like AprNes: pushing PPU onto its own core and coordinating with the CPU core via producer/consumer pattern is a well-known advanced technique.

### 5.4 Recommended Implementation Skeleton

1. **Thread Affinity**: pin each hot-path thread to a specific core.
2. **SPSC (Single-Producer Single-Consumer) Ring Buffer**: lock-free circular buffer, avoid `lock`.
3. **Structured data**: keep the struct passed between cores within one Cache Line (64 bytes) if possible.
4. **Avoid SMT sharing**: prefer logical cores on different physical cores (usually even-numbered).

---

## 6. Which Cache Level Is "I-Cache" Exactly?

### Q: When we talk about "I-Cache" in performance discussions, which level of CPU cache exactly? How does it differ from L2 / L3?

**It is L1 Instruction Cache.**

### 6.1 Harvard vs Von Neumann

L1 follows the **Harvard architecture** — instructions and data are **fully separated**:

- **L1 I-Cache**: only stores machine code the CPU is about to execute.
- **L1 D-Cache**: only stores data for computation (variables, objects, arrays).

L2 and beyond are mixed; no I / D distinction.

### 6.2 Cache Hierarchy Numbers

| Level | Latency | Typical capacity | Role |
| --- | --- | --- | --- |
| **L1 I-Cache** | **~1–4 cycles** | **32–64 KB** | Determines whether the Game Loop runs without stalling |
| **L1 D-Cache** | ~1–4 cycles | 32–64 KB | Home of hot data |
| **L2** | ~10–15 cycles | 256 KB – 1 MB | Mixed I + D |
| **L3** | ~40–60 cycles | 2 MB – 96 MB+ | Shared across cores |
| **RAM** | **~100–300+ cycles** | GB-scale | Slow — falling here instantly tanks performance |

### 6.3 Why Large Logic Kills Performance

When hot-path machine code exceeds 32 KB:

1. **Fill**: L1 I-Cache is already full halfway through the loop.
2. **Eviction**: CPU kicks out the first half to fetch the second half from L2 / L3.
3. **Thrashing**: next frame starts; evict second half to re-fetch first half. Back-and-forth forever.

Like a brain that can hold 10 actions at a time but is asked to execute 50 — every step requires flipping back through the manual.

### 6.4 The Essence of Multi-Core Splitting

L1 I-Cache is **private per core**. Therefore:

- Single core → one copy of 32 KB.
- Four cores → effectively four copies of 32 KB (128 KB total) to host instructions.

This is the root reason multi-core pipelining relieves I-Cache pressure.

---

## 7. The Cost of Inter-Core Communication

### Q: Even if we split the hot path across cores, we still have to account for inter-core communication overhead — what are the specific costs?

Exactly. This is the **core difficulty** of the whole trade-off. If we save "flipping manual pages" (I-Cache) by shuttling parts between cities (inter-core comms), the net may be worse.

### 7.1 Concrete Latency Numbers

| Scenario | Latency | In cycles (@ 4 GHz) |
| --- | --- | --- |
| **Same CCX / adjacent cores** (via shared L3 or Ring Bus) | **30–50 ns** | ~100–200 cycles |
| **Cross CCX / cross Die** (Ryzen asymmetric, multi-socket) | **100 ns+** | 400+ cycles |

100 cycles already lets a CPU execute several hundred instructions. If the split only saves 10 instructions of I-Cache miss cost, it's a **net loss**.

### 7.2 The Hidden Cost of MESI Protocol

Inter-core communication isn't just "sending data" — it's "state synchronisation":

1. Core A modifies a Cache Line → marked Modified.
2. Core B detects invalidation → issues request.
3. Hardware ensures both cores see consistent values.

This flows through the CPU interconnect (Infinity Fabric / Ring Bus) and produces **bus traffic**. If the loop syncs millions of times per second, this bus saturates instantly.

### 7.3 Mitigations: Batch, Lock-Free, Avoid False Sharing

#### 7.3.1 Increase Granularity

**Don't** send one pixel to the next core. **Do** let Core A finish a full scanline — or full frame — before handing off.

#### 7.3.2 Lock-Free Ring Buffer

`System.Threading.Channels` is a good start; for extreme performance, roll your own struct-based SPSC ring buffer. Avoid `lock` / `Monitor`.

#### 7.3.3 Avoid False Sharing

Two cores modify different variables that happen to sit in the same 64-byte Cache Line → CPU treats the whole line as dirty and invalidates it repeatedly.

**Fix**: use `StructLayout` + `FieldOffset` to force variables onto separate Cache Lines:

```csharp
[StructLayout(LayoutKind.Explicit, Size = 128)]
public struct PaddedCounters
{
    [FieldOffset(0)]  public long CpuCounter;  // written by Core 1
    [FieldOffset(64)] public long PpuCounter;  // written by Core 2
}
```

### 7.4 Decision Formula

```text
Net gain = reduced I-Cache miss cost − added inter-core communication latency
```

| Situation | Recommendation |
| --- | --- |
| **Small logic, frequent sync** | Single core + `NoInlining` to push cold paths out |
| **Big logic, high compute density** | Multi-core pipeline + buffered async (e.g. PPU runs half a frame ahead of CPU) |

---

## 8. Ensuring Threads Actually Land on Different Cores in C#

### Q: In C#, spawning two Threads doesn't guarantee they actually get scheduled on separate physical cores. What are the recommended practices to ensure multi-core utilisation?

The OS scheduler, pursuing energy efficiency, thermal balance, or serving background tasks, may at any moment place your two threads on the same core or bounce them between cores (Context Switch) — **instantly invalidating the I-Cache you painstakingly built up**.

Here's the SOP for "real multi-core":

### 8.1 Set Thread Affinity

The most important trick. Tell the OS: "this thread only runs on core N".

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;

[DllImport("kernel32.dll")]
static extern int GetCurrentThreadId();

public static void PinToCore(int coreIndex)
{
    int tid = GetCurrentThreadId();
    foreach (ProcessThread pt in Process.GetCurrentProcess().Threads)
    {
        if (pt.Id == tid)
        {
            // Bitmask: 1<<0 = Core 0, 1<<1 = Core 1, ...
            pt.ProcessorAffinity = (IntPtr)(1 << coreIndex);
            break;
        }
    }
}
```

**Tip**: avoid Core 0 on Windows — many system interrupt handlers live there.

### 8.2 Use `new Thread()`, Not `Task.Run` / ThreadPool

```csharp
var ppuThread = new Thread(PpuLoop)
{
    IsBackground = true,
    Priority = ThreadPriority.Highest
};
ppuThread.Start();
```

Why:
- **ThreadPool** is designed for "short, numerous" work items. It auto-resizes and recycles threads → your affinity setting gets invalidated, your I-Cache gets trashed.
- **Task / async-await** sits on top of ThreadPool; same story.

For emulators or similar programs that permanently occupy N cores, explicitly `new Thread()` and guarantee lifetime.

### 8.3 Solve False Sharing

If two cores share one object, pad any cross-core fields onto separate Cache Lines. See §7.3.3.

### 8.4 Watch Out for SMT / Hyper-Threading

`Environment.ProcessorCount` returns **logical cores**, not physical cores:

- On an 8-core/16-thread CPU, logical Core 0 and Core 1 actually **share one physical core's L1 I-Cache**.
- For extreme I-Cache utilisation, pick even-numbered logical cores (0, 2, 4, 6 …) — each maps to a different physical core.

### 8.5 Practical Monitoring: Process Lasso

**Process Lasso** shows in real time which core each thread lands on and can forcibly pin threads. Useful in development to verify `SetThreadAffinityMask` actually took effect.

### 8.6 Recommended SOP

1. Manually `new Thread()`, set `Highest` priority.
2. On entering the thread, immediately `PinToCore(n)`.
3. Use a lock-free ring buffer for inter-core transfer.
4. Pad shared data to avoid false sharing.
5. Verify placement with Process Lasso / VTune.

---

## 9. Do These Ideas Generalise to Other Languages?

### Q: Even stripped of C# and JIT context, do these optimisation ideas apply to other languages like C++ / Rust?

**Almost universally.** Because these techniques don't target a "language" — they target the **three foundations of CPU architecture**:

1. **Pipeline**
2. **Cache Hierarchy**
3. **Branch Prediction**

Whatever language you write, the CPU eventually faces these three.

### 9.1 How Much Control Each Language Offers

| Language | Control granularity | Techniques |
| --- | --- | --- |
| **C / C++ / Rust / Zig** | **Highest** | Direct control of struct alignment (`alignas` / `#[repr(C, align(64))]`), inline assembly, PGO / LTO, manual prefetch intrinsics |
| **C# / Java / Kotlin (managed)** | **Medium** | Bound by JIT and GC, but can use `StructLayout` / `ValueType` / `Span<T>` as hints |
| **Python / JavaScript** | **Low** | Run atop VM / interpreter; what you "optimise" is the interpreter's cache, not your code's |

### 9.2 Universal Issues

- **Code bloat**: C++'s excessive `inline` / template expansion blows out L1 I-Cache just the same.
- **False sharing**: same problem in Java, Go, C++. Same fix: padding / alignment.
- **PGO**: C++ (Clang `-fprofile-use`, MSVC PGO), Rust (`cargo-pgo`), .NET dynamic PGO — all the same philosophy: profile then recompile hotspots with optimal block layout.

### 9.3 Real-World Scenarios

The "multi-core pipeline + one small piece of logic per core" design is widely used in:

- **AAA game engines** (Unreal's Job System).
- **High-frequency trading (HFT)**: C++ with core pinning; even kernel bypass; FPGAs for hot-path hardening.
- **Network switches (DPDK)**: multi-core pipelines for packet parsing / filtering / forwarding at tens-of-millions-per-second rates.

### 9.4 Hardware Progress vs Software Optimisation

- Large caches (X3D) **mask** bad code — but only mask.
- Code optimised for L1 runs even faster on big-cache CPUs and frees budget for extras (better filters, finer-grained cycle accuracy).

**The "balancing act" you learn on AprNes transcends the C# frame — it's the mindset of a computing architect.**

---

## 10. Extending to High-Concurrency Web Services

### Q: Do these hardware-oriented optimisation techniques help web services under high-concurrency / high-throughput loads?

**Yes — and this is one of the dividing lines between "regular engineer" and "systems architect".**

When QPS reaches hundreds of thousands or millions, even tiny inefficiencies get amplified into disasters.

### 10.1 I-Cache and Instruction Compactness

- Beware "god functions": running thousands of lines of routing logic per request thrashes I-Cache.
- High-performance web servers (Nginx modules, Kestrel, Envoy) aggressively shrink hot-path middleware so the main loop fully fits L1 I-Cache.

### 10.2 Multi-Core Pipeline vs NIC RSS

- Modern NICs support **RSS (Receive Side Scaling)** — packets are auto-distributed to per-core queues.
- Software side: Core A parses TCP, Core B runs business logic, Core C handles DB I/O — isomorphic to the CPU/PPU split in emulators.
- Communication cost here manifests as **Context Switch**; fix is the same — Thread Affinity.

### 10.3 False Sharing: The Silent Killer of High Concurrency

Classic case: a global counter `RequestCount` hit by many threads via `Interlocked.Increment`. Thread-safe, but performance **drops** with more cores because they fight over the same Cache Line.

**Fix**: per-core counters that sum at the end; or pad the fields apart.

### 10.4 Memory Layout and GC Pressure (D-Cache)

In web services D-Cache miss is usually more lethal than I-Cache:

- `class` is a reference type, scattered across the GC Heap. Traversing a User list means random RAM accesses.
- Fix: `struct` arrays, `MemoryPool<T>`, `ArrayPool<T>` — keep data contiguous so the CPU prefetcher can pre-load the next record before the instruction asks for it.

### 10.5 Mindset Comparison

| Aspect | Typical web service | High-concurrency optimisation |
| --- | --- | --- |
| Scalability | Scale Out (add machines) | Scale Up (max out one machine) |
| Code structure | More abstraction is better | Hot path flat and compact |
| Metric | Average latency | **P99 / P99.9 tail latency** |

**Tail latency** is what I-Cache miss, GC pause, and Context Switch actually cause. Emulator developers train this muscle naturally.

---

## 11. Field Appendix: AprNes's JIT / I-Cache Analysis Workflow

This section grounds the abstract concepts in reproducible commands — the JIT and I-Cache quantification pipeline AprNes has been using.

### 11.1 Tool Overview

| Tool | Purpose | Location |
| --- | --- | --- |
| **PerfView.exe** | ETW + PMU trace collection | `temp/PerfView.exe` |
| **bench_profile.bat** | Launch AprNes with a benchmark ROM | `temp/bench_profile.bat` |
| **run_perfview.bat** | CPU sampling + JIT events collection | `temp/run_perfview.bat` |
| **run_perfview_pmu.bat** | PMU hardware counter collection (I-Cache miss etc.) | `temp/run_perfview_pmu.bat` |
| **EtlAnalyzer** (.NET 10) | Parses ETL → CPU hotspot + JIT / Inlining report | `temp/EtlAnalyzer/` |
| **PmuAnalyzer** (.NET 10) | Parses PMU events → per-method I-Cache miss rate | `temp/PmuAnalyzer/` |
| Report output | Time-stamped markdown | `MD/jit/` |

### 11.2 Regular JIT / CPU Hotspot Analysis (daily workflow)

```text
Step 1: Build target
  powershell -NoProfile -Command "& 'C:\Program Files\Microsoft Visual Studio\
    2022\Community\MSBuild\Current\Bin\MSBuild.exe' AprNes.csproj /p:Configuration=Debug ..."

Step 2: Start trace collection
  cmd //C "temp\run_perfview.bat"
  → produces temp/aprnes_jit.etl (~18 MB, CPU sampling + JIT/Inlining events)

Step 3: Parse
  dotnet run --project temp/EtlAnalyzer -c Release
  → produces temp/profile_report.txt

Step 4: Archive
  cp temp/profile_report.txt MD/jit/<YYYYMMDD_HHMMSS>_<topic>.md
```

EtlAnalyzer report content:

1. **CPU Sampling — Exclusive**: per-method self-time percentage
2. **CPU Sampling — Inclusive**: per-method CPU time including callees
3. **NesCore-only Exclusive**: emulator-core-only view, plus aggregated NesCore total
4. **JIT Compilation**: all JIT-compiled methods + IL size
5. **Inlining**: successful / failed inline attempts with reasons
6. **Hot Path Inline Status**: cross-analysis — are hot methods actually inlined?

### 11.3 PMU Hardware Counters: See I-Cache Miss Directly

PerfView supports PMU hardware counters via `/CpuCounters`. On AMD Ryzen 7 3700X (Zen 2), available counters include:

| ID | Counter | Meaning |
| --- | --- | --- |
| 0 | `Timer` | Traditional clock-based sampling |
| 9 | `IcacheMisses` | **L1 I-Cache miss count** |
| 19 | `TotalCycles` | Total cycle count |
| 20 | `IcacheIssues` | **L1 I-Cache fetch count** (denominator) |

PMU hardware only has 4–6 programmable slots (Zen 2: 4), so at most 4 counters at once.

```text
cmd //C "temp\run_perfview_pmu.bat"    # ~30 sec, ~3M samples
dotnet run --project temp/PmuAnalyzer -c Release
→ produces temp/pmu_report.txt
```

PmuAnalyzer reads `PMCSample` events from the ETL, groups by JIT'd method name, and outputs per-method miss rate (miss / fetch).

### 11.4 Reading the Indicator: Health Thresholds

| Global I-Cache Miss Rate | Status | Meaning |
| --- | --- | --- |
| **< 1%** | excellent | Working set sits comfortably in L1 |
| **1–3%** | healthy | Minor eviction, L2 absorbs cost |
| **3–10%** | concerning | Significant L2 traffic |
| **> 10%** | bad | Observable stall-related FPS loss |

### 11.5 AprNes Real Measurements (2026-04-14, master @ 47f7876)

- Global L1 I-Cache miss rate: **0.52%** (3,143 misses / 603,569 fetches)
- Hot methods miss rate:

| Method | Miss % |
| --- | --- |
| `ppu_step_new` | 0.31% |
| `Run_NTSC` | 0.36% |
| `PpuPhase4_SpriteEvalAndInit` | 0.36% |
| `apu_step` | 0.47% |
| `Crt_Render` (CRT pipeline lambda) | 0.93% |
| `Curvature+Convergence` (lambda) | 1.28% |
| `DemodulateRow_Core` | **1.43%** (largest IL in the pipeline) |

Conclusion: emulation core is firmly in the excellent tier; CRT pipeline is elevated but still healthy. Proves the AprNes core (~20 KB machine code) fits comfortably in Zen 2's 32 KB L1 I-Cache.

### 11.6 Why Static Estimates Often Overshoot

Earlier static analysis (IL × 4 heuristic) suggested hot working set was ~47 KB — apparently already exceeding L1; real measurement shows only 0.52% miss. Reasons:

1. **Narrow execution window**: within any 12-MC window only 2–3 methods are actively executing (`Run_NTSC` + one of `ppu_step_new` / `apu_step` / etc.). Actual concurrent-in-L1 code is much smaller than the total.
2. **Strong branch locality**: branches are highly predictable, hot path effectively runs the same basic-block trace repeatedly; even with many methods, the actually-fetched instruction footprint is small.
3. **Prefetcher**: modern CPUs have strong prefetch on sequential instruction streams, effectively "hiding" some miss cost.

This reinforces §3's laddered principle — **measurement always trumps estimation**.

### 11.7 Analysis Workflow Summary

```text
┌─────────────────────┐
│ 1. Plan hot-path    │
│    edit             │
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 2. Baseline trace   │  ← run_perfview.bat + run_perfview_pmu.bat
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 3. Parse report     │  ← EtlAnalyzer + PmuAnalyzer
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 4. Edit code        │
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 5. Re-capture trace │
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 6. Diff two reports │
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 7. Archive MD/jit/  │
└─────────────────────┘
```

Every iteration must complete the cycle before you can tell whether a change is "real optimisation" or "coincidental speedup".

### 11.8 Pairing With the 3-Run Benchmark Protocol

For FPS measurements we use the project-wide **3-run protocol**:

1. **Run 1**: JIT warm-up, **discarded** (.NET TieredPGO runs Tier-0 and gathers PGO)
2. sleep 60 (let CPU cool, avoid thermal throttling)
3. **Run 2**: measured (now on Tier-1 optimised code)
4. sleep 60
5. **Run 3**: measured
6. Average of Runs 2 & 3

PMU analysis should also be collected **after warm-up**, otherwise Tier-0 compile overhead gets folded into the "steady state".

---

## Appendix: Quick Reference

```csharp
// Force no inline (cold path)
[MethodImpl(MethodImplOptions.NoInlining)]
void ColdHandler() { /* ... */ }

// Aggressive inline (tiny hot method)
[MethodImpl(MethodImplOptions.AggressiveInlining)]
static int FastAdd(int a, int b) => a + b;

// Avoid false sharing
[StructLayout(LayoutKind.Explicit, Size = 128)]
public struct PerCoreState
{
    [FieldOffset(0)]  public long CpuCounter;
    [FieldOffset(64)] public long PpuCounter;
}

// Pin thread to core
[DllImport("kernel32.dll")] static extern int GetCurrentThreadId();
foreach (ProcessThread pt in Process.GetCurrentProcess().Threads)
    if (pt.Id == GetCurrentThreadId())
        pt.ProcessorAffinity = (IntPtr)(1 << coreIndex);

// Environment variables: enable dynamic PGO
//   set DOTNET_TieredPGO=1
//   set DOTNET_TC_QuickJitForLoops=1
```

```bat
REM Standard PerfView trace
temp\run_perfview.bat

REM PerfView PMU trace (must run as Administrator)
temp\run_perfview_pmu.bat

REM Parse
dotnet run --project temp\EtlAnalyzer -c Release
dotnet run --project temp\PmuAnalyzer -c Release
```

---

## Closing Thoughts

Starting from the Game Loop, we've walked through inlining strategy, hot/cold path splitting, I-Cache topology, multi-core pipelining, thread affinity, false sharing, all the way to web-service tail latency.

The heart of all these ideas is just one sentence:

> **Keep the most frequently executed instructions and most frequently used data as close, as contiguous, and as uninterrupted as possible.**

When to inline, when to split across cores, when hardware sync is worth it — these are all concrete expressions of that same sentence in different contexts.

Internalise that mindset and you'll spot performance bottlenecks at a glance whether you're writing a C# emulator, a Rust blockchain node, a C++ autonomous-driving controller, or a Go API gateway. And the AprNes PMU / EtlAnalyzer pipeline is the concrete engineering embodiment of making that mindset repeatable.
