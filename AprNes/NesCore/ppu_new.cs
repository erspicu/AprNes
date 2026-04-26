// TriCNES PPU complete port — replaces ppu_step/ppu_step_rendering/ppu_rendering_tick/ppu_half_step
// Source: ref/TriCNES-main/Emulator.cs _EmulatePPU (line 1256) + _EmulateHalfPPU (line 1809)
//
// Execution order matches TriCNES exactly:
//   _EmulatePPU: deferred→scroll→dot++→wrap→events→VSET→mapper→A12→oddskip
//                →evaldelay→spriteeval→evaldelay→addrbus→$2001→$2001emph
//                →pipeline→commit→tilefetch→calculatepixel→spriteshift→draw
//   _EmulateHalfPPU: BGshift→commitHalf→tileHalf→VSET_half→spr0_half→OAMbuf

using System;
using System.Numerics;
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
        // _EmulatePPU — thin dispatcher over 341-slot function-pointer tables.
        // Three tables (visible / preRender / vblank) each hold a handler
        // specialised to its scanline state. See ppu_dispatch.cs for handlers
        // and InitPpuDispatchTable() setup.
        // TriCNES: Emulator.cs line 1256
        // ════════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_step_new()
        {
            int cx = ppu_cycles_x;
            int sl = scanline;
#if NET10_0_OR_GREATER
            delegate* unmanaged<void>* table;
#else
            delegate*<void>* table;
#endif

            // Hot-first ordering: Visible (77-92%) → VBlank (8-23%) → PreRender (0.3-0.4%).
            if      (sl <  240)            table = ppuTickVisibleTable;
            else if (sl <  preRenderLine)  table = ppuTickVBlankTable;   // sl ∈ [240, preRenderLine-1]
            else                           table = ppuTickPreRenderTable; // sl == preRenderLine
            table[cx]();
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

        // ── ppu_step_new cold helpers (Pattern A: condition at call site, NoInlining) ──

        // NTSC odd-frame dot skip — pre-render dot 341 becomes dot 0 of next frame's scanline 0.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void PpuPhase_DoOddFrameSkip(ref int cx)
        {
            if (mmc5Ref != null)
                mmc5Ref.NotifyVramRead(0x2000 | (vram_addr & 0x0FFF));
            scanline = 0;
            ppu_cycles_x = cx = 0;
            skippedPreRenderDot341 = true;
        }

        // Delayed OAM corruption trigger (TriCNES 1695-1711) — fires on $2001 disable during rendering.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void PpuPhase_HandleDelayedOamCorruption(bool isActiveScanline)
        {
            if (--oamCorruptDelay == 0 &&
                oamCorruptWasRendering && isActiveScanline &&
                !oamCorruptPending && (oamCorrupt2001Value & 0x18) == 0)
            {
                oamCorruptDisabledFlag = true;
            }
        }

        // $2001 delayed mask update (TriCNES 1681-1694).
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void PpuPhase_Apply2001Mask()
        {
            if (--ppu2001UpdateDelay == 0)
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
        }

        // $2001 emphasis delay (TriCNES 1712-1722).
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void PpuPhase_Apply2001Emphasis()
        {
            if (--ppu2001EmphasisDelay == 0)
            {
                byte v = ppu2001EmphasisPending;
                ppuEmphasis = (byte)((v >> 5) & 0x7);
                if (Region != RegionType.NTSC)
                    ppuEmphasis = (byte)((ppuEmphasis & 0x4) | ((ppuEmphasis & 1) << 1) | ((ppuEmphasis >> 1) & 1));
            }
        }

        // Frame render at SL240 cx1 — once per frame (1/89K dots).
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void PpuPhase_FrameRender()
        {
            // Phase B: when analog render thread is running, wait for the previous
            // frame's CRT/blit to complete BEFORE we touch linearBuffer. Crt_Render
            // (now on render thread) reads linearBuffer; we must not overwrite it
            // via Ntsc_FlushPendingRows until that read is done.
            if (AnalogEnabled && renderThreadRunning)
            {
                renderDone.Wait();
                renderDone.Reset();
            }

            // Parallel-demod all 240 captured scanlines (writes linearBuffer).
            if (AnalogEnabled) Ntsc_FlushPendingRows();

            // Phase B: snapshot frame_count INTO CrtScreen BEFORE signaling render thread.
            // This guarantees the render thread's Crt_Render reads the correct per-frame
            // state (interlace jitter direction, frame age in phosphor blending, etc.)
            // for THIS frame, regardless of when emu's frame_count++ happens next.
            if (AnalogEnabled) Crt_SetFrameCount(frame_count);

            // Phase C-3: digital path — emu pre-converts palette indices → RGB into
            // digitalFrameRgb. Render thread reads digitalFrameRgb race-free; emu's
            // next frame writes ntsc_rowPalettes (separate buffer) without touching
            // the converted RGB buffer.
            if (!AnalogEnabled) Convert_PalIdxFrameToRGB(digitalFrameRgb);

            RenderScreen();
            frame_count++;
            // Phase B: Ntsc_SetFrameCount stays here (ntsc_frameCount is consumed inside
            // FlushPendingRows on emu thread). Crt_SetFrameCount moved up — no longer
            // duplicated here.
            if (AnalogEnabled) Ntsc_SetFrameCount(frame_count);
        }

        // ════════════════════════════════════════════════════════════════
        // Phase 4 entry for pre-render and slot-340 wrap.
        // Visible handlers now call their specialised helpers directly; this method keeps
        // only the pre-render path plus the evalDot==0 dummy-fetch case after wrap.
        // ════════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void PpuPhase4_SpriteEvalAndInit()
        {
            PpuPhase4_HandleOamCorruptionIfNeeded();

            int evalDot = ppu_cycles_x; // post-increment PPU_Dot

            if (evalDot == 0)
            {
                PpuPhase4_DummyNTFetch(0);
                return;
            }

            if (scanline == preRenderLine)
            {
                PpuPhase4_PreRenderDot(evalDot);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void PpuPhase4_VisiblePixelZone(int evalDot)
        {
            bool renderDelayed = ShowBG_EvalDelay || ShowSpr_EvalDelay;
            if (renderDelayed && evalDot <= 64)
            {
                if (evalDot == 1) { evalOam2Addr = 0; evalOam2Full = false; evalTick = 0; evalOamOverflowed = false; }
                if ((evalDot & 1) != 0) oamCopyBuffer = 0xFF;
                else if (evalDot > 0)
                {
                    secondaryOAM[evalOam2Addr] = oamCopyBuffer;
                    evalOam2Addr++;
                    evalOam2Addr &= 0x1F;
                }
            }

            if (evalDot == 65)
            {
                evalOam2Addr = 0;
                nineObjectsOnLine = false;
            }

            if (ShowBackGround_Instant || ShowSprites_Instant)
            {
                if (evalDot == 65)
                {
                    sprite0_eval_addr = spr_ram_add;
                    SpriteEvalInit();
                    SpriteEvalTick();
                }
                else if (evalDot > 65)
                {
                    SpriteEvalTick();
                    if (evalDot == 256) SpriteEvalEnd();
                }
            }

            if (evalDot == 1) PpuPhase4_VisibleScanlineDot1Init();
            if (spriteOverflowCycle >= 0 && evalDot == spriteOverflowCycle) isSpriteOverflow = true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void PpuPhase4_PreRenderDot(int evalDot)
        {
            bool renderDelayed = ShowBG_EvalDelay || ShowSpr_EvalDelay;
            if (renderDelayed && evalDot <= 64)
            {
                if (evalDot == 1) { evalOam2Addr = 0; evalOam2Full = false; evalTick = 0; evalOamOverflowed = false; }
                if ((evalDot & 1) != 0) oamCopyBuffer = secondaryOAM[evalOam2Addr];
                else if (evalDot > 0)
                {
                    evalOam2Addr++;
                    evalOam2Addr &= 0x1F;
                }
            }

            if (evalDot == 65)
            {
                evalOam2Addr = 0;
                nineObjectsOnLine = false;
            }

            if (evalDot >= 65 && evalDot <= 256)
            {
                if (ShowBackGround_Instant || ShowSprites_Instant)
                {
                    if (evalDot == 65)
                    {
                        sprite0_eval_addr = spr_ram_add;
                        SpriteEvalInit();
                        SpriteEvalTick();
                    }
                    else
                    {
                        SpriteEvalTick();
                        if (evalDot == 256) SpriteEvalEnd();
                    }
                }
                else if (evalDot == 65 && ppuRenderingEnabled)
                {
                    sprite0_eval_addr = spr_ram_add;
                }
            }

            if (evalDot >= 257 && evalDot <= 320) PpuPhase4_SpriteFetch(evalDot);

            if (evalDot == 257)
            {
                sprSlotCount = evalSpriteCount;
                sprZeroInSlots = evalSprite0Visible;
                if (ppuRenderingEnabled) PrecomputePreRenderSprites();
            }

            if (evalDot == 339) PpuPhase4_Dot339();
            if (evalDot >= 337) PpuPhase4_DummyNTFetch(evalDot);
        }

        // ── PpuPhase4 cold helpers (NoInlining: keep them out of the hot path's IL budget) ──
        // Called either from the pre-render Phase4 entry or directly from visible dispatch handlers.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PpuPhase4_HandleOamCorruptionIfNeeded()
        {
            if (oamCorruptPending || oamCorruptDisabledFlag)
                PpuPhase4_HandleOamCorruption();
        }

        // OAM corruption handling — only invoked when oamCorruptPending or oamCorruptDisabledFlag is set.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void PpuPhase4_HandleOamCorruption()
        {
            // TriCNES line 2491-2501: OAM corruption on first rendered dot after re-enable
            if ((ShowBackGround_Instant || ShowSprites_Instant) && oamCorruptPending)
            {
                oamCorruptPending = false;
                if (!oamCorruptSuppressed) ProcessOamCorruption();
                oamCorruptSuppressed = false;
            }
            // TriCNES line 2528-2534: capture corruption index when delayed flag fires
            if (oamCorruptDisabledFlag)
            {
                oamCorruptDisabledFlag = false;
                oamCorruptPending = true;
                oamCorruptIndex = evalOam2Addr;
            }
        }

        // Visible scanline dot 1 init: fill backdrop + precompute overflow.
        // Called once per visible scanline (240/89K dots = 0.27%).
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void PpuPhase4_VisibleScanlineDot1Init()
        {
            int scanOff = scanline << 8;
            // Phase A5: only ntsc_rowPalettes prefill needed; ScreenBuf1x retired.
            byte bgIdx = (byte)(ppu_ram[0x3f00] & 0x3f);
            System.Runtime.CompilerServices.Unsafe.InitBlockUnaligned(ntsc_rowPalettes + scanOff, bgIdx, 256);
            PrecomputeOverflow();
        }

        // Dots 257-320 sprite fetch — tier 4 gate, includes dummy BG fetch (evalDot in [257,320]).
        // TriCNES sprite eval cases 0-7 (line 2855-2993): even phase = ALE, odd phase = READ.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void PpuPhase4_SpriteFetch(int evalDot)
        {
            if (ShowBG_EvalDelay || ShowSpr_EvalDelay) spr_ram_add = 0;
            if (evalDot == 257) evalOam2Addr = 0;
            if (evalDot == 262) spriteSizeLatchedForFetch = Spritesize8x16;

            int sprPhase = (evalDot - 257) & 7;
            int slot = (evalDot - 257) >> 3;
            bool sprFetchEnabled = ShowBG_EvalDelay || ShowSpr_EvalDelay;

            // TriCNES line 2833-2836: OctalLatch guard before sprite switch
            if (ppu2007_PPU_READ) ppuOctalLatch = (byte)ppuAddressBus;

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
                    if (sprPhase == 3) sprXCounter[slot] = oamCopyBuffer;

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
                        // Branchless flip: FlipTable[val | ((attr & 0x40) << 2)] selects identity/reversed half
                        byte tile = FlipTable[val | ((sprFetchAttr[slot] & 0x40) << 2)];
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

            if (mmc5Ref != null)
            {
                if      (sprPhase == 1) mmc5Ref.NotifyVramRead(0x2000);
                else if (sprPhase == 3) mmc5Ref.NotifyVramRead(0x23C0);
                else if (sprPhase == 5) mmc5Ref.NotifyVramRead(SpPatternTableAddr);
                else if (sprPhase == 7) mmc5Ref.NotifyVramRead(SpPatternTableAddr | 8);
            }
        }

        // Dot 339: sprite active flag + conditional counter init.
        // Rendering ON  → counters retain fetch-set values
        // Rendering OFF → zero all counters (halted mode, allows stale shift data behavior)
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void PpuPhase4_Dot339()
        {
            if (!(ShowSprites || ShowBackGround))
            {
                // sprXCounter is 8 bytes = one ulong write
                *(ulong*)sprXCounter = 0;
            }
            // SWAR: check 8+8 bytes in one 64-bit OR
            spriteAnyActive = ((*(ulong*)sprShiftH) | (*(ulong*)sprShiftL)) != 0;

            // #5: re-pick PixelZone handler if sprite activity changed for next scanline.
            // No-op fast-path when state unchanged (~99% of scanlines).
            UpdatePpuVisibleDispatchForNextScanline();
        }

        // Garbage/Dummy NT fetch (TriCNES: PPU_Render_ShiftRegistersAndBitPlanes_DummyNT)
        // dots 337-340 + dot 0: set bus to NT addr, do dummy fetch, update OctalLatch.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void PpuPhase4_DummyNTFetch(int evalDot)
        {
            if (ShowBG_EvalDelay || ShowSpr_EvalDelay) // TriCNES: _Delayed gate
            {
                // OctalLatch guard before (TriCNES line 3697-3700)
                if (ppu2007_PPU_READ) ppuOctalLatch = (byte)ppuAddressBus;

                if (evalDot == 0)
                {
                    // Dot 0: idle/setup. Use NT address (A12=0) to maintain M2Filter
                    // for correct MMC3 scanline counter behavior with BG at $1000.
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

            if (mmc5Ref != null && (evalDot == 337 || evalDot == 339))
                mmc5Ref.NotifyVramRead(0x2000 | (vram_addr & 0x0FFF));
        }

        // ════════════════════════════════════════════════════════════════
        // $2007 SR Latch Pipeline — split into 3 phase-specific helpers.
        //   Step1: dot start (signal setup, even latch advance)
        //   BusRead: PD_RB-gated buffer refill (used by Step2 callers + inside Step3)
        //   Step3: half-step (TStep + bus read + odd latch + write)
        // Phase 2 callers just invoke Ppu2007_BusRead directly — no wrapper needed.
        // ════════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PPU_DATA_Pipeline_Step1()
        {
            bool BLNK = (!ShowBackGround && !ShowSprites) || (scanline >= 240 && scanline < preRenderLine);
            ppu2007_BLNK_Latch = BLNK;
            bool H0_DASH = (ppu_cycles_x & 1) == 0;
            ppu2007_PaletteRAMEnable = ((ppuAddressBus & 0x3F00) == 0x3F00) && BLNK;

            // Derive flags from OLD latch (new bit 2 = ~old bit 1, new bit 4 = ~old bit 3):
            //   (new & 0x14) == 0x10 ⇔ (old & 0x0A) == 0x02  (PD_RB)
            //   (new & 0x14) == 0x04 ⇔ (old & 0x0A) == 0x08  (ALE)
            ppu2007_PD_RB    = (readLatch  & 0x0A) == 0x02;
            ppu2007_ReadALE  = (readLatch  & 0x0A) == 0x08;
            ppu2007_WriteALE = (writeLatch & 0x0A) == 0x08;

            readLatch  = (byte)((readLatch  & 0x0A) | (ppu2007_Read_SR  ? 1 : 0) | ((~readLatch  << 1) & 0x14));
            writeLatch = (byte)((writeLatch & 0x0A) | (ppu2007_Write_SR ? 1 : 0) | ((~writeLatch << 1) & 0x14));

            ppu2007_PPU_READ = ppu2007_PD_RB || (!BLNK && H0_DASH);
            ppu2007_TStep_Latch = ppu2007_DB_PAR;
            ppu2007_PPU_ALE = ppu2007_ReadALE || ppu2007_WriteALE || (!BLNK && !H0_DASH);

            if ((ppu2007_ReadALE || ppu2007_WriteALE) && !ppu2007_PPU_READ)
            {
                ppuAddressBus = vram_addr;
                ppuOctalLatch = (byte)vram_addr;
            }
        }

        // PD_RB-gated bus read + buffer refill. Shared between Phase 2 (after tile fetch,
        // uses ALE from Step1) and Phase 3 (after TStep updates ALE via ReadALE/WriteALE).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Ppu2007_BusRead()
        {
            if (!ppu2007_PD_RB) return;
            int addr = (ppuAddressBus & 0x3F00) | ppuOctalLatch;
            byte data = PpuBusRead(addr >= 0x3F00 ? addr & 0x2FFF : addr);
            ppu_2007_buffer = data;
            ppuAddressBus = (ppuAddressBus & 0xFF00) | data;
            if (ppu2007_PPU_ALE) ppuOctalLatch = data;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PPU_DATA_Pipeline_Step3()
        {
            // Idle fast-path (#2): when both latches are at the settled idle pattern (0x0A)
            // and no CPU $2007 activity is pending, every operation in this method is a
            // no-op on observable state. Skip the entire body.
            //
            // Settled invariants (derived from Step1/Step3 bit-update equations):
            //   readLatch  == 0x0A → PD_RB / ReadALE both false
            //   writeLatch == 0x0A → DB_PAR / WriteALE both false (so TStep_Latch false)
            //   readLatch / writeLatch updates produce 0x0A again (fixed point)
            //   PPU_ALE was already false (written by Step1 when no rendering ALE source)
            // CPU-side conditions: Read_SR / Write_SR / PD_RB / DB_PAR all false
            // means no transition from idle is in flight; safe to skip.
            if (readLatch == 0x0A && writeLatch == 0x0A
                && !ppu2007_Read_SR && !ppu2007_Write_SR
                && !ppu2007_PD_RB && !ppu2007_DB_PAR)
            {
                return;
            }

            ppu2007_TStep = ppu2007_TStep_Latch || ppu2007_PD_RB;
            if (ppu2007_TStep)
            {
                vram_addr = (ushort)(vram_addr + VramaddrIncrement);
                if (!ppu2007_BLNK_Latch) Yinc();
            }
            ppu2007_PPU_ALE = ppu2007_ReadALE || ppu2007_WriteALE;

            Ppu2007_BusRead();

            // ── Odd latch + write ──
            // new bit 3 = ~(old bit 2), so "new & 0x08 == 0" ⇔ "old & 0x04 != 0"
            if ((readLatch & 0x04) != 0) ppu2007_Read_SR = false;
            readLatch = (byte)((readLatch & 0x15) | ((~readLatch << 1) & 0x0A));

            if ((writeLatch & 0x04) != 0) ppu2007_Write_SR = false;
            // (new writeLatch & 0x0A) == 0x02 ⇔ (old & 0x05) == 0x04
            ppu2007_DB_PAR = (writeLatch & 0x05) == 0x04;
            writeLatch = (byte)((writeLatch & 0x15) | ((~writeLatch << 1) & 0x0A));

            ppu2007_PPU_WRITE = !ppu2007_PaletteRAMEnable && ppu2007_DB_PAR;
            if (ppu2007_DB_PAR)
            {
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
            // Range magic: (uint)(hsDot-1) < 257 covers 1..257; (uint)(hsDot-321) < 16 covers 321..336
            if (isRendering && isActiveScanline
                && ((uint)(hsDot - 1) < 257u || (uint)(hsDot - 321) < 16u))
            {
                renderLow  <<= 1;
                renderHigh = (renderHigh << 1) | 1;
                renderAttrLow  = (renderAttrLow << 1) | (attrLatch & 1);
                renderAttrHigh = (renderAttrHigh << 1) | ((attrLatch >> 1) & 1);
            }

            // ── CommitShiftRegistersAndBitPlanes — TriCNES line 1691 (inside _EmulateHalfPPU) ──
            // Hardware fetch pipeline is strictly sequential within a half-step:
            // at most one commitXFetch flag is true, so else-if short-circuits the rest.
            if (commitNTFetch)
            {
                commitNTFetch = false;
                NTVal = renderTemp;
                // TriCNES line 3661-3669: update PAR_CHR tile number from bus low byte
                ppuPAR_CHR &= 0b1000000001111; // keep bit12 + fine Y bits 0-2
                // Range magic: (uint)(hsDot-256) > 64 ≡ hsDot<256 || hsDot>320
                if ((uint)(hsDot - 256) > 64u)
                    ppuPAR_CHR |= (ushort)((byte)(ppuAddressBus) << 4); // BG: tile from bus
                else
                    ppuPAR_CHR |= (ushort)(secondaryOAM[(evalOam2Addr & 0x1C) + 1] << 4); // Sprite: tile from OAM2
            }
            else if (commitATFetch)
            {
                commitATFetch = false;
                if (extAttrEnabled && extAttrNTOffset < 960) {
                    byte exVal = extAttrRAM[extAttrNTOffset];
                    extAttrChrBank = (exVal & 0x3F) | (extAttrChrUpperBits << 6);
                    ATVal = (byte)((exVal >> 6) & 3);
                } else {
                    ATVal = (byte)((renderTemp >> (((vram_addr >> 4) & 0x04) | (vram_addr & 0x02))) & 0x03);
                }
                pendingAttrLatch = ATVal;
            }
            else if (commitPatLowFetch) { commitPatLowFetch = false; pendingTileLow = renderTemp; }
            else if (commitPatHighFetch)
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
            // Range magic: (uint)scanline < 240 rejects preRenderLine/negative in one compare
            if (isRendering && (uint)scanline < 240u)
            {
                // Range magic: (uint)(hsDot-1) >= 320 covers hsDot==0 || hsDot>320
                if ((uint)(hsDot - 1) >= 320u) ppuOamBuffer = secondaryOAM[0];
                else if (hsDot <= 64)          ppuOamBuffer = 0xFF;
                else                           ppuOamBuffer = oamCopyBuffer;
            }

            // ── Sprite0 hit pipeline ──
            isSprite0hit_Delayed = isSprite0hit;
            if (pendingSprite0Hit2) { pendingSprite0Hit2 = false; isSprite0hit = true; }
            if (pendingSprite0Hit)  { pendingSprite0Hit  = false; pendingSprite0Hit2 = true; }

            // Phase 3: PPU_DATA_StateMachine_Half — v inc + second FetchPPU + write (TriCNES line 1734)
            PPU_DATA_Pipeline_Step3();
        }
    }
}
