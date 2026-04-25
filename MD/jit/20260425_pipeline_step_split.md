# PPU_DATA_Pipeline_Step Split — JIT + PMU

- **Date**: 2026-04-25
- **Change**: split monolithic `PPU_DATA_Pipeline_Step(int phase)` into 3 phase-specific functions + extracted `Ppu2007_BusRead` helper
- **Build**: Debug x64, .NET Framework 4.8.1
- **Config**: NTSC, Audio Mode 2, Ultra Analog RF, CRT, 4× resolution

## Refactor

```
Before:
  PPU_DATA_Pipeline_Step(int phase)  — single function with 3 if (phase == ?) blocks
  - phase 1 callers: 3
  - phase 2 callers: 7
  - phase 3 callers: 1

After:
  PPU_DATA_Pipeline_Step1()     — Phase 1 body (no phase discriminator)
  Ppu2007_BusRead()              — PD_RB-gated bus read (was Phase 2 body, also called inside Step3)
  PPU_DATA_Pipeline_Step3()     — Phase 3 body (TStep + Ppu2007_BusRead + odd latch + write)

  Phase 2 callers now invoke Ppu2007_BusRead() directly — no wrapper needed.
```

## JIT Inline Stats

| Function | Inline events |
|---|---:|
| `Ppu2007_BusRead` (4-line micro-helper) | **19** |
| `PPU_DATA_Pipeline_Step3` | 12 |
| `PPU_DATA_Pipeline_Step1` | 7 |

`Ppu2007_BusRead` being a tiny gate-then-read helper, JIT eats it everywhere. Step1/Step3 are larger but still fit inline budget at the relevant call sites.

## PMU L1 I-cache miss rate

| Method | bf51c3e | After | Δ |
|---|---:|---:|---:|
| **Global** | 0.54% | **0.53%** | -0.01 |
| `Ppu_Tick_Visible_PixelZone` | 0.49% | **0.37%** | **-0.12** |
| `PpuPhase4_VisiblePixelZone` | 0.60% | **0.35%** | **-0.25** |
| `PpuPhase4_SpriteFetch` | 0.67% | **0.51%** | -0.16 |
| `Ppu_Tick_Visible_Prefetch` | 0.52% | **0.42%** | -0.10 |
| `Run_NTSC` | 0.45% | 0.42% | -0.03 |
| `apu_step` | 0.43% | 0.39% | -0.04 |
| `CpuRead` | 0.40% | 0.37% | -0.03 |
| `DemodulateRow_Core` | 1.13% | 1.08% | -0.05 |
| CRT `<Render>` | 0.94% | 0.97% | +0.03 |

Multiple hot methods improved; biggest wins on `PpuPhase4_VisiblePixelZone` (-0.25pp) and `PixelZone` itself (-0.12pp). The reduced phase==N branch overhead in inlined call sites means callers' machine code is tighter, fewer cache lines per dispatch.

## Pure-core baseline FPS

`AprNes/bin/Debug/benchmark_baseline.bat` (NetFx Debug, NY2011, Audio 0 / 1× / no filter):

| Version | JIT warm | Run 2 | Run 3 | Avg(2+3) |
|---|---:|---:|---:|---:|
| bf51c3e | — | 144.22 | 144.49 | **144.36** |
| After refactor | 145.88 | 143.99 | 144.72 | **144.36** |

**FPS unchanged.** I-cache improvements absorbed into noise / not yet limiting.

## Conclusion

- ✅ Source cleanliness: phase discriminator gone, each helper has single responsibility
- ✅ Inline behaviour: `Ppu2007_BusRead` 19× inline (tiny helper does that well)
- ✅ I-cache: hot methods improved noticeably (PixelZone -0.12pp, VisiblePixelZone -0.25pp)
- ✓ FPS: no regression, no gain (within noise)
- ✓ Global miss rate: persistently in excellent tier (0.53%)

Compile-time elimination of the `phase` discriminator delivered measurable per-method I-cache improvements without any framework-level cost.
