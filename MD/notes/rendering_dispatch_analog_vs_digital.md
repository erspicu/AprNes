# Rendering Dispatch — Analog vs Digital path comparison

Branch: `feature/rendering-refactor`
Originally written: 2026-04-25 (pre-refactor)
Updated: 2026-04-25 (post Phase A–C implementation)

> **Status**: Phases A1–A5, B, C-1–C-3 are merged. Sections below describe the
> *implemented* model. The "before" snapshot is preserved at the bottom for
> historical context.

## Quick summary (current model)

| Frequency | Digital | Analog |
|---|---|---|
| Per-dot (≈2.55M/sec NTSC) | palette-index byte write to `ntsc_rowPalettes[scanline*256 + cx]` | palette-index byte write to `ntsc_rowPalettes[scanline*256 + cx]` (identical write path) |
| Per-scanline (15K/sec) | — | — (Phase A1 dropped `Ntsc_CaptureScanline`; analog reads `ntsc_rowPalettes` row-major in flush) |
| Per-frame (60/sec, emu thread) | `Convert_PalIdxFrameToRGB(digitalFrameRgb)` then signal render | `Ntsc_FlushPendingRows()` snapshot of phase / frame-count, then signal render |
| Per-frame (60/sec, render thread) | optional filter pipeline (xBRZ / Scalex / NN / Scanline) → blit | `Crt_Render()` if Ultra+CRT → `SwapAnalogBuffers()` → blit |
| Display blit | `Render_resize` / `RenderPipeline` reads `_output` (= `digitalFrameRgb` aliased, or owned filter target) | `NativeGDI.UpdateDataPtr(AnalogScreenBufBack)` |

## Dispatch flow (current)

### Common pixel path

```
PpuPhase4_Dot339 ──┐
   sets spriteAnyActive   │
   ConfigurePpuVisibleDispatch() picks one of 4
   { Digital_Spr | Digital_NoSpr | Analog_Spr | Analog_NoSpr }
   based on AnalogEnabled + spriteAnyActive
                         │
PixelZoneImpl<TMode> (256 dots × 240 lines = 61,440 pixel writes/frame)
   ├── Mode-specialised pixel composition (palette-index lookup; for digital
   │   we no longer compute RGB per-dot — only the 6-bit palette idx)
   ├── prevDot pipeline shift (carries dotPalIdx / prevDot*PalIdx in all modes)
   └── per-pixel write at cx≥4:
         ntsc_rowPalettes[scanline*256 + (cx-4)] = prevPrevPrevDotPalIdx

(Phase A5: dotColor / RGB pipeline deleted from PixelZone. All four variants
write palette indices only.)
```

### Digital frame end (emu thread)

```
End of frame at scanline=240 cx=1:
   PpuPhase_FrameRender:
      if (!AnalogEnabled) Convert_PalIdxFrameToRGB(digitalFrameRgb)
         → ntsc_rowPalettes[256*240] → digitalFrameRgb[256*240 uint]
         (NesColors[] LUT, runs on emu thread; race-free: this completes
          before RenderScreen signals the render thread.)
      RenderScreen()
         → renderReady.Set();  emuWaiting=true;  _event.WaitOne();
      frame_count++
```

### Analog frame end (emu thread)

```
End of frame at scanline=240 cx=1:
   PpuPhase_FrameRender:
      renderDone.Wait(); renderDone.Reset();   // pace with previous frame
      Ntsc_FlushPendingRows();                 // serial scanPhase / RfBuzz capture
      Crt_SetFrameCount(frame_count);          // snapshot before render thread reads
      RenderScreen()
         → renderReady.Set();  emuWaiting=true;  _event.WaitOne();
      frame_count++
      Ntsc_SetFrameCount(frame_count);
```

### Render thread loop (single, always running)

