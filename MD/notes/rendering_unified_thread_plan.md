# Rendering Refactor — Unified render thread + CRT/filter relocation (Option 2+3)

Branch: `feature/rendering-refactor`
Originally written: 2026-04-25
Last updated: 2026-04-25

## Implementation status

| Phase | Status | Commits |
|---|---|---|
| A1 — Analog writes directly to `ntsc_rowPalettes` | ✅ Merged | `8be4815` |
| A2 — `ntsc_rowPalettes` always-allocated | ✅ Merged | `c37359f` |
| A3 — Digital writes palIdx to `ntsc_rowPalettes` (dual-write transitional) | ✅ Merged | `90ea96b` |
| A4a — `Render_resize` reads `ntsc_rowPalettes` via palette→RGB conversion | ✅ Merged | `37d1411` |
| A4b — Avalonia `RenderPipeline` reads `ntsc_rowPalettes` | ✅ Merged | `a7c0627` |
| A5 — Drop `ScreenBuf1x` + dotColor pipeline + dual write | ✅ Merged | `36b7acb` |
| B — Move `Crt_Render` + `SwapAnalogBuffers` to render thread | ✅ Merged | `e2f4b26` |
| C-1 — Rename `analog*` render thread symbols → `render*` | ✅ Merged | `1eb6ff6` |
| C-2 — Render thread always-running across mode toggles | ✅ Merged | `d18c889` |
| C-3 — Digital path through render thread (NetFx) | ✅ Merged | `d0e06a1` |
| C-3 fix — `_ownsOutput` flag (don't free aliased buffers) | ✅ Merged | `1b1235e` |
| D — Cleanup + Avalonia parity + documentation | 🚧 In progress | — |

**Validated**: 184/184 blargg + 138/138 AccuracyCoin v2 (no regression).

The implemented model differs from this plan in one important way: there is
**no `palBuf` / `palBuf_back` double buffer**. `ntsc_rowPalettes` is the
single 60 KB palette-index frame buffer; the digital path pre-converts
to a separate `digitalFrameRgb` (256×240 uint, always allocated) on the
emu thread *before* signaling the render thread, which makes the swap
unnecessary. See `rendering_dispatch_analog_vs_digital.md` for the
post-refactor architecture.

## Why option 1 was unstable (lessons learned)

User reported a previous attempt at "give digital its own render thread too" hit deadlock issues during mode switching. Root cause analysis:

1. **Two render threads + UI thread + emu thread = 4 threads sharing GDI device** — too many shared resources.
2. **Mode switch** has to stop one thread and start another; if either is mid-blit when the swap happens, signal ordering becomes path-dependent.
3. **Buffer ownership** transfers across thread boundaries during the switch — easy to free a buffer that another thread still holds.

## Design goal

```
                  ┌─────────────────────┐
                  │   Emu thread        │
                  │   (CPU/PPU/APU)     │  ← only emulator state advancing
                  │   Output: palBuf    │
                  └──────────┬──────────┘
                             │ swap signal
                             ▼
                  ┌─────────────────────┐
                  │   Render thread     │  ← 1 thread, mode-aware internally
                  │   (a) palette→RGB   │
                  │       digital path  │
                  │   (b) NTSC demod    │
                  │       + CRT pipeline
                  │       analog path   │
                  │   (c) blit to GDI   │
                  └──────────┬──────────┘
                             │ done signal
                             ▼
                          (UI thread receives Invalidate)
```

Key invariants:

- **Emu thread never blocks on GDI** (eliminates UI-paced FPS limit on digital).
- **Render thread is mode-aware but always exists** (no start/stop on mode toggle → no deadlock risk during switch).
- **Buffers are mode-stable**: same pointer slots regardless of digital/analog. Allocation lifetime = process lifetime.
- **Single double-buffer pair** (`palBuf` + `palBuf_back` = 60 KB × 2 = 120 KB) is the only swap-protected buffer. Everything downstream (linearBuffer, AnalogScreenBuf) is render-thread-private.

## Phased plan

### Phase A — Unify emu output to palette indices

**Goal**: emu thread writes only palette indices to `palBuf`, regardless of digital/analog.

Current state:
- Digital `PixelZone_Digital_Spr/NoSpr` writes RGB uint to `ScreenBuf1x`
- Analog `PixelZone_Analog_Spr/NoSpr` writes palette idx to `ntscScanBuf` → captured to `palBuf`

After Phase A:
- Both digital and analog handlers write palette idx to `palBuf` (or `ntscScanBuf` per-scanline → `palBuf`, same as analog today)
- `ScreenBuf1x` deleted
- Digital pipeline RGB conversion moves to render thread

Risk:
- Per-pixel work shifts — `palCache[pa]` lookup happens at scale time on render thread instead of per-emu-dot. Could affect timing-sensitive features like sprite-0-hit if not careful (but sprite-0-hit reads `bgColor`, not RGB, so safe).
- Test impact: 184 blargg + AC v2 should be unchanged. Validate.

Effort: ~2-3 days. Touches `ppu_dispatch.cs` (4 handler variants), `Main.cs` (buffer allocation), `tool/InterfaceGraphic.cs` (Render_resize), `Avalonia/EmulatorEngine.cs`.

### Phase B — Move CRT out of emu thread

**Goal**: `Crt_Render()` no longer called from `PpuPhase_FrameRender` / `RenderScreen`. Render thread invokes it after demod.

Current flow (analog):
```
emu thread:
   PpuPhase_FrameRender → Ntsc_FlushPendingRows() → Crt_Render() → SwapAnalogBuffers
   (~20 ms of work on emu thread)
```

After Phase B:
```
emu thread:
   PpuPhase_FrameRender → swap palBuf → signal render thread → return immediately
render thread:
   Ntsc_FlushPendingRows() → Crt_Render() → blit
   (~16 ms but parallel with next emu frame)
```

Net effect: **emu thread per-frame CPU drops by ~50%** for analog mode. Real FPS gain in `--ultra-analog --crt` config could be substantial (currently emu+CRT ≈ 22ms/frame; after split, max(emu, CRT) ≈ 12ms).

Risk:
- `linearBuffer` becomes render-thread-private (was shared between emu and render) — must verify nothing else reads it from emu side. Quick grep should confirm.
- `Ntsc_FlushPendingRows` uses `Parallel.For` internally — moving to render thread is fine; render thread becomes the parent for that parallelism.
- CRT GPU backend (Avalonia default) currently runs on its own `CrtGpuRenderThread` — this Phase B applies to **Scalar/SIMD CRT only**. GPU backend already off emu thread.

Effort: ~3-4 days. Touches `RenderScreen()`, render thread loop, both NetFx and Avalonia path.

### Phase C — Unify render thread to one entry, mode-aware internally

**Goal**: replace `AnalogRenderThreadLoop` with `UnifiedRenderThreadLoop` that handles both modes.

```csharp
unsafe void UnifiedRenderThreadLoop()
{
    while (renderThreadRunning)
    {
        renderReady.Wait();
        renderReady.Reset();

        // Read snapshot of mode AFTER signal — emu thread guarantees mode is
        // stable for this frame (set during ApplyRenderSettings, frozen during
        // active frames).
        bool analog = NesCore.AnalogEnabled;
        bool crt = NesCore.CrtEnabled;
        bool ultra = NesCore.UltraAnalog;

        if (analog) {
            Ntsc_FlushPendingRows();          // demod palBuf → linearBuffer or analogScreenBuf
            if (crt && backend != GPU) Crt_Render();  // CPU CRT pipelines
            // GPU CRT handled by Avalonia's GPU thread (no-op here)
        } else {
            // Digital: convert palBuf → display buffer with optional filter/scale
            DigitalRenderPipeline(palBuf_back);
        }

        NativeGDI.UpdateDataPtr(displayBufferBack);
        NativeGDI.DrawImageHighSpeedtoDevice();

        if (VideoRecorder.IsRecording) VideoRecorder.PushFrame(displayBufferBack);
        if (LimitFPS) FpsLimitSleep();

        renderDone.Set();
    }
}
```

Buffer ownership becomes simple:
- `palBuf` / `palBuf_back` (60 KB each) — emu writes front, render reads back. Always allocated.
- `linearBuffer`, `analogScreenBuf`, `analogScreenBufBack` — render-thread-private. Allocated lazily on first analog use, never freed during runtime.
- `displayBuffer` (digital scaled / analog post-CRT) — render-thread-private.

Mode toggle no longer requires render thread restart:
- `ApplyRenderSettings()` sets `AnalogEnabled` between frames (still signaled via `_event` like today).
- Render thread reads the flag at the top of each loop iteration. No stale state.
- No buffer reallocation needed at mode toggle (buffers always exist).

Effort: ~2-3 days. Touches `AprNesUI.cs` (rename + merge thread loops), Avalonia `EmulatorEngine.cs`, ApplyRenderSettings paths.

### Phase D — Cleanup + Avalonia parity

- Delete `ScreenBuf1x` and `Render_resize` if Phase A/B/C wiring replaces all consumers.
- Ensure headless `TestRunner` falls through to a synchronous path (no render thread, just call render functions directly on emu thread).
- Avalonia: confirm Avalonia's compositor + `EmulatorEngine.OnFrameComplete` still receive correct buffer pointer.
- Documentation: rewrite `MD/notes/rendering_dispatch_*.md` to reflect new unified model.

Effort: ~2 days.

## Total estimate

~10 days of focused work (~3-4 commits per phase). Each phase is independently testable and revertable.

## Anti-deadlock guarantees

The previous attempt deadlocked because:
1. Two render threads (digital + analog) competed for GDI device.
2. Mode switch had to stop one and start the other; signal ordering unclear.

This plan eliminates both by:
1. **Always exactly one render thread.** Mode is data, not topology.
2. **Render thread reads mode at loop top.** No mid-frame mode change possible.
3. **Buffer pointers are stable** for the process lifetime. No allocation/free during normal operation.
4. **No GDI access from emu thread.** Emu thread only writes palBuf (CPU memory). Render thread is the sole GDI consumer.

## Risk register

| Risk | Mitigation |
|---|---|
| Phase A regresses sprite-0-hit / palette corruption tests | Phase A doesn't change emu logic — only the output write target. Pixel composition still computes bgColor for sprite-0-hit. |
| Phase B exposes a hidden read of linearBuffer from emu thread | Pre-Phase-B grep + audit. Easy to verify. |
| Avalonia GPU CRT backend disrupted | Phase B only affects CPU CRT (Scalar/Simd). GPU runs on Avalonia's own thread, unrelated. |
| Render thread can't keep up → frame drops | Acceptable degradation: render thread skips frames if behind. Emu maintains real-time. |
| Mode toggle race with active render | Render thread reads mode at loop top; ApplyRenderSettings waits for current render to finish (existing `analogRenderDone.Wait` mechanism). |
| Headless TestRunner has no render thread | Synchronous fall-through (no signal, just direct call). Already partially handled at PPU.cs:917 (`!analogRenderThreadRunning` branch). |
| FPS limiter currently lives in render thread | Stays in render thread (single FPS limit point, unaffected by mode). |
| Video recording / screenshot | Render thread is the natural producer for both — pushes to VideoRecorder, snapshots displayBufferBack. |

## What this DOESN'T solve (out of scope)

- **Avalonia GPU CRT path** — already separate via `CrtGpuRenderThread`. We don't unify GPU and CPU CRT pipelines (they have different SDK requirements: GPU needs Avalonia's SkiaSharp lease).
- **Multi-NES instances** — current emu is single-instance. Multi-instance would need per-instance buffers + per-instance render threads.
- **Tear-free vsync** — render thread blits to GDI device which doesn't expose vblank. Vsync would need DXGI/Composition or Avalonia compositor integration.

