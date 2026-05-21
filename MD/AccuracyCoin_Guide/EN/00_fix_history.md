# AccuracyCoin fix chronicle (2026-02 → 2026-05)

> This is the **complete timeline** of AprNes conquering AccuracyCoin, organized front-to-back from the git log and `MD/bugfix/`.
> The teaching chapters are organized "by subsystem"; this one is the "by time" full record, so you can see the cause-and-effect of the whole evolution.
> The `PASS` number after each commit: early ones are **blargg** (174 ROMs); from March on they're **AC** (AccuracyCoin).

## Three test-version eras (get the numbers straight first)

| Era | Tests | Reached | Key |
|-----|-------|---------|-----|
| AC v1 | 136 | **136/136** on 2026-03-14 | per-cycle CPU + full DMC/DMA timing |
| AC v2 | 138 | **138/138** after the TriCNES PPU core port | $2005/$2001 TriCNES model, SR latch |
| AC 20260521 | 139 | **139/139** on 2026-05-22 | internal/external data-bus split |

---

## Phase 0 — Foundation & cycle-accurate PPU (2026-02-19 ~ 02-22)

This phase was mostly about pushing **blargg to 174** while laying the cycle-accurate foundation. Without precise PPU/CPU timing, AC is a non-starter.

| commit | date | problem → fix | blargg |
|--------|------|---------------|--------|
| `24687f0` `be3f979` | 02-19 | PPU changed from coarse to **cycle-accurate rendering** (3-stage attribute pipeline), fix CHR bank timing | — |
| `a289801` `e5c7486` | 02-19 | NMI suppression, NMI edge trigger, sprite 0 hit timing, VBL suppress, OAM read | — |
| `13ceb89` | 02-20 | **headless test runner** (`$6000` protocol) + CPU dummy reads + APU open bus | bootstrap |
| `7671455` | 02-21 | **PPU VBL/NMI 1-cycle delay model** (the most critical jump) | 154 (+15) |
| `5461fe7` | 02-22 | sprite timing: per-pixel hit + cycle-accurate overflow + hardware bug (BUGFIX17) | 165 (+4) |
| `1dd9024` | 02-22 | CPU interrupt timing: **penultimate-cycle IRQ** + NMI deferral + DMA align (BUGFIX18) | 169 (+4) |
| `f3188b9` | 02-22 | **DMC DMA cycle stealing** + TestRunner CRC detection (BUGFIX19) | 171 (+2) |
| `7cfef01` | 02-22 | PPU `$2007` read cooldown (6-dot ignoreVramRead, BUGFIX20) | 172 (+1) |

See: [BUGFIX4](../../bugfix/2026-02-20_1823_BUGFIX4.md), [BUGFIX13–20](../../bugfix/).
> Key concept: the **VBL/NMI 1-cycle delay model** (rising edge → `nmi_delay` → `nmi_pending`) is the ticket to passing the NMI test series, and it was laid down here.

---

## Phase 1 — AccuracyCoin frontal assault (2026-03-04 ~ 03-08)

With blargg near perfect, focus shifted to AC. This phase was "fix as much as possible on the existing (instruction-level CPU) model," filling in a ton of PPU/OAM/APU behavior one by one, finally stalling at **122/136**.

