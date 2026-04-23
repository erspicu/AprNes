# PPU Dispatch-Table Refactor Proposals — Analysis & Plan

- **Date**: 2026-04-23
- **Branch**: `feature/ppu-refactor`
- **Source**: `temp/0423/01 … 12_*.txt` (12 AI-discussion threads)
- **Status**: Analysis only. **No code changed.**

---

## 1. Proposal Summary

Across the 12 threads, the evolving proposal is:

### Stage A — Tri-state scanline dispatch
Replace the scanline-type `if/else if/else` at the top of `ppu_step_new` with three `delegate*<void>*` arrays of length 341:

- `render_actions[341]` for visible scanlines (0-239)
- `prerender_actions[341]` for the pre-render line (261 / 311)
- `vblank_actions[341]` for VBlank lines (240-260 / 240-310)

Initial state: all 341 slots in each array point to the same scanline-type handler. `ppu_step_new` reduces to:

```csharp
delegate*<void>* table =
    scanline < 240              ? render_actions :
    scanline == preRenderLine   ? prerender_actions :
                                  vblank_actions;
table[cx]();
```

### Stage B — Per-dot-range specialisation
Within each state, split the 341 cx values into **14 logical micro-blocks** (Dot 0, Dot 1, 2-64, 65, 66-255, 256, 257, 258-259, 260, 261-320, 321-336, 337-338, 339, 340) and populate the array so each range points to its own specialised handler.

Shared prologue/middle/epilogue are extracted into `[AggressiveInlining]` helpers that get folded back into each of the 14×3 = 42 handlers.

### Compatibility story (Files 10-12)
- `delegate*<void>*` (**managed** function pointer, no `unmanaged` keyword) is **already** available on .NET Framework 4.8.1 — the `calli` IL instruction has existed since .NET 1.0 and the C# 9.0 compiler emits it. No `[UnmanagedCallersOnly]` needed.
- Proof: `CPU.cs` already uses `delegate*<void>* opFnPtrs` successfully on both runtimes.
- Upgrade to `delegate* unmanaged<void>*` + `[UnmanagedCallersOnly]` is a **.NET 10-only** micro-gain (~1-3 cycles/call by skipping GC safe-point polling).

---

## 2. Timing-Correctness Assessment

The user's explicit concern: **timing correctness**. AprNes's "one source of truth" is TriCNES. Baseline: blargg 184/184 + AccuracyCoin 138/138 must not regress.

### Stage A is low risk, but not trivial

Stage A is **a refactor**, not a behaviour change — each of the three handlers still runs the same per-dot logic that `ppu_step_new` currently runs. If the split is done correctly, timing should be identical.

Pitfalls to watch:

| Concern | Detail |
| --- | --- |
| **Mid-dot `ppu_cycles_x = ++cx` pattern** | TriCNES increments `cx` mid-function so that the first half sees pre-increment and the second half sees post-increment. Any handler must preserve this exact sequence position. File 04's draft does preserve it. |
| **Pre-render-only logic** | `PpuPhase_DoOddFrameSkip` (cx==340, oddSwap guard), `vram_addr` Y-scroll reset (cx 280-304), and `skippedPreRenderDot341` reset live **only** on the pre-render line. File 04's `Ppu_Tick_PreRenderLine` includes them; visible-line handler must NOT include them. ✓ in draft |
| **Phase 3 event gating** | `PpuPhase3_Events(cx)` currently fires conditionally on `scanline >= nmiTriggerLine`. File 04 places it on pre-render and VBlank handlers only, skipping visible. This is correct for NTSC (nmiTriggerLine=241) but needs verifying for PAL/Dendy. |
| **NMI / VBlank flag sequencing** | `isVblank`, `ppuVSET_Latch1`, `ppu2002ReadPending`, `oamCorruptDelay`, `ppu2001UpdateDelay`, `ppu2001EmphasisDelay` all live in a specific slot in the per-dot sequence. Any reordering breaks AC. |
| **`MapperObj.PpuClock()` every dot** | Must fire **once per PPU tick on every scanline** (including VBlank). Draft's VBlank handler includes it. ✓ |
| **`open_bus_decay_timer` tick** | Fires every dot regardless of scanline. ✓ in draft |

