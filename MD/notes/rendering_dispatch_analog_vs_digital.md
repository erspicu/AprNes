# Rendering Dispatch — Analog vs Digital path comparison

Branch: `feature/rendering-refactor`
Date: 2026-04-25

## Quick summary

| Frequency | Digital | Analog |
|---|---|---|
| Per-dot (≈2.55M/sec NTSC) | RGB uint write to `ScreenBuf1x[]` | palette-index byte write to `ntscScanBuf[]` |
| Per-scanline (15K/sec) | — | `Ntsc_CaptureScanline()` copies `ntscScanBuf` + emphasis into per-frame `palBuf` |
| Per-frame (60/sec) | `RenderScreen()` (1× upscale + filter pipeline) | `Ntsc_FlushPendingRows()` + (optional) `Crt_Render()` |
| Display blit | `Render_resize` reads `ScreenBuf1x` | `Render_Analog` reads `AnalogScreenBuf` |

## Dispatch flow

### Digital

```
PpuPhase4_Dot339 ──┐
   sets spriteAnyActive   │
   ConfigurePpuVisibleDispatch() picks
   { Digital_Spr | Digital_NoSpr }
   based on AnalogEnabled=false + spriteAnyActive
                         │
PixelZone_Digital_Spr/NoSpr (256 dots × 240 lines = 61,440/frame)
   ├── Mode-specialised pixel composition (palCache lookup → uint compositeColor)
   ├── prevDot pipeline shift (ONLY dotColor / prevDot*Color)
   └── per-pixel write at cx≥4:
         ScreenBuf1x[scanline*256 + (cx-4)] = prevPrevPrevDotColor

… (no per-scanline capture; SpriteFetch / Prefetch / Dummy / Tail are
   shared between digital and analog) …

End of frame at scanline=240 cx=1:
   PpuPhase_FrameRender:
      RenderScreen()
      frame_count++

UI side (Render_resize):
   ScreenBuf1x (256×240 uint) → optional filter (xBRZ / Scalex / Scanline) →
   final scaled buffer → GDI blit to window
```

### Analog

```
PpuPhase4_Dot339 ──┐
   sets spriteAnyActive   │
   ConfigurePpuVisibleDispatch() picks
   { Analog_Spr | Analog_NoSpr }
   based on AnalogEnabled=true + spriteAnyActive
                         │
PixelZone_Analog_Spr/NoSpr (256 dots × 240 lines = 61,440/frame)
   ├── Mode-specialised pixel composition (ppu_ram[0x3f00 + pa] → byte compositePalIdx)
   ├── prevDot pipeline shift (ONLY dotPalIdx / prevDot*PalIdx)
   └── per-pixel write at cx≥4:
         ntscScanBuf[cx-4] = prevPrevPrevDotPalIdx

Visible_SpriteFetch at cx=259 (entry 258):
   ntscScanBuf[255] = prevPrevPrevDotPalIdx  (final pixel of scanline)
Visible_SpriteFetch at cx=260 (entry 259):
   Ntsc_CaptureScanline(scanline, ntscScanBuf, ppuEmphasis)
      → copies 256 bytes + emphasis into palBuf[scanline*256..]

End of frame at scanline=240 cx=1:
   PpuPhase_FrameRender:
      Ntsc_FlushPendingRows()  ← Parallel.For(0..240) per scanline
         per scanline:
            UltraAnalog → DecodeScanline_Physical_Worker
               GenerateWaveform / GenerateWaveform_SVideo → DemodulateRow_Core
            non-Ultra   → DecodeScanline_Fast_Worker
               → DecodeAV_Composite | DecodeAV_SVideo (RunDecodeLoop<Scale*>)

         DemodulateRow_Core / DecodeAV_* writes:
            CrtEnabled=true  → linearBuffer (RGB float planes, 1024×240×3)
            CrtEnabled=false → ntsc_analogScreenBuf (uint RGB)

      RenderScreen()
      Ntsc_SetFrameCount(frame_count)
      Crt_SetFrameCount(frame_count)
      frame_count++

      (separately) Crt_Render() called from UI thread / async pipeline:
         CrtScreenScalar | Simd | Gpu .Render()
            consumes linearBuffer
            → ApplyHorizontalBlur, scanline weights, curvature, glow, …
            → AnalogScreenBuf

UI side (Render_Analog):
   AnalogScreenBuf (Crt_DstW × Crt_DstH uint, e.g. 1024×960 at 4×) →
   direct GDI blit (no upscale/filter on top)
```

## Dispatch decision points

Three orthogonal axes determine which handler / pipeline runs:

| Axis | Set when | Affects |
|---|---|---|
| `AnalogEnabled` | UI config (rebuilt via `ConfigurePpuVisibleDispatch()` on toggle) | PixelZone variant + `Ntsc_CaptureScanline` runs only when true + `Ntsc_FlushPendingRows` runs only when true |
| `spriteAnyActive` | end of previous scanline (`PpuPhase4_Dot339`) | Spr vs NoSpr PixelZone variant |
| `ntsc_ultraAnalog` (analog only) | UI config | `DecodeScanline_Physical` (slow, full waveform) vs `DecodeScanline_Fast` (skip waveform synthesis) |
| `ntsc_crtEnabled` (analog only) | UI config | DemodulateRow output: `linearBuffer` (CRT input) vs direct `analogScreenBuf` |
| `ntsc_analogOutput` (analog only) | AV / SVideo / RF | Inside DecodeScanline: composite vs SVideo path; inside Physical: RF noise / herring on |

