# PixelZone Split — Digital / Analog (#1)

- **Date**: 2026-04-25
- **Source**: `MD/optimization/PPU_NTSC_CRT_Optimization_Notes.md` item #1
- **Build**: Debug x64, .NET Framework 4.8.1

## Refactor

`Ppu_Tick_Visible_PixelZone()` (the hottest visible-line handler, 256 dispatches per scanline × 240 lines = 61 440/frame) used to maintain BOTH output pipelines and gate at draw via `if (AnalogEnabled)`. Replaced with two specialized variants selected at config time:

```
Ppu_Tick_Visible_PixelZone_Digital()  — only dotColor pipeline (4 prevDot*Color)
                                       — composition computes only compositeColor
                                       — draw: ScreenBuf1x[pos] = prevPrevPrevDotColor

Ppu_Tick_Visible_PixelZone_Analog()   — only dotPalIdx pipeline (4 prevDot*PalIdx)
                                       — composition computes only compositePalIdx
                                       — draw: ntscScanBuf[cx-4] = prevPrevPrevDotPalIdx
```

Wired through new public helper `ConfigurePpuVisibleDispatch()` that re-populates `ppuTickVisibleTable[0..255]` based on current `AnalogEnabled`. Called from:
- `InitPpuDispatchTable()` (init time)
- `AprNesUI.ApplyRenderSettings()` (NetFx, runtime AnalogEnabled toggle)
- `EmulatorEngine.ApplyRenderSettings()` (Avalonia, runtime AnalogEnabled toggle)

Sprite 0 hit, palette corruption, bgColor logic, sprite mux all kept identical between handlers (sprite 0 needs bgColor regardless of mode).

## Per-method results (AV-heavy trace, AnalogEnabled = true → Analog handler active)

| Metric | Before (generic PixelZone) | After (PixelZone_Analog) | Δ |
|---|---:|---:|---:|
| IL size | 1891 bytes | **1773 bytes** | **-118 (-6%)** |
| CPU Excl% | 8.8% | **8.5%** | -0.3pp |
| I-cache miss% | 0.79% | **0.70%** | **-0.09pp** ↑ |

## Pure-core baseline (AnalogEnabled = false → Digital handler active)

`benchmark_baseline.bat` (NTSC, Audio 0, 1×, no filter):

| Version | Run 2 | Run 3 | Avg |
|---|---:|---:|---:|
| Before split | 143.99 / 145.92 | 144.72 / 144.41 | 145.17 |
| After split | 147.09 | 146.73 | **146.91** |

**+1.74 FPS (+1.2%)** — likely real (Digital handler skips ~10 lines of palIdx work × 61 440 dispatches/frame). Source doc estimated 2-5% for digital mode; we got a smaller portion at the lower bound but still positive.

## Global metrics

| Metric | Before | After |
|---|---:|---:|
| Global I-cache miss | 0.51% | 0.52% (noise) |
| Total NesCore CPU | ~84.9% | similar |

## Avalonia compatibility

- Avalonia uses the same shared NesCore source. Both Digital and Analog handlers are compiled.
- Default Avalonia config (GPU CRT + ultra-analog): both handlers benefit when active.
- Runtime AnalogEnabled toggle wired via `EmulatorEngine.ApplyRenderSettings()` (analogous to NetFx hook).

## Conclusion

- ✅ Two handlers compile and run on both NetFx + .NET 10
- ✅ Digital path: +1.2% FPS (pure-core baseline)
- ✅ Analog path: -118 IL, -0.09pp cache miss, -0.3pp CPU Excl
- ✅ Sprite-0 hit / palette / sprite mux behavior preserved
- ✅ Runtime AnalogEnabled toggle supported via ConfigurePpuVisibleDispatch()
