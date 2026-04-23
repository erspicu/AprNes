# AprNes PMU L1 I-Cache Miss Analysis — PPU Refactor @ 1bea3d1

- **Date**: 2026-04-23 22:40
- **Branch**: `feature/ppu-refactor-v2` @ `c9ee658` (1bea3d1 + dispatch reorder + docs)
- **Build**: Debug x64, .NET Framework 4.8.1
- **CPU**: AMD Ryzen 7 3700X (Zen 2, 8-core, L1i 32 KB × 8)
- **Config**: NTSC, Audio Mode 2, Ultra Analog RF, CRT, 4× resolution
- **Duration**: 30 s benchmark, 3 684 307 TotalCycles samples

Trace: `temp/aprnes_pmu.etl` (337 MB).

---

## 1. Global Health — Back in Excellent Tier

| Period | Global L1 I-Cache Miss Rate | Tier |
|---|---:|---|
| 2026-04-14 (pre-PPU-refactor) | **0.52%** | excellent |
| 2026-04-23 (post-mem-refactor, pre-PPU-refactor) | **1.73%** | healthy (concerning trend) |
| **2026-04-23 @ 1bea3d1 (current)** | **0.53%** | excellent ✓ |

Industry thresholds:
- < 1%: excellent — working set fits in L1
- 1-3%: healthy — minor eviction, L2 absorbs cost
- 3-10%: concerning — significant L2 traffic
- > 10%: bad — observable stall-related FPS loss

**Back to excellent tier after the PPU dispatch refactor**. The +1.73% scare from the post-mem-refactor period has been fully absorbed.

---

## 2. Per-Method Miss Rate (hot paths)

| Method | Misses | Fetches | Miss % |
|---|---:|---:|---:|
| `Ppu_Tick_Visible_PixelZone` | 429 | 94 884 | **0.45%** |
| `Run_NTSC` | 325 | 66 521 | **0.49%** |
| `PpuPhase4_SpriteEvalAndInit` | 176 | 33 895 | **0.52%** |
| `apu_step` | 150 | 31 879 | **0.47%** |
| `PpuPhase4_SpriteFetch` | 49 | 9 203 | **0.53%** |
| `Ppu_Tick_Visible_SpriteFetch` | 56 | 8 594 | **0.65%** |
| `Ppu_Tick_Visible_Prefetch` | 33 | 5 091 | **0.65%** |
| `CpuRead` | 32 | 4 127 | **0.78%** |
| `DemodulateRow_Core` (NTSC) | 69 | 5 874 | **1.17%** |
| CRT `<Render>b__0` lambda | 126 | 11 061 | **1.14%** |
| CRT `<Curvature>b__1` lambda | 97 | 6 658 | **1.46%** |

All NesCore hot-path methods firmly in **< 1%** tier. NTSC demod + CRT lambdas are slightly higher (1.1-1.5%) but still "healthy".

---

## 3. Evolution Against Previous Baselines

| Method | 04-14 baseline | post-mem-refactor | **1bea3d1** |
|---|---:|---:|---:|
| PPU main hot path | 0.31% (`ppu_step_new`) | 3.45% (`ppu_step_new`) | **0.45%** (`PixelZone`) ✓ |
| `PpuPhase4_SpriteEvalAndInit` | 0.36% | 4.30% | **0.52%** ✓ |
| `Run_NTSC` | 0.36% | 3.15% | **0.49%** ✓ |
| `apu_step` | 0.47% | 3.10% | **0.47%** ✓ |
| `DemodulateRow_Core` | 1.43% | 0.94% | 1.17% |
| CRT `<Render>` lambda | 0.93% | 1.10% | 1.14% |

**PPU hot path recovered completely** (3.45% → 0.45%). The post-mem-refactor regression was driven by growing monolithic `ppu_step_new` (2 331 IL); splitting into zone-specialised handlers restored the I-cache health.

---

## 4. Why the Refactor Helped I-Cache

Three complementary effects:

### 4.1 Each handler is smaller than the old monolith

- Old `ppu_step_new`: 2 331 IL bytes
- New `Ppu_Tick_Visible_PixelZone`: 1 885 IL bytes
- `Ppu_ActiveScanline_RenderBlock` helper: 1 474 IL bytes (inlined into PixelZone + PreRenderLine + VisibleLine)

When the JIT produces machine code, each handler's body is contiguous and fits more comfortably in L1 D/I-cache. The hot path (PixelZone for 256 × 240 = 61 440 dispatches/frame) has a **smaller resident footprint** than the old monolith.

### 4.2 Dead-code elimination at compile time

Each specialised handler bakes out scanline/cx gates:
- Visible PixelZone: no events check (scanline always < nmiTriggerLine), no Yinc/CopyHoriV, no frame render, no NTSC capture
- SpriteFetch / Prefetch / Dummy: all pixel / sprite shift / draw blocks stripped

The JIT produces **less machine code** per handler than its source-line count would suggest. This shrinks per-handler footprint below the source-level IL size.

### 4.3 Cold handlers don't pollute L1

- `Ppu_Tick_VBlankLine`: 468 IL, runs 7 161 dispatches/frame (NTSC) — not often enough to stay resident
- `Ppu_Tick_VisibleLine` (generic fallback, slots 256/257/340): 678 IL, 720 dispatches/frame — same
- `Ppu_Tick_Visible_Dummy`: 0.1% CPU — cold

These handlers get **evicted between frames**, leaving L1 free for the hot PixelZone / SpriteFetch / Prefetch trio.

---

## 5. Comparison With What Got Worse

Only two slight regressions:
- `DemodulateRow_Core`: 1.43% → 0.94% → 1.17% (post-refactor up from mid-stage but still better than 04-14)
- CRT `<Render>` lambda: 0.93% → 1.10% → 1.14% (slight uptrend)

Both are NTSC+CRT rendering paths, not PPU emulation core — the refactor's concern didn't reach them. They remain in "healthy" tier.

---

## 6. Cross-check: Does This Match FPS?

- Global miss rate dropped from 1.73% → 0.53% (**3.3× lower**)
- FPS went from pre-PPU-refactor ~120 → 136.30 (**+11.4%**)

The FPS gain is partly from I-cache recovery + partly from branch-elimination. The gain is consistent with both effects contributing.

---

## 7. Takeaways

1. **PPU dispatch refactor did NOT cause I-cache bloat**, despite adding 7 specialised handlers + 1 shared helper on top of the old monolith.
2. **The zone-specialisation + AggressiveInlining pattern is net I-cache-friendly** — shrinks per-handler footprint via dead-code elimination, even as code-line count grows at source level.
3. **Cold handlers (VBlank, PreRender, single-dot specialisations) won't pollute L1** as long as they stay cold — they naturally get evicted.
4. **Next TriCNES sync can proceed without I-cache anxiety** — the architecture has proven room to grow without hitting cache ceilings.

Target to watch in future measurements: keep global miss rate under **1%** and PPU hot-path methods under **0.5%**. Anything crossing those boundaries is a signal to investigate before FPS degrades noticeably.
