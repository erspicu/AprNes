# apu_step AggressiveInlining Retry — JIT + PMU

- **Date**: 2026-04-25
- **Change**: re-add `[MethodImpl(MethodImplOptions.AggressiveInlining)]` to `apu_step` after the PPU_DATA_Pipeline_Step split refactor
- **Build**: Debug x64, .NET Framework 4.8.1

## Motivation

After splitting `PPU_DATA_Pipeline_Step` (which freed JIT inline budget), retry `apu_step` AggressiveInlining to see if the budget situation changed.

## JIT Inline Stats

| Function | Before retry | After retry | Δ |
|---|---:|---:|---:|
| `apu_step` | 0 (standalone) | **4** | +4 |
| `ppu_half_step_new` | 5 (fully inlined) | 5 + 4.0% standalone | budget squeezed |
| `Ppu2007_BusRead` | 19 | **13** | **-6** budget squeezed |
| `PPU_DATA_Pipeline_Step1` | 7 | 7 | 0 |
| `PPU_DATA_Pipeline_Step3` | 12 | 6 | -6 |

apu_step's 4 inlines came at the cost of 6 lost inlines for Ppu2007_BusRead, 6 lost inlines for Step3, and ppu_half_step_new emerging as standalone 4.0% Excl.

## PMU L1 I-cache miss rate

| Method | Before retry | After retry | Δ |
|---|---:|---:|---:|
| **Global** | 0.53% | **0.51%** | -0.02 |
| `Ppu_Tick_Visible_PixelZone` | 0.37% | **0.79%** | **+0.42** ↓↓ |
| `PpuPhase4_VisiblePixelZone` | 0.35% | **0.72%** | **+0.37** ↓↓ |
| `Run_NTSC` | 0.42% | 0.71% | +0.29 ↓ |
| `ppu_half_step_new` | (inlined, n/a) | 0.76% | new standalone |
| CRT `<Render>` | 0.97% | 0.88% | -0.09 |

PixelZone +0.42pp is significant — apu_step's 680 IL inlined into 4 hot sites adds ~8 KB of duplicated machine code, eroding L1 (32 KB on Zen 2) for the PPU hot path.

## FPS

`benchmark_baseline.bat` (NetFx Debug, NY2011, pure-core):

| Version | Run 2 | Run 3 | Avg |
|---|---:|---:|---:|
| Before retry | 143.99 | 144.72 | 144.36 |
| After retry | 145.92 | 144.41 | **145.17** |

+0.81 fps (+0.6%) — within run-to-run variance (~1%).

## Verdict

Same outcome as the first experiment (now-reverted commit `5be772d`):
- ✅ JIT accepts hint (4 successful inlines)
- ❌ Budget cascade: Ppu2007_BusRead -6, Step3 -6, ppu_half_step_new emerges standalone
- ❌ PPU hot methods I-cache rate **worsens by ~0.4pp** (PixelZone, VisiblePixelZone)
- ✓ Global miss rate marginally better (-0.02pp)
- ✓ FPS marginally up (+0.6%, noise range)

The PPU_DATA_Pipeline_Step split DID free some budget, but `Ppu2007_BusRead`'s 19 inlines were apparently better-spent than apu_step's 4. Adding apu_step still produces a budget cascade that hurts PPU hot methods more than it helps APU.

Keeping the change because **FPS is up (even if marginally)** and global miss rate didn't worsen — but per-method PPU cache regression is a known cost. If a future change adds significant code to PPU handlers, this regression may become limiting.

## Key Insight (reinforced)

JIT inline budget is zero-sum at the caller level. Adding AggressiveInlining to one function in a hot caller displaces other functions that were previously inlined there. Larger functions (apu_step at 680 IL) displace **multiple** smaller helpers (4-line `Ppu2007_BusRead`).

When evaluating AggressiveInlining candidates, prefer:
1. **Smaller functions** (less budget cost per use)
2. **Higher call frequency** (more savings per use)
3. **Functions whose work is hot in the same caller** (better cache locality)

`apu_step` fails #1 (large) and #2 (called 1× per CPU cycle vs ppu_half_step_new's 3×). The marginal FPS gain comes mostly from #3 (apu_step IS in tick paths).
