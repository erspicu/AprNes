# 08 PPU Rendering Pipeline

## What This Chapter Solves

The NES background is not a bitmap, and sprites are not painted by the CPU. The PPU follows fixed timing to fetch tiles, attributes, patterns, and sprite data, then funnels everything through shift registers and priority rules to produce a palette index per pixel.

This chapter describes the PPU rendering pipeline and how AprNes implements dot-level behaviour in `ppu_new.cs`.

## NES Hardware Concepts

**Everyday analogy**: think of the PPU as a restaurant **plating decorator**, drawing 60 plate compositions per second. A complete plating consists of 240 horizontal **decoration strips** (scanlines). Each strip takes 341 actions (dots):
- Actions 1–256: **actually plating** (visible pixels).
- Actions 257–340: prepping the next strip (prefetching the next scanline's tile/sprite data).

After 240 strips, the restaurant briefly closes for 20 strips (VBlank) — only then can the chef (CPU) safely restock (modify PPU VRAM).

```text
One frame = 262 scanlines (NTSC):

  scanline   0 ─┐
              │ │ ← 240 visible scanlines
              │ │   each is 341 dots; first 256 are visible pixels
  scanline 239 ─┘
  scanline 240    post-render (PPU idles)
  scanline 241 ─┐ ← VBlank starts
              │ │ ← VBlank period (20 scanlines)
              │ │   CPU updates PPU contents now
  scanline 260 ─┘   PPU asserts NMI to notify CPU
  scanline 261    pre-render (PPU prefetches scanline 0 data)

  next frame's scanline 0 begins ...
```

**Why do games concentrate PPU writes during VBlank?** Because during visible scanlines the PPU is using the VRAM bus to fetch tile data. If the CPU writes VRAM at the same time, it disrupts the active rendering. VBlank is the **VRAM-access pause** window — the game's "free moment" to modify the screen.

Each scanline is 341 dots. Visible pixels are the first 256, but in the later dots the PPU is preparing the next scanline's data.

### Background pipeline

The background fetches one tile every 8 dots:

```text
Name table fetch
Attribute table fetch
Pattern low fetch
Pattern high fetch
Load shift registers
```

The shift registers shift each dot, and combined with `FineX` produce the low/high bit of the current pixel; together with attribute bits, that's the palette index.

### Sprite pipeline

Sprite data lives in OAM, 4 bytes per sprite:

```text
byte 0  Y position - 1   (note: "Y - 1" because of PPU comparison timing)
byte 1  Tile index
byte 2  Attributes        (palette / priority / horizontal flip / vertical flip)
byte 3  X position
```

OAM totals 256 bytes = 64 sprites. **But each scanline can show only 8 sprites** — that's an iron rule of NES hardware. Why? Because the PPU doesn't have time to scan all 64 each scanline.

**Everyday analogy**: sprite evaluation is like **casting**. Before each scanline, the sprite agent (PPU sprite-evaluation hardware) has 192 dots to pick "the 8 actors who appear on the next scanline" out of 64 candidates (OAM). Once 8 are filled, casting stops — even if a 9th would have qualified.

```text
during scanline N:
  dot   1- 64: clear secondary OAM (8 sprite-slot scratch area)
  dot  65-256: scan OAM for sprites whose Y falls on scanline N+1,
                fill secondary OAM (up to 8); the 9th onward sets
                the sprite overflow flag
  dot 257-320: fetch sprite pattern data for sprites in secondary OAM,
                load sprite shifter

scanline N+1 begins (dot 1-256):
  each dot uses sprite shifter to output a sprite pixel,
  composing with background pixel
```

**Sprite 0 hit**: sprite #0 in OAM is "special" — when its non-transparent pixel overlaps a non-transparent background pixel, the PPU sets `$2002` bit 6. Games exploit this flag for two famous tricks:
1. **Split screen**: place sprite 0 on a specific scanline. When the CPU sees the hit flag, it knows the raster reached that line → immediately write `$2005` to change scroll → the lower half uses a different scroll. *Super Mario Bros.* keeps the status bar fixed while the lower playfield scrolls using exactly this technique.
2. **Timing measurement**: knowing the raster position is equivalent to knowing the time, useful for precise timing.

Emulators must handle the precise dot timing of sprite-0-hit. Off by one dot and the game's split-screen jitters.

Each scanline displays at most 8 sprites. Sprite overflow and sprite-0-hit both have hardware quirks that defy intuition.

### Pixel composition

Each dot yields:

- a background pixel.
- a sprite pixel.

If the sprite pixel is non-transparent and the background pixel is non-transparent, sprite-0-hit may fire. The sprite attribute's priority bit decides whether the sprite goes in front of or behind the background.

## Beginner-Friendly Simplification

A first version can do scanline-based rendering:

1. Use scroll to find the background tiles needed for this scanline.
2. Decode pattern table to compute background pixels.
3. Scan OAM to find sprites on this scanline.
4. Compose sprites.
5. Output to framebuffer.

This is enough to display games. When you need split-scroll, precise sprite-0-hit timing, or MMC3 IRQ, switch to dot-level.

## AprNes / NesCore Implementation Mapping

`ppu_new.cs` is AprNes's main PPU implementation.

Main entry points:

- `ppu_step_new()`: dispatches by visible / vblank / pre-render.
- `ppu_half_step_new()`: handles background shift, fetch commit, VBlank latch, sprite-0 pipeline, second stage of `$2007`.

Background fields:

- `renderLow`, `renderHigh`.
- `renderAttrLow`, `renderAttrHigh`.
- `NTVal`, `ATVal`.
- `pendingTileLow`, `pendingTileHigh`.
- `pendingAttrLatch`.

Sprite fields:

- `spr_ram`: primary OAM.
- `secondaryOAM`: sprites selected for this scanline.
- `sprShiftL`, `sprShiftH`.
- `sprXCounter`.
- `sprFetchAttr`.
- `sprSlotCount`.
- `sprZeroInSlots`.

Important functions:

- `SpriteEvalTick()`: per-dot sprite evaluation.
- `SpriteEvalEnd()`: end of evaluation; compute sprite count.
- `PpuPhase4_SpriteFetch()`: dots 257-320 sprite pattern fetch.
- `PpuPhase4_VisibleScanlineDot1Init()`: initialise palette-index buffer at the start of each visible scanline.
- `PpuPhase_FrameRender()`: at frame end, convert palette indices and emit the frame.

AprNes's video pipeline first writes palette indices into `ntsc_rowPalettes`. If not in analog mode, at frame end it calls `Convert_PalIdxFrameToRGB(digitalFrameRgb)`, and the render path outputs.

## Common Mistakes

- Storing the background as a pixel array, ignoring the tile/attribute structure.
- Scanning all 64 sprites at once instead of modelling the 8-sprite limit and overflow behaviour.
- Implementing sprite-0-hit as a post-frame full-screen check, breaking timing.
- Ignoring the pre-render line's reset of scroll and status flags.
- Counting MMC3 IRQ by scanline number rather than PPU A12 behaviour.

## Chapter Recap

1. The PPU is a fixed-timing data pipeline, not a graphics function the CPU calls.
2. Background and sprites both emerge through fetch / shift / compose.
3. AprNes expresses PPU pipeline timing via dot dispatch and half steps.

## Bridge to the Next Chapter

The next chapter covers the APU, focusing on AprNes's `AudioMode = 0` Pure Digital output path.
