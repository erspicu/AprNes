# AprNes — Community Introduction Posts

> Draft posts for different communities. Pick the version that fits best.
> This file is local only — not committed to the repo.

---

## Version A — NESdev Forum (deep technical)

**Title:**
`AprNes: C# NES emulator achieves blargg 174/174 + AccuracyCoin 136/136 perfect score — built with AI collaboration`

---

Hi NESdev community,

I'd like to share **AprNes**, a C# NES emulator I started in 2015, put on hiatus in 2017, and recently revived in 2026 with a very different development approach: using **Claude AI as a co-developer** to research hardware behavior, cross-reference Mesen2/TriCNES source, analyze test ROM failures, and design fix strategies.

The results have been better than I expected. Here's where things stand:

### Accuracy

- ✅ **blargg 174/174 PASS** — cpu_interrupts_v2, instr_test, ppu_vbl_nmi, apu_test, dmc_dma_during_read4, sprite_hit_tests, sprite_overflow_tests, mmc3_irq_tests, mmc3_test, mmc3_test_2, and more
- ✅ **AccuracyCoin 136/136 PASS** — perfect score, including the notoriously tricky DMA timing tests, per-dot sprite evaluation, PPU rendering enable delay, and BGSerialIn

### What made AccuracyCoin hard

The DMA section was the biggest challenge. Several edge cases needed to be correct simultaneously:

- **OAM DMA start parity**: countdown is `(apucycle & 1) != 0 ? 2 : 3` — parity-dependent
- **DMC phantom reads**: the last CPU bus address (`cpuBusAddr`) is re-read during DMC DMA
- **Implicit DMC abort**: certain conditions silently abort an in-flight DMA
- **Bus conflict handling**: OAM DMA and DMC DMA interacting at specific cycle boundaries

All of these are verified by AccuracyCoin's P13/P14 DMA test groups.

For the PPU side:
- Per-dot sprite evaluation FSM (dots 1–64: clear secondary OAM with `$FF`; dots 65–256: evaluate; correct `oamCopyBuffer` returned during rendering)
- Delayed rendering enable: 1-dot delay from `$2001` write, matching hardware behavior
- Correct VBL/NMI timing with 1-cycle delay model: rising edge → `nmi_delay` → next tick promotes to `nmi_pending`

### Architecture

