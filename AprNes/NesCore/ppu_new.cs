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
            PPU_DATA_Pipeline_Step(1);

            // ── Delayed OAM corruption (TriCNES lines 1695-1711) ──
            if (oamCorruptDelay != 0 && --oamCorruptDelay == 0 &&
                oamCorruptWasRendering && isActiveScanline &&
                !oamCorruptPending && (oamCorrupt2001Value & 0x18) == 0)
            {
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
                            if (fetchPair < 2) // 0: NT, 1: AT
                            {
                                if (fetchPair == 0)
                                    ppuPAR_NT = (ushort)(0x2000 | (vram_addr & 0x0FFF));
                                else
                                    ppuPAR_AT = (ushort)(0x23C0 | (vram_addr & 0x0C00) | ((vram_addr >> 4) & 0x38) | ((vram_addr >> 2) & 0x07));
                                ppuPAR_MUX = (fetchPair == 0) ? ppuPAR_NT : ppuPAR_AT;
                            }
                            else // 2: CHR-L, 3: CHR-H
                            {
                                PPU_CheckPAR();
                                ppuPAR_CHR = (ushort)((ppuPAR_CHR & ~8) | ((fetchPair & 1) << 3));
                                ppuPAR_MUX = ppuPAR_CHR;
                            }
                            ppuAddressBus = ppuPAR_MUX;
                        }
                        else // even cx = READ — TriCNES cycleTick 1,3,5,7
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
                            ppuAddressBus = (ppuAddressBus & 0xFF00) | renderTemp;

                            if (fetchPair == 0) { commitNTFetch = true; if (extAttrEnabled) extAttrNTOffset = (ushort)(ppuAddressBus & 0x3FF); }
                            else if (fetchPair == 1) commitATFetch = true;
                            else if (fetchPair == 2) commitPatLowFetch = true;
                            else                     commitPatHighFetch = true;

                            if (mmc5Ref != null) mmc5Ref.NotifyVramRead(ppuAddressBus);
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
                            for (int i = 0; i < 8; i++)
                            {
                                if (sprXCounter[i] == 0 || skippedPreRenderDot341)
                                {
                                    int h = sprShiftH[i], l = sprShiftL[i];
                                    if ((h | l) >= 128)
                                    {
                                        int attr = sprFetchAttr[i];
                                        sprColor = ((h >> 7) << 1) | (l >> 7);
                                        sprPalette = (attr & 3) | 4;
                                        sprPriority = (attr & 0x20) == 0;
                                        sprSlot = i;
                                        break;
                                    }
                                }
                            }

                            if (sprColor != 0)
                            {
                                if (canDetectSprite0Hit && sprSlot == 0 && sprZeroInSlots && showBG && bgColor != 0)
                                { if ((ShowSprLeft8 || cx > 8) && cx < 256) { pendingSprite0Hit = true; canDetectSprite0Hit = false; } }

                                bool ow = (bgColor == 0) | sprPriority;
                                bgColor = ow ? sprColor : bgColor;
                                bgPalette = ow ? sprPalette : bgPalette;
                            }
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
                            int renderShift = renderEnabled ? 1 : 0;
                            for (int s = 0; s < 8; s++)
                            {
                                int isZero = sprXCounter[s] == 0 ? 1 : 0;
                                sprXCounter[s] -= 1 - isZero;
                                int doShift = isZero & renderShift;
                                sprShiftL[s] <<= doShift;
                                sprShiftH[s] <<= doShift;
                            }
                        }
                    }
                }
            }

            // Phase 2: PPU_DATA_StateMachine2 — buffer refill after rendering (TriCNES line 1657)
            PPU_DATA_Pipeline_Step(2);

            // PpuClock moved to start of dot (TriCNES line 1478, before SM)

            // ── DrawToScreen (TriCNES line 1764) ──
            if (scanline >= 0 && scanline < 240)
            {
                if (cx >= 4 && cx <= 259)
                {
                    int pos = (scanline << 8) + (cx - 4);
                    if (AnalogEnabled) ntscScanBuf[cx - 4] = prevPrevPrevDotPalIdx;
                    else ScreenBuf1x[pos] = prevPrevPrevDotColor;
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

                // TriCNES sprite eval cases 0-7 (line 2855-2993) — even=ALE, odd=READ
                if (sprFetchEnabled)
                {
                    oamCopyBuffer = secondaryOAM[evalOam2Addr];

                    if ((sprPhase & 1) == 0)
                    {
                        // Even phases (0, 2, 4, 6): Address Latch Enable
                        if (sprPhase < 4)
                        {
                            if (sprPhase == 0) ppuInRangeCheck = (ushort)((scanline & 0xFF) - oamCopyBuffer);
                            else               sprFetchAttr[slot] = oamCopyBuffer; // Phase 2
                            ppuPAR_NT = (ushort)(0x2000 | (vram_addr & 0x0FFF));
                            ppuPAR_MUX = ppuPAR_NT;
                        }
                        else
                        {
                            PPU_CheckPAR();
                            ppuPAR_CHR = (ushort)((ppuPAR_CHR & ~8) | ((sprPhase & 2) << 2));
                            ppuPAR_MUX = ppuPAR_CHR;
                        }
                        ppuAddressBus = ppuPAR_MUX;
                    }
                    else
                    {
                        // Odd phases (1, 3, 5, 7): Memory Read
                        if (sprPhase == 3) { sprXPos[slot] = oamCopyBuffer; sprXCounter[slot] = oamCopyBuffer; }

                        ushort baseAddr = (sprPhase == 1) ? ppuPAR_NT : ((sprPhase == 3) ? ppuPAR_AT : ppuPAR_CHR);
                        ppuAddressBus = (ushort)((baseAddr & 0xFF00) | ppuOctalLatch);

                        if (sprPhase >= 5)
                        {
                            ppuChrFetchA12 = (ppuAddressBus >> 12) & 1;
                            if (mapperNeedsA12 && (sprPhase == 5 || !mapperA12IsMmc3))
                                NotifyMapperA12(ppuAddressBus);
                        }

                        byte val = PpuBusRead(ppuAddressBus);
                        ppuAddressBus = (ppuAddressBus & 0xFF00) | val;

                        if (sprPhase < 5)
                        {
                            renderTemp = val;
                            if (sprPhase == 1) commitNTFetch = true;
                            else               commitATFetch = true;
                        }
                        else
                        {
                            byte tile = (sprFetchAttr[slot] & 0x40) != 0 ? FlipByte(val) : val;
                            if (slot >= sprSlotCount || ppuInRangeCheck >= (Spritesize8x16 ? 16 : 8))
                                tile = 0;
                            if (sprPhase == 5) sprShiftL[slot] = tile;
                            else               sprShiftH[slot] = tile;
                        }
                    }
                }
                // Branchless increment: phases 0,1,2,7 → mask 0x87 (10000111)
                evalOam2Addr += (byte)((0x87 >> sprPhase) & 1);

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
                    // SWAR: clear 8 ints (32 bytes) as 4 ulongs
                    ulong* xc = (ulong*)sprXCounter;
                    xc[0] = 0; xc[1] = 0; xc[2] = 0; xc[3] = 0;
                }
                // SWAR: check 8+8 bytes in one 64-bit OR
                spriteAnyActive = ((*(ulong*)sprShiftH) | (*(ulong*)sprShiftL)) != 0;
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
                    if (AnalogEnabled) { byte bgIdx = (byte)(ppu_ram[0x3f00] & 0x3f); for (int i = 0; i < 256; i++) ntscScanBuf[i] = bgIdx; }
                    else { uint bgColor = palCache[0]; ulong fill = bgColor | ((ulong)bgColor << 32); ulong* sp = (ulong*)(ScreenBuf1x + scanOff); for (int i = 0; i < 128; i++) sp[i] = fill; }
                    PrecomputeOverflow();
                }
                if (spriteOverflowCycle >= 0 && evalDot == spriteOverflowCycle) isSpriteOverflow = true;
            }
        }

        // ════════════════════════════════════════════════════════════════
        // _EmulateHalfPPU — half PPU step (called at mcPpuClock == 2)
        // ════════════════════════════════════════════════════════════════
        // Unified $2007 SR Latch Pipeline — merges Phase 1/2/3
        // phase 1: dot start (signal setup), phase 2: after tile fetch (buffer refill),
        // phase 3: half-step (v inc + odd latch + write)
        // ════════════════════════════════════════════════════════════════
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        static void PPU_DATA_Pipeline_Step(int phase)
        {
            if (phase == 1)
            {
                bool BLNK = (!ShowBackGround && !ShowSprites) || (scanline >= 240 && scanline < preRenderLine);
                ppu2007_BLNK_Latch = BLNK;
                bool H0_DASH = ((ppu_cycles_x - 1) & 1) != 0;
                ppu2007_PaletteRAMEnable = ((ppuAddressBus & 0x3F00) == 0x3F00) && BLNK;

                byte newREven = (byte)((ppu2007_Read_SR ? 1 : 0) | ((~readLatch << 1) & 0x14));
                readLatch = (byte)((readLatch & 0x0A) | newREven);
                ppu2007_PD_RB = (readLatch & 0x14) == 0x10;
                ppu2007_ReadALE = (readLatch & 0x14) == 0x04;
                ppu2007_PPU_READ = ppu2007_PD_RB || (!BLNK && H0_DASH);

                byte newWEven = (byte)((ppu2007_Write_SR ? 1 : 0) | ((~writeLatch << 1) & 0x14));
                writeLatch = (byte)((writeLatch & 0x0A) | newWEven);
                ppu2007_WriteALE = (writeLatch & 0x14) == 0x04;

                ppu2007_TStep_Latch = ppu2007_DB_PAR;
                ppu2007_PPU_ALE = ppu2007_ReadALE || ppu2007_WriteALE || (!BLNK && !H0_DASH);

                if ((ppu2007_ReadALE || ppu2007_WriteALE) && !ppu2007_PPU_READ)
                {
                    ppuAddressBus = vram_addr;
                    ppuOctalLatch = (byte)vram_addr;
                }
            }
            else
            {
                if (phase == 3)
                {
                    ppu2007_TStep = ppu2007_TStep_Latch || ppu2007_PD_RB;
                    if (ppu2007_TStep)
                    {
                        vram_addr = (ushort)(vram_addr + VramaddrIncrement);
                        if (!ppu2007_BLNK_Latch) Yinc();
                    }
                    ppu2007_PPU_ALE = ppu2007_ReadALE || ppu2007_WriteALE;
                }

                if (ppu2007_PD_RB)
                {
                    int addr = (ppuAddressBus & 0x3F00) | ppuOctalLatch;
                    byte data = PpuBusRead(addr >= 0x3F00 ? addr & 0x2FFF : addr);
                    ppu_2007_buffer = data;
                    ppuAddressBus = (ppuAddressBus & 0xFF00) | data;
                    if (ppu2007_PPU_ALE) ppuOctalLatch = data;
                }

                if (phase == 2) return;

                byte newROdd = (byte)(((~readLatch) << 1) & 0x0A);
                readLatch = (byte)((readLatch & 0x15) | newROdd);
                if ((readLatch & 0x08) == 0) ppu2007_Read_SR = false;

                byte newWOdd = (byte)(((~writeLatch) << 1) & 0x0A);
                writeLatch = (byte)((writeLatch & 0x15) | newWOdd);
                if ((writeLatch & 0x08) == 0) ppu2007_Write_SR = false;

                ppu2007_DB_PAR = (writeLatch & 0x0A) == 0x02;
                ppu2007_PPU_WRITE = !ppu2007_PaletteRAMEnable && ppu2007_DB_PAR;
                if (ppu2007_DB_PAR)
                {
                    PpuBusWrite(ppuAddressBus, ppu2007SM_writeValue);
                }
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
            PPU_DATA_Pipeline_Step(3);
        }
    }
}
