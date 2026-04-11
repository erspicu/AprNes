// TriCNES PPU complete port — replaces ppu_step/ppu_step_rendering/ppu_rendering_tick/ppu_half_step
// Source: ref/TriCNES-main/Emulator.cs _EmulatePPU (line 1256) + _EmulateHalfPPU (line 1809)
//
// Execution order matches TriCNES exactly:
//   _EmulatePPU: deferred→scroll→dot++→wrap→events→VSET→mapper→A12→oddskip
//                →evaldelay→spriteeval→evaldelay→addrbus→$2001→$2001emph
//                →pipeline→commit→tilefetch→calculatepixel→spriteshift→draw
//   _EmulateHalfPPU: BGshift→commitHalf→tileHalf→VSET_half→spr0_half→OAMbuf

using System;
using System.Runtime.CompilerServices;

namespace AprNes
{
    unsafe static public partial class NesCore
    {
        // ════════════════════════════════════════════════════════════════
        // Toggle: set to true in Main.cs to use new PPU step
        // ════════════════════════════════════════════════════════════════
        // useNewPpuStep removed — always use new PPU step

        // TriCNES: CopyV flag — set when $2006 delayed copy fires, used for scroll conflict detection

        // ════════════════════════════════════════════════════════════════
        // _EmulatePPU — full PPU step (called at mcPpuClock == 0)
        // TriCNES: Emulator.cs line 1256
        // ════════════════════════════════════════════════════════════════
        static void ppu_step_new()
        {
            int cx = ppu_cycles_x; // local alias, PRE-increment value

            // ══════════════════════════════════════════════════════
            // Deferred register updates (TriCNES lines 1263-1496)
            // Guard: >99% of dots have no pending updates, skip the call entirely
            // ══════════════════════════════════════════════════════
            if (ppu2006UpdateDelay != 0 || ppu2005UpdateDelay != 0)
                PpuPhase2_DeferredUpdates(cx);

            // Open bus decay (runs every dot, too small to extract)
            if (--open_bus_decay_timer == 0) { open_bus_decay_timer = 77777; openbus = 0; }

            // ══════════════════════════════════════════════════════
            // Scroll increments — PRE-increment (TriCNES lines 1498-1516)
            // Uses PPU_Dot BEFORE PPU_Dot++ (= cx = ppu_cycles_x)
            // ══════════════════════════════════════════════════════
            if (scanline < 240 || scanline == preRenderLine)
            {
                if (ShowBackGround || ShowSprites) // Tier 2 gate
                {
                    if (cx == 256)
                        Yinc();
                    else if (cx == 257)
                        CopyHoriV();
                    if (cx >= 280 && cx <= 304 && scanline == preRenderLine)
                    {
                        // ResetYScroll: copy vert bits of t to v
                        vram_addr = (vram_addr & ~0x7BE0) | (vram_addr_internal & 0x7BE0);
                    }
                }
            }

            // ══════════════════════════════════════════════════════
            // PPU_Dot++ + scanline wrap (TriCNES lines 1518-1530)
            // ══════════════════════════════════════════════════════
            ppu_cycles_x = ++cx;
            if (cx > 340)
            {
                ppu_cycles_x = cx = 0;
                scanline++;
                if (scanline > preRenderLine)
                {
                    scanline = 0;
                }
            }

            // Cache active scanline flag (visible 0-239 or pre-render)
            // VBlank scanlines (240-260) skip all rendering logic below
            bool isActiveScanline = scanline < 240 || scanline == preRenderLine;

            // ══════════════════════════════════════════════════════
            // Phase 3: Events (TriCNES lines 1532-1606)
            // Guard: only scanlines >= nmiTriggerLine (241+) have events
            // ══════════════════════════════════════════════════════
            if (scanline >= nmiTriggerLine)
                PpuPhase3_Events(cx);

            // ── VSET latch pipeline (TriCNES lines 1608-1618, per-dot) ──
            ppuVSET_Latch1 = !ppuVSET;
            if (ppuVSET && !ppuVSET_Latch2)
                isVblank = true;
            if (ppu2002ReadPending)
            {
                ppu2002ReadPending = false;
                isVblank = false;
            }

            // ── Sprite overflow delayed (TriCNES line 1619) ──
            isSpriteOverflow_Delayed = isSpriteOverflow;

            // ── Mapper + A12 (TriCNES line 1478-1479: START of _EmulatePPU, BEFORE SM) ──
            MapperObj.PpuClock();
            ppuA12Prev = (ppuAddressBus & 0x1000) != 0;
            if (debug2007Log && scanline == 0 && cx >= 45 && cx <= 57)
                System.Console.Error.WriteLine($"DOT sl=0 cx={cx} bus={ppuAddressBus:X4} a12={(ppuAddressBus & 0x1000) != 0}");

            // ── Odd frame skip (TriCNES lines 1629-1643) ──
            // PAL/Dendy: no dot skip (PAL phase alternation eliminates dot crawl naturally)
            if (Region == RegionType.NTSC && oddSwap && (ShowBackGround || ShowSprites))
            {
                if (scanline == preRenderLine && cx == 340)
                {
                    if (mmc5Ref != null)
                        mmc5Ref.NotifyVramRead(0x2000 | (vram_addr & 0x0FFF));
                    scanline = 0;
                    ppu_cycles_x = cx = 0;
                    skippedPreRenderDot341 = true;
                }
            }
            if (oddSwap && (ShowBackGround || ShowSprites) && scanline == 0 && cx == 2)
                skippedPreRenderDot341 = false;

            // ── Eval delay: non-phase-3 (TriCNES line 1506: BEFORE SM) ──
            if ((mcCpuClock & 3) != 3)
            {
                ShowBG_EvalDelay = ShowBackGround;
                ShowSpr_EvalDelay = ShowSprites;
            }

            // ── PPU_DATA_StateMachine — Phase 1 (TriCNES line 1513) ──
            PPU_DATA_StateMachine();

            // ── Delayed OAM corruption (TriCNES lines 1695-1711) ──
            if (oamCorruptDelay != 0 && --oamCorruptDelay == 0)
            {
                if (oamCorruptWasRendering && (oamCorrupt2001Value & 0x18) == 0)
                    if (isActiveScanline && !oamCorruptPending)
                        oamCorruptDisabledFlag = true;
            }

            // ── Sprite evaluation (TriCNES line 1664, inside scanline gate) ──
            if (isActiveScanline)
                PpuPhase4_SpriteEvalAndInit();

            // ── Eval delay: phase-3 (TriCNES lines 1667-1673) ──
            if ((mcCpuClock & 3) == 3)
            {
                ShowBG_EvalDelay = ShowBackGround;
                ShowSpr_EvalDelay = ShowSprites;
            }

            // ── ppuAddressBus = vram_addr when rendering disabled (TriCNES line 1530-1535) ──
            if (!ShowBackGround && !ShowSprites)
            {
                ppuAddressBus = vram_addr;
            }

            // ── $2001 delayed mask update (TriCNES lines 1681-1694) ──
            if (ppu2001UpdateDelay > 0 && --ppu2001UpdateDelay == 0)
            {
                if (debug2007Log && scanline >= 0 && scanline < 10)
                    System.Console.Error.WriteLine($"D2001 sl={scanline} cx={ppu_cycles_x} val={ppu2001PendingValue:X2} bgON={(ppu2001PendingValue & 0x08) != 0}");
                ppuGreyscale   = (ppu2001PendingValue & 0x01) != 0;
                ShowBgLeft8    = (ppu2001PendingValue & 0x02) != 0;
                ShowSprLeft8   = (ppu2001PendingValue & 0x04) != 0;
                ShowBackGround = (ppu2001PendingValue & 0x08) != 0;
                ShowSprites    = (ppu2001PendingValue & 0x10) != 0;
                // TriCNES line 1691: re-sync Instant flags to Delayed
                ShowBackGround_Instant = ShowBackGround;
                ShowSprites_Instant = ShowSprites;
            }

            // ── $2001 emphasis delay (TriCNES lines 1712-1722) ──
            if (ppu2001EmphasisDelay > 0 && --ppu2001EmphasisDelay == 0)
            {
                byte v = ppu2001EmphasisPending;
                ppuEmphasis = (byte)((v >> 5) & 0x7);
                if (Region != RegionType.NTSC)
                    ppuEmphasis = (byte)((ppuEmphasis & 0x4) | ((ppuEmphasis & 1) << 1) | ((ppuEmphasis >> 1) & 1));
            }

            // ══════════════════════════════════════════════════════
            // Phase 5: Pipeline + commit + tile fetch + pixel + draw
            // (TriCNES lines 1724-1807)
            // ══════════════════════════════════════════════════════


            // ── Pipeline shift (TriCNES line 1724: ALL scanlines, ALL dots, OUTSIDE any gate) ──
            prevPrevPrevDotColor = prevPrevDotColor; prevPrevDotColor = prevDotColor; prevDotColor = dotColor;
            prevPrevPrevDotPalIdx = prevPrevDotPalIdx; prevPrevDotPalIdx = prevDotPalIdx; prevDotPalIdx = dotPalIdx;

            // CommitShiftRegistersAndBitPlanes moved to half-step (TriCNES line 1691: inside _EmulateHalfPPU)

            // ── Tile fetch + CalculatePixel + UpdateSpriteShift (TriCNES lines 1728-1751) ──
            if (isActiveScanline)
            {
                // BG tile fetch (TriCNES line 1585: PPU_Dot >= 1 && <= 256, or >= 321 && <= 336)
                // BG tile fetch via PAR (TriCNES PPU_Render_ShiftRegistersAndBitPlanes, line 3588)
                if ((cx >= 1 && cx <= 256) || (cx >= 321 && cx <= 336))
                {
                    if (ShowBG_EvalDelay || ShowSpr_EvalDelay) // Tier 2 (TriCNES: _Delayed gate)
                    {
                        // TriCNES line 3593-3596: OctalLatch guard before fetch
                        if (ppu2007_PPU_ALE && ppu2007_PPU_READ)
                            ppuOctalLatch = (byte)ppuAddressBus;

                        // cycleTick: TriCNES uses (PPU_Dot+7)&7. Maps to ((cx-1)>>1)&3 for fetchPair.
                        int fetchPair = ((cx - 1) >> 1) & 3;
                        if ((cx & 1) != 0) // odd cx = ALE — TriCNES cycleTick 0,2,4,6
                        {
                            if (fetchPair == 0) { // NT ALE
                                ppuPAR_NT = (ushort)(0x2000 | (vram_addr & 0x0FFF));
                                ppuPAR_MUX = ppuPAR_NT;
                                ppuAddressBus = ppuPAR_MUX;
                            }
                            else if (fetchPair == 1) { // AT ALE
                                ppuPAR_AT = (ushort)(0x23C0 | (vram_addr & 0x0C00) | ((vram_addr >> 4) & 0x38) | ((vram_addr >> 2) & 0x07));
                                ppuPAR_MUX = ppuPAR_AT;
                                ppuAddressBus = ppuPAR_MUX;
                            }
                            else if (fetchPair == 2) { // CHR-L ALE
                                PPU_CheckPAR();
                                ppuPAR_CHR &= 0b1111111110111; // clear bit 3
                                ppuPAR_MUX = ppuPAR_CHR;
                                ppuAddressBus = ppuPAR_MUX;
                            }
                            else { // CHR-H ALE
                                PPU_CheckPAR();
                                ppuPAR_CHR |= 8; // set bit 3
                                ppuPAR_MUX = ppuPAR_CHR;
                                ppuAddressBus = ppuPAR_MUX;
                            }
                        }
                        else // even cx = READ — TriCNES cycleTick 1,3,5,7
                        {
                            // FetchPPU: addr = (PAR & 0xFF00) | OctalLatch
                            if (fetchPair == 0) { // NT READ
                                ppuAddressBus = (ushort)((ppuPAR_NT & 0xFF00) | ppuOctalLatch);
                                if (mapperA12IsMmc3) NotifyMapperA12(ppuAddressBus);
                                renderTemp = PpuBusRead(ppuAddressBus); commitNTFetch = true;
                                ppuAddressBus = (ppuAddressBus & 0xFF00) | renderTemp;
                                if (extAttrEnabled) extAttrNTOffset = (ushort)(ppuAddressBus & 0x3FF);
                                if (mmc5Ref != null) mmc5Ref.NotifyVramRead(ppuAddressBus);
                            }
                            else if (fetchPair == 1) { // AT READ
                                ppuAddressBus = (ushort)((ppuPAR_AT & 0xFF00) | ppuOctalLatch);
                                renderTemp = PpuBusRead(ppuAddressBus); commitATFetch = true;
                                ppuAddressBus = (ppuAddressBus & 0xFF00) | renderTemp;
                                if (mmc5Ref != null) mmc5Ref.NotifyVramRead(ppuAddressBus);
                            }
                            else if (fetchPair == 2) { // CHR-L READ
                                ppuAddressBus = (ushort)((ppuPAR_CHR & 0xFF00) | ppuOctalLatch);
                                ppuChrFetchA12 = (ppuAddressBus >> 12) & 1;
                                if (mapperNeedsA12) NotifyMapperA12(ppuAddressBus);
                                renderTemp = PpuBusRead(ppuAddressBus); commitPatLowFetch = true;
                                ppuAddressBus = (ppuAddressBus & 0xFF00) | renderTemp;
                                if (mmc5Ref != null) mmc5Ref.NotifyVramRead(ppuAddressBus);
                            }
                            else { // CHR-H READ
                                ppuAddressBus = (ushort)((ppuPAR_CHR & 0xFF00) | ppuOctalLatch);
                                ppuChrFetchA12 = (ppuAddressBus >> 12) & 1;
                                if (mapperNeedsA12 && !mapperA12IsMmc3) NotifyMapperA12(ppuAddressBus);
                                renderTemp = PpuBusRead(ppuAddressBus); commitPatHighFetch = true;
                                ppuAddressBus = (ppuAddressBus & 0xFF00) | renderTemp;
                                if (mmc5Ref != null) mmc5Ref.NotifyVramRead(ppuAddressBus);
                            }
                        }

                        // TriCNES line 3649-3652: OctalLatch guard after fetch
                        if (ppu2007_PPU_ALE && !ppu2007_PPU_READ)
                            ppuOctalLatch = (byte)ppuAddressBus;

                        // MMC5 CHR A/B switch at first tile of each group
                        if ((cx == 1 || cx == 321) && chrABAutoSwitch) { byte*[] src = Spritesize8x16 ? (chrBGUseASet ? chrBankPtrsA : chrBankPtrsB) : chrBankPtrsA; for (int i = 0; i < 8; i++) chrBankPtrs[i] = src[i]; }
                    }
                }

                // CalculatePixel + UpdateSpriteShift (TriCNES line 1600: PPU_Dot > 0 && <= 256)
                if (cx > 0 && cx <= 256)
                {
                    // Local cache of static fields for JIT register allocation
                    bool showBG = ShowBackGround;
                    bool showSpr = ShowSprites;

                    if (scanline < 240) // visible scanlines only for CalculatePixel
                    {
                        // ── CalculatePixel (TriCNES line 3073) ──
                        byte backdropIdx = (byte)(ppu_ram[0x3f00] & 0x3f);
                        uint compositeColor = palCache[0];
                        byte compositePalIdx = backdropIdx;
                        int bgColor = 0, bgPalette = 0;

                        if (cx <= 256 && showBG && (cx > 8 || ShowBgLeft8))
                        {
                            int bit = 15 - FineX;
                            bgColor = (((renderHigh >> bit) & 1) << 1) | ((renderLow >> bit) & 1);
                            // TriCNES: attribute from shift registers at bit (7 - FineX)
                            { int ab = 7 - FineX;
                              bgPalette = (((renderAttrHigh >> ab) & 1) << 1) | ((renderAttrLow >> ab) & 1); }
                            if (bgColor == 0) bgPalette = 0;
                        }

                        int sprColor = 0, sprPalette = 0, sprSlot = -1;
                        bool sprPriority = false;
                        if (cx <= 256 && showSpr && (cx > 8 || ShowSprLeft8) && spriteAnyActive)
                        {
                            // Sprite loop fully unrolled — (H|L)>=128 fast-checks bit7 without shift
                            if (sprXCounter[0] == 0 || skippedPreRenderDot341) { if ((sprShiftH[0] | sprShiftL[0]) >= 128) { sprColor = ((sprShiftH[0] >> 7) << 1) | (sprShiftL[0] >> 7); sprPalette = (sprFetchAttr[0] & 3) | 4; sprPriority = ((sprFetchAttr[0] >> 5) & 1) == 0; sprSlot = 0; goto SpriteFound; } }
                            if (sprXCounter[1] == 0 || skippedPreRenderDot341) { if ((sprShiftH[1] | sprShiftL[1]) >= 128) { sprColor = ((sprShiftH[1] >> 7) << 1) | (sprShiftL[1] >> 7); sprPalette = (sprFetchAttr[1] & 3) | 4; sprPriority = ((sprFetchAttr[1] >> 5) & 1) == 0; sprSlot = 1; goto SpriteFound; } }
                            if (sprXCounter[2] == 0 || skippedPreRenderDot341) { if ((sprShiftH[2] | sprShiftL[2]) >= 128) { sprColor = ((sprShiftH[2] >> 7) << 1) | (sprShiftL[2] >> 7); sprPalette = (sprFetchAttr[2] & 3) | 4; sprPriority = ((sprFetchAttr[2] >> 5) & 1) == 0; sprSlot = 2; goto SpriteFound; } }
                            if (sprXCounter[3] == 0 || skippedPreRenderDot341) { if ((sprShiftH[3] | sprShiftL[3]) >= 128) { sprColor = ((sprShiftH[3] >> 7) << 1) | (sprShiftL[3] >> 7); sprPalette = (sprFetchAttr[3] & 3) | 4; sprPriority = ((sprFetchAttr[3] >> 5) & 1) == 0; sprSlot = 3; goto SpriteFound; } }
                            if (sprXCounter[4] == 0 || skippedPreRenderDot341) { if ((sprShiftH[4] | sprShiftL[4]) >= 128) { sprColor = ((sprShiftH[4] >> 7) << 1) | (sprShiftL[4] >> 7); sprPalette = (sprFetchAttr[4] & 3) | 4; sprPriority = ((sprFetchAttr[4] >> 5) & 1) == 0; sprSlot = 4; goto SpriteFound; } }
                            if (sprXCounter[5] == 0 || skippedPreRenderDot341) { if ((sprShiftH[5] | sprShiftL[5]) >= 128) { sprColor = ((sprShiftH[5] >> 7) << 1) | (sprShiftL[5] >> 7); sprPalette = (sprFetchAttr[5] & 3) | 4; sprPriority = ((sprFetchAttr[5] >> 5) & 1) == 0; sprSlot = 5; goto SpriteFound; } }
                            if (sprXCounter[6] == 0 || skippedPreRenderDot341) { if ((sprShiftH[6] | sprShiftL[6]) >= 128) { sprColor = ((sprShiftH[6] >> 7) << 1) | (sprShiftL[6] >> 7); sprPalette = (sprFetchAttr[6] & 3) | 4; sprPriority = ((sprFetchAttr[6] >> 5) & 1) == 0; sprSlot = 6; goto SpriteFound; } }
                            if (sprXCounter[7] == 0 || skippedPreRenderDot341) { if ((sprShiftH[7] | sprShiftL[7]) >= 128) { sprColor = ((sprShiftH[7] >> 7) << 1) | (sprShiftL[7] >> 7); sprPalette = (sprFetchAttr[7] & 3) | 4; sprPriority = ((sprFetchAttr[7] >> 5) & 1) == 0; sprSlot = 7; } }
                            SpriteFound:

                            if (canDetectSprite0Hit && sprSlot == 0 && sprZeroInSlots && showBG && showSpr && bgColor != 0 && sprColor != 0)
                            { if ((ShowSprLeft8 || cx > 8) && cx < 256) { pendingSprite0Hit = true; canDetectSprite0Hit = false; } }

                            // Branchless pixel blend — uses | (not ||) to avoid short-circuit, ternary for potential CMOV
                            if (sprColor != 0 && showSpr) { bool ow = (bgColor == 0) | sprPriority; bgColor = ow ? sprColor : bgColor; bgPalette = ow ? sprPalette : bgPalette; }
                        }

                        // TriCNES v2: palette corruption check
                        if (ppuPaletteCorruptionFromVChange | ppuPaletteCorruptionFromDisable)
                        {
                            ppuPaletteCorruptionFromVChange = false;
                            ppuPaletteCorruptionFromDisable = false;
                            CorruptPalettes(bgColor, vram_addr);
                        }

                        if ((showBG || showSpr) && cx <= 256)
                        { int pa = (bgPalette << 2) | bgColor; if (bgColor == 0) pa = 0; compositeColor = palCache[pa]; compositePalIdx = (byte)(ppu_ram[0x3f00 + pa] & 0x3f); }
                        else if (cx <= 256) { if ((vram_addr & 0x3F1F) >= 0x3F00) { int pa = vram_addr & 0x1F; if ((pa & 3) == 0) pa &= 0x0F; compositeColor = NesColors[ppu_ram[0x3f00 + pa] & 0x3f]; compositePalIdx = (byte)(ppu_ram[0x3f00 + pa] & 0x3f); } }

                        dotColor = compositeColor;
                        dotPalIdx = compositePalIdx;
                    }

                    // ── UpdateSpriteShiftRegisters (TriCNES line 3718, inside PPU_Dot>0 && <=257 block) ──
                    if (cx <= 256)
                    {
                        bool renderEnabled = showSpr || showBG;
                        bool canDecrement = !skippedPreRenderDot341;
                        // SWAR fast path: when all X counters are zero (or canDecrement is false),
                        // batch-shift all 8 sprite shift registers as a single ulong operation
                        ulong* xc = (ulong*)sprXCounter;
                        if (!canDecrement || (xc[0] | xc[1] | xc[2] | xc[3]) == 0)
                        {
                            if (renderEnabled)
                            {
                                *(ulong*)sprShiftL = (*(ulong*)sprShiftL << 1) & 0xFEFEFEFEFEFEFEFEUL;
                                *(ulong*)sprShiftH = (*(ulong*)sprShiftH << 1) & 0xFEFEFEFEFEFEFEFEUL;
                            }
                        }
                        else
                        {
                            for (int s = 0; s < 8; s++)
                            {
                                if (sprXCounter[s] > 0) sprXCounter[s]--;
                                else if (renderEnabled) { sprShiftL[s] <<= 1; sprShiftH[s] <<= 1; }
                            }
                        }
                    }
                }
            }

            // Phase 2: PPU_DATA_StateMachine2 — buffer refill after rendering (TriCNES line 1657)
            PPU_DATA_StateMachine2();

            // PpuClock moved to start of dot (TriCNES line 1478, before SM)

            // ── DrawToScreen (TriCNES line 1764) ──
            if (scanline >= 0 && scanline < 240)
            {
                if (cx >= 4 && cx <= 259)
                {
                    int pos = (scanline << 8) + (cx - 4);
                    ScreenBuf1x[pos] = prevPrevPrevDotColor;
                    if (AnalogEnabled) ntscScanBuf[cx - 4] = prevPrevPrevDotPalIdx;
                }
                if (AnalogEnabled && cx == 260)
                    DecodeScanline(scanline, ntscScanBuf, ppuEmphasis);
            }

            // ── Frame render at SL240 cx1 ──
            if (scanline == 240 && cx == 1)
            {
                RenderScreen();
                frame_count++;
                if (AnalogEnabled) { Ntsc_SetFrameCount(frame_count); Crt_SetFrameCount(frame_count); }
            }

            // ── End of dot: update ppuRenderingEnabled ──
            ppuRenderingEnabled = ShowBackGround_Instant || ShowSprites_Instant;
        }