File 04's draft looks structurally sound but **would need line-by-line verification against current `ppu_step_new` + per-dot helpers** before being trusted. Blind copy = expected AC regression.

### Stage B is high risk

Forty-two specialised handlers mean:

- Every per-dot invariant has to be replicated correctly across three state lines (visible / pre-render / VBlank), each having 14 dot-range sub-handlers.
- If `Common_Tick_Start/Middle/End` helpers drift from what they need to be, **every** handler silently diverges.
- Extracting cx-range branching out of `PpuPhase4_SpriteEvalAndInit`, `PpuPhase5_TileFetchAndPixel`, etc. into specialised sub-functions duplicates cx-range logic across yet another layer.

AC 138/138 is a very tight filter. Historical experience in this project (see `MD/notes/AccuracyCoin_TODO.md`): a single mis-placed per-dot operation can cost 2-5 AC tests.

---

## 3. Performance Assessment

### Stage A expected gain

Current `ppu_step_new` IL = 2331 bytes, Excl% = 9.1% (see `20260423_post_mem_refactor_analysis.md`). Hot method, standalone (not inlined).

Splitting into three handlers of ~600-800 IL each:

- **Pro**: Each handler has a smaller register-live set, JIT can allocate registers better, less stack spilling. Smaller I-cache footprint per invocation (only the active scanline-type lives in L1i).
- **Con**: Replaces two predicted branches (`scanline < 240`, `scanline == preRenderLine`) with one indirect `calli`. These branches today are ~98% predictable (scanline changes only at cx=340 wrap), so mispredict penalty isn't free but is rare. `calli` itself is ~2-3 cycles with predicted target.

Net: **expected 0-2% FPS**. Could also be a wash or marginal regression if the indirect-call overhead dominates.

### Stage B expected gain

`AggressiveInlining` of `Common_Tick_Start/Middle/End` into 42 handlers duplicates those bodies 42 times. If common helpers are ~100 IL each, that's ~12 KB of duplicated IL on top of the already-large core.

Reference: the 04-23 PMU snapshot already shows ~1.73% global I-cache miss rate, **3.3× worse than the 04-14 baseline** because earlier refactors grew the hot working set. Stage B would push further in the same direction.

**Could easily be net-negative on FPS** if the 42-handler bloat pushes the frontline over L1i ceiling.

