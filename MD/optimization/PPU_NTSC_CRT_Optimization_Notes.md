# PPU / NTSC / CRT Optimization Notes

Date: 2026-04-25

Scope:

- Included: `AprNes/NesCore/PPU.cs`, `ppu_new.cs`, `ppu_dispatch.cs`, `NTSC_CRT/Ntsc.cs`, `NTSC_CRT/CrtScreen*.cs`
- Excluded: all mapper implementation files
- Goal: document remaining practical performance opportunities without weakening timing correctness by default

## Current Baseline

The current PPU path is already heavily optimized:

- `ppu_dispatch.cs` uses 341-slot function-pointer tables and separates visible pixel zone, sprite fetch, prefetch, dummy, tail, vblank, and pre-render handlers.
- Visible pixel zone has a large inlined hot path instead of one monolithic generic PPU tick.
- Sprite shifters use 64-bit SWAR operations.
- Palette colors are cached in `palCache`.
- NTSC analog decode is deferred per scanline and flushed with `Parallel.For`.
- CRT scalar path already uses `Vector<T>` in several loops.

Because of that, the remaining digital PPU gains are likely incremental. The biggest remaining gains are in output-mode specialization and analog/CRT post-processing.

## 1. Split Digital And Analog Pixel Handlers

### Problem

`Ppu_Tick_Visible_PixelZone()` currently maintains both:

- RGB output pipeline: `dotColor`, `prevDotColor`, `prevPrevDotColor`, `prevPrevPrevDotColor`
- Palette-index output pipeline: `dotPalIdx`, `prevDotPalIdx`, `prevPrevDotPalIdx`, `prevPrevPrevDotPalIdx`

Digital mode writes `ScreenBuf1x[pos] = prevPrevPrevDotColor`.

Analog mode writes `ntscScanBuf[cx - 4] = prevPrevPrevDotPalIdx`, then later `Ntsc_CaptureScanline()` and `Ntsc_FlushPendingRows()` consume palette indices.

So each mode computes and shifts state that the other mode does not need.

### Proposed Implementation

Create separate visible pixel-zone handlers:

- `Ppu_Tick_Visible_PixelZone_Digital()`
- `Ppu_Tick_Visible_PixelZone_Analog()`

Then populate `ppuTickVisibleTable[0..255]` based on `AnalogEnabled` in `InitPpuDispatchTable()` or a new `ConfigurePpuDispatchTable()` method.

Digital handler:

- Keep color pipeline only.
- Remove `dotPalIdx` and `prev*DotPalIdx` updates from the hot handler.
- Pixel composition should compute `uint compositeColor` through `palCache[pa]`.
- Do not compute `compositePalIdx = ppu_ram[0x3f00 + pa] & 0x3f` unless a rare side path requires it.
- Draw path remains `ScreenBuf1x[pos] = prevPrevPrevDotColor`.

Analog handler:

- Keep palette-index pipeline only.
- Remove `dotColor` and `prev*DotColor` updates from the hot handler.
- Pixel composition should compute `byte compositePalIdx`.
- Use `ppu_ram[0x3f00 + pa] & 0x3f` for final palette index.
- Draw path remains `ntscScanBuf[cx - 4] = prevPrevPrevDotPalIdx`.

Shared logic can be copied rather than abstracted into a helper if helper calls regress the hot path. This file already favors duplication for hot specialization, so duplicated handlers would match the existing style.

### Correctness Notes

- Sprite 0 hit still depends on `bgColor`, sprite priority, and `canDetectSprite0Hit`; keep that logic identical in both handlers.
- Palette corruption still needs `bgColor` and `vram_addr`; keep `CorruptPalettes(bgColor, vram_addr)` behavior identical.
- Analog mode should still update `ntscScanBuf[255]` in `Ppu_Tick_Visible_SpriteFetch()` and call `Ntsc_CaptureScanline()` at `cx == 260`.
- If `AnalogEnabled` can be toggled while a ROM is running, rebuild dispatch table during the same pause/reinit sequence that swaps render buffers. If mode changes only through hard reset/init, rebuilding from `init()` is enough.

### Expected Benefit

- Digital mode: about 2-5%.
- Analog PPU-side work: about 3-8%.
- Higher gains are possible in games with simple scenes where post-processing is not the bottleneck.

