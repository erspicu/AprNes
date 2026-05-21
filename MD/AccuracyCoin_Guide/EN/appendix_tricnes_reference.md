# Appendix B: Using TriCNES as ground truth (and its known failures)

> AccuracyCoin's author, **100thCoin**, wrote their own emulator, **TriCNES**, which scores essentially perfect on their own tests. So when there's a dispute over "how should the hardware behave," TriCNES is the most direct reference. But it is **not 100% correct** — there are a few tests it fails too. Knowing which parts not to trust matters more than copying blindly.

---

## 1. Why TriCNES, and not some other emulator

Priority order (a project iron rule): **hardware docs (NESdev wiki) > test-ROM expectation > TriCNES**.

- The NESdev wiki is the highest authority, but some sub-cycle behaviors aren't spelled out clearly there.
- The AC test ROM's expectations are "what the author deems correct," but the ROM only tells you pass/fail, not "which cycle in between is wrong."
- **TriCNES is the author's "implemented" version of those expectations** — when you need to see "what a correct implementation looks like, what the intermediate value should be," tracing TriCNES is the fastest route.

> ⚠️ We do **not** reference BeesNES (only 96/136 on AC, poor showing); Mesen2 can serve as a mapper-logic reference but isn't guaranteed 100% correct (as a rule we no longer reference Mesen2 for bug fixes).

---

## 2. How to use it

**Paths**:
- `ref/TriCNES-main-20260521/` — latest (gitignored locally, not in the repo)
- `ref/TriCNES-main-20260410/` — previous version (lets you compare TriCNES's own evolution)

**Structure**: TriCNES is C# WinForms, with the core all in **a single big `Emulator.cs`** (CPU/PPU/APU/DMA all in it, ~11000+ lines). Mappers are in `mappers/`.

**Usage**:
1. Run the **same ROM** through TriCNES and compare behavior / intermediate values.
2. Trace `Emulator.cs` against our NesCore implementation. Common comparison points:
   - `Fetch(ushort)` — the shared CPU/DMA read (including the bus behavior of `$4015`, controllers, and PPU registers).
   - `internalBus` / `dataBus` — the internal/external data buses.
   - DMC timer / `APU_PutCycle` / `CannotRunDMCDMARightNow` — DMA parity and cooldown.
3. For the version diff, see [`MD/notes/TriCNES_20260521_vs_20260410_diff`](../../notes/TriCNES_20260521_vs_20260410_diff.md) (the recent dual-bus fix was found from this diff).

---

## 3. ⚠️ Align "semantics," not "numbers"

The easiest place to crash: **TriCNES's internal counting cadence differs from ours, so copying its constants verbatim will be wrong.**

Example ([BUGFIX56](../../bugfix/2026-03-14_BUGFIX56_Implicit_DMA_Abort.md)):
- TriCNES's DMC timer **decrements by 2 per GET cycle** (always even).
- AprNes's DMC timer **decrements by 1 per cycle**.
- And the pending→active transition has a **+3 position offset**.

So where TriCNES writes `timer == 10 && !PutCycle`, our corresponding condition is `dmctimer == 8 && !getCycle` — not 10. **Understand each side's timer-decrement semantics first, then convert the positions**, otherwise lifting the magic number directly is guaranteed to be wrong.

> This is also an extension of "no compensating hacks": copying numbers without understanding the semantics is guessing, and it will regress sooner or later.

---

## 4. Tests TriCNES itself fails (don't treat it as truth for these)

The following are tests TriCNES itself fails or whose behavior is disputed — for these, **do not** take TriCNES as authoritative; go back to the NESdev wiki / real hardware / multi-source cross-checking:

| Item | Note |
|------|------|
| `6-MMC3_alt` | MMC3 alternate behavior |
| `6-MMC6` | MMC6 |
| `5-MMC3_rev_A` | MMC3 rev A variant |
| `read_write_2007` | certain edge cases of `$2007` read/write |
| `power_up_palette` | power-on palette initial values |

> **⚠️ Don't immediately read "TriCNES disagrees with the wiki" as TriCNES being wrong.** Sometimes the wiki is imprecise and TriCNES is right. See the resolved case below.

---

## 4b. Resolved case: sprite X-counter behavior during forced blank (once a three-way disagreement)

This was once an open question of "NESdev wiki / AC test / TriCNES disagree three ways," where it wasn't clear whom to trust. **It has now been confirmed by AC author 100thCoin in [TriCNES issue #23](https://github.com/100thCoin/TriCNES/issues/23)** ("Works as intended"):

- the sprite X-position counter has two modes: **halted / counting**.
- the condition to enter counting: **rendering is on at dot 339 of a scanline**.
- **once in counting, turning rendering off (forced blank) does NOT stop the counter** — it keeps decrementing; only the L/H shift register's shifting is gated by rendering-enabled.
- not revision-specific (tested on C/E/G/H).

**Conclusion**: TriCNES's behavior (counter decrements unconditionally, only the shifter is gated) is correct; the NESdev wiki's literal "rendering halted immediately" is imprecise. AprNes already matched (`ppu_dispatch.cs:368-389`: the `sprXCounter` decrement is *outside* the `if (renderEnabled)` block), and P19 is all PASS.

> **Two lessons**: (1) when there's a three-way disagreement, don't force it — set it aside and ask the author (this one was solved exactly that way); (2) **don't hoist `renderEnabled` out of the sprite shift block** — that would freeze the counter too, and P19 fails immediately.

---

## 5. TriCNES's mapper coverage (confirm before tracing)

TriCNES **only implements these mappers**:

```
0 (NROM), 1 (MMC1), 2 (UxROM), 3 (CNROM),
4 (MMC3/MMC6), 7 (AOROM), 9 (MMC2), 69 (FME-7)
```

**For mappers not on this list, don't trace TriCNES** (it simply hasn't implemented them; tracing is wasted effort). AccuracyCoin itself is NROM (mapper 0), so the TriCNES comparisons used during the AC effort center on mapper 0 + the CPU/PPU/APU/DMA core. For other mappers, use Mesen2 (`ref/Mesen2-master/Core/NES/Mappers/`) as an implementation reference, but reimplement in our own `IMapper` style — don't copy directly.

---

## Summary

- TriCNES is the most practical ground truth for conquering AC — but **with limits**: it covers only 8 mappers, and there are 5+ tests it fails too.
- The way to use it is "align semantics," not "copy numbers" (the timer-decrement cadence differs).
- Always keep the priority order: **hardware docs > test ROM > TriCNES**.

Back to the [guide home](README.md).
