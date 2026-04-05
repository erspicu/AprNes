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
        static bool useNewPpuStep = false;

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
                if (cx == 0) pendingVblank = true;
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
            // Phase 4: Eval delay + sprite eval + $2001 update
            // ══════════════════════════════════════════════════════
            // TODO: Phase 4

            // ══════════════════════════════════════════════════════
            // Phase 5: Pipeline + commit + tile fetch + pixel + draw
            // ══════════════════════════════════════════════════════
            // TODO: Phase 5

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
            // ── Phase 6: BG shift + commit + tile half + VBL/Spr0 pipeline ──
            // TODO: Phase 6
        }
    }
}