        // ════════════════════════════════════════════════════════════════
        // Phase 2: Deferred register updates — extracted from ppu_step_new
        // Called only when a deferred update is pending (>99% of dots skip this).
        // ════════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void PpuPhase2_DeferredUpdates(int cx)
        {
            // ── $2006 delayed t→v copy (TriCNES lines 1263-1284) ──
            if (ppu2006UpdateDelay != 0 && --ppu2006UpdateDelay == 0)
            {
                int prevAddr = vram_addr;
                vram_addr = ppu2006PendingAddr;
                ppuAddressBus = vram_addr;
                // TriCNES v2: palette corruption when v leaves palette range
                if ((prevAddr & 0x3FFF) >= 0x3F00 && (vram_addr & 0x3FFF) < 0x3F00)
                    if (scanline < 240 && cx <= 256 && (prevAddr & 0xF) != 0)
                        ppuPaletteCorruptionFromVChange = true;
                if (mapperNeedsA12 && !((ShowBackGround_Instant || ShowSprites_Instant) && (scanline < 240 || scanline == preRenderLine)))
                    NotifyMapperA12(vram_addr);
            }

            // ── $2005 delayed scroll (TriCNES lines 1286-1304) ──
            if (ppu2005UpdateDelay != 0 && --ppu2005UpdateDelay == 0)
            {
                byte v = ppu2005PendingValue;
                if (!vram_latch)
                {
                    FineX = v & 0x07;
                    vram_addr_internal = (vram_addr_internal & 0x7FE0) | ((v & 0xF8) >> 3);
                }
                else
                {
                    vram_addr_internal = (vram_addr_internal & 0x0C1F) | ((v & 0x7) << 12) | ((v & 0xF8) << 2);
                }
                vram_latch = !vram_latch;
            }

            // $2000 delayed control removed — now handled by 2MC push in ppu_w_2000 (TriCNES model)

            // $2007 SM Phase 1 moved to ppu_step_new (runs every dot via PPU_DATA_StateMachine)
        }