| commit | date | problem → fix | AC |
|--------|------|---------------|-----|
| `7e4c1b2` | 03-04 | branch dummy reads, CPU/PPU/controller **open bus** (BUGFIX29) | — |
| `8a04051` `afe3e17` | 03-06 | **Load DMA parity-dependent countdown** (GET/PUT phase via cpuCycleCount parity, BUGFIX31/32) | 174 blargg |
| `86743fe` | 03-06 | added **Master Clock infrastructure** (paving the way for future sub-cycle timing) | — |
| `24328e9` | 03-06 | controller strobe, OAMADDR reset, S0H rendering flags (BUGFIX33) | — |
| `ab42f68` | 03-07 | `$2007` rendering increment, `$2004` read/write during rendering (BUGFIX34) | — |
| `43d34a9` | 03-07 | arbitrary sprite zero, misaligned OAM (BUGFIX35) | — |
| `09599c1` | 03-07 | **OAM corruption** on rendering enable/disable (BUGFIX36) | — |
| `c9fd77e` | 03-07 | Frame Counter IRQ: deferred clear + unconditional flag set (BUGFIX37) | — |
| `3edef15` | 03-07 | **INC `$4014`**: defer OAM DMA to the next read cycle (BUGFIX38) | — |
| `a35646b` | 03-07 | controller strobing put/get cycle parity (deferred `$4016` write, BUGFIX39) | — |
| `0acad44` | 03-07 | **stale BG shift registers** + deferred Load DMA model (BUGFIX40) | — |
| `56cfcd0` | 03-07 | `$2004` read during sprite evaluation returns the evaluation position (BUGFIX41) | — |
| `b789e95` | 03-07 | **suddenly resize sprite**: sprite size latch at dot 261 (BUGFIX42) | — |
| `c5895d0` | 03-07 | rendering flag: freeze BG shift registers when rendering off (BUGFIX43) + OAM DMA APU activation (BUGFIX44) | — |
| `da92e7e` | 03-07 | `$2002` flag clear timing stagger (M2 duty cycle, BUGFIX45) | — |
| `ce904d0` | 03-08 | P19 **Sprites On Scanline 0**: secondary OAM + per-dot sprite eval FSM (BUGFIX47) | — |
| `5a7d56f` | 03-08 | P19 **`$2004` Stress Test**: per-dot read accuracy (BUGFIX48) | — |
| `a991af3` | 03-08 | **Milestone: 174/174 blargg + 122/136 AC** (the best state before switching models) | **122** |

See: [BUGFIX29–49](../../bugfix/).
> Key concept: many PPU behaviors (OAM corruption, sprite eval, shift-register freeze) can only be modeled **per-dot**. This phase pushed the "instruction-level CPU" model to its limit — then hit the wall.

---

## Phase 2 — Switching the timing model: per-cycle CPU (2026-03-09 ~ 03-14) ⭐

**The turning point of the whole effort.** The instruction-level CPU could only insert DMA at instruction boundaries, so DMC stolen-cycle timing didn't line up and a whole class of tests wouldn't pass no matter what. So the CPU was rewritten to "step one cycle at a time," letting DMA insert at any read-cycle boundary. From here it was a straight run to v1 perfect.