## Decisions (resolved)

1. **Per-pixel direct write** to `palBuf` for both digital and analog. Reasoning: 256 sequential bytes per scanline stay L1-resident across the visible run; eliminating the per-scanline copy step (the current analog `Ntsc_CaptureScanline` pattern) saves ~14K memcpy ops per frame. Analog still stores per-scanline emphasis, but in a separate small array (`emphPerLine[240]`), not bundled with `palBuf`.

2. **Keep xBRZ / Scalex / Scanline filters.** Render thread is the new owner — it reads `palBuf`, applies palette→RGB, then runs filter pipeline, then blits.

3. (Open) Whether render thread does its own CRT GPU dispatch or leaves that to Avalonia. Defer to Phase D — depends on Avalonia integration discoveries during earlier phases.

## Recommended starting point

**Phase A first**, on `feature/rendering-refactor` branch:
- Smallest delta, immediate testability
- Validates the "emu writes palette indices uniformly" assumption
- If it passes 184 blargg + AC v2 + visual diff on screenshot CRC, proceed to Phase B
- If it regresses, we learn early without large rollback

Want to commit this plan as the branch's first commit and start Phase A?

---

## Post-implementation deviations from the plan

The shipped result diverges from the original plan in a few places worth
recording for anyone tracing the commit history.

