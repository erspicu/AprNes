# Emulator Techniques Q&A

> From JIT, DBT, and KVM to Static Recompilation, transistor-level simulation, and formal verification. A Q&A-style reference covering common technical terms, boundaries, and selection guidance for readers interested in writing their own emulators or understanding how modern emulators work internally.

This document is organised into eight major topics:

1. [Foundations: Distinguishing "Emulator JIT" from "Language JIT"](#1-foundations-distinguishing-emulator-jit-from-language-jit)
2. [JIT's Role in Emulators](#2-jits-role-in-emulators)
3. [JIT vs. DBT vs. KVM](#3-jit-vs-dbt-vs-kvm)
4. [LLVM and Other Compiler Backends](#4-llvm-and-other-compiler-backends)
5. [Static Recompilation](#5-static-recompilation)
6. [Practice-Target Recommendations for the Four Techniques](#6-practice-target-recommendations-for-the-four-techniques)
7. [High-Level Techniques in Modern Emulators](#7-high-level-techniques-in-modern-emulators)
8. [Research Directions and Formal Verification](#8-research-directions-and-formal-verification)

---

## 1. Foundations: Distinguishing "Emulator JIT" from "Language JIT"

### Q1. I've heard about JIT in .NET / Java, and JIT in emulators — are they the same thing?

**No.** Both are called JIT (Just-In-Time) and both fit the common definition of "translating some form of code into host machine code at runtime." But the **input source, semantic information, translation unit, implementation layer, and failure modes** are entirely different. Conflating the two is one of the most common misconceptions among newcomers.

The table below puts the most easily confused points side by side:

| Aspect | .NET / Java JIT | Emulator JIT (Dynarec / DBT) |
|---|---|---|
| **Translation source** | Bytecode / IL produced by a language compiler | The Guest machine's **native machine code** (already compiled to 6502 / ARM / x86 etc.) |
| **Semantic information** | Rich: classes, methods, types, variable names, control-flow graphs | Sparse: only bit patterns, register numbers, memory addresses |
| **Translation unit** | Method / function | Basic Block (one instruction up to the next jump/branch) |
| **Trigger** | Detecting "hot methods" (frequently called) and tier-up recompiling | First time a Guest PC address is encountered, translate and store in Code Cache |
| **Implementation location** | Built into the runtime (CLR / JVM does it for you) | Written by the emulator author — you emit machine code, or borrow LLVM etc. |
| **Common problems** | Codegen bugs, GC interaction, tiered recompile latency | Self-modifying code, cycle timing, indirect jumps, cache invalidation |
| **Goal** | Defer compilation for platform independence + runtime profile-guided optimisation | Run code from architecture A on architecture B at acceptable speed |

### Q2. Why do people get them confused?

The shared name is the obvious reason. The deeper reason is that **architecturally they look very similar**:

- Both have a "translate" phase
- Both have a Code Cache
- Both have an "execute the translated code" phase
- Both involve hot-path / cold-path trade-offs

But the answers to "**what is the input, what is the output, what is the translation unit?**" are completely different. Mixing the two leads to thinking that .NET concepts like Tiered Compilation, PGO, and On-Stack Replacement apply to your emulator's JIT — when in fact those are runtime-internal strategies operating on a completely different layer than your Guest→Host translation work.

### Q3. When writing an emulator in .NET / C# / Java, will the runtime's JIT do the emulator's JIT work for me?

**No.** The .NET and JVM JITs only handle the C# / Java code you wrote (CIL / Bytecode → native). The instructions executed by the Guest machine (e.g., the NES 6502 you're emulating) are, from the runtime's perspective, just numbers in a byte array. The runtime has no idea those bytes are "another ISA's machine code."

So if you want to implement an emulator JIT on top of C#, you have to **build another layer** above .NET. Common approaches:

1. **Use `System.Reflection.Emit` to generate CIL dynamically** — translate Guest instructions into CIL, then let the .NET runtime compile that CIL down to native. Pros: cross-platform, safe, no need to write x64 / ARM64 machine code yourself. Cons: the CIL middleware adds overhead, and CIL can't express certain low-level operations (like flag register computation) cleanly.
2. **mmap and emit native machine code directly** — request executable memory pages, emit x64 / ARM64 byte sequences yourself, and jump there via function pointers. Pros: highest performance ceiling, full register control. Cons: separate implementation per host, debugging is brutal, GC interaction is tricky.
3. **Use LLVM as the backend** — translate Guest instructions to LLVM IR, let LLVM handle optimisation and codegen. See [Section 4: LLVM and Other Compiler Backends](#4-llvm-and-other-compiler-backends).

### Q4. What about TieredPGO, AOT, and ReadyToRun in .NET 10? Do they relate to emulator JIT?

**Almost not at all.** Those are the .NET runtime's own compilation strategies. They affect "how fast your C# emulator host code runs," not "how your Guest→Host translation layer is built."

Two indirect effects worth noting:

- If you go the **Reflection.Emit route** (letting the .NET runtime second-pass-compile your generated CIL), then .NET's own JIT codegen quality directly determines your emulator JIT's final codegen quality. .NET 10's TieredPGO genuinely helps here.
- If you go the **direct machine-code route**, the .NET runtime is not involved in your JIT at all — you're just using .NET to host a hand-written compiler.

### Q5. Are JVM HotSpot's C1/C2 the same thing as emulator JIT?

No, but the concepts are similar. HotSpot's C1 (client) and C2 (server) are two compilers for Java methods (one fast-compile, one slow-compile-but-better-codegen). Profile counters track how often a method is called and decide when to escalate from interpreter to C1, then to C2.

This "tiered compilation" idea has analogues in emulator JITs too — Dolphin, for example, has multi-tiered JIT compilers. But the **unit is basic block, not method**; the **input is PowerPC machine code, not JVM bytecode**. **The shape is similar; the substance is not.**

### Q6. What are the differences between .NET, .NET Framework, and Java JITs internally? And how do those relate to emulator JIT?

This comparison causes the most confusion. All three have evolved over decades and their internal strategies keep changing. The crucial point: **all three are "language-IR → host machine code" JITs, while "emulator JIT" is "Guest machine code → Host machine code." These are two completely different categories of work.**

Internal differences first:

| Aspect | .NET (5+ / 6+ / 8 / 10) | .NET Framework (4.x) | Java HotSpot |
|---|---|---|---|
| **JIT compiler** | RyuJIT | RyuJIT (4.6+) / JIT64 (older) | C1 (client) + C2 (server) |
| **Intermediate code** | CIL | CIL | JVM Bytecode |
| **Tiered compilation** | ✅ (Tier 0 → Tier 1) | ❌ Not enabled by default | ✅ (Interpreter → C1 → C2) |
| **Profile-Guided Opt (PGO)** | ✅ TieredPGO (on by default since .NET 6) | ❌ | ✅ HotSpot has always profiled |
| **AOT** | ✅ ReadyToRun (R2R) / Native AOT | ❌ (NGen is half-counts) | GraalVM Native Image (third-party) |
| **On-Stack Replacement** | ✅ (.NET 7+) | ❌ | ✅ |
| **Cross-platform** | ✅ Windows / Linux / macOS / Android / iOS | ❌ Windows only | ✅ |
| **GC interaction** | Register allocation accounts for GC pause points | Same | Same |

In short:

- **.NET Framework 4.x**: Microsoft's older Windows-only runtime. JIT is RyuJIT (or older JIT64). **No Tiered Compilation, no PGO**. A method is compiled once on first call and runs that way forever. In maintenance mode, no new features.
- **.NET (from .NET 5 onward)**: the cross-platform successor (formerly .NET Core). Same RyuJIT lineage, but **gains Tiered Compilation** (fast Tier 0 first, then re-compile Tier 1 with collected profile) and **TieredPGO** (uses that profile for further optimisation). .NET 7 adds OSR (On-Stack Replacement, swapping a running method's code mid-execution).
- **Java HotSpot**: the mainstream JVM implementation. C1 is the "client compiler" (fast compile, less optimisation); C2 is the "server compiler" (slow compile, heavy optimisation). Methods are escalated based on call counts — conceptually nearly identical to .NET's Tiered Compilation, only HotSpot did it almost two decades earlier.

**Crucial clarification: none of these inter-runtime differences matter when discussing "emulator JIT."**

Why? Because all three JITs handle "language IR" (CIL / Bytecode) and emit native code that runs **the C# / Java logic you wrote**. **Guest instructions (NES, GBA, PS3) always bypass these JITs entirely** — to .NET / JVM, those Guest instructions are just numbers in a byte array; the runtime has no business translating them.

So:

- Choosing .NET 10 for your emulator does NOT mean TieredPGO will "auto-accelerate" your emulator JIT. TieredPGO only helps your C# main loop run a bit faster.
- Choosing .NET Framework 4.8 for your emulator: the lack of Tiered Compilation does NOT change your emulator JIT design — the work is identical, only the host-side C# is slightly slower.
- Choosing Java for your emulator: HotSpot's C2 will NOT magically translate Guest instructions into host machine code.

The one case where these inter-runtime differences genuinely matter: **if you go the Reflection.Emit route** (relying on the runtime to second-pass-compile your generated CIL), then the JIT codegen quality of these three runtimes directly determines the quality of your emulator JIT's final output:

| Runtime | Codegen quality of Reflection.Emit output |
|---|---|
| .NET 10 | Best (TieredPGO + 256-bit Vector + FMA) |
| .NET Framework 4.x | Medium (RyuJIT but no tiering) |
| JVM | Good (C2 has long had strong codegen) |

But **if you mmap and emit machine code directly**, all three runtimes' differences become irrelevant — you're just borrowing the runtime to host your hand-written compiler; the runtime's JIT is not involved.

**TL;DR**: ".NET / .NET Framework / Java JIT" is about how your high-level code runs; "Emulator JIT" is about how, *within* your high-level code, you translate one ISA into another ISA's host machine code. The two questions are independent; comparing them as if they were the same thing is meaningless.

---

## 2. JIT's Role in Emulators

### Q7. Does an emulator have to use JIT?

Not always. Whether to use JIT depends on the **complexity of the target architecture** and your **performance requirements**.

**Cases that don't need JIT**: 8-bit consoles (NES, Game Boy, Atari 2600). The CPU clock is only a few MHz; a pure interpreter on modern PCs runs them with massive headroom. The development focus for these systems is usually **Cycle Accuracy** and **timing synchronisation between the CPU and peripherals like PPU/APU**. Adding JIT actually makes precise timing control harder (because JIT batches multiple instructions into a basic block). Overkill for the workload.

**Cases where JIT is essential**: x86 PCs, N64, PS2, PS3, Switch — systems with complex/large ISAs or hundreds of MHz to several GHz of clock. Pure interpreters re-do fetch + decode every instruction, which alone consumes all your CPU budget. JIT caches translation results in the Code Cache; subsequent visits to the same Guest PC skip fetch/decode entirely. Performance differences on the order of 10× are typical.

Representative emulators using JIT: Dolphin (GameCube/Wii), RPCS3 (PS3), Ryujinx (Switch), Citra (3DS), PCSX2 (PS2).

### Q8. What specific bottleneck does JIT solve?

A pure interpreter loop looks like this:

```
while (running) {
    opcode = fetch(PC);    // read one Guest instruction
    decoded = decode(opcode); // disassemble the opcode
    execute(decoded);      // run a chain of if/switch dispatch
}
```

Every instruction redoes `fetch + decode`, but decode is highly repetitive in most games — the same code block can run thousands of times in a loop. JIT observes this:

1. **First time a Guest code segment is encountered**: translate the entire segment (one basic block, from some instruction to the next jump/branch) into Host native instructions and store in the Code Cache.
2. **Subsequent visits to the same Guest PC**: jump straight to the Code Cache and execute the translation, skipping fetch + decode.

For games that saturate the CPU, this can save 80–95% of decoding overhead.

### Q9. What's the most painful part of implementing emulator JIT?

In rough difficulty order:

1. **Cache invalidation**: if the Guest program modifies its own instructions at runtime (**Self-Modifying Code**, SMC), the previously-cached translation becomes stale. The emulator must rapidly detect Guest writes to "memory pages with translated code" and invalidate corresponding cache entries. SMC is common in older games (FDS dynamic loading, ROM hacks, copy-protection mechanisms all use it).
2. **Indirect jumps**: `JMP (reg)` or `RET` — the jump target is only known at runtime, so static analysis can't pre-translate. In practice you maintain a hash table from Guest PC to Host function pointers within the Code Cache.
3. **State mapping**: where do you store Guest registers? In a .NET / C struct? Or do you try to map them onto Host registers? The former is easier to write but every access goes through memory; the latter is faster but reduces portability and clashes with GC / call conventions.
4. **Cycle accuracy**: a JIT executes multiple Guest instructions per basic block, but PPU / DMA peripherals may need to advance their timing at any point in between. Either you insert cycle-accumulation and sync checks after each instruction (expensive), or you accept reduced precision (in exchange for performance).

### Q10. Can JIT and "Cycle-Accurate" coexist?

They can, but it's painful. The standard approach is to insert sync points at the end of each basic block ("we've executed N cycles") and let peripherals (PPU/APU) "catch up to this cycle" — this is the catch-up model. But this approach degrades precision for cycle-accurate edge cases (like "PPU reads register X at cycle 256"). Emulators chasing absolute precision typically don't go the JIT route.

---

## 3. JIT vs. DBT vs. KVM

### Q11. What's the difference between JIT and DBT (Dynamic Binary Translation)?

The two are often used interchangeably in emulator circles because the underlying logic is very similar. But the definitional emphasis differs:

| Aspect | JIT (Just-In-Time) | DBT (Dynamic Binary Translation) |
|---|---|---|
| **Origin** | Programming language VMs (Java VM, .NET CLR) | System emulation and binary compatibility (QEMU, Rosetta 2) |
| **Input source** | Intermediate code (Bytecode / IL) | Native machine code |
| **Goal** | Defer compilation for platform independence + runtime optimisation | Make programs from architecture A run on architecture B |
| **Semantic info** | Rich (classes, methods, types preserved) | Sparse (only registers and memory addresses) |

What emulator folks loosely call "JIT" is technically DBT — the input is the Guest machine's native machine code, not intermediate code. But "JIT" is more intuitive, so the two terms are essentially interchangeable in emulator discussions. The academic name is **Dynarec (Dynamic Recompiler)**.

### Q12. What's the standard DBT pipeline in an emulator?

1. Read machine code from Guest memory.
2. Decode the instructions into an internal IR (intermediate representation).
3. Optimise the IR (eliminate redundant flag computations, allocate registers, etc.).
4. Emit the IR as Host instructions (x64 / ARM64 byte sequence).
5. Write the result into the Code Cache; next visit to the same address jumps straight there.

### Q13. So what is KVM, and how does it differ from JIT/DBT?

**KVM (Kernel-based Virtual Machine)** is fundamentally different from JIT/DBT: it does **not translate instructions in software**. Instead, it uses the CPU's hardware virtualisation extensions (Intel VT-x / AMD-V) to **execute Guest instructions directly on the physical CPU**.

- **JIT/DBT**: translates from architecture A to architecture B in software, then executes
- **KVM**: tells the CPU "please execute this segment of architecture A code in a hardware-isolated environment"

Because of the hardware-direct execution, KVM achieves near-100% native performance. But it has **a critical constraint**:

### Q14. What is the critical constraint on KVM?

**KVM requires "same-ISA"**.

- Emulating an ARM system on an x64 PC? KVM can't help — x64 CPUs don't execute ARM instructions.
- Emulating x86 on ARM64? Also no.
- Emulating another x86 system on an x64 PC? Yes, KVM works perfectly.
- Emulating another ARM system on an ARM64 device? Also yes.

Compared with JIT/DBT:

| Aspect | JIT / DBT (software translation) | KVM (hardware acceleration) |
|---|---|---|
| **Performance** | 20–50% of native | Near 100% of native |
| **ISA requirement** | Cross-ISA capable | Same-ISA only |
| **Implementation difficulty** | Very high (you write a compiler backend) | Medium (mostly Kernel API calls) |
| **Privilege level** | User mode | Kernel mode |
| **Typical case** | mGBA, RPCS3, Dolphin | x86 Android emulator, QEMU acceleration mode |

### Q15. Why is KVM common with Android emulators or QEMU?

Two reasons:

1. **Development efficiency**: the Android Studio emulator runs an x86 build of Android. Combined with KVM on the developer's x64 PC, it runs near native speed.
2. **QEMU's elasticity**: QEMU uses TCG (a DBT technique) for cross-ISA emulation, and automatically switches to KVM for same-ISA emulation to get near-native performance.

### Q16. Could you use KVM to run GBA / NDS games on a real ARM CPU in an ARM64 environment?

Theoretically possible, but with a "technical gap."

GBA/NDS use **ARMv4T (ARM7TDMI) / ARMv5TE (ARM946E-S)**, both 32-bit ARM. Modern ARM64 (AArch64) is significantly different from the early 32-bit ARM ISA. For direct execution to work, your ARM64 CPU must support **AArch32 execution mode** (32-bit backward compatibility).

Even with ISA compatibility, you still hit several obstacles:

- **Privileged instruction limits**: GBA programs talk directly to hardware. Under KVM, when the GBA program tries to switch processor modes or access system registers, it traps. KVM hands control back to your emulator, and you must manually emulate that behaviour (**Trap and Emulate**).
- **Memory mapping**: GBA's memory layout (e.g., `0x08000000` is ROM) is completely different from a Linux process. You need KVM's stage-2 translation to rebuild it.
- **Hardware peripherals**: KVM only accelerates CPU instructions. The GBA's PPU, APU, and DMA simply don't exist on a real ARM64 CPU; you still need to emulate them in software.

The closest practical path is **QEMU + `--enable-kvm`** on ARM64 Linux targeting 32-bit ARM. With hardware support, QEMU automatically switches to KVM mode. But for cycle-accurate GBA emulation, this approach loses control over CPU timing details — generally the wrong fit.

---

## 4. LLVM and Other Compiler Backends

### Q17. Why do some emulators use LLVM as a JIT backend?

Traditional dynarecs are hand-written machine-code emitters (translating ARM instructions into x64 directly). The pain points:

- **Maintenance burden**: you must master x64, ARM64, RISC-V, and other host assembly languages.
- **Limited optimisation**: hand-written emitters struggle to match professional-compiler-grade instruction reordering, register allocation, dead code elimination.

LLVM benefits:

1. **World-class optimiser**: LLVM's PassManager does dead code elimination, constant folding, loop invariant hoisting, etc., for you.
2. **Multi-platform codegen**: you only translate Guest instructions to **LLVM IR**. LLVM automatically produces host machine code for Windows (x64), macOS (Apple Silicon), or Linux (ARM64).

### Q18. What's the standard LLVM backend pipeline?

1. **Frontend**: emulator reads Guest binary instructions.
2. **IR Generation**: translate to **LLVM IR** (an assembly-like intermediate language with rich type information).
3. **Optimisation**: invoke the LLVM PassManager.
4. **Execution (JIT)**: use LLVM's **ORC (On-Request Compilation)** or MCJIT engine to compile the IR on demand and mmap it into executable memory.

### Q19. What's the cost of LLVM?

It's not a silver bullet. For some emulators it's too heavy:

- **Compile latency**: LLVM optimisation is slow. Hitting unseen code at runtime can cause noticeable stuttering. This is why RPCS3 / Ryujinx and others do "shader cache pre-compilation."
- **Bulk**: LLVM libraries are large; emulator binaries balloon from a few MB to hundreds of MB.
- **C# integration friction**: calling LLVM's C++ API from .NET requires P/Invoke or a wrapper layer (e.g., LLVMSharp).

### Q20. Which emulators use LLVM?

- **RPCS3 (PS3)**: compiles PPU/SPU instructions into x86-64 — the key to running AAA titles smoothly.
- **Cemu (Wii U)**: also uses LLVM as a translation backend.
- **Dolphin** has experimented with LLVM as a backend.

### Q21. What are alternatives if you don't want LLVM?

- **Dynarmic**: a dynarec library purpose-built for ARM ISAs, used by Citra, yuzu, etc. Much lighter than LLVM, with fast compilation.
- **Hand-written emitters**: target a single host architecture; emit byte sequences directly. Dolphin's earlier JIT followed this path.
- **Cranelift**: the Rust ecosystem's low-latency codegen backend used by wasmtime. The emulator scene is starting to experiment with it.

---

## 5. Static Recompilation

### Q22. What is static recompilation, and how is it different from JIT?

**JIT/DBT** translates at runtime — translate as you go. **Static Recompilation** disassembles the entire ROM **before execution**, **translates all instructions to modern PC C++ / machine code**, and produces a standalone `.exe`.

Running it doesn't feel like "emulation" — it's more like **a Native Port**.

### Q23. Why bother?

- **Maximum performance**: no runtime translation overhead.
- **Unlimited optimisation potential**: compilers (GCC / Clang) have time to do deep analysis.
- **Modern features integrate naturally**: ultra-high resolutions, widescreen, even Ray Tracing.
- **No JIT permission needed**: friendly to closed platforms like iOS that ban third-party JIT — the binary is already a compiled native app.

### Q24. What are the challenges of static recompilation?

It's very hard to implement, which is why these projects are rare:

- **Code/Data ambiguity**: in a ROM, instructions and data are often interleaved. The recompiler crashes if it mistakes data for instructions.
- **Indirect jumps**: `JMP (reg)` targets are only known at runtime; statically you cannot predict all possibilities.
- **Self-modifying code**: a static `.exe` cannot adapt to runtime instruction modifications.

### Q25. Notable success stories?

- **The Legend of Zelda: Ocarina of Time (Ship of Harkinian)**: extracted the N64 source and reconstructed it in C++. Runs at 4K/60fps on PC with full mod support.
- **Super Mario 64 PC Port**: statically maps MIPS instructions to modern architectures. Runs on virtually any modern hardware.
- **N64 Recomp**: a more general toolchain. It doesn't depend on manual reverse engineering — it automatically transpiles N64 ROMs into C. The recent Majora's Mask PC port was made this way.

This technique blends "software engineering and reverse engineering" rather than pure hardware emulation. The goal is not 100% hardware accuracy, but **"giving this game its best experience on modern platforms."**

---

## 6. Practice-Target Recommendations for the Four Techniques

### Q26. If I want to practice JIT, DBT, KVM, and Static Recompilation, what should each target be?

**Practising JIT — recommended target: Game Boy Advance (GBA)**
- ARM7TDMI has clear rules and excellent documentation.
- GBA sits in the sweet spot: "interpreter works, but JIT gives a huge boost."
- Practice: basic-block recognition, Code Cache management, handling interrupts during JIT execution.
- Difficulty: ★★★☆☆

**Practising DBT — recommended target: Intel 8086 / 80286**
- x86 flag register computation is frequent — practise "Lazy Flag Evaluation."
- Few registers makes Guest→Host register mapping a deep exercise.
- Variable-length instructions (1–15 bytes) train your decoder more than fixed-length ARM.
- Difficulty: ★★★★☆

**Practising KVM — recommended target: i386 or earlier PC (real mode / protected mode support)**
- x64 host emulating x86 Guest is the only scenario that exercises KVM properly.
- Practice: Linux Kernel API (`ioctl`), virtual CPU register setup, VM Exit handling.
- When a Guest executes `OUT` (hardware port write), KVM traps and you simulate the corresponding hardware behaviour.
- Difficulty: ★★★★☆

**Practising Static Recompilation — recommended target: Chip-8 or one specific simple NES game**
- Chip-8's structure is simple; perfect for a proof-of-concept.
- For a harder challenge, pick a small NES game (e.g., *Donkey Kong*, *Super Mario Bros.*) — you'll directly face indirect jumps, code/data separation, etc.
- Difficulty: ★★★★★ (the hardest part is automating reverse analysis)

### Practice-Path Summary

| Technique | Recommended target | Core practice value |
|---|---|---|
| **JIT** | GBA | IR generation, dynamic compile pipeline |
| **DBT** | x86 16-bit | Instruction optimisation, flag synchronisation |
| **KVM** | x86 32-bit | Hypervisor APIs, hardware exception trapping |
| **Static Recompilation** | NES / Chip-8 | AOT static analysis, native code porting |

---

## 7. High-Level Techniques in Modern Emulators

### Q27. Why do almost all NDS / 3DS emulators use JIT?

These two consoles' CPU complexity and clock speeds exceed what a pure interpreter on a mid-tier PC can realistically handle.

**NDS (ARM7 + ARM9 dual-core)**: clock isn't high (67 MHz / 33 MHz), but it's dual-core with extensive hardware-interrupt synchronisation. melonDS / DeSmuME started as interpreters and later added JIT recompilers, yielding 1.5×–2× performance on PC. On Android, DraStic's signature trick — an ARM-on-ARM JIT — is what let it run at full speed on phones from over a decade ago.

**3DS (ARM11 MPCORE)**: dual-core (quad-core on New 3DS), 268–804 MHz. Citra running pure interpreter on big titles (*Pokémon*, *Zelda*) might not break 10 fps even on a high-end i9. Citra's JIT compiles ARM11 to x86-64, enabling 4K rendering.

iOS is the counter-example: Apple's App Store rules forbid third-party JIT, so 3DS emulators on the latest iPhone hardware run terribly unless the user side-loads with a method that grants JIT permission.

### Q28. What is GPU API bridging (HLE graphics)?

Older emulators used software to simulate every PPU register and scanline (**LLE — Low-Level Emulation**). Modern consoles (PS3 / Switch) draw via Vulkan / OpenGL / NVN. The emulator no longer paints each pixel by CPU but acts as a translator that **forwards drawing commands to the PC's GPU**.

Pipeline:
1. Intercept the game's draw calls (e.g., `glDrawElements` or `vkCmdDrawIndexed`).
2. Convert parameters (vertices, textures, shaders) into formats the host GPU understands.
3. Call the host-side Vulkan / D3D12 API, letting the GPU do the work.

This is **HLE (High-Level Emulation) graphics**.

### Q29. What is shader recompilation, and why do new scenes stutter?

Console GPUs (e.g., Switch's Maxwell) have their own shader machine code that PC GPUs can't run directly. Emulators must **recompile the game's shaders at runtime** into formats the host GPU supports (SPIR-V / HLSL).

The side effect is the "Compiling Shaders…" stutter when entering new areas. Cloud Shader Cache solves this — every player generates the same shaders, so download someone else's pre-compiled cache. Dolphin and Citra both support this.

### Q30. How does GPU bridging relate to CPU JIT?

**JIT/KVM handles CPU computation; GPU bridging handles GPU computation.** They're complementary. WINE / Proton's DXVK is the apex of this technique — bridging Windows DirectX commands into Linux Vulkan in real time. That's what powers Steam Deck.

### Q31. What other recent trends in emulation should be on the radar?

The frontier has moved from "make it run" to "make it run better than the original":

1. **AI texture upscaling**: textures are upscaled with ESRGAN-class models before being uploaded to the GPU, producing 4K-class output. Popular in PS2 / GameCube emulator communities.
2. **Real-time OCR translation**: RetroArch and others use OCR to capture on-screen text, send to Google/DeepL, and overlay translation back onto the screen.
3. **Rollback Netcode**: originally a fighting-game technique, now applied to legacy emulators. Predict the opponent's input; if wrong, leverage instant save-state to roll back several frames and recompute. Fightcade enables smooth global *Street Fighter* matches over high-latency connections.
4. **HLE audio reconstruction**: replaces the LLE DSP simulation by translating the Guest audio stream directly into host XAudio2 / SDL Audio commands. Solves multi-core synchronisation pressure.
5. **Cloud Shader Cache**: see Q29.
6. **FPGA emulation**: e.g., MiSTer FPGA, recreating original chip-level logic in HDL. Zero latency, perfect cycle accuracy — the "feel-of-the-original-hardware" endgame, but strictly speaking it's no longer a software emulator.

### Q32. What kinds of "enhancement hacks" exist for legacy games?

Beyond reconstruction, modern hardware can compensate for technical compromises of the original:

- **2D-to-3D rendering**: 3dSen profiles NES sprites and assigns them depth, letting you watch Mario sidewise jumping in pipes. The mechanism: intercept PPU draw calls, classify backgrounds vs. characters, project to 3D space.
- **Widescreen patches**: for PS1/N64/PS2, edit the projection matrix or FOV value in game memory at runtime. The engine renders content originally cropped at 4:3 boundaries — turning the game effectively 16:9.
- **HD texture replacement**: hash each texture; when the game requests a low-res texture, swap in the player's 4K copy from disk. The *Wind Waker* and *Monster Hunter* communities have stunning HD packs.
- **MSU-1 audio replacement (SNES)**: invents a fake hardware extension chip. The emulator intercepts 8-bit chiptune playback and reads external PCM files (CD-quality audio) instead. You can play *Chrono Trigger* or *A Link to the Past* with full orchestral BGM.

### Q33. How can these techniques be summarised in a single overview?

| Layer | Goal | Representative techniques |
|---|---|---|
| **Foundation (Core)** | Run it; run it accurately | Interpreter, Cycle Accuracy |
| **Acceleration (Speed)** | Run it smoothly; cross-platform | JIT, DBT, KVM, LLVM backend |
| **Enhancement (Enhance)** | Make it look/sound better | HD Textures, MSU-1, Widescreen Hack, AI Upscale, Rollback |
| **Native (Native)** | Leave emulation behind | Static Recompilation, Source Port |

---

## 8. Research Directions and Formal Verification

### Q34. Has anyone successfully run NES games via Visual6502-style transistor-level simulation?

**Visual6502** is a **Transistor-level Simulator**. It doesn't simulate instructions, it doesn't simulate logic gates — it simulates **the on/off state of every transistor and the physical behaviour of every wire**.

Technically achieved, but **too slow to actually play**.

The NES CPU is the Ricoh 2A03 (based on the 6502 core). The Visual6502 team developed **Visual2A03**, which simulates every wire of the NES CPU's circuit in a browser. You can load tiny programs and watch register and transistor states in motion. But running a full NES game (e.g., *Super Mario Bros.*) reduces even modern high-end PCs to a few frames per second.

The biggest challenge is the **PPU (2C02)**, whose circuit complexity vastly exceeds the CPU's and involves substantial analog signal generation (NTSC). Despite projects like PerfectPPU attempting comparable transistor scans, achieving a coupled "CPU + PPU" transistor-level simulation grows the computational pressure geometrically.

### Q35. If it's too slow to play, why does this technique matter?

It serves as the **ultimate reference manual** for emulator developers:

- **Resolves hardware mysteries**: previously emulator authors guessed at edge cases. Transistor-level simulation reveals exactly why a particular instruction at a specific time produces a particular bug.
- **Refines cycle accuracy**: many of today's cycle-accurate timing documents directly trace back to people scrutinising transistor-level behaviour.

### Q36. How was Visual2C02 produced?

Researchers dissolved the chip's surface with strong acid, photographed it under an electron microscope, and manually vectorised tens of thousands of transistors. The result fully exposed the underlying logic of NES colour generation and sprite overflow.

### Q37. How can simulation technique be classified by layer?

| Layer | Simulation unit | Speed | Use case |
|---|---|---|---|
| **Instruction-level** (JIT / Interpreter) | OpCode | Very fast | Playing games |
| **Logic-gate-level** (FPGA / HDL) | Gate / Flip-flop | Native speed | Precise hardware reproduction |
| **Transistor-level** (Visual6502) | Transistor / Wire | Very slow (Hz range) | Scientific research, deep reverse engineering |

### Q38. Are emulators a viable graduate thesis topic?

Yes. Common research directions include:

1. **DBT and performance optimisation**: how to make A-arch instructions run faster on B-arch. Many theses explore LLVM as a QEMU backend (HQEMU framework), multi-threaded DBT, software TLB, indirect branch caching, Code Cache management, etc.
2. **Full-system emulation and virtualisation**: KVM / hardware-assisted virtualisation. Stanford McKeown's High-Fidelity Emulation work, embedded systems cross-platform emulation, etc.
3. **Instruction set simulation and formal verification**: formal-methods verification of instruction decoders. RISC-V teaching simulators are also common topics.
4. **Graphics API translation**: cross-platform graphics command translation (OpenGL → Vulkan / DirectX), overlapping with modern 3DS / Switch emulator core technology.

English search keywords: `Dynamic Binary Translation (DBT)`, `JIT Compilation`, `Full System Emulator`, `Cycle-Accurate Simulation`.

### Q39. Can Lean (the proof assistant / language) be used for emulators?

Yes, but it's still in the academic stage — no mainstream emulator is written purely in Lean. Three application areas:

1. **Verified ISA simulators**: define a CPU's ISA mathematically in Lean, then prove that the simulator implementation conforms to the spec. Common in RISC-V / ARM formal models, used as "golden reference models" before chip development.
2. **Verifying JIT compiler correctness**: groups like LambdaClass use Lean 4 to develop **proven-correct optimisation engines** — proving Guest→Host translation preserves semantics. Crucial for high-security simulation environments like ZK virtual machines.
3. **Lean 4 as a foundation for high-performance emulators**: Lean 4 uses reference counting + Functional-but-In-Place techniques. Pure functional code can compile down to direct memory mutation similar to C — well-suited to register/memory simulation. Some community projects implement Chip-8 / 6502 in Lean 4.

### Q40. Where does Lean fit (or not fit)?

| Pros | Challenges |
|---|---|
| Proof guarantees that `cpu.Execute()` matches hardware spec exactly | Steep learning curve (you also need to understand Dependent Type Theory) |
| Lean 4 compiles to lean and fast C code | Ecosystem is still small; few SDL2/Qt/audio bindings |
| Powerful metaprogramming for auto-generating instruction translators | Proof-checking slows development relative to C# / Rust |

For developers chasing the strictest "instruction-behaviour fidelity," Lean offers the ultimate guarantee: **"if it compiles, it's correct."** But for mainstream game emulator developers today, Lean 4 is more of an "experimental tool" for verifying the most critical instruction-translation logic, rather than the implementation language for the whole system.

---

## Closing Thoughts

Emulator technology has evolved from simple instruction interpretation to today's multi-layered architecture: CPUs accelerated with JIT or KVM, GPUs bridged via HLE to host APIs, shaders compiled with cloud-based caching, textures upscaled with AI, and entire ROMs sometimes statically recompiled into native PC applications.

Not every layer is required. Which ones you adopt depends on your goal — a cycle-accurate academic simulator for a small console may not need JIT at all, while smooth playback of PS3/Switch AAA games on modern hardware is impossible without the JIT + HLE GPU bridge + shader cache trinity.

Writing an emulator is more than "making old games run." Each subtopic connects to bigger fields: compilers, virtualisation, formal verification, reverse engineering, computer architecture. Starting from an interpreter and gradually expanding into JIT / DBT / KVM / static recompilation is a learning path that's both technically deep and structurally rewarding.
