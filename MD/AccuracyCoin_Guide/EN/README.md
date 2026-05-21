# AccuracyCoin Guide — Conquering the Full AccuracyCoin Suite

> Audience: anyone trying to make their own NES emulator pass the entire [AccuracyCoin](https://github.com/100thCoin/AccuracyCoin) suite.
> Built around AprNes's actual journey to a perfect score — for each page of tests: what hardware behavior it checks, why it's hard, and how to implement it correctly.
>
> 🇹🇼 中文版見 [`../ZH/`](../ZH/README.md)。

---

## What this guide is

AccuracyCoin is the most hardcore NES test ROM out there — a single NROM cartridge packing **139 accuracy tests** (`20260521` build), covering sub-cycle behavior of the CPU / PPU / APU / DMA / buses. Many of these tests simply cannot pass on a typical "frame-level" or "scanline-level" emulator; you need **dot-level / master-clock-level** precision.

AprNes hit a lot of walls on its way from zero to a **139/139 perfect score**. This guide turns those walls into a teachable walkthrough — not a raw bug log (those live in [`MD/bugfix/`](../../bugfix/)).

**Current baseline**: AccuracyCoin `20260521` = **139/139 PASS** (blargg 184/184, no regression).

---

## First, the honest part: the cost of conquering AC

Before you commit to chasing a perfect AC score, this guide owes you an honest accounting of the cost. Getting AprNes from start to 139/139 took roughly:

- **Time / effort**: ~3 months and **57+ separate bugfixes** (`MD/bugfix/`, from `BUGFIX1` (2026-02-19) to dual data-bus (2026-05-22)). Almost every one required reading the NESdev wiki / hardware docs and cross-checking TriCNES *before* writing a line — research time vastly outweighed coding time.
- **Performance**: passing the later tests requires **sub-cycle / per-master-clock** precision — a finite state machine covering every sub-cycle of CPU/PPU/APU/DMA, and it is **expensive**. On .NET Framework 4.8.1, the analog pipeline (Ultra NTSC + CRT) at 6×/8× scale dipped below 60 FPS — which directly forced the whole **.NET 10 migration** (aprnesava), where TieredPGO / OSR claw the overhead back.
- **Complexity**: a high-precision timing model is far less readable than an ordinary emulator; a seemingly unrelated change can ripple through many places.
- **Diminishing returns**: the last few percent of tests model obscure edge cases that **almost no commercial game ever hits** — internal/external open bus, DMA explicit/implicit abort, stale sprite shift registers, the source of `$4015` bit5… The effort is wildly out of proportion to "how many more games run."
- **Regression risk (compensation chains)**: every fix can break another. The project's iron rule, "**no compensating hacks**," exists precisely because of this — never add a hack / tweak a parameter to bypass correct behavior, or you build conflicting compensation chains that never converge. (The recent dual data-bus change is a live example: fixing P20 regressed P14.)

> **The blunt truth**: if your goal is just "run commercial games," **scanline-level** precision is genuinely enough and the ROI is far higher. A perfect AC score is a **research-grade / hardware-archaeology** goal — about understanding real hardware down to the dot/cycle, not about playing games. Decide before you set out.

---

## Why we ultimately "wholesale replaced" the timing model

This is the single most important turning point of the whole effort, worth its own section because it can save you the detour we took.

**Starting point (coarse model)**: originally the CPU executed at the "instruction level" — it ran all cycles of an instruction at once, so DMA could only be inserted at **instruction boundaries**. PPU timing was also coarse. This model plus incremental patches (`BUGFIX1`–`BUGFIX49`) got us a fair way, but quickly hit a wall: DMA stolen-cycle timing didn't line up, and a whole class of tests (DMC DMA, IFlagLatency…) wouldn't pass no matter how we tweaked.

**Turning point 1 — per-cycle CPU rewrite** (`BUGFIX50`, 2026-03-10, commit `533d1d4`): the CPU went from "run a whole instruction at once" to "**step one cycle at a time**," so DMA could be inserted at any read-cycle boundary. AccuracyCoin 122 → 126. This was the first "replace the model, don't patch it."

**Turning point 2 — wholesale port of the TriCNES timing model**: even after reaching 136/136, the **actual on-screen image** still had PPU rendering inaccuracies (`scanline-a1`, `colorwin_ntsc.nes`). Tracing it down, the root cause was **insufficient PPU timing precision**, and it was "unpatchable" on the old architecture — every further patch just stacked compensation on a wrong foundation. So we decided to **replace it wholesale**: port the TriCNES **per-master-clock execution model + fine-grained PPU sub-cycle state machine** (an equivalent reimplementation, not copied code). The cost was the performance problem above, which in turn forced the .NET 10 migration.

> **The lesson (the one thing this guide most wants to convey)**: past a certain accuracy threshold, the cumulative cost of "keep patching a coarse model" **exceeds** the cost of "just switch to a correct timing model." Recognizing early that the foundation is wrong and rewriting decisively is far cheaper than a long patch-then-regress cycle. We learned this the long way around — you can start from a correct timing model directly.
>
> Background (timing-model tiers, catch-up vs global tick): see the existing long-form pieces in [`MD/techbook/`](../../techbook/).

---

## Chapter layout (systematic, by subsystem — NOT by git time)

> Deliberately **not in git chronological order** (too jumpy and chaotic). Organized by **subsystem**, each part covering the hardware behavior of its related tests in one place.
> Every chapter follows the same skeleton: **① what the test checks (page / error code) → ② real hardware behavior → ③ the pitfalls we hit → ④ how to fix it (with key code + comments) → ⑤ commit / file:line references**.
> **All chapters complete ✅** (2026-05-22).

### Part 0: Methodology & full record
- ✅ [`00_fix_history.md`](00_fix_history.md) — **fix chronicle (2026-02 → 05)**: the complete front-to-back git timeline, in 4 phases (cycle-accurate foundation → AC frontal assault → per-cycle CPU model switch → PPU alignment to TriCNES → dual data-bus), each entry with commit, problem, fix, and PASS counts. **Read this first if you want "how the whole thing unfolded."**
- ✅ [`00_methodology.md`](00_methodology.md) — running AC, reading error codes, the debug menu, page-by-page headless testing, using TriCNES as a reference, and fix discipline.
- ✅ [`00_timing_model.md`](00_timing_model.md) — why per-master-clock / dot-level timing is needed; the three-generation model evolution, the master-clock main loop, VBL/NMI 1-cycle delay, catch-up vs global tick.

### Part 1: ✅ [CPU (Page 1, 10–12, 20)](01_cpu.md)
- open bus (the data-bus model), dummy read/write cycles, decimal flag, B flag.
- unofficial opcodes (including SH\*'s ignoreH), interrupt timing (penultimate-cycle sampling, no NMI polling during the interrupt sequence).
- **internal vs external data bus** (`$4015` bit5) → [dual data-bus fix](../../bugfix/2026-05-22_AC_InternalDataBus_DualDataBus.md).

### Part 2: ✅ [DMA (Page 13)](02_dma.md)
- OAM / DMC DMA, GET/PUT parity, bus conflicts when DMA hits a register (including `$4015` external bus).
- Explicit / Implicit DMA abort (the final two steps that clinched v1 136/136).

### Part 3: ✅ [APU (Page 14)](03_apu.md)
- Length counter/table, frame counter IRQ (deferred clear / inhibit means "happens then is retracted").
- DMC enable delay, APU register activation, controller strobing/clocking.

### Part 4: ✅ [PPU (Page 16–19)](04_ppu.md)
- VBlank/NMI 1-cycle delay, `$2002` flag stagger (M2 duty), `$2006`/`$2005` delayed t→v copy.
- read buffer, palette quirks, sprite eval FSM, sprite 0 hit, OAM corruption, shift-register freeze.

### Appendices
- ✅ [`appendix_error_code_index.md`](appendix_error_code_index.md) — 20-page / error-code quick index + the trickiest signature tests.
- ✅ [`appendix_tricnes_reference.md`](appendix_tricnes_reference.md) — using TriCNES as ground truth (align semantics, not numbers) + its known failures + mapper coverage.

---

## Relationship to other directories

| Directory | Contents | Relation to this guide |
|-----------|----------|------------------------|
| [`MD/bugfix/`](../../bugfix/) | Per-bug fix records (with root causes) | This guide's "raw material"; chapters cite it |
| [`MD/notes/AccuracyCoin_TODO.md`](../../notes/AccuracyCoin_TODO.md) | Per-page pass status tracking | Progress board |
| [`MD/notes/AccuracyCoin_20260521_diff_and_result.md`](../../notes/AccuracyCoin_20260521_diff_and_result.md) | ROM version diff | Version history |
| [`MD/techbook/`](../../techbook/) | General NES-emulator long-form articles | Background on timing model / catch-up |
| `ref/TriCNES-main-*/` | The AC author's own emulator (perfect-score reference) | Ground-truth comparison |

---

## Writing principles

1. **Hardware behavior is the spine**, not "tweak parameters to pass a test." Each chapter explains how the real hardware works first, then how the test checks it, then how we implement it.
2. **Self-contained**: each chapter opens by stating which page / error codes it maps to.
3. **Cite real commits and file:line** so readers can jump straight into the AprNes source to compare.
4. **Chinese-first** ([`../ZH/`](../ZH/README.md)); this is the English translation. Technical terms kept in English on both sides.