| commit | date | problem → fix | AC |
|--------|------|---------------|-----|
| `533d1d4` | 03-09 | **per-cycle CPU rewrite**: `cpu_step_one_cycle()`, each cycle a separate `StartCpuCycle→bus→EndCpuCycle`, DMA insertable anywhere (BUGFIX50) | **126** (+4) |
| `3a3d728` | 03-09 | **SH\* unofficial opcodes** (SHA/SHX/SHY/SHS's `&(H+1)` and page-cross behavior, BUGFIX51) | **131** (+5) |
| `5af6fdb` | 03-09 | **DMC DMA cooldown** (TriCNES `CannotRunDMCDMARightNow`, BUGFIX52) | **132** (+1) |
| `38368d9` | 03-13 | **DMC Load DMA countdown** timing (TriCNES-style, BUGFIX53) | **133** (+1) |
| `bb0f231` | 03-13 | **DMC DMA bus conflict** + deferred `$4015` status (BUGFIX54) | **134** (+1) |
| `7f83583` | 03-13 | P13 **Explicit DMA Abort** (BUGFIX55) | **135** (+1) |
| `f94fd51` | 03-14 | P13 **Implicit DMA Abort** (BUGFIX56) → **136/136 PERFECT** 🎉 | **136** (+1) |

See: [BUGFIX50](../../bugfix/2026-03-10_BUGFIX50_per_cycle_CPU.md), [BUGFIX51–56](../../bugfix/).
> **Lesson**: at this precision, the cost of "patching a coarse model" already exceeded "just switching to a per-cycle model." After switching, 122→136 took only 5 days and 7 commits — proof that once the foundation is right, the rest goes fast.

---

## Phase 3 — PPU refinement + full TriCNES alignment (2026-03-23 ~ 2026-04)

After 136/136, the **actual on-screen image** still had PPU rendering flaws (`scanline-a1`, `colorwin_ntsc.nes`, Mega Man 5 vertical shake). The root cause was insufficient PPU timing precision that the old architecture couldn't patch → we began **aligning to TriCNES's per-master-clock PPU model item by item**, which ultimately pushed AC to v2's **138/138**.

| commit | date | problem → fix |
|--------|------|---------------|
| `c383e1b` | 03-23 | **`$2006` delayed t→v copy**: fix vertical-scroll shake (Mega Man 5 etc., BUGFIX57) |
| `898703a` | 03-23 | **read-time CIRAM mirroring**: nametable no longer corrupts when switching mirror mode |
| `2bdb155`–`97633e2` | 03-24/25 | **MMC5** full rewrite (PRG/CHR banking, scanline IRQ, pre-sprite-render CHR bank, extended attributes) |
| `14754fe` `7216705` | 04-02 | **PPU / non-PPU timing comparison docs** (AprNes vs TriCNES) — systematically finding differences |
| `6d3ce08` | 04-02 | **`$2005` scroll write delay** (2 PPU dots, TriCNES model) |
| `93086bf` | 04-02 | **`$2001` four-tier flag system** (TriCNES model) |

See: [BUGFIX57](../../bugfix/2026-03-23_BUGFIX57_PPU2006_Delayed_Copy.md), [CIRAM](../../bugfix/2026-03-23_CIRAM_ReadTime_Mirroring.md), [MMC5](../../bugfix/2026-03-25_MMC5_PreSpriteRender_CHR_Fix.md).
> Later the `feature/tricnes-sync` branch ported the **SR latch pipeline + PPU core** as an equivalent reimplementation, cherry-picked into master, and with the AC ROM upgraded to v2 (138 tests) reached **138/138**.

---

## Phase 4 — Latest (2026-05-22)

| commit | date | problem → fix | AC |
|--------|------|---------------|-----|
| `e354371` | 05-22 | AC upgraded to `20260521` (138→**139** tests, new P20 `Internal Data Bus`); **internal/external data-bus split** (`internalBus` vs `cpubus`, `$4015` bit5 source) | 139 |
| `11a16ad` | 05-22 | regression fix: **DMA read of `$4015` uses external bus, CPU uses internal bus** (a live example of fixing P20 but regressing P14) | **139/139** ✓ |

See: [dual data-bus](../../bugfix/2026-05-22_AC_InternalDataBus_DualDataBus.md).

---

## Summary curve

```
blargg:  ~110 ──(cycle-accurate PPU + VBL 1-cycle delay)──▶ 172 ──▶ 174/174
                                                                  │
AC v1:        122 ──(per-cycle CPU model switch)──▶ 126 ─31─32─33─34─35─▶ 136/136 PERFECT
                                                                  │
AC v2:        136 ──(TriCNES PPU core / $2005 / $2001 / SR latch)──▶ 138/138
                                                                  │
AC 20260521:  138 ──(internal/external data bus split)──▶ 139/139
```

**Two main threads**:
1. **CPU/DMA timing** — instruction-level → per-cycle (BUGFIX50) → full DMC/DMA timing (51–56).
2. **PPU precision** — cycle-accurate rendering → per-dot FSM → alignment to TriCNES per-master-clock.

Both prove the same point: **with the foundation (timing model) right, hole-patching converges; with it wrong, hole-patching regresses forever.**