### Risk

Medium. The risk is not algorithmic complexity; it is keeping the two handlers bit-for-bit behavior-equivalent for sprite 0, palette corruption, left-edge masking, and delayed pixel pipeline.

### Verification

- Run PPU timing and sprite tests.
- Run screenshot CRC comparisons in digital mode.
- Run analog screenshots for AV, SVideo, RF with `AnalogSize` 2/4/6/8.
- Compare at least one sprite-heavy title and one no-sprite/title-screen scene.

## 2. Add A `$2007` Pipeline Idle Fast Path

### Problem

`PPU_DATA_Pipeline_Step(int phase)` runs during full and half PPU steps. Most frames spend a large number of dots with no active CPU `$2007` read/write latch and no buffered read/write work pending.

The current code still evaluates the latch pipeline every phase.

### Proposed Implementation

Introduce a compact activity flag, for example:

```csharp
static bool ppu2007PipelineActive;
```

Set it when CPU-side handlers trigger `$2007` work:

- In `ppu_r_2007()`, after setting `ppu2007_Read_SR = true`
- In `ppu_w_2007()`, after setting `ppu2007_Write_SR = true`

Keep it active while any of these are true:

- `ppu2007_Read_SR`
- `ppu2007_Write_SR`
- `ppu2007_PD_RB`
- `ppu2007_DB_PAR`
- read/write latch values are not back at idle pattern

At the start of `PPU_DATA_Pipeline_Step`, add a very cheap branch:

```csharp
if (!ppu2007PipelineActive && phase != 1 && !ppu2007_PD_RB)
    return;
```

That exact guard is only illustrative. The real guard must preserve normal rendering fetch behavior because `PPU_DATA_Pipeline_Step(1)` also derives `ppu2007_PPU_READ` and `ppu2007_PPU_ALE`, which are consumed by tile fetch and octal latch behavior. A safer first implementation is:

- Only fast-return from phase 3 when there is no `$2007` read/write activity and no `ppu2007_PD_RB`.
- Then expand to phase 2 after tests prove no behavior loss.
- Be conservative for visible/pre-render rendering dots.

Alternative lower-risk split:

- Keep a full `PPU_DATA_Pipeline_Step1()` for phase 1.
- Add `PPU_DATA_Pipeline_Step2_IdleAware()`.
- Add `PPU_DATA_Pipeline_Step3_IdleAware()`.

### Correctness Notes

This is timing-sensitive. `$2007` read buffer, palette reads, and delayed writes are frequent emulator test targets. Do not skip phase 1 blindly unless every consumer of `ppu2007_PPU_READ`, `ppu2007_PPU_ALE`, `ppuOctalLatch`, and `ppuAddressBus` is accounted for.

### Expected Benefit

- About 2-6% in digital mode.
- Could be higher in PAL/Dendy because there are more scanlines/dots per frame.

### Risk

Medium to high if over-aggressive. Start with phase 3 only.

### Verification

- `ppu_read_buffer`
- `vram_access`
- `ppu_open_bus`
- palette RAM tests
- sprite DMA / `$2007` interaction tests if available
- compare screenshots around games that do mid-frame VRAM reads/writes

## 3. Specialize NTSC Decode By `AnalogSize`

### Problem

`Ntsc.cs` decode loops use division in the inner pixel loop:

- `int d = x / N`
- `int d = outX / N`

`N` is `ntsc_analogSize`, usually one of 2, 4, 6, or 8. Division inside the per-output-pixel loop is expensive, especially at larger output sizes.

### Proposed Implementation

Add size-specialized decode paths:

```csharp
switch (ntsc_analogSize)
{
    case 2: RunDecodeLoopScale2(...); break;
    case 4: RunDecodeLoopScale4(...); break;
    case 6: RunDecodeLoopScale6(...); break;
    case 8: RunDecodeLoopScale8(...); break;
    default: RunDecodeLoopGeneric(...); break;
}
```

Each specialized loop should iterate NES dots first:

```csharp
for (int d = 0; d < 256; d++)
{
    // compute per-dot source values once
    // emit N output pixels
}
```

