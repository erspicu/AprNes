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
        static bool useNewPpuStep = true;

        // TriCNES: CopyV flag — set when $2006 delayed copy fires, used for scroll conflict detection
        static bool copyV = false;

        // ════════════════════════════════════════════════════════════════
        // _EmulatePPU — full PPU step (called at mcPpuClock == 0)
        // TriCNES: Emulator.cs line 1256
        // ════════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_step_new()
        {
            int cx = ppu_cycles_x; // local alias, PRE-increment value

            // ══════════════════════════════════════════════════════
            // Phase 2: Deferred register updates (TriCNES lines 1263-1496)
            // ══════════════════════════════════════════════════════

            // ── $2006 delayed t→v copy (TriCNES lines 1263-1284) ──
            copyV = false;
            if (ppu2006UpdateDelay > 0)
            {
                ppu2006UpdateDelay--;
                if (ppu2006UpdateDelay == 0)
                {
                    int prevAddr = vram_addr;
                    copyV = true;
                    vram_addr = ppu2006PendingAddr;
                    ppuAddressBus = vram_addr;
                    // P4-2: Palette corruption when leaving palette range
                    if ((prevAddr & 0x3FFF) >= 0x3F00 && (vram_addr & 0x3FFF) < 0x3F00)
                    {
                        if (scanline < 240 && cx <= 256)
                        {
                            if ((prevAddr & 0xF) != 0)
                                paletteCorruptFromVAddr = true;
                        }
                    }
                    // A12 notify outside active rendering
                    if (mapperNeedsA12 && !((ShowBackGround_Instant || ShowSprites_Instant) && (scanline < 240 || scanline == preRenderLine)))
                        NotifyMapperA12(vram_addr);
                }
            }

            // ── $2005 delayed scroll (TriCNES lines 1286-1304) ──
            if (ppu2005UpdateDelay > 0)
            {
                ppu2005UpdateDelay--;
                if (ppu2005UpdateDelay == 0)
                {
                    byte v = ppu2005PendingValue;
                    if (!vram_latch) // first write
                    {
                        FineX = v & 0x07;
                        vram_addr_internal = (vram_addr_internal & 0x7FE0) | ((v & 0xF8) >> 3);
                    }
                    else // second write
                    {
                        vram_addr_internal = (vram_addr_internal & 0x0C1F) | ((v & 0x7) << 12) | ((v & 0xF8) << 2);
                    }
                    vram_latch = !vram_latch;
                }
            }

            // ── $2000 delayed control (TriCNES lines 1306-1320) ──
            if (ppu2000UpdateDelay > 0)
            {
                ppu2000UpdateDelay--;
                if (ppu2000UpdateDelay == 0)
                {
                    NMIable = (ppu2000PendingValue & 0x80) != 0;
                    VramaddrIncrement = (ppu2000PendingValue & 0x04) != 0 ? 32 : 1;
                    Spritesize8x16 = (ppu2000PendingValue & 0x20) != 0;
                    SpPatternTableAddr = (ppu2000PendingValue & 0x08) != 0 ? 0x1000 : 0;
                    BgPatternTableAddr = (ppu2000PendingValue & 0x10) != 0 ? 0x1000 : 0;
                    vram_addr_internal = (ushort)((vram_addr_internal & 0x73FF) | ((ppu2000PendingValue & 3) << 10));
                }
            }

            // ── $2007 state machine (TriCNES lines 1322-1496) ──
            Ppu2007SmTick();

            // Open bus decay (AprNes-specific, runs every dot)
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
                if (scanline > 261)
                {
                    scanline = 0;
                    if (ShowBackGround_Instant || ShowSprites_Instant)
                        ProcessOamCorruption();
                }
            }

            // ══════════════════════════════════════════════════════
            // Phase 3: Events (TriCNES lines 1532-1606)
            // All use POST-increment cx (= PPU_Dot after ++)
            // ══════════════════════════════════════════════════════
            if (scanline == nmiTriggerLine) // 241
            {
                if (cx == 0) pendingVblank = true; // TriCNES: PPU_Dot == 0
                // cx == 1: FrameAdvance (emulator-specific, not PPU logic)
            }
            else if (scanline == 260 && cx == 340)
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

            // ── VSET latch pipeline (TriCNES lines 1608-1618) ──
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

            // ── Mapper + A12 (TriCNES lines 1627-1628) ──
            MapperObj.PpuClock();
            ppuA12Prev = (ppuAddressBus & 0x1000) != 0;

            // ── Odd frame skip (TriCNES lines 1629-1643) ──
            if (oddSwap && (ShowBackGround || ShowSprites)) // Tier 2 delayed flags
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

            // ══════════════════════════════════════════════════════
            // Phase 4: Eval delay + sprite eval + $2001 + emphasis
            // (TriCNES lines 1652-1722)
            // ══════════════════════════════════════════════════════

            // ── Eval delay: non-phase-3 (TriCNES lines 1653-1658) ──
            if ((mcCpuClock & 3) != 3)
            {
                ShowBG_EvalDelay = ShowBackGround;
                ShowSpr_EvalDelay = ShowSprites;
            }

            // ── Sprite evaluation (TriCNES line 1664, inside scanline gate) ──
            if (scanline < 240 || scanline == preRenderLine)
            {
                // Per-dot sprite evaluation (existing AccuracyOptA code)
                // TODO Phase 7: port PPU_Render_SpriteEvaluation from TriCNES
                // For now, call existing sprite eval logic inline:
                if (AccuracyOptA)
                {
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

                        // Dummy BG fetch (cases 0-3)
                        if (sprPhase <= 3)
                        {
                            int bgPhase = evalDot & 7;
                            if (bgPhase == 1) ppuAddressBus = (ushort)(0x2000 | (vram_addr & 0x0FFF));
                            else if (bgPhase == 3) ppuAddressBus = (ushort)(0x23C0 | (vram_addr & 0x0C00) | ((vram_addr >> 4) & 0x38) | ((vram_addr >> 2) & 0x07));
                        }

                        // OAM2 reads + sprite tile fetch
                        if (sprPhase == 0) { if (sprFetchEnabled) oamCopyBuffer = secondaryOAM[evalOam2Addr]; evalOam2Addr++; }
                        else if (sprPhase == 1) { if (sprFetchEnabled) { oamCopyBuffer = secondaryOAM[evalOam2Addr]; if (mapperNeedsA12) NotifyMapperA12(ppuAddressBus); } evalOam2Addr++; }
                        else if (sprPhase == 2) { if (sprFetchEnabled) { oamCopyBuffer = secondaryOAM[evalOam2Addr]; sprFetchAttr[slot] = oamCopyBuffer; } evalOam2Addr++; }
                        else if (sprPhase == 3) { if (sprFetchEnabled) { oamCopyBuffer = secondaryOAM[evalOam2Addr]; sprXPos[slot] = oamCopyBuffer; } }
                        else if (sprPhase == 4) { if (sprFetchEnabled) { oamCopyBuffer = secondaryOAM[evalOam2Addr]; ppuAddressBus = ComputeSpritePatternAddr(slot); ppuChrFetchA12 = (ppuAddressBus >> 12) & 1; } }
                        else if (sprPhase == 5)
                        {
                            if (sprFetchEnabled)
                            {
                                oamCopyBuffer = secondaryOAM[evalOam2Addr];
                                if (mapperNeedsA12) NotifyMapperA12(ppuAddressBus);
                                int addr = ppuAddressBus; byte tile = chrBankPtrs[(addr >> 10) & 7][addr & 0x3FF];
                                sprShiftL[slot] = (sprFetchAttr[slot] & 0x40) != 0 ? FlipByte(tile) : tile;
                                if (slot >= sprSlotCount) sprShiftL[slot] = 0;
                            }
                            // TriCNES in-range check (line 2926)
                            { int sprY = secondaryOAM[slot * 4]; int h = Spritesize8x16 ? 16 : 8; int diff = (scanline & 0xFF) - sprY; if (!(diff >= 0 && diff < h)) sprShiftL[slot] = 0; }
                        }
                        else if (sprPhase == 6) { if (sprFetchEnabled) { oamCopyBuffer = secondaryOAM[evalOam2Addr]; ppuAddressBus = ComputeSpritePatternAddr(slot) + 8; ppuChrFetchA12 = (ppuAddressBus >> 12) & 1; } }
                        else // sprPhase == 7
                        {
                            if (sprFetchEnabled)
                            {
                                oamCopyBuffer = secondaryOAM[evalOam2Addr];
                                if (mapperNeedsA12 && !mapperA12IsMmc3) NotifyMapperA12(ppuAddressBus);
                                int addr = ppuAddressBus; byte tile = chrBankPtrs[(addr >> 10) & 7][addr & 0x3FF];
                                sprShiftH[slot] = (sprFetchAttr[slot] & 0x40) != 0 ? FlipByte(tile) : tile;
                                if (slot >= sprSlotCount) sprShiftH[slot] = 0;
                            }
                            // TriCNES in-range check (line 2961)
                            { int sprY = secondaryOAM[slot * 4]; int h = Spritesize8x16 ? 16 : 8; int diff = (scanline & 0xFF) - sprY; if (!(diff >= 0 && diff < h)) sprShiftH[slot] = 0; }
                            evalOam2Addr++;
                        }

                        if (mmc5Ref != null) { if (sprPhase == 1) mmc5Ref.NotifyVramRead(0x2000); else if (sprPhase == 3) mmc5Ref.NotifyVramRead(0x23C0); else if (sprPhase == 5) mmc5Ref.NotifyVramRead(SpPatternTableAddr); else if (sprPhase == 7) mmc5Ref.NotifyVramRead(SpPatternTableAddr | 8); }
                    }

                    // Dot 321 equivalent
                    if (evalDot == 322 && scanline < 240 && (ShowBackGround_Instant || ShowSprites_Instant))
                        oamCopyBuffer = secondaryOAM[0];

                    // Dot 257: copy sprite slot count
                    if (scanline >= 0 && scanline < 240 && evalDot == 257) { sprSlotCount = evalSpriteCount; sprZeroInSlots = evalSprite0Visible; }
                    else if (scanline == preRenderLine && evalDot == 257) { sprSlotCount = evalSpriteCount; sprZeroInSlots = evalSprite0Visible; }
                    if (scanline == preRenderLine && evalDot == 257 && ppuRenderingEnabled) PrecomputePreRenderSprites();

                    // Dot 339: X counter init
                    if (evalDot == 339)
                    {
                        for (int i = 0; i < 8; i++)
                            sprXCounter[i] = (ShowSprites || ShowBackGround) ? sprXPos[i] : 0;
                    }

                    // Garbage NT fetch (dots 336-340)
                    if (evalDot == 336 || evalDot == 338) { ppuAddressBus = 0x2000 | (vram_addr & 0x0FFF); PpuBusRead(ppuAddressBus); }
                    else if (evalDot == 337 || evalDot == 339) { NTVal = ppu_ram[CIRAMAddr(ppuAddressBus)]; if (mapperNeedsA12) NotifyMapperA12(ppuAddressBus); }
                    else if (evalDot == 340) { ppuAddressBus = BgPatternTableAddr | (NTVal << 4) | ((vram_addr >> 12) & 7); ppuChrFetchA12 = (ppuAddressBus >> 12) & 1; }

                    if (mmc5Ref != null && (evalDot == 337 || evalDot == 339)) mmc5Ref.NotifyVramRead(0x2000 | (vram_addr & 0x0FFF));

                    // Per-cycle sprite overflow
                    if (scanline >= 0 && scanline < 240)
                    {
                        if (evalDot == 1)
                        {
                            int scanOff = scanline << 8;
                            int* bgp = Buffer_BG_array + scanOff;
                            for (int* bge = bgp + 256; bgp < bge; bgp++) *bgp = 0;
                            { uint bgColor = NesColors[ppu_ram[0x3f00] & 0x3f]; uint* sp = ScreenBuf1x + scanOff; for (uint* se = sp + 256; sp < se; sp++) *sp = bgColor; if (AnalogEnabled) { byte bgIdx = (byte)(ppu_ram[0x3f00] & 0x3f); for (int i = 0; i < 256; i++) ntscScanBuf[i] = bgIdx; } }
                            PrecomputeOverflow();
                        }
                        if (spriteOverflowCycle >= 0 && evalDot == spriteOverflowCycle) isSpriteOverflow = true;
                    }
                }
            }

            // ── Eval delay: phase-3 (TriCNES lines 1667-1673) ──
            if ((mcCpuClock & 3) == 3)
            {
                ShowBG_EvalDelay = ShowBackGround;
                ShowSpr_EvalDelay = ShowSprites;
            }

            // ── ppuAddressBus = vram_addr when rendering disabled (TriCNES line 1674) ──
            if (!ShowBackGround && !ShowSprites)
            {
                ppuAddressBus = vram_addr;
                ppuChrFetchA12 = (vram_addr >> 12) & 1;
            }

            // ── $2001 delayed mask update (TriCNES lines 1681-1694) ──
            if (ppu2001UpdateDelay > 0 && --ppu2001UpdateDelay == 0)
            {
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

            // ── CommitShiftRegistersAndBitPlanes — UNGATED (TriCNES line 1727) ──
            if (commitNTFetch) { commitNTFetch = false; NTVal = renderTemp; }
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
            if (commitPatHighFetch) { commitPatHighFetch = false; pendingTileHigh = renderTemp; CXinc(); }

            // ── Tile fetch + CalculatePixel + UpdateSpriteShift (TriCNES lines 1728-1751) ──
            if (scanline < 240 || scanline == preRenderLine)
            {
                // BG tile fetch (TriCNES line 1730-1735)
                if ((cx >= 0 && cx < 257) || (cx > 320 && cx <= 336))
                {
                    if (ShowBackGround || ShowSprites) // Tier 2
                    {
                        // PPU_Render_ShiftRegistersAndBitPlanes — 8-phase tile fetch
                        int phase = cx & 7;
                        if (phase == 0) { ioaddr = 0x2000 | (vram_addr & 0x0FFF); }
                        else if (phase == 1) { ppuAddressBus = ioaddr; if (mapperA12IsMmc3) NotifyMapperA12(ioaddr); renderTemp = PpuBusRead(ioaddr); commitNTFetch = true; if (extAttrEnabled) extAttrNTOffset = (ushort)(ioaddr & 0x3FF); if (mmc5Ref != null) mmc5Ref.NotifyVramRead(ioaddr); }
                        else if (phase == 2) { ioaddr = 0x23C0 | (vram_addr & 0x0C00) | ((vram_addr >> 4) & 0x38) | ((vram_addr >> 2) & 0x07); }
                        else if (phase == 3) { ppuAddressBus = ioaddr; renderTemp = PpuBusRead(ioaddr); commitATFetch = true; if (mmc5Ref != null) mmc5Ref.NotifyVramRead(ioaddr); }
                        else if (phase == 4) { ioaddr = (extAttrEnabled && extAttrChrSize > 0) ? (extAttrChrBank << 12) | (NTVal << 4) | ((vram_addr >> 12) & 7) : BgPatternTableAddr | (NTVal << 4) | ((vram_addr >> 12) & 7); }
                        else if (phase == 5) { ppuAddressBus = ioaddr; ppuChrFetchA12 = (ioaddr >> 12) & 1; if (mapperNeedsA12) NotifyMapperA12(ioaddr); renderTemp = PpuBusRead(ioaddr); commitPatLowFetch = true; if (mmc5Ref != null) mmc5Ref.NotifyVramRead(ioaddr); }
                        else if (phase == 6) { ioaddr = (extAttrEnabled && extAttrChrSize > 0) ? (extAttrChrBank << 12) | (NTVal << 4) | ((vram_addr >> 12) & 7) | 8 : BgPatternTableAddr | (NTVal << 4) | ((vram_addr >> 12) & 7) | 8; }
                        else { ppuAddressBus = ioaddr; ppuChrFetchA12 = (ioaddr >> 12) & 1; if (mapperNeedsA12 && !mapperA12IsMmc3) NotifyMapperA12(ioaddr); renderTemp = PpuBusRead(ioaddr); commitPatHighFetch = true; if (mmc5Ref != null) mmc5Ref.NotifyVramRead(ioaddr); if (scanline < 240 && cx < 257 && ppuRenderingEnabled) RenderBGTile(cx); }

                        // MMC5 CHR A/B switch at first tile of each group
                        if ((cx == 1 || cx == 321) && chrABAutoSwitch) { byte*[] src = Spritesize8x16 ? (chrBGUseASet ? chrBankPtrsA : chrBankPtrsB) : chrBankPtrsA; for (int i = 0; i < 8; i++) chrBankPtrs[i] = src[i]; }
                    }
                }

                // CalculatePixel + UpdateSpriteShift (TriCNES lines 1745-1751)
                if (cx > 0 && cx <= 257)
                {
                    if (scanline < 240) // visible scanlines only for CalculatePixel
                    {
                        // ── CalculatePixel (TriCNES line 3073) ──
                        byte backdropIdx = (byte)(ppu_ram[0x3f00] & 0x3f);
                        uint compositeColor = NesColors[backdropIdx];
                        byte compositePalIdx = backdropIdx;
                        int bgColor = 0, bgPalette = 0;

                        if (cx <= 256 && ShowBackGround && (cx > 8 || ShowBgLeft8))
                        {
                            int bit = 15 - FineX;
                            bgColor = (((renderHigh >> bit) & 1) << 1) | ((renderLow >> bit) & 1);
                            bgPalette = (bit >= 8) ? bg_attr_p3 : bg_attr_p2;
                            if (bgColor == 0) bgPalette = 0;
                        }

                        int sprColor = 0, sprPalette = 0, sprSlot = -1;
                        bool sprPriority = false;
                        if (cx <= 256 && ShowSprites && (cx > 8 || ShowSprLeft8))
                        {
                            for (int s = 0; s < 8; s++)
                            {
                                if (sprXCounter[s] == 0 || skippedPreRenderDot341)
                                {
                                    int px = ((sprShiftH[s] >> 7) << 1) | (sprShiftL[s] >> 7);
                                    if (px != 0 && sprColor == 0)
                                    { sprColor = px; sprPalette = (sprFetchAttr[s] & 3) | 4; sprPriority = ((sprFetchAttr[s] >> 5) & 1) == 0; sprSlot = s; }
                                }
                            }

                            if (canDetectSprite0Hit && sprSlot == 0 && sprZeroInSlots && ShowBackGround && ShowSprites && bgColor != 0 && sprColor != 0)
                            { if ((ShowSprLeft8 || cx > 8) && cx < 256) { pendingSprite0Hit = true; canDetectSprite0Hit = false; } }
                            // DEBUG removed

                            // DEBUG removed

                            if (sprColor != 0 && ShowSprites) { if (bgColor == 0 || sprPriority) { bgColor = sprColor; bgPalette = sprPalette; } }
                        }

                        if ((ShowBackGround || ShowSprites) && cx <= 256)
                        { int pa = (bgPalette << 2) | bgColor; if (bgColor == 0) pa = 0; compositeColor = NesColors[ppu_ram[0x3f00 + pa] & 0x3f]; compositePalIdx = (byte)(ppu_ram[0x3f00 + pa] & 0x3f); }
                        else if (cx <= 256) { if ((vram_addr & 0x3F1F) >= 0x3F00) { int pa = vram_addr & 0x1F; if ((pa & 3) == 0) pa &= 0x0F; compositeColor = NesColors[ppu_ram[0x3f00 + pa] & 0x3f]; compositePalIdx = (byte)(ppu_ram[0x3f00 + pa] & 0x3f); } }

                        dotColor = compositeColor;
                        dotPalIdx = compositePalIdx;
                    }

                    // ── UpdateSpriteShiftRegisters (TriCNES line 3718, inside PPU_Dot>0 && <=257 block) ──
                    if (cx <= 256)
                    {
                        for (int s = 0; s < 8; s++)
                        {
                            if (sprXCounter[s] > 0 && !skippedPreRenderDot341) sprXCounter[s]--;
                            else { if (ShowSprites || ShowBackGround) { sprShiftL[s] <<= 1; sprShiftH[s] <<= 1; } }
                        }
                    }
                }
            }

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
        // _EmulateHalfPPU — half PPU step (called at mcPpuClock == 2)
        // TriCNES: Emulator.cs line 1809
        // ════════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_half_step_new()
        {
            int hsDot = ppu_cycles_x; // post-increment PPU_Dot

            // ── BG shift register shift (TriCNES line 1818: PPU_UpdateShiftRegisters) ──
            if ((scanline < 240 || scanline == preRenderLine)
                && ((hsDot > 0 && hsDot <= 257) || (hsDot > 320 && hsDot <= 336)))
            {
                if (ShowBackGround || ShowSprites) // Tier 2
                {
                    renderLow  <<= 1;
                    renderHigh = (ushort)((renderHigh << 1) | 1);
                    renderAttrLow  = (ushort)((renderAttrLow << 1) | (attrLatch & 1));
                    renderAttrHigh = (ushort)((renderAttrHigh << 1) | ((attrLatch >> 1) & 1));
                }
            }

            // ── CommitShiftRegistersAndBitPlanes_HalfDot — UNGATED (TriCNES line 1822) ──
            if (commitLoadShiftReg)
            {
                commitLoadShiftReg = false;
                renderLow  = (ushort)((renderLow  & 0xFF00) | pendingTileLow);
                renderHigh = (ushort)((renderHigh & 0xFF00) | pendingTileHigh);
                attrLatch = pendingAttrLatch; // TriCNES: PPU_AttributeLatchRegister = PPU_Attribute
            }

            // ── Half-step tile fetch (TriCNES line 1829: PPU_Render_ShiftRegistersAndBitPlanes_HalfDot) ──
            if ((scanline < 240 || scanline == preRenderLine)
                && ((hsDot >= 0 && hsDot < 257) || (hsDot >= 320 && hsDot < 336)))
            {
                if (ShowBackGround || ShowSprites) // Tier 2
                {
                    if ((hsDot & 7) == 7)
                        commitLoadShiftReg = true;
                }
            }

            // ── VBL latch half-step (TriCNES lines 1833-1840) ──
            ppuVSET = false;
            if (pendingVblank) { pendingVblank = false; ppuVSET = true; }
            ppuVSET_Latch2 = !ppuVSET_Latch1;

            // ── OAM buffer update (TriCNES lines 1842-1860) ──
            if ((ShowBackGround || ShowSprites) && scanline >= 0 && scanline < 240)
            {
                if (hsDot == 0 || hsDot > 320) ppuOamBuffer = secondaryOAM[0];
                else if (hsDot > 0 && hsDot <= 64) ppuOamBuffer = 0xFF;
                else if (hsDot <= 256) ppuOamBuffer = oamCopyBuffer;
                else ppuOamBuffer = oamCopyBuffer; // 257-320
            }

            // ── Sprite0 hit pipeline (TriCNES lines 1862-1872) ──
            isSprite0hit_Delayed = isSprite0hit;
            if (pendingSprite0Hit2) { pendingSprite0Hit2 = false; isSprite0hit = true; }
            if (pendingSprite0Hit) { pendingSprite0Hit = false; pendingSprite0Hit2 = true; }

            // ── $2007 state machine half-step tick ──
            Ppu2007SmTick();

            // ── P4-2: Palette corruption placeholder ──
            if (paletteCorruptFromDisable || paletteCorruptFromVAddr)
            { paletteCorruptFromDisable = false; paletteCorruptFromVAddr = false; }
        }
    }
}