The claimed Stage B wins (JIT constant-folding of `cx` when it's a compile-time literal in per-dot handlers like `Ppu_Vis_Dot256_YInc`) are **real but narrow** — they only kick in on the 10 or so dots that are truly unique. For the 66-255 visible-pixel range (190 dots sharing one handler), `cx` remains a variable.

### Alternative path: cold extraction (the project's historical pattern)

From the post-mem-refactor analysis, the highest-ROI optimisation in this project has been **cold extraction on large hot methods**:

- `PpuPhase4` cold extraction commits (330036a, eee8dd2, a26d1e5, etc.) each delivered 1-3% FPS with minimal risk
- `apu_step` function-pointer dispatch (671db3e) gave +1.9% FPS with IL shrink of 35%
- CRT lambda extraction etc.

Applying this to `ppu_step_new` directly — **push rarely-taken branches (VBlank event bits, `oamCorruptDelay`, `ppu2001UpdateDelay`, etc.) into `[NoInlining]` cold helpers** — is a **lower-risk, incremental, proven** technique that shrinks the hot-path IL without introducing dispatch overhead.

---

## 4. Compatibility Assessment

Files 10-12 are correct: `delegate*<void>*` (managed function pointer) works on both .NET Fx 4.8.1 and .NET 10 via the pre-existing `calli` IL instruction. CPU.cs's existing `opFnPtrs` proves this.

Therefore, **no `#if NET10_0_OR_GREATER` is needed for the basic dispatch-table architecture**. The only conditional would be for the `[UnmanagedCallersOnly]` + `delegate* unmanaged` upgrade, which saves ~1-3 cycles/call on .NET 10 only.

Cost of unmanaged upgrade:

- Adds `#if` around 3+ handler definitions and array declarations
- Requires AllocUnmanaged vs AllocHGlobal differentiation
- Forbids the target function from capturing/throwing managed exceptions

**Verdict**: skip the `unmanaged` upgrade for now. Stick with `delegate*<void>*` on both runtimes — code stays single-source, and the ~3-cycle potential saving is under measurement noise for PPU workload.

---

## 5. Recommended Plan (If Proceeding)

A staged, test-every-step approach is the only way to not destroy timing.

### Step 1 — Feasibility probe on the branch
- Keep `ppu_step_new` intact.
- In a new file or appended section, write `Ppu_Tick_VisibleLine`, `Ppu_Tick_PreRenderLine`, `Ppu_Tick_VBlankLine` that each call into the same existing phase helpers currently invoked by `ppu_step_new`.
- Wire a new entry point `ppu_step_new_table` that does the 3-way dispatch via `delegate*<void>*`.
- Behind a `#define` or `static bool`, switch which version runs.
- Run blargg. If 184/184 passes, run AC. If 138/138 passes, measure FPS with `benchmark_baseline.bat`.

### Step 2 — Go / no-go
- If FPS improves by > 1% AND blargg+AC are clean: keep, commit, remove the old path.
- If FPS is wash or regresses: revert, document why in this MD, stop.
- If any AC test fails: revert, do not iterate on this approach.

### Step 3 — (Optional, only if Step 2 wins) Per-dot-range specialisation
- Within the visible-line handler only, try splitting visible (1-256) / sprite-fetch (257-320) / prefetch (321-340) / idle (0, 337-340) into maybe **3-5 sub-handlers maximum** — not 14.
- Measure again, go/no-go again.

### Step 4 — (Very optional) `unmanaged` upgrade on .NET 10 path
- Only if Steps 1-3 all won and there's still measurable headroom.
- Add `#if NET10_0_OR_GREATER` guarded `[UnmanagedCallersOnly]`.

### What NOT to do
- Do **not** attempt Stage B's full 42-handler split in one go. Too many failure modes at once.
- Do **not** drop to `Action[]` on any fallback path. `delegate*<void>*` works on .NET Fx 4.8.1 (see File 10 analysis + CPU.cs precedent).
- Do **not** skip AC tests. PPU timing is exactly what AC stresses.
- Do **not** re-order the mid-dot `ppu_cycles_x = ++cx` TriCNES pattern. AC tests depend on pre-increment / post-increment position.

---

## 6. Non-Goals

Explicitly **not** pursuing:

- Scanline-level catch-up (rejected in File 02, correctly — breaks master-clock architecture).
- 2D 341×312 matrix dispatch (memory fine, but overkill; 1D cx dispatch is sufficient).
- Replacing `IMapper.PpuClock()` virtual call with function pointer (orthogonal, separate issue).

---

## 7. Open Questions

Before implementing, would need to resolve:

1. Does splitting `ppu_step_new` into three handlers have any hidden cost from breaking current JIT inlining of `PpuPhase4_*` and `PpuPhase5_*`? (Probably not — those are already standalone.)
2. Can the new dispatch-table core path coexist with the existing `NestedTickN_NTSC` structural-unroll pattern? Does the unroll call `ppu_step_new` or equivalent? Need to audit `MEM.cs` tick flow.
3. Is `ppu_step_new` called from `MasterClockTickUnrolledNTSC` (Phase 2 structural unroll) the exact same way, or does the unroll already specialise the call path?

Answering these before Step 1 of the plan.

---

## 8. Summary Verdict

- **Phase 1 (tri-state scanline dispatch)**: legitimate refactor, low risk if done carefully, expected +0-2% FPS. **Worth a controlled probe** per the Step 1-2 plan.
- **Phase 2 (14 micro-blocks per state)**: high risk of timing regression + I-cache bloat. **Not recommended** as a first move.
- **Cold extraction (alternative)**: lower risk, proven ROI in this project. **May be the better use of effort**.

The AI discussion threads are technically well-reasoned but over-sell the gains. The "physical limit / holy grail" language in Files 05, 07 is marketing. The actual expected wins are modest (<3% FPS) and the floor is negative if the bloat is mishandled.

Start with the feasibility probe. Measure. Decide.