For composite decode, be careful: `ph` currently advances per output pixel, not per source NES dot. The specialized loop must still advance subcarrier phase for each emitted output pixel.

For example, scale 4 conceptually becomes:

```csharp
for (int d = 0; d < 256; d++)
{
    float yD = dotY[d];
    float iD = dotI[d];
    float qD = dotQ[d];

    // emit 4 pixels, advancing ph each time
}
```

This removes division and also reduces repeated `dotY[d]`, `dotI[d]`, `dotQ[d]` address calculations.

Do the same for `DecodeAV_SVideo()`.

### Correctness Notes

- Composite phase progression must remain per output pixel.
- RF noise/herringbone state must remain per output pixel.
- `VerticalFillRows()` behavior is unchanged.
- The generic path should remain for unusual sizes or future settings.

### Expected Benefit

- Analog fast decode: about 5-15%.
- Higher at `AnalogSize` 6/8 because division and per-pixel work dominate.

### Risk

Medium-low. This is mostly mechanical if phase and noise state are preserved.

### Verification

- Pixel-diff analog screenshots for each `AnalogSize`: 2, 4, 6, 8.
- Test AV, SVideo, RF.
- Include `UltraAnalog=false` and `UltraAnalog=true` if the physical path is also specialized.

## 4. Reuse CRT Horizontal Blur Scratch Buffers

### Problem

`CrtScreenScalar.ApplyHorizontalBlur()` uses `stackalloc float[Crt_SrcW]` inside a `Parallel.For` row worker. `Crt_SrcW` is 1024, so this is about 4 KB per row invocation.

That is not catastrophic, but it is repeated many times per frame:

- 3 color planes
- 240 source rows
- every CRT-rendered frame

### Proposed Implementation

Use per-thread scratch buffers, similar to `Ntsc.cs`:

```csharp
[ThreadStatic] static float* tls_crtBlurRow;
```

Allocate once per worker thread:

```csharp
static void EnsureCrtBlurScratch()
{
    if (tls_crtBlurRow != null) return;
    tls_crtBlurRow = (float*)NesCore.AllocUnmanaged(Crt_SrcW * sizeof(float));
}
```

Then replace:

```csharp
float* src = stackalloc float[Crt_SrcW];
```

with:

```csharp
EnsureCrtBlurScratch();
float* src = tls_crtBlurRow;
```

Keep the `Buffer.MemoryCopy()` snapshot because it breaks the read-after-write hazard.

### Correctness Notes

- `ThreadStatic` buffers are safe because each `Parallel.For` worker owns its row scratch.
- Memory is process-lifetime, matching existing NTSC scratch behavior.
- Do not share one global scratch buffer across rows.

### Expected Benefit

- CRT path: about 3-10%.
- More noticeable when `HBeamSpread > 0`.

### Risk

Low to medium. The main risk is accidentally sharing a scratch buffer across threads.

### Verification

- CRT screenshots with `HBeamSpread = 0`, default, and high values.
- Check for row corruption under repeated fullscreen/resize transitions.
- Benchmark CRT render time alone if possible.

## 5. Add No-Sprite Visible Scanline Fast Path

### Problem

The pixel hot path still checks sprite state every pixel:

```csharp
if (showSpr && (cx > 8 || ShowSprLeft8) && spriteAnyActive)
```

When no sprite pixels are active on a scanline, this branch is cheap but still executed 256 times per scanline.

### Proposed Implementation

Use a per-scanline flag after sprite fetch/eval has established whether sprites can contribute:

```csharp
static bool scanlineSpritesActive;
```

Set it at the point where `spriteAnyActive` and `sprSlotCount` are known. Then split visible pixel handlers into:

- BG-only / no-sprite handler
- BG+sprite handler

This can be done by dispatch-table switching per scanline, but that is awkward because the table is indexed only by `cx`. Lower-risk alternative:

- Keep one handler.
- Add a cold branch at scanline start to set a cached `renderSpritesThisLine`.
- In pixel zone, use that cached value instead of recomputing gates.

Full specialization would require a scanline-state dispatch pointer or a second visible pixel table selected when entering a scanline.

### Correctness Notes