```
RenderThreadLoop:
   while (renderThreadRunning) {
      renderReady.Wait(); renderReady.Reset();
      bool analog = NesCore.AnalogEnabled;       // read mode AT loop top
      if (analog) {
         if (UltraAnalog && CrtEnabled) Crt_Render();
         SwapAnalogBuffers();
         NativeGDI.UpdateDataPtr(AnalogScreenBufBack);
         NativeGDI.DrawImageHighSpeedtoDevice();
      } else {
         RenderObj.Render();                     // Render_resize.RenderFilter() + GDI blit
                                                 //   reads digitalFrameRgb, optional filter
      }
      // VideoRecorder push + FPS limit (single FPS limiter point)
      renderDone.Set();
   }
```

### Headless / Avalonia (sync fallback inside `RenderScreen`)

`renderThreadRunning == false` path:

```
RenderScreen (sync):
   screen_lock = true;
   if (AnalogEnabled && UltraAnalog && CrtEnabled) Crt_Render();
   VideoOutput?.Invoke(...);    // host renders synchronously
   screen_lock = false;
   emuWaiting = true; _event.WaitOne(); emuWaiting = false;
```

Avalonia uses this path; its `EmulatorEngine` callback consumes
`digitalFrameRgb` (digital) or `AnalogScreenBuf` (analog) directly.

## Buffers — ownership and size (current)

| Buffer | Path | Owner | Size | Layout / lifetime |
|---|---|---|---|---|
| `ntsc_rowPalettes` | both | NesCore | 256 × 240 × byte = 60 KB | per-pixel palette idx, scanline-major. **Always allocated** (Phase A2) for the entire ROM session. |
| `digitalFrameRgb` | digital | NesCore | 256 × 240 × uint = 240 KB | per-frame RGB pre-conversion target on emu thread. Aliased by `Render_resize._output` and `RenderPipeline._output` in the no-filter case. **Always allocated.** |
| `linearBuffer` | analog + CRT | NesCore (NTSC_CRT) | 1024 × 240 × 3 × float = ~2.81 MB | NTSC decoder output, RGB float planes |
| `ntsc_analogScreenBuf` / `AnalogScreenBuf` | analog | NesCore | up to 7.86 MB | direct decoded RGB / final blit target |
| `AnalogScreenBufBack` | analog | NesCore | same | back buffer; render thread blits this side after `SwapAnalogBuffers()` |

**No `palBuf` double buffer.** The plan originally proposed a `palBuf` ↔
`palBuf_back` swap, but `Convert_PalIdxFrameToRGB` runs on the emu thread
*before* signaling the render thread. The render thread only ever reads
`digitalFrameRgb`, and the emu thread never writes `digitalFrameRgb`
between the signal and `_event.WaitOne()` returning. Race-free without a
swap.

**No `ScreenBuf1x`.** Phase A5 deleted it. Headless test ROM signature
checks (`TestRunnerCore.cs`) read `ntsc_rowPalettes` directly.

## Dispatch decision points (unchanged from original)

| Axis | Set when | Affects |
|---|---|---|
| `AnalogEnabled` | UI config; `ConfigurePpuVisibleDispatch()` reseats slots; `PauseEmuAndRender()` ensures swap is safe | PixelZone variant + render thread per-frame branch |
| `spriteAnyActive` | end of previous scanline (`PpuPhase4_Dot339`) | Spr vs NoSpr PixelZone variant |
| `ntsc_ultraAnalog` | UI config | `DecodeScanline_Physical` vs `DecodeScanline_Fast` |
| `ntsc_crtEnabled` | UI config | DemodulateRow target: `linearBuffer` vs `analogScreenBuf` directly |
| `ntsc_analogOutput` | AV / SVideo / RF | composite vs SVideo path |

## Mode-toggle topology (Phase C-2 invariant)

The render thread **never starts/stops** during normal operation. It is
spawned once when ROM starts and joined only at app exit. Mode toggles
(digital ↔ analog) only flip data:

1. UI calls `PauseEmuAndRender()` — resets `_event`, spins until
   `emuWaiting == true`, then waits for in-flight render via
   `renderDone.Wait()`. Both threads are now quiesced at known points.
2. `ApplyRenderSettings` swaps `RenderObj`, reallocates analog buffers,
   re-runs `ConfigurePpuVisibleDispatch()`.