1. **No `palBuf` / `palBuf_back` swap.** The digital path pre-converts on
   the emu thread (`Convert_PalIdxFrameToRGB`) before the render-thread
   signal. The render thread reads `digitalFrameRgb` exclusively; emu
   never writes that buffer between signal and `_event` resume. Phase A's
   "double-buffered palette" idea collapsed to a single
   `ntsc_rowPalettes` (palette buffer) + a single `digitalFrameRgb` (RGB
   buffer), both always allocated.

2. **`Render_resize._output` aliases `digitalFrameRgb` in 1× no-filter
   case.** This avoids a per-frame copy, but introduced a use-after-free
   when toggling digital → analog (the alias was being freed in
   `freeMem`). Resolved by adding a parallel `_ownsOutput` bool that
   `freeMem` consults — the Phase C-3 fix commit (`1b1235e`). Same
   pattern applied to `AprNesAvalonia/Platform/RenderPipeline.cs`.

3. **`PauseEmuAndRender` helper instead of `analogRenderDone.Wait`
   alone.** Mode-toggle quiescence needs both threads parked: emu at
   `_event.WaitOne()` (signaled by `emuWaiting=true`) AND render at the
   tail of its loop iteration (signaled by `renderDone.Wait()`). The
   helper centralises this in `AprNesUI.cs`.

4. **Headless / Avalonia sync fallback path.** `RenderScreen` branches
   on `renderThreadRunning`. When false, it runs CRT + `VideoOutput`
   inline on the emu thread and then waits on `_event` — the same exit
   condition as the threaded path. This kept Avalonia working unchanged
   through the refactor.

5. **Phase D scope reduced.** `Render_resize` is *not* deleted —
   Avalonia still uses an equivalent (`RenderPipeline`) and NetFx still
   uses it directly. Phase D's actual deliverables are: documentation
   updates (this file + `rendering_dispatch_analog_vs_digital.md`) and
   minor dead-code cleanup (residual `_input` / `_rgbInput` fields in
   `Render_resize` and `RenderPipeline`).