        // ════════════════════════════════════════════════════════════════
        // Phase 3: Scanline events — extracted from ppu_step_new
        // Called only on scanlines >= nmiTriggerLine (241, preRenderLine-1, preRenderLine).
        // ════════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void PpuPhase3_Events(int cx)
        {
            if (scanline == nmiTriggerLine) // 241
            {
                if (cx == 0) pendingVblank = true;
            }
            else if (scanline == (preRenderLine - 1) && cx == 340)
            {
                oddSwap = !oddSwap;
            }
            else if (scanline == preRenderLine && cx == 1)
            {
                isVblank = false;
                canDetectSprite0Hit = true;
                isSprite0hit = false;
                isSpriteOverflow = false;
                isSprite0hit_Delayed = false;
                pendingSprite0Hit = false;
                pendingSprite0Hit2 = false;
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Phase 4: Sprite evaluation + scanline init — extracted from ppu_step_new
        // to reduce method body size and allow JIT to optimize the hot Phase 5 path.
        // Runs only on active scanlines (0-239 + pre-render).
        // ════════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void PpuPhase4_SpriteEvalAndInit()
        {
            // TriCNES line 2491-2501: OAM corruption on first rendered dot after re-enable
            if ((ShowBackGround_Instant || ShowSprites_Instant) && oamCorruptPending)
            {
                oamCorruptPending = false;
                if (!oamCorruptSuppressed)
                    ProcessOamCorruption();
                oamCorruptSuppressed = false;
            }

            // TriCNES line 2528-2534: capture corruption index when delayed flag fires
            if (oamCorruptDisabledFlag)
            {
                oamCorruptDisabledFlag = false;
                oamCorruptPending = true;
                oamCorruptIndex = evalOam2Addr;
            }

            // Per-dot sprite evaluation
            bool evalScanline = (scanline >= 0 && scanline < 240) || scanline == preRenderLine;
            bool ro = scanline == preRenderLine;
            int evalDot = ppu_cycles_x; // post-increment PPU_Dot

            // Dots 0-64: clear secondary OAM (Tier 4 gate)
            if (evalScanline && evalDot >= 0 && evalDot <= 64 && (ShowBG_EvalDelay || ShowSpr_EvalDelay))
            {
                if (evalDot == 1) { evalOam2Addr = 0; evalOam2Full = false; evalTick = 0; evalOamOverflowed = false; }
                if ((evalDot & 1) != 0) { oamCopyBuffer = ro ? secondaryOAM[evalOam2Addr] : (byte)0xFF; }
                else if (evalDot > 0) { if (!ro) secondaryOAM[evalOam2Addr] = oamCopyBuffer; evalOam2Addr++; evalOam2Addr &= 0x1F; }
            }

            // Dot 65: init (outside rendering gate)
            if (evalScanline && evalDot == 65) { evalOam2Addr = 0; nineObjectsOnLine = false; }

            // Dots 65-256: evaluation (Tier 1 Instant gate)
            if (evalScanline && evalDot >= 65 && evalDot <= 256 && (ShowBackGround_Instant || ShowSprites_Instant))
            {
                if (evalDot == 65) { sprite0_eval_addr = spr_ram_add; SpriteEvalInit(); SpriteEvalTick(); }
                else { SpriteEvalTick(); if (evalDot == 256) SpriteEvalEnd(); }
            }
            else if (ro && evalDot == 65 && ppuRenderingEnabled) { sprite0_eval_addr = spr_ram_add; }

            // Dots 257-320: sprite fetch (Tier 4 gate, includes dummy BG fetch)
            if (evalDot >= 257 && evalDot <= 320)
            {
                if (ShowBG_EvalDelay || ShowSpr_EvalDelay) spr_ram_add = 0;
                if (evalDot == 257) evalOam2Addr = 0;
                if (evalDot == 262) spriteSizeLatchedForFetch = Spritesize8x16;

                int sprPhase = (evalDot - 257) & 7;
                int slot = (evalDot - 257) >> 3;
                bool sprFetchEnabled = ShowBG_EvalDelay || ShowSpr_EvalDelay;

                // TriCNES line 2833-2836: OctalLatch guard before sprite switch
                if (ppu2007_PPU_READ) ppuOctalLatch = (byte)ppuAddressBus;

                // TriCNES sprite eval cases 0-7 (line 2855-2993) — uses PAR system
                if (sprPhase == 0)
                {
                    // Case 0: Y pos + NT ALE (TriCNES line 2859-2867)
                    if (sprFetchEnabled)
                    {
                        oamCopyBuffer = secondaryOAM[evalOam2Addr];
                        ppuPAR_NT = (ushort)(0x2000 | (vram_addr & 0x0FFF));
                        ppuPAR_MUX = ppuPAR_NT;
                        ppuAddressBus = ppuPAR_MUX;
                        ppuInRangeCheck = (ushort)((scanline & 0xFF) - oamCopyBuffer);
                    }
                    evalOam2Addr++;
                }
                else if (sprPhase == 1)
                {
                    // Case 1: Pattern + dummy NT READ via tile fetch (TriCNES line 2876)
                    if (sprFetchEnabled)
                    {
                        oamCopyBuffer = secondaryOAM[evalOam2Addr];
                        // TriCNES calls PPU_Render_ShiftRegistersAndBitPlanes → cycleTick 1 → NT READ
                        ppuAddressBus = (ushort)((ppuPAR_NT & 0xFF00) | ppuOctalLatch);
                        renderTemp = PpuBusRead(ppuAddressBus);
                        commitNTFetch = true;
                        ppuAddressBus = (ppuAddressBus & 0xFF00) | renderTemp;
                    }
                    evalOam2Addr++;
                }
                else if (sprPhase == 2)
                {
                    // Case 2: Attribute + AT ALE (TriCNES line 2884-2889)
                    if (sprFetchEnabled)
                    {
                        oamCopyBuffer = secondaryOAM[evalOam2Addr];
                        sprFetchAttr[slot] = oamCopyBuffer;
                        ppuPAR_NT = (ushort)(0x2000 | (vram_addr & 0x0FFF));
                        ppuPAR_MUX = ppuPAR_NT;
                        ppuAddressBus = ppuPAR_MUX;
                    }
                    evalOam2Addr++;
                }
                else if (sprPhase == 3)
                {
                    // Case 3: X pos + dummy AT READ via tile fetch (TriCNES line 2900)
                    if (sprFetchEnabled)
                    {
                        oamCopyBuffer = secondaryOAM[evalOam2Addr];
                        sprXPos[slot] = oamCopyBuffer; sprXCounter[slot] = oamCopyBuffer;
                        // TriCNES calls PPU_Render_ShiftRegistersAndBitPlanes → cycleTick 3 → AT READ
                        ppuAddressBus = (ushort)((ppuPAR_AT & 0xFF00) | ppuOctalLatch);
                        renderTemp = PpuBusRead(ppuAddressBus);
                        commitATFetch = true;
                        ppuAddressBus = (ppuAddressBus & 0xFF00) | renderTemp;
                    }
                }
                else if (sprPhase == 4)
                {
                    // Case 4: sprite CHR ALE (low plane) via PAR (TriCNES line 2911-2918)
                    if (sprFetchEnabled)
                    {
                        oamCopyBuffer = secondaryOAM[evalOam2Addr];
                        // TriCNES: GetSpriteAddress sets AddressBus, then CheckPAR updates PAR_CHR
                        // PAR_CHR tile number was set by NT commit (line 3668: OAM2 pattern byte)
                        PPU_CheckPAR(); // sets bit12 (pattern table) + fine Y from InRangeCheck
                        ppuPAR_CHR &= 0b1111111110111; // clear bit 3 (low plane)
                        ppuPAR_MUX = ppuPAR_CHR;
                        ppuAddressBus = ppuPAR_MUX;
                    }
                }
                else if (sprPhase == 5)
                {
                    // Case 5: sprite CHR READ (low plane) via FetchPPU (TriCNES line 2927)
                    if (sprFetchEnabled)
                    {
                        oamCopyBuffer = secondaryOAM[evalOam2Addr];
                        ppuAddressBus = (ushort)((ppuPAR_CHR & 0xFF00) | ppuOctalLatch);
                        ppuChrFetchA12 = (ppuAddressBus >> 12) & 1;
                        if (mapperNeedsA12) NotifyMapperA12(ppuAddressBus);
                        byte tile = PpuBusRead(ppuAddressBus);
                        ppuAddressBus = (ppuAddressBus & 0xFF00) | tile;
                        sprShiftL[slot] = (sprFetchAttr[slot] & 0x40) != 0 ? FlipByte(tile) : tile;
                        if (slot >= sprSlotCount) sprShiftL[slot] = 0;
                        if (!(ppuInRangeCheck < (Spritesize8x16 ? 16 : 8))) sprShiftL[slot] = 0;
                    }
                }
                else if (sprPhase == 6)
                {
                    // Case 6: sprite CHR ALE (high plane) via PAR (TriCNES line 2951-2959)
                    if (sprFetchEnabled)
                    {
                        oamCopyBuffer = secondaryOAM[evalOam2Addr];
                        // TriCNES: recalculate GetSpriteAddress, AddressBus |= 8, CheckPAR, PAR |= 8
                        PPU_CheckPAR();
                        ppuPAR_CHR |= 8; // set bit 3 (high plane)
                        ppuPAR_MUX = ppuPAR_CHR;
                        ppuAddressBus = ppuPAR_MUX;
                    }
                }
                else // sprPhase == 7
                {
                    // Case 7: sprite CHR READ (high plane) via FetchPPU (TriCNES line 2969)
                    if (sprFetchEnabled)
                    {
                        oamCopyBuffer = secondaryOAM[evalOam2Addr];
                        ppuAddressBus = (ushort)((ppuPAR_CHR & 0xFF00) | ppuOctalLatch);
                        ppuChrFetchA12 = (ppuAddressBus >> 12) & 1;
                        if (mapperNeedsA12 && !mapperA12IsMmc3) NotifyMapperA12(ppuAddressBus);
                        byte tile = PpuBusRead(ppuAddressBus);
                        ppuAddressBus = (ppuAddressBus & 0xFF00) | tile;
                        sprShiftH[slot] = (sprFetchAttr[slot] & 0x40) != 0 ? FlipByte(tile) : tile;
                        if (slot >= sprSlotCount) sprShiftH[slot] = 0;
                        if (!(ppuInRangeCheck < (Spritesize8x16 ? 16 : 8))) sprShiftH[slot] = 0;
                    }
                    evalOam2Addr++;
                }

                // TriCNES line 2995-2998: OctalLatch guard after sprite switch
                if (ppu2007_PPU_ALE && !ppu2007_PPU_READ) ppuOctalLatch = (byte)ppuAddressBus;

                if (mmc5Ref != null) { if (sprPhase == 1) mmc5Ref.NotifyVramRead(0x2000); else if (sprPhase == 3) mmc5Ref.NotifyVramRead(0x23C0); else if (sprPhase == 5) mmc5Ref.NotifyVramRead(SpPatternTableAddr); else if (sprPhase == 7) mmc5Ref.NotifyVramRead(SpPatternTableAddr | 8); }
            }

            // Dot 321 equivalent
            if (evalDot == 322 && scanline < 240 && (ShowBackGround_Instant || ShowSprites_Instant))
                oamCopyBuffer = secondaryOAM[0];

            // Dot 257: copy sprite slot count
            if (scanline >= 0 && scanline < 240 && evalDot == 257) { sprSlotCount = evalSpriteCount; sprZeroInSlots = evalSprite0Visible; }
            else if (scanline == preRenderLine && evalDot == 257) { sprSlotCount = evalSpriteCount; sprZeroInSlots = evalSprite0Visible; }
            if (scanline == preRenderLine && evalDot == 257 && ppuRenderingEnabled) PrecomputePreRenderSprites();

            // Dot 339: sprite active flag + conditional counter init
            // TriCNES v2: rendering ON at dot 339 → counters NOT touched here
            //   (they were set during sprite fetch dots 257-320 via sprXPos;
            //    if fetch was skipped due to rendering off, counters keep their previous value)
            // Rendering OFF at dot 339 → zero all counters (halted mode)
            // This enables stale sprite shift register behavior: if rendering was off during
            // sprite fetch but re-enabled before dot 339, the counter retains its old value
            // (likely 0 = halted) and stale shift data outputs immediately.
            if (evalDot == 339)
            {
                if (!(ShowSprites || ShowBackGround))
                {
                    for (int i = 0; i < 8; i++)
                        sprXCounter[i] = 0;
                }
                bool anyActive = false;
                for (int i = 0; i < 8; i++)
                    if ((sprShiftH[i] | sprShiftL[i]) != 0) anyActive = true;
                spriteAnyActive = anyActive;
            }

            // Garbage/Dummy NT fetch (TriCNES: PPU_Render_ShiftRegistersAndBitPlanes_DummyNT)
            // dots 337-340 + dot 0: set bus to NT addr, do dummy fetch, update OctalLatch
            if (evalDot >= 337 || evalDot == 0)
            {
                if (ShowBG_EvalDelay || ShowSpr_EvalDelay) // TriCNES: _Delayed gate
                {
                    // OctalLatch guard before (TriCNES line 3697-3700)
                    if (ppu2007_PPU_READ) ppuOctalLatch = (byte)ppuAddressBus;

                    if (evalDot == 0)
                    {
                        // Dot 0: idle/setup. Use NT address (A12=0) to maintain M2Filter
                        // for correct MMC3 scanline counter behavior with BG at $1000.
                        // TriCNES uses CHR PAR here but also fails mmc3_test #8.
                        ppuAddressBus = 0x2000 | (vram_addr & 0x0FFF);
                    }
                    else
                    {
                        int dt = evalDot - 337;
                        if (dt == 0 || dt == 2) // ALE: set NT address
                        {
                            ppuAddressBus = 0x2000 | (vram_addr & 0x0FFF);
                        }
                        else if (dt == 1) // READ: fetch NT (commit)
                        {
                            ppuAddressBus = 0x2000 | (vram_addr & 0x0FFF);
                            renderTemp = (byte)PpuBusRead((ppuAddressBus & 0xFF00) | ppuOctalLatch);
                            ppuAddressBus = (ppuAddressBus & 0xFF00) | renderTemp;
                            commitNTFetch = true;
                        }
                        else if (dt == 3) // READ: dummy fetch (no commit)
                        {
                            renderTemp = (byte)PpuBusRead((ppuAddressBus & 0xFF00) | ppuOctalLatch);
                            ppuAddressBus = (ppuAddressBus & 0xFF00) | renderTemp;
                        }
                    }

                    // OctalLatch guard after (TriCNES line 3734-3737)
                    if (ppu2007_PPU_ALE && !ppu2007_PPU_READ) ppuOctalLatch = (byte)ppuAddressBus;
                }

                if (mmc5Ref != null && (evalDot == 337 || evalDot == 339)) mmc5Ref.NotifyVramRead(0x2000 | (vram_addr & 0x0FFF));
            }

            if (mmc5Ref != null && (evalDot == 337 || evalDot == 339)) mmc5Ref.NotifyVramRead(0x2000 | (vram_addr & 0x0FFF));

            // Per-cycle sprite overflow + scanline init
            if (scanline >= 0 && scanline < 240)
            {
                if (evalDot == 1)
                {
                    int scanOff = scanline << 8;
                    // SWAR: clear 256 ints (1024 bytes) as 128 ulongs
                    ulong* bgp = (ulong*)(Buffer_BG_array + scanOff);
                    for (int i = 0; i < 128; i++) bgp[i] = 0;
                    // SWAR: fill 256 uints with backdrop color as 128 ulongs
                    { uint bgColor = palCache[0]; ulong fill = bgColor | ((ulong)bgColor << 32); ulong* sp = (ulong*)(ScreenBuf1x + scanOff); for (int i = 0; i < 128; i++) sp[i] = fill; if (AnalogEnabled) { byte bgIdx = (byte)(ppu_ram[0x3f00] & 0x3f); for (int i = 0; i < 256; i++) ntscScanBuf[i] = bgIdx; } }
                    PrecomputeOverflow();
                }
                if (spriteOverflowCycle >= 0 && evalDot == spriteOverflowCycle) isSpriteOverflow = true;
            }
        }

        // ════════════════════════════════════════════════════════════════
        // _EmulateHalfPPU — half PPU step (called at mcPpuClock == 2)
        // ════════════════════════════════════════════════════════════════
        // Method 1: PPU_DATA_StateMachine — TriCNES line 1761-1806
        // Phase 1: full dot, BEFORE rendering. Advances SR latch pipeline,
        // computes PD_RB/ReadALE/PPU_READ/PPU_ALE signals.
        // Called from PpuPhase2_DeferredUpdates.
        // ════════════════════════════════════════════════════════════════
        static void PPU_DATA_StateMachine()
        {
            // TriCNES line 1763-1764
            bool BLNK = (!ShowBackGround && !ShowSprites) || (scanline >= 240 && scanline < preRenderLine);
            ppu2007_BLNK_Latch = BLNK;
            // TriCNES line 1765: H0_DASH = (PPU_Dot - 1 & 1) != 0
            // Odd dot = ALE (H0_DASH=false), Even dot = READ (H0_DASH=true)
            bool H0_DASH = ((ppu_cycles_x - 1) & 1) != 0;

            // TriCNES line 1767-1768
            ppu2007_PaletteRAMEnable = ((ppuAddressBus & 0x3F00) == 0x3F00) && ppu2007_BLNK_Latch;
            ppu2007_Read_XRB = ppu2007_Read && ppu2007_PaletteRAMEnable;

            // TriCNES line 1770-1774: advance read latches (even index)
            ppu2007_ReadLatches[0] = ppu2007_Read_SR;
            if (ppu2007_Read)
                ppu2007_Read = false;
            ppu2007_ReadLatches[2] = !ppu2007_ReadLatches[1];
            ppu2007_ReadLatches[4] = !ppu2007_ReadLatches[3];

            // TriCNES line 1777-1778: derive PD_RB and ReadALE
            ppu2007_PD_RB = ppu2007_ReadLatches[4] && !ppu2007_ReadLatches[2];
            ppu2007_ReadALE = !ppu2007_ReadLatches[4] && ppu2007_ReadLatches[2];

            // TriCNES line 1782: PPU_READ — true on READ dots
            ppu2007_PPU_READ = ppu2007_PD_RB || (!BLNK && H0_DASH);

            // TriCNES line 1784-1791: advance write latches (even index)
            ppu2007_WriteLatches[0] = ppu2007_Write_SR;
            if (ppu2007_Write)
                ppu2007_Write = false;
            ppu2007_WriteLatches[2] = !ppu2007_WriteLatches[1];
            ppu2007_WriteLatches[4] = !ppu2007_WriteLatches[3];
            ppu2007_WriteALE = !ppu2007_WriteLatches[4] && ppu2007_WriteLatches[2];

            // TriCNES line 1793
            ppu2007_TStep_Latch = ppu2007_DB_PAR;

            // TriCNES line 1795-1796: PPU_ALE
            bool b = !BLNK && !H0_DASH;
            ppu2007_PPU_ALE = ppu2007_ReadALE || ppu2007_WriteALE || b;

            // TriCNES line 1798-1805: SM ALE → address bus
            if (ppu2007_ReadALE || ppu2007_WriteALE)
            {
                if (!ppu2007_PPU_READ)
                {
                    ppuAddressBus = vram_addr;
                    ppuOctalLatch = (byte)ppuAddressBus;
                }
            }

        }

        // ════════════════════════════════════════════════════════════════
        // Method 2: PPU_DATA_StateMachine2 — TriCNES line 1807-1826
        // Phase 2: full dot, AFTER rendering. Executes buffer refill when PD_RB.
        // Called from ppu_step_new after tile fetch.
        // ════════════════════════════════════════════════════════════════
        static void PPU_DATA_StateMachine2()
        {
            if (ppu2007_PD_RB)
            {
                // TriCNES line 1820: PPU_ReadBuffer = FetchPPU()
                // FetchPPU: addr = (AddressBus & 0x3F00) | OctalLatch, then AddressBus low = data
                int addr = (ppuAddressBus & 0x3F00) | ppuOctalLatch;
                byte data = PpuBusRead(addr >= 0x3F00 ? addr & 0x2FFF : addr & 0x3FFF);
                ppu_2007_buffer = data;
                // TriCNES FetchPPU side effect: AddressBus = (AddressBus & 0xFF00) | data
                ppuAddressBus = (ppuAddressBus & 0xFF00) | data;

                if (debug2007Log && scanline >= 0 && scanline < 240)
                    System.Console.Error.WriteLine($"SM2 sl={scanline} cx={ppu_cycles_x} addr={addr:X4} buf={data:X2} bus={ppuAddressBus:X4} ol={ppuOctalLatch:X2}");

                // TriCNES line 1821-1824
                if (ppu2007_PPU_ALE)
                    ppuOctalLatch = (byte)ppuAddressBus;
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Method 3: PPU_DATA_StateMachine_Half — TriCNES line 1827-1868
        // Phase 3: half-step. v increment + second FetchPPU + write execution
        // + odd-index latch advancement + SR reset.
        // Called from ppu_half_step_new.
        // ════════════════════════════════════════════════════════════════
        static void PPU_DATA_StateMachine_Half()
        {
            // TriCNES line 1829-1837: TStep → v increment
            ppu2007_TStep = ppu2007_TStep_Latch || ppu2007_PD_RB;
            if (ppu2007_TStep)
            {
                if (debug2007Log && scanline >= 0 && scanline < 240)
                    System.Console.Error.WriteLine($"TStp sl={scanline} cx={ppu_cycles_x} v={vram_addr:X4} TL={ppu2007_TStep_Latch} PD={ppu2007_PD_RB}");
                // TriCNES line 1832: always increment v (no rendering gate, no 14-bit mask)
                vram_addr = (ushort)(vram_addr + VramaddrIncrement);
                if (!ppu2007_BLNK_Latch)
                {
                    // TriCNES line 1835: also IncrementScrollY during rendering
                    Yinc();
                }
                // TriCNES: NO ppuAddressBus/ppuOctalLatch/mapper update here
                // Bus is updated by rendering-OFF check on next dot, or by tile fetch during rendering
            }

            // TriCNES line 1839
            ppu2007_PPU_ALE = ppu2007_ReadALE || ppu2007_WriteALE;

            // TriCNES line 1840-1848: second FetchPPU (after v increment)
            if (ppu2007_PD_RB)
            {
                int addr = (ppuAddressBus & 0x3F00) | ppuOctalLatch;
                byte data = PpuBusRead(addr >= 0x3F00 ? addr & 0x2FFF : addr & 0x3FFF);
                ppu_2007_buffer = data;
                ppuAddressBus = (ppuAddressBus & 0xFF00) | data; // FetchPPU side effect
                if (ppu2007_PPU_ALE)
                    ppuOctalLatch = (byte)ppuAddressBus;
            }

            // TriCNES line 1849-1854: advance read latches (odd index) + SR reset
            ppu2007_ReadLatches[1] = !ppu2007_ReadLatches[0];
            ppu2007_ReadLatches[3] = !ppu2007_ReadLatches[2];
            if (!ppu2007_ReadLatches[3])
                ppu2007_Read_SR = false;

            // TriCNES line 1856-1860: advance write latches (odd index) + SR reset
            ppu2007_WriteLatches[1] = !ppu2007_WriteLatches[0];
            ppu2007_WriteLatches[3] = !ppu2007_WriteLatches[2];
            if (!ppu2007_WriteLatches[3])
                ppu2007_Write_SR = false;

            // TriCNES line 1862-1867: DB_PAR → write execution
            ppu2007_DB_PAR = ppu2007_WriteLatches[1] && !ppu2007_WriteLatches[3];
            ppu2007_PPU_WRITE = !ppu2007_PaletteRAMEnable && ppu2007_DB_PAR;
            if (ppu2007_DB_PAR)
            {
                // TriCNES line 1866: StorePPUData(AddressBus, WriteData)
                PpuBusWrite(ppuAddressBus, ppu2007SM_writeValue);
            }
        }

        // ════════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_half_step_new()
        {
            int hsDot = ppu_cycles_x;
            bool isRendering = ShowBackGround || ShowSprites;
            bool isActiveScanline = scanline < 240 || scanline == preRenderLine;

            // ── BG shift register shift ──
            if (isActiveScanline && isRendering
                && ((hsDot > 0 && hsDot <= 257) || (hsDot > 320 && hsDot <= 336)))
            {
                renderLow  <<= 1;
                renderHigh = (renderHigh << 1) | 1;
                renderAttrLow  = (renderAttrLow << 1) | (attrLatch & 1);
                renderAttrHigh = (renderAttrHigh << 1) | ((attrLatch >> 1) & 1);
            }

            // ── CommitShiftRegistersAndBitPlanes — TriCNES line 1691 (inside _EmulateHalfPPU) ──
            // Process commit flags set by tile fetch in previous full dot, then load shift registers.
            if (commitNTFetch)
            {
                commitNTFetch = false;
                NTVal = renderTemp;
                // TriCNES line 3661-3669: update PAR_CHR tile number from bus low byte
                ppuPAR_CHR &= 0b1000000001111; // keep bit12 + fine Y bits 0-2
                if (ppu_cycles_x < 256 || ppu_cycles_x > 320)
                    ppuPAR_CHR |= (ushort)((byte)(ppuAddressBus) << 4); // BG: tile from bus
                else
                    ppuPAR_CHR |= (ushort)(secondaryOAM[(evalOam2Addr & 0x1C) + 1] << 4); // Sprite: tile from OAM2
            }
            if (commitATFetch)
            {
                commitATFetch = false;
                byte atRaw = renderTemp;
                if (extAttrEnabled && extAttrNTOffset < 960) {
                    byte exVal = extAttrRAM[extAttrNTOffset];
                    extAttrChrBank = (exVal & 0x3F) | (extAttrChrUpperBits << 6);
                    ATVal = (byte)((exVal >> 6) & 3);
                } else {
                    ATVal = (byte)((atRaw >> (((vram_addr >> 4) & 0x04) | (vram_addr & 0x02))) & 0x03);
                }
                pendingAttrLatch = ATVal;
            }
            if (commitPatLowFetch) { commitPatLowFetch = false; pendingTileLow = renderTemp; }
            if (commitPatHighFetch)
            {
                commitPatHighFetch = false;
                pendingTileHigh = renderTemp;
                // TriCNES line 3689-3690: LoadShiftRegisters + IncrementScrollX
                renderLow  = (renderLow & 0xFF00) | pendingTileLow;
                renderHigh = (renderHigh & 0xFF00) | pendingTileHigh;
                attrLatch  = pendingAttrLatch;
                CXinc();
            }

            // ── VBL latch half-step (branchless) ──
            ppuVSET = pendingVblank;
            pendingVblank = false;
            ppuVSET_Latch2 = !ppuVSET_Latch1;

            // ── OAM buffer update (redundant branch eliminated) ──
            if (isRendering && scanline >= 0 && scanline < 240)
            {
                if (hsDot == 0 || hsDot > 320) ppuOamBuffer = secondaryOAM[0];
                else if (hsDot <= 64)          ppuOamBuffer = 0xFF;
                else                           ppuOamBuffer = oamCopyBuffer;
            }

            // ── Sprite0 hit pipeline ──
            isSprite0hit_Delayed = isSprite0hit;
            if (pendingSprite0Hit2) { pendingSprite0Hit2 = false; isSprite0hit = true; }
            if (pendingSprite0Hit)  { pendingSprite0Hit  = false; pendingSprite0Hit2 = true; }

            // Phase 3: PPU_DATA_StateMachine_Half — v inc + second FetchPPU + write (TriCNES line 1734)
            PPU_DATA_StateMachine_Half();
        }
    }
}
