// ppu_dispatch.cs — Function-pointer dispatch table for 341-dot PPU timing
//
// Replaces the monolithic ppu_step_new() with a tri-state dispatch:
//   scanline < 240              → ppuTickVisibleTable[cx]()
//   scanline == preRenderLine   → ppuTickPreRenderTable[cx]()
//   else (VBlank)               → ppuTickVBlankTable[cx]()
//
// Initial version: all 341 slots in each table point to the same
// scanline-state handler. The handler contains the full existing
// per-dot logic but with scanline-state gates baked out (specialised
// at compile time via direct value inlining rather than runtime branch).
//
// Future per-cx specialisation can be added by populating individual
// slots with smaller cx-range handlers.
//
// .NET 10 path uses delegate* unmanaged<void>* + [UnmanagedCallersOnly]
// (skips GC safe-point poll per dispatch, ~1-3 cycles).
// .NET Framework 4.8.1 falls back to managed delegate*<void>* which
// still emits calli but retains the poll.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AprNes
{
    unsafe static public partial class NesCore
    {
        // ════════════════════════════════════════════════════════════════
        // 341-slot dispatch tables (one per scanline state)
        // ════════════════════════════════════════════════════════════════
#if NET10_0_OR_GREATER
        public static delegate* unmanaged<void>* ppuTickVisibleTable;
        public static delegate* unmanaged<void>* ppuTickPreRenderTable;
        public static delegate* unmanaged<void>* ppuTickVBlankTable;
#else
        public static delegate*<void>* ppuTickVisibleTable;
        public static delegate*<void>* ppuTickPreRenderTable;
        public static delegate*<void>* ppuTickVBlankTable;
#endif

        public static void InitPpuDispatchTable()
        {
            int sz = 341 * sizeof(IntPtr);
#if NET10_0_OR_GREATER
            if (ppuTickVisibleTable == null)
            {
                ppuTickVisibleTable   = (delegate* unmanaged<void>*)AllocUnmanaged(sz);
                ppuTickPreRenderTable = (delegate* unmanaged<void>*)AllocUnmanaged(sz);
                ppuTickVBlankTable    = (delegate* unmanaged<void>*)AllocUnmanaged(sz);
            }
            // Visible line per-cx specialisation:
            //   slots 0-255  → Ppu_Tick_Visible_PixelZone  (hot pixel generation)
            //   slots 256-340 → Ppu_Tick_VisibleLine       (scroll/sprite-fetch/prefetch/dummy)
            for (int i = 0; i < 256; i++)
                ppuTickVisibleTable[i] = &Ppu_Tick_Visible_PixelZone;
            for (int i = 256; i < 341; i++)
                ppuTickVisibleTable[i] = &Ppu_Tick_VisibleLine;

            for (int i = 0; i < 341; i++)
            {
                ppuTickPreRenderTable[i] = &Ppu_Tick_PreRenderLine;
                ppuTickVBlankTable[i]    = &Ppu_Tick_VBlankLine;
            }
#else
            if (ppuTickVisibleTable == null)
            {
                ppuTickVisibleTable   = (delegate*<void>*)AllocUnmanaged(sz);
                ppuTickPreRenderTable = (delegate*<void>*)AllocUnmanaged(sz);
                ppuTickVBlankTable    = (delegate*<void>*)AllocUnmanaged(sz);
            }
            for (int i = 0; i < 256; i++)
                ppuTickVisibleTable[i] = &Ppu_Tick_Visible_PixelZone;
            for (int i = 256; i < 341; i++)
                ppuTickVisibleTable[i] = &Ppu_Tick_VisibleLine;

            for (int i = 0; i < 341; i++)
            {
                ppuTickPreRenderTable[i] = &Ppu_Tick_PreRenderLine;
                ppuTickVBlankTable[i]    = &Ppu_Tick_VBlankLine;
            }
#endif
        }

        // ════════════════════════════════════════════════════════════════
        // Ppu_Tick_Visible_PixelZone — visible scanline, entry cx ∈ [0, 255]
        // (post-inc cx ∈ [1, 256], the pixel generation hot zone).
        // Specialisations vs Ppu_Tick_VisibleLine:
        //   - Scroll ops: entry cx never 256/257/280-304 → entire block removed.
        //   - Scanline wrap: entry cx < 340 → never wraps → scanline stays < 240.
        //   - Events: scanline < 240 < nmiTriggerLine → check removed.
        //   - Odd-frame-skip / skippedPreRenderDot341 reset: preRender-only → removed.
        //   - Tile fetch gate `(cx∈[1,256] ∪ [321,336])` → always true → gate removed.
        //   - Pixel gate `cx > 0 && cx <= 256` → always true → gate removed.
        //   - Scanline gate inside pixel block `scanline < 240` → always true → removed.
        //   - Sprite shift gate `cx <= 256` → always true → removed.
        //   - Draw gate `cx >= 4 && cx <= 259` → simplified to `cx >= 4`.
        //   - NTSC capture `cx == 260` → never true → removed.
        //   - Frame render `scanline==240 && cx==1` → never true here → removed.
        // ════════════════════════════════════════════════════════════════
#if NET10_0_OR_GREATER
        [UnmanagedCallersOnly]
#endif
        static void Ppu_Tick_Visible_PixelZone()
        {
            int cx = ppu_cycles_x; // entry cx ∈ [0, 255]

            if (ppu2006UpdateDelay != 0 || ppu2005UpdateDelay != 0)
                PpuPhase2_DeferredUpdates(cx);

            if (--open_bus_decay_timer == 0) { open_bus_decay_timer = 77777; openbus = 0; }

            // (Scroll ops skipped — entry cx ∉ {256, 257} in this zone.)

            // ++cx (no wrap possible; cx becomes 1..256).
            ppu_cycles_x = ++cx;

            // (Events skipped — scanline < 240 < nmiTriggerLine.)

            ppuVSET_Latch1 = !ppuVSET;
            if (ppuVSET && !ppuVSET_Latch2) isVblank = true;
            if (ppu2002ReadPending) { ppu2002ReadPending = false; isVblank = false; }

            isSpriteOverflow_Delayed = isSpriteOverflow;

            MapperObj.PpuClock();
            ppuA12Prev = (ppuAddressBus & 0x1000) != 0;
            // NTSC odd-frame skip (entry cx==340 preRender only) — skipped, never fires here.
            // But skippedPreRenderDot341 reset at (scanline==0, post-inc cx==2) DOES fire in this zone
            // (entry cx=1, scanline still 0). Keep the check.
            if (oddSwap && (ShowBackGround || ShowSprites) && scanline == 0 && cx == 2)
                skippedPreRenderDot341 = false;

            if ((mcCpuClock & 3) != 3)
            {
                ShowBG_EvalDelay = ShowBackGround;
                ShowSpr_EvalDelay = ShowSprites;
            }

            PPU_DATA_Pipeline_Step(1);

            if (oamCorruptDelay != 0) PpuPhase_HandleDelayedOamCorruption(true);

            PpuPhase4_SpriteEvalAndInit();

            if ((mcCpuClock & 3) == 3)
            {
                ShowBG_EvalDelay = ShowBackGround;
                ShowSpr_EvalDelay = ShowSprites;
            }

            if (!ShowBackGround && !ShowSprites) ppuAddressBus = vram_addr;

            if (ppu2001UpdateDelay > 0) PpuPhase_Apply2001Mask();
            if (ppu2001EmphasisDelay > 0) PpuPhase_Apply2001Emphasis();

            prevPrevPrevDotColor = prevPrevDotColor; prevPrevDotColor = prevDotColor; prevDotColor = dotColor;
            prevPrevPrevDotPalIdx = prevPrevDotPalIdx; prevPrevDotPalIdx = prevDotPalIdx; prevDotPalIdx = dotPalIdx;

            // ── Inlined render block (tile-fetch gate always true, pixel+shift always run) ──
            // cx ∈ [1, 256] throughout.
            if (ShowBG_EvalDelay || ShowSpr_EvalDelay)
            {
                if (ppu2007_PPU_ALE && ppu2007_PPU_READ)
                    ppuOctalLatch = (byte)ppuAddressBus;

                int fetchPair = ((cx - 1) >> 1) & 3;
                if ((cx & 1) != 0)
                {
                    if (fetchPair < 2)
                    {
                        if (fetchPair == 0)
                            ppuPAR_NT = (ushort)(0x2000 | (vram_addr & 0x0FFF));
                        else
                            ppuPAR_AT = (ushort)(0x23C0 | (vram_addr & 0x0C00) | ((vram_addr >> 4) & 0x38) | ((vram_addr >> 2) & 0x07));
                        ppuPAR_MUX = (fetchPair == 0) ? ppuPAR_NT : ppuPAR_AT;
                    }
                    else
                    {
                        PPU_CheckPAR();
                        ppuPAR_CHR = (ushort)((ppuPAR_CHR & ~8) | ((fetchPair & 1) << 3));
                        ppuPAR_MUX = ppuPAR_CHR;
                    }
                    ppuAddressBus = ppuPAR_MUX;
                }
                else
                {
                    ppuAddressBus = (ushort)((ppuPAR_MUX & 0xFF00) | ppuOctalLatch);

                    if (fetchPair >= 2)
                    {
                        ppuChrFetchA12 = (ppuAddressBus >> 12) & 1;
                        if (mapperNeedsA12 && (fetchPair == 2 || !mapperA12IsMmc3))
                            NotifyMapperA12(ppuAddressBus);
                    }
                    else if (fetchPair == 0 && mapperA12IsMmc3)
                    {
                        NotifyMapperA12(ppuAddressBus);
                    }

                    renderTemp = PpuBusRead(ppuAddressBus);
                    ppuAddressBus = (ushort)((ppuAddressBus & 0xFF00) | renderTemp);

                    if (fetchPair == 0) { commitNTFetch = true; if (extAttrEnabled) extAttrNTOffset = (ushort)(ppuAddressBus & 0x3FF); }
                    else if (fetchPair == 1) commitATFetch = true;
                    else if (fetchPair == 2) commitPatLowFetch = true;
                    else                     commitPatHighFetch = true;

                    if (mmc5Ref != null) mmc5Ref.NotifyVramRead(ppuAddressBus);
                }

                if (ppu2007_PPU_ALE && !ppu2007_PPU_READ)
                    ppuOctalLatch = (byte)ppuAddressBus;

                // cx == 321 never in this zone; only cx == 1 possible.
                if (cx == 1 && chrABAutoSwitch)
                {
                    byte** src = Spritesize8x16 ? (chrBGUseASet ? chrBankPtrsA : chrBankPtrsB) : chrBankPtrsA;
                    *(PtrBlock8*)chrBankPtrs = *(PtrBlock8*)src;
                }
            }

            // ── Pixel composition (scanline < 240 guaranteed) ──
            {
                bool showBG = ShowBackGround;
                bool showSpr = ShowSprites;

                byte backdropIdx = (byte)(ppu_ram[0x3f00] & 0x3f);
                uint compositeColor = palCache[0];
                byte compositePalIdx = backdropIdx;
                int bgColor = 0, bgPalette = 0;

                if (showBG && (cx > 8 || ShowBgLeft8))
                {
                    int bit = 15 - FineX;
                    bgColor = (((renderHigh >> bit) & 1) << 1) | ((renderLow >> bit) & 1);
                    { int ab = 7 - FineX;
                      bgPalette = (((renderAttrHigh >> ab) & 1) << 1) | ((renderAttrLow >> ab) & 1); }
                    if (bgColor == 0) bgPalette = 0;
                }

                if (showSpr && (cx > 8 || ShowSprLeft8) && spriteAnyActive)
                {
                    ulong xc = *(ulong*)sprXCounter;
                    ulong has_bits = ((xc & 0x7F7F7F7F7F7F7F7FUL) + 0x7F7F7F7F7F7F7F7FUL) | xc;
                    ulong active_mask = skippedPreRenderDot341
                        ? 0x8080808080808080UL
                        : (~has_bits & 0x8080808080808080UL);
                    ulong pixel_mask = (*(ulong*)sprShiftH | *(ulong*)sprShiftL) & 0x8080808080808080UL;
                    ulong valid = active_mask & pixel_mask;

                    if (valid != 0)
                    {
#if NET10_0_OR_GREATER
                        int i = System.Numerics.BitOperations.TrailingZeroCount(valid) >> 3;
#else
                        ulong lowest = valid & (ulong)(-(long)valid);
                        int i = (int)((0x0001020304050607UL * (lowest >> 7)) >> 56);
#endif

                        byte h = sprShiftH[i], l = sprShiftL[i];
                        int attr = sprFetchAttr[i];
                        int sprColor = ((h >> 7) << 1) | (l >> 7);
                        int sprPalette = (attr & 3) | 4;
                        bool sprPriority = (attr & 0x20) == 0;

                        if (i == 0 && canDetectSprite0Hit && sprZeroInSlots && showBG && bgColor != 0)
                        { if (cx < 256) { pendingSprite0Hit = true; canDetectSprite0Hit = false; } }

                        bool ow = (bgColor == 0) | sprPriority;
                        bgColor = ow ? sprColor : bgColor;
                        bgPalette = ow ? sprPalette : bgPalette;
                    }
                }

                if (ppuPaletteCorruptionFromVChange | ppuPaletteCorruptionFromDisable)
                {
                    ppuPaletteCorruptionFromVChange = false;
                    ppuPaletteCorruptionFromDisable = false;
                    CorruptPalettes(bgColor, vram_addr);
                }

                if (showBG || showSpr)
                { int pa = (bgPalette << 2) | bgColor; if (bgColor == 0) pa = 0; compositeColor = palCache[pa]; compositePalIdx = (byte)(ppu_ram[0x3f00 + pa] & 0x3f); }
                else { if ((vram_addr & 0x3F1F) >= 0x3F00) { int pa = vram_addr & 0x1F; if ((pa & 3) == 0) pa &= 0x0F; byte idx = (byte)(ppu_ram[0x3f00 + pa] & 0x3f); compositeColor = NesColors[idx]; compositePalIdx = idx; } }

                dotColor = compositeColor;
                dotPalIdx = compositePalIdx;
            }

            // ── Sprite shift (cx <= 256 always true) ──
            {
                bool showBG = ShowBackGround;
                bool showSpr = ShowSprites;
                bool renderEnabled = showSpr || showBG;
                bool canDecrement = !skippedPreRenderDot341;
                ulong v = *(ulong*)sprXCounter;
                if (!canDecrement || v == 0)
                {
                    if (renderEnabled)
                    {
                        *(ulong*)sprShiftL = (*(ulong*)sprShiftL << 1) & 0xFEFEFEFEFEFEFEFEUL;
                        *(ulong*)sprShiftH = (*(ulong*)sprShiftH << 1) & 0xFEFEFEFEFEFEFEFEUL;
                    }
                }
                else
                {
                    ulong dec_mask = ((v | ((v & 0x7F7F7F7F7F7F7F7FUL) + 0x7F7F7F7F7F7F7F7FUL))
                                     & 0x8080808080808080UL) >> 7;
                    *(ulong*)sprXCounter = v - dec_mask;

                    if (renderEnabled)
                    {
                        ulong mask_0 = ~(dec_mask * 255UL);
                        ulong sl = *(ulong*)sprShiftL;
                        ulong sh = *(ulong*)sprShiftH;
                        *(ulong*)sprShiftL = (((sl << 1) & 0xFEFEFEFEFEFEFEFEUL) & mask_0) | (sl & ~mask_0);
                        *(ulong*)sprShiftH = (((sh << 1) & 0xFEFEFEFEFEFEFEFEUL) & mask_0) | (sh & ~mask_0);
                    }
                }
            }

            PPU_DATA_Pipeline_Step(2);

            // ── Draw to screen (cx ∈ [1, 256]; gate simplifies to cx >= 4) ──
            if (cx >= 4)
            {
                int pos = (scanline << 8) + (cx - 4);
                if (AnalogEnabled) ntscScanBuf[cx - 4] = prevPrevPrevDotPalIdx;
                else ScreenBuf1x[pos] = prevPrevPrevDotColor;
            }
            // (NTSC capture cx == 260 never fires here; frame render never fires here.)

            ppuRenderingEnabled = ShowBackGround_Instant || ShowSprites_Instant;
        }

        // ════════════════════════════════════════════════════════════════
        // Ppu_Tick_VisibleLine — scanline ∈ [0, 239]
        // Known at entry: scanline < 240 == true, scanline == preRenderLine == false.
        // The scanline wrap at cx==340 may land us in SL240 (VBlank); post-wrap
        // branches remain runtime-checked.
        // ════════════════════════════════════════════════════════════════
#if NET10_0_OR_GREATER
        [UnmanagedCallersOnly]
#endif
        static void Ppu_Tick_VisibleLine()
        {
            int cx = ppu_cycles_x; // PRE-increment

            if (ppu2006UpdateDelay != 0 || ppu2005UpdateDelay != 0)
                PpuPhase2_DeferredUpdates(cx);

            if (--open_bus_decay_timer == 0) { open_bus_decay_timer = 77777; openbus = 0; }

            // Scroll increments (visible: always active scanline; no vert reset).
            if (ShowBackGround || ShowSprites)
            {
                if (cx == 256)      Yinc();
                else if (cx == 257) CopyHoriV();
                // Vert reset (cx 280-304) fires only on preRenderLine — not here.
            }

            // PPU_Dot++ + scanline wrap.
            ppu_cycles_x = ++cx;
            if (cx > 340)
            {
                ppu_cycles_x = cx = 0;
                scanline++;
                scanline &= (scanline - (preRenderLine + 1)) >> 31;
            }

            // Phase 3 events fire only when scanline >= nmiTriggerLine.
            // After a potential wrap from SL 239 to SL 240, scanline may now be 240.
            // nmiTriggerLine is 241 (NTSC) / 241 (PAL) so SL 240 doesn't fire events.
            // But we still keep the runtime check for correctness if wrap lands past that.
            if (scanline >= nmiTriggerLine)
                PpuPhase3_Events(cx);

            ppuVSET_Latch1 = !ppuVSET;
            if (ppuVSET && !ppuVSET_Latch2) isVblank = true;
            if (ppu2002ReadPending) { ppu2002ReadPending = false; isVblank = false; }

            isSpriteOverflow_Delayed = isSpriteOverflow;

            MapperObj.PpuClock();
            ppuA12Prev = (ppuAddressBus & 0x1000) != 0;
            // Odd-frame dot skip only on preRenderLine — not here.
            if (oddSwap && (ShowBackGround || ShowSprites) && scanline == 0 && cx == 2)
                skippedPreRenderDot341 = false;

            if ((mcCpuClock & 3) != 3)
            {
                ShowBG_EvalDelay = ShowBackGround;
                ShowSpr_EvalDelay = ShowSprites;
            }

            PPU_DATA_Pipeline_Step(1);

            if (oamCorruptDelay != 0) PpuPhase_HandleDelayedOamCorruption(true);

            // Sprite eval: active scanline.
            PpuPhase4_SpriteEvalAndInit();

            if ((mcCpuClock & 3) == 3)
            {
                ShowBG_EvalDelay = ShowBackGround;
                ShowSpr_EvalDelay = ShowSprites;
            }

            if (!ShowBackGround && !ShowSprites) ppuAddressBus = vram_addr;

            if (ppu2001UpdateDelay > 0) PpuPhase_Apply2001Mask();
            if (ppu2001EmphasisDelay > 0) PpuPhase_Apply2001Emphasis();

            prevPrevPrevDotColor = prevPrevDotColor; prevPrevDotColor = prevDotColor; prevDotColor = dotColor;
            prevPrevPrevDotPalIdx = prevPrevDotPalIdx; prevPrevDotPalIdx = prevDotPalIdx; prevDotPalIdx = dotPalIdx;

            // Tile fetch + CalcPixel + SpriteShift — active scanline gate collapses.
            Ppu_ActiveScanline_RenderBlock(cx);

            PPU_DATA_Pipeline_Step(2);

            // DrawToScreen — only on visible scanlines (scanline < 240).
            // Post-wrap scanline could be 240 (VBlank), runtime check kept.
            if (scanline >= 0 && scanline < 240)
            {
                if (cx >= 4 && cx <= 259)
                {
                    int pos = (scanline << 8) + (cx - 4);
                    if (AnalogEnabled) ntscScanBuf[cx - 4] = prevPrevPrevDotPalIdx;
                    else ScreenBuf1x[pos] = prevPrevPrevDotColor;
                }
                if (AnalogEnabled && cx == 260)
                    Ntsc_CaptureScanline(scanline, ntscScanBuf, ppuEmphasis);
            }

            // Frame render — fires once per frame at SL 240 cx 1. Possible after wrap.
            if (scanline == 240 && cx == 1) PpuPhase_FrameRender();

            ppuRenderingEnabled = ShowBackGround_Instant || ShowSprites_Instant;
        }

        // ════════════════════════════════════════════════════════════════
        // Ppu_Tick_PreRenderLine — scanline == preRenderLine (261 NTSC / 311 PAL/Dendy)
        // Known at entry: scanline < 240 == false, scanline == preRenderLine == true,
        // scanline >= nmiTriggerLine == true.
        // Wrap at cx==340 lands us in SL 0 (visible).
        // ════════════════════════════════════════════════════════════════
#if NET10_0_OR_GREATER
        [UnmanagedCallersOnly]
#endif
        static void Ppu_Tick_PreRenderLine()
        {
            int cx = ppu_cycles_x;

            if (ppu2006UpdateDelay != 0 || ppu2005UpdateDelay != 0)
                PpuPhase2_DeferredUpdates(cx);

            if (--open_bus_decay_timer == 0) { open_bus_decay_timer = 77777; openbus = 0; }

            // Scroll + vert reset (preRender specific).
            if (ShowBackGround || ShowSprites)
            {
                if (cx == 256)      Yinc();
                else if (cx == 257) CopyHoriV();
                if (cx >= 280 && cx <= 304)
                    vram_addr = (vram_addr & ~0x7BE0) | (vram_addr_internal & 0x7BE0);
            }

            ppu_cycles_x = ++cx;
            if (cx > 340)
            {
                ppu_cycles_x = cx = 0;
                scanline++;
                scanline &= (scanline - (preRenderLine + 1)) >> 31;
            }

            // Phase 3 events: always active on preRender (scanline >= nmiTriggerLine).
            // Post-wrap scanline may be 0 (visible) → runtime check kept.
            if (scanline >= nmiTriggerLine)
                PpuPhase3_Events(cx);

            ppuVSET_Latch1 = !ppuVSET;
            if (ppuVSET && !ppuVSET_Latch2) isVblank = true;
            if (ppu2002ReadPending) { ppu2002ReadPending = false; isVblank = false; }

            isSpriteOverflow_Delayed = isSpriteOverflow;

            MapperObj.PpuClock();
            ppuA12Prev = (ppuAddressBus & 0x1000) != 0;
            // Odd-frame dot skip — fires only on preRenderLine at cx==340 pre-wrap.
            // Note: after wrap cx is 0 or unchanged; check against pre-wrap state via scanline guard.
            if (Region == RegionType.NTSC && oddSwap && (ShowBackGround || ShowSprites)
                && scanline == preRenderLine && cx == 340)
                PpuPhase_DoOddFrameSkip(ref cx);
            if (oddSwap && (ShowBackGround || ShowSprites) && scanline == 0 && cx == 2)
                skippedPreRenderDot341 = false;

            if ((mcCpuClock & 3) != 3)
            {
                ShowBG_EvalDelay = ShowBackGround;
                ShowSpr_EvalDelay = ShowSprites;
            }

            PPU_DATA_Pipeline_Step(1);

            if (oamCorruptDelay != 0) PpuPhase_HandleDelayedOamCorruption(true);

            // Sprite eval: active scanline.
            PpuPhase4_SpriteEvalAndInit();

            if ((mcCpuClock & 3) == 3)
            {
                ShowBG_EvalDelay = ShowBackGround;
                ShowSpr_EvalDelay = ShowSprites;
            }

            if (!ShowBackGround && !ShowSprites) ppuAddressBus = vram_addr;

            if (ppu2001UpdateDelay > 0) PpuPhase_Apply2001Mask();
            if (ppu2001EmphasisDelay > 0) PpuPhase_Apply2001Emphasis();

            prevPrevPrevDotColor = prevPrevDotColor; prevPrevDotColor = prevDotColor; prevDotColor = dotColor;
            prevPrevPrevDotPalIdx = prevPrevDotPalIdx; prevPrevDotPalIdx = prevDotPalIdx; prevDotPalIdx = dotPalIdx;

            // Tile fetch + SpriteShift — CalcPixel internally gates on scanline < 240, which is false here.
            Ppu_ActiveScanline_RenderBlock(cx);

            PPU_DATA_Pipeline_Step(2);

            // Draw to screen: scanline < 240 is false here (except after wrap to SL 0).
            if (scanline >= 0 && scanline < 240)
            {
                if (cx >= 4 && cx <= 259)
                {
                    int pos = (scanline << 8) + (cx - 4);
                    if (AnalogEnabled) ntscScanBuf[cx - 4] = prevPrevPrevDotPalIdx;
                    else ScreenBuf1x[pos] = prevPrevPrevDotColor;
                }
                if (AnalogEnabled && cx == 260)
                    Ntsc_CaptureScanline(scanline, ntscScanBuf, ppuEmphasis);
            }

            // Frame render: preRender→SL0 wrap at cx==340 skips over SL 240.
            // But guard still runtime-evaluated for safety.
            if (scanline == 240 && cx == 1) PpuPhase_FrameRender();

            ppuRenderingEnabled = ShowBackGround_Instant || ShowSprites_Instant;
        }

        // ════════════════════════════════════════════════════════════════
        // Ppu_Tick_VBlankLine — scanline ∈ [240, preRenderLine-1]
        // Known at entry: scanline < 240 == false, scanline == preRenderLine == false.
        // No rendering; lots of universal per-dot work still executes.
        // Wrap at cx==340 moves to next scanline (could reach preRenderLine).
        // ════════════════════════════════════════════════════════════════
#if NET10_0_OR_GREATER
        [UnmanagedCallersOnly]
#endif
        static void Ppu_Tick_VBlankLine()
        {
            int cx = ppu_cycles_x;

            if (ppu2006UpdateDelay != 0 || ppu2005UpdateDelay != 0)
                PpuPhase2_DeferredUpdates(cx);

            if (--open_bus_decay_timer == 0) { open_bus_decay_timer = 77777; openbus = 0; }

            // No scroll ops — gated on active scanline which is false here.

            ppu_cycles_x = ++cx;
            if (cx > 340)
            {
                ppu_cycles_x = cx = 0;
                scanline++;
                scanline &= (scanline - (preRenderLine + 1)) >> 31;
            }

            // Phase 3 events: scanline >= nmiTriggerLine is true throughout VBlank.
            if (scanline >= nmiTriggerLine)
                PpuPhase3_Events(cx);

            ppuVSET_Latch1 = !ppuVSET;
            if (ppuVSET && !ppuVSET_Latch2) isVblank = true;
            if (ppu2002ReadPending) { ppu2002ReadPending = false; isVblank = false; }

            isSpriteOverflow_Delayed = isSpriteOverflow;

            MapperObj.PpuClock();
            ppuA12Prev = (ppuAddressBus & 0x1000) != 0;
            // Odd-skip: not here (preRender only).

            if ((mcCpuClock & 3) != 3)
            {
                ShowBG_EvalDelay = ShowBackGround;
                ShowSpr_EvalDelay = ShowSprites;
            }

            PPU_DATA_Pipeline_Step(1);

            if (oamCorruptDelay != 0) PpuPhase_HandleDelayedOamCorruption(false);

            // No sprite eval (active scanline == false).

            if ((mcCpuClock & 3) == 3)
            {
                ShowBG_EvalDelay = ShowBackGround;
                ShowSpr_EvalDelay = ShowSprites;
            }

            if (!ShowBackGround && !ShowSprites) ppuAddressBus = vram_addr;

            if (ppu2001UpdateDelay > 0) PpuPhase_Apply2001Mask();
            if (ppu2001EmphasisDelay > 0) PpuPhase_Apply2001Emphasis();

            prevPrevPrevDotColor = prevPrevDotColor; prevPrevDotColor = prevDotColor; prevDotColor = dotColor;
            prevPrevPrevDotPalIdx = prevPrevDotPalIdx; prevPrevDotPalIdx = prevDotPalIdx; prevDotPalIdx = dotPalIdx;

            // No active-scanline render block.

            PPU_DATA_Pipeline_Step(2);

            // No DrawToScreen (visible gate false).

            // Frame render: fires at SL 240 cx 1 (first VBlank dot).
            if (scanline == 240 && cx == 1) PpuPhase_FrameRender();

            ppuRenderingEnabled = ShowBackGround_Instant || ShowSprites_Instant;
        }

        // ════════════════════════════════════════════════════════════════
        // Ppu_ActiveScanline_RenderBlock — shared between Visible & PreRender
        // Performs BG tile fetch (cx 1-256 or 321-336) and, on visible only,
        // CalcPixel + sprite shift (cx 1-256). Guarded by cx range internally.
        // Marked AggressiveInlining so JIT folds into the two call sites above.
        // ════════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Ppu_ActiveScanline_RenderBlock(int cx)
        {
            // BG tile fetch (cx 1-256 or 321-336).
            if ((cx >= 1 && cx <= 256) || (cx >= 321 && cx <= 336))
            {
                if (ShowBG_EvalDelay || ShowSpr_EvalDelay)
                {
                    if (ppu2007_PPU_ALE && ppu2007_PPU_READ)
                        ppuOctalLatch = (byte)ppuAddressBus;

                    int fetchPair = ((cx - 1) >> 1) & 3;
                    if ((cx & 1) != 0)
                    {
                        if (fetchPair < 2)
                        {
                            if (fetchPair == 0)
                                ppuPAR_NT = (ushort)(0x2000 | (vram_addr & 0x0FFF));
                            else
                                ppuPAR_AT = (ushort)(0x23C0 | (vram_addr & 0x0C00) | ((vram_addr >> 4) & 0x38) | ((vram_addr >> 2) & 0x07));
                            ppuPAR_MUX = (fetchPair == 0) ? ppuPAR_NT : ppuPAR_AT;
                        }
                        else
                        {
                            PPU_CheckPAR();
                            ppuPAR_CHR = (ushort)((ppuPAR_CHR & ~8) | ((fetchPair & 1) << 3));
                            ppuPAR_MUX = ppuPAR_CHR;
                        }
                        ppuAddressBus = ppuPAR_MUX;
                    }
                    else
                    {
                        ppuAddressBus = (ushort)((ppuPAR_MUX & 0xFF00) | ppuOctalLatch);

                        if (fetchPair >= 2)
                        {
                            ppuChrFetchA12 = (ppuAddressBus >> 12) & 1;
                            if (mapperNeedsA12 && (fetchPair == 2 || !mapperA12IsMmc3))
                                NotifyMapperA12(ppuAddressBus);
                        }
                        else if (fetchPair == 0 && mapperA12IsMmc3)
                        {
                            NotifyMapperA12(ppuAddressBus);
                        }

                        renderTemp = PpuBusRead(ppuAddressBus);
                        ppuAddressBus = (ushort)((ppuAddressBus & 0xFF00) | renderTemp);

                        if (fetchPair == 0) { commitNTFetch = true; if (extAttrEnabled) extAttrNTOffset = (ushort)(ppuAddressBus & 0x3FF); }
                        else if (fetchPair == 1) commitATFetch = true;
                        else if (fetchPair == 2) commitPatLowFetch = true;
                        else                     commitPatHighFetch = true;

                        if (mmc5Ref != null) mmc5Ref.NotifyVramRead(ppuAddressBus);
                    }

                    if (ppu2007_PPU_ALE && !ppu2007_PPU_READ)
                        ppuOctalLatch = (byte)ppuAddressBus;

                    if ((cx == 1 || cx == 321) && chrABAutoSwitch)
                    {
                        byte** src = Spritesize8x16 ? (chrBGUseASet ? chrBankPtrsA : chrBankPtrsB) : chrBankPtrsA;
                        *(PtrBlock8*)chrBankPtrs = *(PtrBlock8*)src;
                    }
                }
            }

            // CalcPixel + UpdateSpriteShift (cx 1-256), pixel is visible-only.
            if (cx > 0 && cx <= 256)
            {
                bool showBG = ShowBackGround;
                bool showSpr = ShowSprites;

                if (scanline < 240) // runtime-checked: visible-only for CalcPixel
                {
                    byte backdropIdx = (byte)(ppu_ram[0x3f00] & 0x3f);
                    uint compositeColor = palCache[0];
                    byte compositePalIdx = backdropIdx;
                    int bgColor = 0, bgPalette = 0;

                    if (showBG && (cx > 8 || ShowBgLeft8))
                    {
                        int bit = 15 - FineX;
                        bgColor = (((renderHigh >> bit) & 1) << 1) | ((renderLow >> bit) & 1);
                        { int ab = 7 - FineX;
                          bgPalette = (((renderAttrHigh >> ab) & 1) << 1) | ((renderAttrLow >> ab) & 1); }
                        if (bgColor == 0) bgPalette = 0;
                    }

                    if (showSpr && (cx > 8 || ShowSprLeft8) && spriteAnyActive)
                    {
                        ulong xc = *(ulong*)sprXCounter;
                        ulong has_bits = ((xc & 0x7F7F7F7F7F7F7F7FUL) + 0x7F7F7F7F7F7F7F7FUL) | xc;
                        ulong active_mask = skippedPreRenderDot341
                            ? 0x8080808080808080UL
                            : (~has_bits & 0x8080808080808080UL);
                        ulong pixel_mask = (*(ulong*)sprShiftH | *(ulong*)sprShiftL) & 0x8080808080808080UL;
                        ulong valid = active_mask & pixel_mask;

                        if (valid != 0)
                        {
#if NET10_0_OR_GREATER
                            int i = System.Numerics.BitOperations.TrailingZeroCount(valid) >> 3;
#else
                            ulong lowest = valid & (ulong)(-(long)valid);
                            int i = (int)((0x0001020304050607UL * (lowest >> 7)) >> 56);
#endif

                            byte h = sprShiftH[i], l = sprShiftL[i];
                            int attr = sprFetchAttr[i];
                            int sprColor = ((h >> 7) << 1) | (l >> 7);
                            int sprPalette = (attr & 3) | 4;
                            bool sprPriority = (attr & 0x20) == 0;

                            if (i == 0 && canDetectSprite0Hit && sprZeroInSlots && showBG && bgColor != 0)
                            { if (cx < 256) { pendingSprite0Hit = true; canDetectSprite0Hit = false; } }

                            bool ow = (bgColor == 0) | sprPriority;
                            bgColor = ow ? sprColor : bgColor;
                            bgPalette = ow ? sprPalette : bgPalette;
                        }
                    }

                    if (ppuPaletteCorruptionFromVChange | ppuPaletteCorruptionFromDisable)
                    {
                        ppuPaletteCorruptionFromVChange = false;
                        ppuPaletteCorruptionFromDisable = false;
                        CorruptPalettes(bgColor, vram_addr);
                    }

                    if (showBG || showSpr)
                    { int pa = (bgPalette << 2) | bgColor; if (bgColor == 0) pa = 0; compositeColor = palCache[pa]; compositePalIdx = (byte)(ppu_ram[0x3f00 + pa] & 0x3f); }
                    else { if ((vram_addr & 0x3F1F) >= 0x3F00) { int pa = vram_addr & 0x1F; if ((pa & 3) == 0) pa &= 0x0F; byte idx = (byte)(ppu_ram[0x3f00 + pa] & 0x3f); compositeColor = NesColors[idx]; compositePalIdx = idx; } }

                    dotColor = compositeColor;
                    dotPalIdx = compositePalIdx;
                }

                // SpriteShift runs regardless of scanline < 240 (included for both visible & preRender).
                if (cx <= 256)
                {
                    bool renderEnabled = showSpr || showBG;
                    bool canDecrement = !skippedPreRenderDot341;
                    ulong v = *(ulong*)sprXCounter;
                    if (!canDecrement || v == 0)
                    {
                        if (renderEnabled)
                        {
                            *(ulong*)sprShiftL = (*(ulong*)sprShiftL << 1) & 0xFEFEFEFEFEFEFEFEUL;
                            *(ulong*)sprShiftH = (*(ulong*)sprShiftH << 1) & 0xFEFEFEFEFEFEFEFEUL;
                        }
                    }
                    else
                    {
                        ulong dec_mask = ((v | ((v & 0x7F7F7F7F7F7F7F7FUL) + 0x7F7F7F7F7F7F7F7FUL))
                                         & 0x8080808080808080UL) >> 7;
                        *(ulong*)sprXCounter = v - dec_mask;

                        if (renderEnabled)
                        {
                            ulong mask_0 = ~(dec_mask * 255UL);
                            ulong sl = *(ulong*)sprShiftL;
                            ulong sh = *(ulong*)sprShiftH;
                            *(ulong*)sprShiftL = (((sl << 1) & 0xFEFEFEFEFEFEFEFEUL) & mask_0) | (sl & ~mask_0);
                            *(ulong*)sprShiftH = (((sh << 1) & 0xFEFEFEFEFEFEFEFEUL) & mask_0) | (sh & ~mask_0);
                        }
                    }
                }
            }
        }
    }
}