## Buffers — ownership and size

| Buffer | Path | Owner | Size | Layout |
|---|---|---|---|---|
| `ScreenBuf1x` | Digital | NesCore | 256 × 240 × uint = 245 KB | RGB pixel grid, scanline-major |
| `ntscScanBuf` | Analog (per-scanline) | NesCore | 256 × byte = 256 B | per-scanline palette indices, overwritten each scanline |
| `palBuf` | Analog (frame buffer) | inside Ntsc.cs | 256 × 240 × byte = 60 KB | per-frame palette + emphasis (per scanline) |
| `linearBuffer` | Analog + CRT | NesCore (NTSC_CRT) | 1024 × 240 × 3 × float = 2.81 MB | RGB float planes, NTSC decoder output |
| `ntsc_analogScreenBuf` | Analog (no CRT) | NesCore (NTSC_CRT) | 1024×210×uint × 4× = up to 7.86 MB | direct decoded RGB |
| `AnalogScreenBuf` (= `ntsc_analogScreenBuf`) | Analog (CRT output) | NesCore | same as above | final blit target after CRT |
| `AnalogScreenBufBack` | Analog (double buffer) | NesCore | same | CRT async render swap |

## Key asymmetries (refactor opportunities)

### A. Per-pixel divergence is symmetric — already split

PixelZone has 4 variants (`Digital_Spr/NoSpr`, `Analog_Spr/NoSpr`). The per-pixel paths are entirely separate after the recent refactor. Nothing to consolidate here without losing the const-fold benefit.

### B. Per-scanline path is asymmetric

- Digital: zero per-scanline work (just keeps writing to `ScreenBuf1x`).
- Analog: `Ntsc_CaptureScanline` runs at cx=260 to snapshot into `palBuf` with emphasis.

This is correct — digital doesn't need a snapshot because its data target is already the final framebuffer.

### C. Per-frame path is dramatically different

- Digital: cheap. Just `RenderScreen()` + filter on UI side.
- Analog: heavy. `Ntsc_FlushPendingRows` runs 240 parallel decodes (~2.5 GFLOPS of NTSC math) + optional CRT pipeline (~3 GFLOPS).

This is the correct asymmetry. The PerfView trace shows analog frame work (DemodulateRow_Core 11.9% Excl + CRT lambdas 24.8%+19.0%) dominates the frame. **No restructuring on the dispatch side will help here — the cost is intrinsic to the math.**

### D. Display layer split (`Render_Analog` vs `Render_resize`) is real

- Digital: `Render_resize` reads from `ScreenBuf1x` (256×240) and applies UI-side scaling + filtering.
- Analog: `Render_Analog` directly references `AnalogScreenBuf` (already at full Crt_Dst size).

Avalonia parallels: `EmulatorEngine` instead of `RenderObj`, but same conceptual split.

### E. UI-side toggle of AnalogEnabled

`ApplyRenderSettings()` (NetFx + Avalonia both wired) tears down old `RenderObj` / `EmulatorEngine` pipeline, allocates/frees `AnalogScreenBuf`, calls `Ntsc_Init` + `Crt_Init`, and runs `ConfigurePpuVisibleDispatch()` to repopulate slots 0-255. This is the only point where the dispatch state changes.

## Re-cap of CRT backend selection (analog path only)

| Backend | Source files | When used |
|---|---|---|
| Scalar | `CrtScreen.cs` | NetFx default; Avalonia explicit `--crt-strategy scalar` |
| SIMD | `CrtScreen.Simd.cs` | Avalonia `--crt-strategy simd` (.NET 10 only) |
| GPU (SkSL) | `CrtScreen.Gpu.cs` | Avalonia default (`Crt_SetBackend(Gpu)` in `Program.cs`) |

GPU bypasses `Crt_Render()` on the CPU; instead the SkSL shader consumes `linearBuffer` directly via Avalonia rendering thread (`CrtGpuRenderThread`).

## Refactor candidates worth thinking about

(For the new branch — pure brainstorming, not commitments.)

1. **Unify `Render_Analog` and `Render_resize`** under a single zero-copy interface that just hands a buffer pointer + format to the display layer. Both currently work but go through different `InterfaceGraphic` subclasses and different lifetime management.

2. **Avalonia: drop NetFx-style `Render_resize`** entirely on Avalonia — Avalonia already has its own GameCanvas pipeline that can scale via the framework. Currently we replicate WinForms's filter pipeline on Avalonia even though Avalonia's compositor could do it for us at zero cost.

3. **Pull CRT pipeline trigger out of NesCore** — `Crt_Render` is called from a renderer thread on Avalonia (`CrtGpuRenderThread`) but baked into `RenderScreen()` flow on NetFx. The trigger point should be unified.

4. **`palBuf` → `linearBuffer` direct path** when CRT is on. Currently:
   ```
   ntscScanBuf → palBuf → DemodulateRow → linearBuffer → CrtScreenScalar → AnalogScreenBuf
   ```
   The intermediate `palBuf` could be eliminated if `Ntsc_CaptureScanline` was changed to write directly into the demodulator's input format. Saves 60 KB scratch + one copy/scanline.

5. **PixelZone analog draw direction** — analog path writes per-pixel to `ntscScanBuf` then captures at cx=260. If we changed PixelZone analog to write **directly** into `palBuf[scanline*256 + (cx-4)]`, we'd drop the per-scanline capture step entirely. Risk: `palBuf` is large (60 KB) and writing into a per-frame array per-pixel may have cache implications worth testing.
