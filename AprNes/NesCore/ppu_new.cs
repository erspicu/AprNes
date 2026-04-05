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

        // ════════════════════════════════════════════════════════════════
        // _EmulatePPU — full PPU step (called at mcPpuClock == 0)
        // ════════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_step_new()
        {
            // ── Phase 2: Deferred register updates ──
            // $2006 delayed t→v copy (TriCNES lines 1264-1284)
            // $2005 delayed scroll (TriCNES lines 1286-1304)
            // $2000 delayed control (TriCNES lines 1306-1320)
            // $2007 state machine (TriCNES lines 1322-1496)
            // TODO: Phase 2

            // ── Phase 2: Scroll increments (PRE-increment, TriCNES lines 1498-1516) ──
            // Yinc at PPU_Dot==256, CopyHoriV at PPU_Dot==257, ResetYScroll at 280-304
            // TODO: Phase 2

            // ── PPU_Dot++ + scanline wrap (TriCNES lines 1518-1530) ──
            ppu_cycles_x++;
            if (ppu_cycles_x > 340)
            {
                ppu_cycles_x = 0;
                scanline++;
                if (scanline > 261)
                {
                    scanline = 0;
                    if (ShowBackGround_Instant || ShowSprites_Instant)
                        ProcessOamCorruption();
                }
            }

            // ── Phase 3: Events (TriCNES lines 1532-1606) ──
            // TODO: Phase 3

            // ── Phase 3: VSET latch (TriCNES lines 1608-1620) ──
            // TODO: Phase 3

            // ── Phase 3: Mapper + A12 (TriCNES lines 1627-1628) ──
            MapperObj.PpuClock();
            ppuA12Prev = (ppuAddressBus & 0x1000) != 0;

            // ── Phase 3: Odd frame skip (TriCNES lines 1629-1643) ──
            // TODO: Phase 3

            // ── Phase 4: Eval delay + sprite eval + $2001 update ──
            // TODO: Phase 4

            // ── Phase 5: Pipeline + commit + tile fetch + pixel + draw ──
            // TODO: Phase 5

            // ── End of dot: update ppuRenderingEnabled ──
            ppuRenderingEnabled = ShowBackGround_Instant || ShowSprites_Instant;
        }

        // ════════════════════════════════════════════════════════════════
        // _EmulateHalfPPU — half PPU step (called at mcPpuClock == 2)
        // ════════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_half_step_new()
        {
            // ── Phase 6: BG shift + commit + tile half + VBL/Spr0 pipeline ──
            // TODO: Phase 6
        }
    }
}