- Sprite 0 hit must remain correct.
- Left-8 sprite masking must remain correct.
- Games can change `$2001` mid-scanline, so this optimization is only safe when the sprite visibility state cannot change unexpectedly. A conservative version should disable the fast path if `$2001` writes occur mid-scanline.

### Expected Benefit

- About 1-4%, depending on game.
- Higher on menus/title screens with no active sprites.

### Risk

Medium. Mid-scanline `$2001` changes and sprite 0 hit make this less straightforward than it looks.

### Verification

- Sprite 0 hit tests.
- Left-edge sprite mask tests.
- Games with sprite-heavy HUDs and games with no-sprite title screens.

## 6. Split `PPU_DATA_Pipeline_Step(int phase)` Into Phase-Specific Methods

### Problem

`PPU_DATA_Pipeline_Step(int phase)` branches on `phase` internally. Call sites always know the phase:

- Phase 1 from full PPU tick before fetch
- Phase 2 from full PPU tick after fetch
- Phase 3 from half PPU tick

The JIT may inline and constant-fold some of this, but with a large method and many call sites this is not guaranteed, especially on .NET Framework.

### Proposed Implementation

Create three methods:

- `PPU_DATA_Pipeline_Phase1()`
- `PPU_DATA_Pipeline_Phase2()`
- `PPU_DATA_Pipeline_Phase3()`

Move exact code blocks into each method with no `phase` branch.

Call sites become:

```csharp
PPU_DATA_Pipeline_Phase1();
PPU_DATA_Pipeline_Phase2();
PPU_DATA_Pipeline_Phase3();
```

This also makes the idle fast path in item 2 easier to reason about.

### Correctness Notes

- Preserve exact order of latch updates.
- Do not combine phase 2 and phase 3 unless tests prove it.
- Keep method bodies small enough for inlining; use `AggressiveInlining`.

### Expected Benefit

- About 1-3%.
- Larger benefit possible on .NET Framework 4.8.1 than on modern JITs.

### Risk

Medium-low. It is mechanically safe if copied carefully, but `$2007` timing is fragile.

### Verification

Same as item 2.

## 7. Consider Sprite Overflow Precompute Fusion

### Problem

`PrecomputeOverflow()` scans OAM at dot 1 of each visible scanline. Sprite evaluation later walks OAM again per dot. This duplicates some work.

### Proposed Implementation

Instead of scanning all OAM in `PrecomputeOverflow()`, track enough information during `SpriteEvalTick()` to predict `spriteOverflowCycle`.

Possible approach:

- Maintain `foundCount` during normal evaluation.
- When the eighth sprite is found, begin tracking the overflow bug pseudo-index.
- Set `spriteOverflowCycle` at the same cycle where the current precompute method would set it.

### Correctness Notes

This is the riskiest item. NES sprite overflow behavior is famously weird, and the current precompute is isolated and easy to reason about. Fusion may save a little work but makes the state machine more complex.

### Expected Benefit

- About 1-3%.

### Risk

High. Not recommended as an early optimization unless profiling proves `PrecomputeOverflow()` is a real hot spot.

### Verification

- Dedicated sprite overflow tests.
- Games that rely on sprite overflow timing.
- Compare frame-by-frame screenshots before/after.

## Recommended Order

1. Specialize NTSC decode by `AnalogSize`.
2. Reuse CRT horizontal blur scratch buffers.
3. Split digital and analog visible pixel handlers.
4. Split `PPU_DATA_Pipeline_Step()` into phase-specific methods.
5. Add a conservative `$2007` phase 3 idle fast path.
6. Consider no-sprite scanline fast path only after profiling.
7. Leave sprite overflow fusion for last, or skip it.

## Benchmark Plan

Use separate benchmarks for:

- Digital rendering only
- Analog AV
- Analog SVideo
- Analog RF
- UltraAnalog + CRT
- PAL and Dendy, because they have different scanline counts and timing paths

Suggested metrics:

- Frames per second in headless benchmark mode
- Average frame time
- 1% low frame time if available
- Screenshot CRC / pixel-diff for correctness

Suggested test categories:

- PPU read buffer and VRAM access tests
- Sprite 0 hit tests
- Sprite overflow tests
- `$2001` mid-frame toggle tests
- Palette/open bus tests
- One MMC5 title only for CRT/NTSC output sanity, without modifying mapper files