- Single `partial class NesCore` — all state is `static`, all memory is raw `byte*` via `Marshal.AllocHGlobal`, no GC
- 65536-entry `mem_read_fun[]`/`mem_write_fun[]` managed delegate arrays, O(1) dispatch
- Tick model: every `Mem_r`/`Mem_w` calls `tick()` → 3 PPU dots + 1 APU step — no separate clock scheduler
- CPU opcode dispatch via `delegate*<void>[]` unsafe function pointer table (.NET 4.8, C# 9)
- MMC3 IRQ via A12 rising-edge with a 16-dot low filter

### Performance (Debug JIT, Mega Man 5 headless benchmark)

181 FPS → 248 FPS (**+36.5%**) across 14 optimizations. Notable findings:

| Optimization | Delta |
|---|---|
| `switch(opcode)` → `Action[]` dispatch table | +7.6% |
| Remove redundant inner-loop bounds check (unlocks JIT unroll) | +6.1% |
| 5-flag early-exit guard in `ProcessPendingDma()` | +5.1% |
| `Action[]` → `delegate*<void>[]` function pointer | +1.08% |
| RAM read fast-path ($0000–$1FFF) | +2.8% |
| `[AggressiveInlining]` on small PPU sprite eval methods | +2.2% |

Also documented 12 **failed** optimizations with root-cause analysis — turns out manual loop unrolling, merging rarely-taken `if` branches, and adding cache fields all regress under Debug JIT due to I-cache pressure and cache line layout effects.

Full technical write-up: [`Performance/optimization_study.md`](https://github.com/erspicu/AprNes/blob/master/Performance/optimization_study.md)

### AI collaboration model

The rule I followed: **no compensation hacks**. If correct hardware behavior caused a regression elsewhere, find what else was wrong and fix that — don't patch over correct behavior. The AI turned out to be quite good at maintaining this discipline across long debugging sessions.

### Links

- **GitHub**: https://github.com/erspicu/AprNes
- **Test reports** (HTML with screenshots): in `report/` directory
- **Website**: *(see repo for link)*
- **Latest release**: 2026.03.14

Happy to discuss any implementation details — DMA timing, sprite eval FSM, MMC3 A12 filter, or the Debug JIT optimization findings.

---

---

## Version B — Reddit r/EmuDev

**Title:**
`AprNes (C# NES emulator) — 10-year-old abandoned project revived with AI assistance, now passing blargg 174/174 + AccuracyCoin 136/136`

---

Hey r/EmuDev,

Sharing **AprNes**, my C# NES emulator and a bit of a strange development story.

I started it in 2015, got it to MMC3 support by 2017, then abandoned it with a comment in the source that said "I will be back." Nine years later I actually came back — this time using **Claude AI as a co-developer**.

The results: blargg 174/174, AccuracyCoin 136/136 perfect score, and a documented +36.5% performance improvement under Debug JIT.

---

### The AI collaboration model

I didn't just use AI for code generation. The workflow was:

1. AI reads NESdev Wiki, Mesen2/TriCNES source, and test ROM failure CRCs
2. AI proposes a hypothesis and fix strategy
3. I review, approve, and run the test suite
4. If regression → analyze together, fix the root cause (never patch over correct behavior)

The strict rule: **no compensation hacks**. No special-casing to make a test pass if it means the underlying hardware behavior is wrong.

---

### What was hardest

**DMA timing** (AccuracyCoin P13/P14):

The key insight was that Load DMA start parity is `(apucycle & 1) != 0 ? 2 : 3` — not a fixed value. Getting `double_2007_read`, `count_errors`, `count_errors_fast`, and the implicit abort behavior all correct simultaneously took several iterations.

**PPU sprite evaluation** (AccuracyCoin SprSL0):

The secondary OAM clear during dots 1–64 must write `$FF` (not `$00`) into `oamCopyBuffer` on each write cycle, and `$2004` reads during rendering (dots 1–256) must return `oamCopyBuffer` — not the actual secondary OAM data. Getting this right required implementing the full per-dot FSM rather than a simplified model.

---

### Performance research (unexpected highlight)

I was curious how far you could push a C# emulator under Debug JIT (no Release optimizations), so I benchmarked 26 optimization ideas:

**What worked:**
- Dispatch table (`switch` → `delegate*<void>[]`): **+8.7%** total
- Removing a dead bounds check in an 8-pixel inner loop: **+6.1%** (JIT could finally unroll)
- Early-exit guard on hot function: **+5.1%**

**What failed and why:**
- Manual 8-iteration loop unrolling: **-1.8%** (method size increase → I-cache pressure)
- `[AggressiveInlining]` on 35-line method: **-1.5%** (same reason)
- Adding a cached bool field: **-2.2%** (new field on different cache line than existing hot fields)
- `delegate*` for mapper methods (instance → static wrapper): **-4.8%** (extra call hop per ROM fetch, highest-frequency path)

Full write-up with root-cause analysis for each: [`Performance/optimization_study.md`](https://github.com/erspicu/AprNes/blob/master/Performance/optimization_study.md)

---

### Links

- **GitHub**: https://github.com/erspicu/AprNes
- **Test reports** (HTML): `report/` directory in the repo
- **Download**: 2026.03.14 release
- **Support**: https://buymeacoffee.com/baxermux

---

---

## Version C — Hacker News (Show HN)

**Title:**
`Show HN: AprNes – C# NES emulator rebuilt with AI co-development, passes blargg 174/174 + AccuracyCoin 136/136`

---

**AprNes** is a C# NES emulator I started in 2015, abandoned in 2017, and revived in 2026 using Claude AI as a co-developer.

**What it passes:**
- blargg 174/174 (cpu_interrupts, instr_test, ppu_vbl_nmi, apu_test, DMC DMA, MMC3 IRQ)
- AccuracyCoin 136/136 — a comprehensive accuracy suite covering DMA timing edge cases, per-dot sprite evaluation, PPU rendering sequencing, and more

**Architecture notes:**
- Single large `partial class NesCore`, all state `static`, raw `byte*` pointers via `Marshal.AllocHGlobal`
- 65536-entry function pointer arrays for memory dispatch (no address-range switch at runtime)
- CPU opcode dispatch via `delegate*<void>[]` (C# 9 unmanaged function pointers)
- Tick model: `Mem_r`/`Mem_w` → `tick()` → 3 PPU dots + 1 APU step

**On the AI collaboration:**
The workflow was iterative: AI researches hardware docs and reference emulator source → proposes fix strategy → I review and run tests → analyze regressions together. One strict rule: no compensation hacks — correct hardware behavior, even if it causes short-term regressions elsewhere.

**Performance:** 181 → 248 FPS (+36.5%) under Debug JIT through 14 targeted optimizations. Also documented 12 failed attempts, including why `delegate*` made things *worse* for mapper-dispatched reads (instance method → static wrapper adds a call hop that costs more than the dispatch savings).

GitHub: https://github.com/erspicu/AprNes
Full performance analysis: `Performance/optimization_study.md` in the repo

---

---

## Version D — Twitter / X (thread format)

**Tweet 1 (hook):**
```
🎮 I abandoned my C# NES emulator in 2017 with a comment that said "I will be back."

Nine years later I actually came back — this time with AI as a co-developer.

Here's what happened 🧵
```

**Tweet 2 (results):**
```
The results were better than expected:

✅ blargg 174/174 PASS
✅ AccuracyCoin 136/136 PASS (perfect score — rarely achieved)
✅ 5-channel APU audio (built from scratch, no libs)
✅ +36.5% performance improvement

All correctness verified after every change.
```

**Tweet 3 (hardest part):**
```
Hardest part: DMA timing.

OAM DMA + DMC DMA have dozens of edge cases.
Phantom reads. Implicit aborts. Cycle-parity-dependent start delays.

The key breakthrough: Load DMA countdown = (apucycle & 1) != 0 ? 2 : 3

Getting all of these right simultaneously took weeks.
```

**Tweet 4 (performance finding):**
```
Most surprising finding from the +36.5% perf work:

Removing a single redundant bounds check inside an 8-pixel render loop: +6.1%

The check was dead code. But its presence stopped the JIT from unrolling the loop.

One line → significant speedup.
```

**Tweet 5 (AI model):**
```
The AI collaboration rule that kept things on track:

No compensation hacks.

If correct hardware behavior causes a test regression, find what ELSE is wrong and fix that.

Never patch over correct behavior to make a number green.
```

**Tweet 6 (links):**
```
🔗 GitHub: https://github.com/erspicu/AprNes
📊 Test reports: in repo /report/ directory
📖 Performance study: Performance/optimization_study.md

If you're working on an emulator or curious about AI-assisted development, happy to chat.

☕ https://buymeacoffee.com/baxermux
```

---

---

## Posting Reference

| Community | Version | Key angle | Best time |
|-----------|---------|-----------|-----------|
| NESdev Forum | A | Deep technical, hardware accuracy | Weekday morning (US Pacific) |
| Reddit r/EmuDev | B | Developer story + Debug JIT research | Weekend |
| Hacker News (Show HN) | C | Technical concise, AI angle | Monday–Wednesday morning (US) |
| Twitter / X | D | Thread format, visual hooks | Weekday noon (US Pacific) |

**Always include:**
- GitHub: https://github.com/erspicu/AprNes
- Buy Me a Coffee: https://buymeacoffee.com/baxermux