3. `_event.Set()` resumes the emu thread; next frame's `RenderScreen`
   signals the render thread, which reads the new `AnalogEnabled` flag at
   the top of its loop.

This eliminates the deadlock failure mode of the previous "two render
threads" attempt.

## Buffer-ownership flag (`_ownsOutput`)

Both `Render_resize` (NetFx) and `RenderPipeline` (Avalonia) set
`_output = NesCore.digitalFrameRgb` directly when neither stage 1 nor
stage 2 filter is active (1× no-filter case). To prevent `freeMem()`
from freeing the shared buffer, a parallel `_ownsOutput` bool tracks
whether `_output` was allocated locally (`true`) or aliased
(`false`). `freeMem()` only calls `FreeUnmanaged` when `_ownsOutput`.
This is the Phase C-3 fix for the "1× no-filter → analog mode toggle
crash".

## Asymmetries that remain (intentional)

### Per-frame compute is still asymmetric

- Digital: cheap palette→RGB (one LUT, ~60K entries) on emu thread, then
  optional filter on render thread.
- Analog: heavy `Ntsc_FlushPendingRows` (240 parallel decodes ≈ 2.5 GFLOPS)
  + optional CRT pipeline (~3 GFLOPS) on render thread.

Phase B moved CRT to render thread, so emu thread is no longer blocked on
the analog frame-end work — it just waits on `renderDone` if the previous
frame is still running.

### Display layer is still split

- Digital: `Render_resize.RenderFilter()` runs filter pipeline against
  `digitalFrameRgb`, blits via `NativeGDI`.
- Analog: render thread directly blits `AnalogScreenBufBack` via
  `NativeGDI.UpdateDataPtr` + `DrawImageHighSpeedtoDevice`.

Avalonia parallels: `EmulatorEngine` instead of `RenderObj`. Same
conceptual split, no `NativeGDI`.

## Re-cap of CRT backend selection (analog path only)

| Backend | Source files | When used |
|---|---|---|
| Scalar | `CrtScreen.cs` | NetFx default; Avalonia explicit `--crt-strategy scalar` |
| SIMD | `CrtScreen.Simd.cs` | Avalonia `--crt-strategy simd` (.NET 10 only) |
| GPU (SkSL) | `CrtScreen.Gpu.cs` | Avalonia default (`Crt_SetBackend(Gpu)` in `Program.cs`) |

GPU bypasses `Crt_Render()` on the CPU; the SkSL shader consumes
`linearBuffer` directly via Avalonia's GPU thread (`CrtGpuRenderThread`).
Phase B/C does not touch the GPU CRT path.

## Refactor candidates that are now closed

| Original idea | Status |
|---|---|
| Unify `Render_Analog` / `Render_resize` zero-copy | Partially done — both paths share render thread + `digitalFrameRgb` aliasing. Full interface unification deferred. |
| Avalonia: drop NetFx-style `Render_resize` | Open. Avalonia keeps `RenderPipeline` for the same filter chain. |
| Pull CRT trigger out of NesCore | Done (Phase B): `Crt_Render` is on the render thread. |
| `palBuf` → `linearBuffer` direct path | Done (Phase A1): `Ntsc_CaptureScanline` removed; demod reads `ntsc_rowPalettes` directly. |
| PixelZone analog direct write to `palBuf` | Done (Phase A1, but write target is `ntsc_rowPalettes`, not a separate `palBuf`). |
| Dual-write digital RGB + palette idx (Phase A4 transitional) | Done and removed (Phase A5: digital writes palette idx only). |

---

## Original (pre-refactor) snapshot

The original 2026-04-25 dispatch description has been removed since it no
longer reflects any active code path. The rough shape was:

- Digital wrote RGB uint to `ScreenBuf1x[]` per pixel.
- Analog wrote palette idx to `ntscScanBuf[256]` per pixel, then
  `Ntsc_CaptureScanline` copied it + emphasis into `palBuf[256*240]` at
  cx=260.
- `RenderScreen` and `Crt_Render` both ran on the emu thread; only the
  GDI blit was off-thread (and only for analog).

If you need that text, see this file at commit `ffd73ee`
(`docs(render): rendering refactor plan ...`).
