# Methodology: how to run AccuracyCoin, read results, and debug

> Maps to: all pages. This is the tooling & workflow chapter — before fixing any test, nail down "how to run it, how to read it, how to locate the problem," so you don't flail later.

---

## 1. How AccuracyCoin works

AccuracyCoin is an **NROM (mapper 0)** cartridge packing 139 tests into one menu. Each test prints `PASS` / `FAIL` on screen, and on failure also gives an **error code** (one hex digit, indicating "which sub-test broke"). There are also 5 `DRAW` tests that don't judge anything — they just print info.

Results are written to fixed RAM locations (`result_*` labels, mostly in the `$500`–`$5FF` region); the ROM maintains its own result table. Our headless runner just reads that table + screenshots.

**Manual operation (GUI)**:
- D-pad moves the cursor, `A` runs the current test, `B` marks it to skip (press again to undo).
- With the cursor on the page header (page number): left/right change page, `A` runs the whole page, `Start` runs everything and draws a summary table.
- After running a test, press `Select` to open the **debug menu**, which prints the bytes at `$20`–`$2F`, `$50`–`$6F`, `$500`–`$5FF` — handy for debugging individual tests.

---

## 2. Running headless (our main workflow)

Clicking through the GUI page by page is too slow. AprNes has a headless runner with three scripts:

### Single page / single item (most common, fast)
```bash
bash run_ac_test.sh <page>            # run the whole page
bash run_ac_test.sh <page> <item>     # run only one item (1-based)
bash run_ac_test.sh <page> --skip <item>   # skip an item, run the rest
bash run_ac_test.sh <page> --no-build # skip the build
```
This leaves a screenshot at `result/ac_p<page>_test.png` — **reading the screenshot is the fastest way to judge PASS/FAIL**. The terminal also dumps `AC_RESULTS_HEX:` (the result table in hex), but eyeballing the screenshot beats parsing hex.

> ⚠️ The runner often prints `FAIL(255) | (no $6000 signature)` at the end — that's only the headless **overall exit code** judgment (AC doesn't use blargg's `$6000` protocol). **The real per-test result is in the screenshot**, where each item prints its own PASS/FAIL.

### Full report (all 139 tests + screenshots + HTML)
```bash
bash run_tests_AccuracyCoin_report.sh                 # full run + report
bash run_tests_AccuracyCoin_report.sh --no-build      # skip build
bash run_tests_AccuracyCoin_report.sh --skip 12:1     # skip an item
```
Output at `reports/report/AccuracyCoin_report.html`. **The full report is slow** — for validating a single fix, use the single-page script; save the full run for final acceptance (or hand it to a human).

### Avalonia edition
```bash
bash run_tests_AccuracyCoin_avalonia.sh   # same, against AprNesAvalonia (.NET 10)
```

> All three scripts point their ROM path at `nes-test-roms-master/AccuracyCoin-main-20260521/` (remember to update it together when upgrading to a new build).

---

## 3. How to read an error code

A test's error code is "which sub-test failed." Two reference sources:
1. **The ROM's `README.md`** (`AccuracyCoin-main-20260521/README.md`) — official per-code descriptions, e.g.:
   > Open Bus → `9: Bit 5 of address $4015 should be open bus.`
2. **The `.asm` source** — search for `TEST_<test name>:` (or, as the README hints, `TestPages:`); the sub-tests are what sits between `INC <ErrorCode`. `ErrorCode` counts from 1, incrementing once per `INC`; the FAIL handler reports the current value, so **fail N = the Nth sub-test didn't pass**.

> Example: the [dual data-bus fix](../../bugfix/2026-05-22_AC_InternalDataBus_DualDataBus.md) was "P20 Internal Data Bus, error code 2" → go to `TEST_InternalDataBus` in the `.asm`, count to the 2nd sub-test, and see what it checks.

---

## 4. Using TriCNES as ground truth

AccuracyCoin's author (100thCoin) wrote their own emulator, **TriCNES**, which in principle scores perfect on their own tests. So when there's a dispute over "how should the hardware behave":

- Run the **same ROM** through TriCNES and observe its behavior / intermediate values.
- Trace TriCNES's `Emulator.cs` (one big file — CPU/PPU/APU/DMA all live in it) against our implementation.
- Reference paths: `ref/TriCNES-main-20260521/` (latest), `ref/TriCNES-main-20260410/` (previous).

**But TriCNES is not 100% correct** — there are a few tests it fails too (see [`appendix_tricnes_reference.md`](appendix_tricnes_reference.md)). The priority order is always: **hardware docs (NESdev wiki) > test-ROM expectation > TriCNES**.

---

## 5. Fix discipline (the project's iron rules)

1. **Research before trial-and-error**: when hardware behavior is uncertain, read the NESdev wiki / existing `ref/` material / TriCNES and understand it before changing anything. Time spent researching beats blind parameter-tweaking.
2. **No compensating hacks**: never add a hack or tweak a magic number to bypass correct behavior just to pass a test. Compensation forms conflicting chains that never converge. If correct behavior regresses some test, it means **something else is still wrong** — fix that too, don't patch over the correct behavior.
3. **Fix it completely in one shot**: when a root cause spans multiple subsystems, fix all of them correctly at once rather than patching incrementally. Short-term regressions are acceptable, but the foundation must be correct.
4. **Dual-test gate**: before every fix commit, confirm **blargg 184/184** and **AC** both have no regression. The change is to shared NesCore — both AprNes (NetFx) and AprNesAvalonia must be considered.

> Why so fussy? Because the later AC tests are deeply interdependent (the recent dual data-bus fix regressing P14 while fixing P20 is the lesson). Without discipline you fix one and break another, and never reach a perfect score.

---

## 6. What a typical fix cycle looks like

```
1. Run run_ac_test.sh <page> → check the screenshot: which item FAILs, what error code
2. Read the ROM README + .asm to understand what hardware behavior that sub-test checks
3. Check NESdev wiki / ref/ / TriCNES to confirm how the real hardware behaves
4. Edit NesCore (CPU/PPU/APU/MEM…), with comments spelling out "why"
5. Rebuild → run_ac_test.sh <page> again, confirm that item PASSes
6. Run the "affected neighboring pages" + blargg 184, confirm no regression
7. Update AccuracyCoin_TODO + write MD/bugfix/ + commit + push (one fix, one commit)
```

Next: [`00_timing_model.md`](00_timing_model.md) — why dot/cycle-level precision is mandatory, and what AprNes's tick model looks like.
