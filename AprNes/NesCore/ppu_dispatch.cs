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
#else
            if (ppuTickVisibleTable == null)
            {
                ppuTickVisibleTable   = (delegate*<void>*)AllocUnmanaged(sz);
                ppuTickPreRenderTable = (delegate*<void>*)AllocUnmanaged(sz);
                ppuTickVBlankTable    = (delegate*<void>*)AllocUnmanaged(sz);
            }
#endif
            // Static cx-zone slots (independent of AnalogEnabled).
            //   slots 256,257,340 → VisibleTail  (Yinc / CopyHoriV / wrap-to-next-scanline)
            //   slots 258-319     → SpriteFetch  (no tile fetch, no pixel, no draw except post-259)
            //   slots 320-335     → Prefetch     (tile fetch only)
            //   slots 336-339     → Dummy        (no render work, only universal per-dot)
            ppuTickVisibleTable[256] = &Ppu_Tick_VisibleTail;
            ppuTickVisibleTable[257] = &Ppu_Tick_VisibleTail;
            for (int i = 258; i < 320; i++) ppuTickVisibleTable[i] = &Ppu_Tick_Visible_SpriteFetch;
            for (int i = 320; i < 336; i++) ppuTickVisibleTable[i] = &Ppu_Tick_Visible_Prefetch;
            for (int i = 336; i < 340; i++) ppuTickVisibleTable[i] = &Ppu_Tick_Visible_Dummy;
            ppuTickVisibleTable[340] = &Ppu_Tick_VisibleTail;

            for (int i = 0; i < 341; i++)
            {
                ppuTickPreRenderTable[i] = &Ppu_Tick_PreRenderLine;
                ppuTickVBlankTable[i]    = &Ppu_Tick_VBlankLine;
            }

            // Slots 0-255 (PixelZone hot path) depend on AnalogEnabled — set via helper
            // so it can be re-applied when the user toggles AnalogEnabled at runtime.
            ConfigurePpuVisibleDispatch();
        }

        // Tracks last-applied dispatch state so PpuPhase4_Dot339 can short-circuit
        // when the next scanline's sprite activity matches the current handler.
        static bool _dispatchSpritesActive = true;

        // Re-populates ppuTickVisibleTable[0..255] for the current
        // (AnalogEnabled, spriteAnyActive) combination. Safe to call any time
        // the emu thread is paused (ApplyRenderSettings) or from the per-scanline
        // dispatch update hook in PpuPhase4_Dot339.
        public static void ConfigurePpuVisibleDispatch()
        {
            if (ppuTickVisibleTable == null) return; // Init not yet called
            bool analog = AnalogEnabled;
            bool sprActive = spriteAnyActive;
            _dispatchSpritesActive = sprActive;
#if NET10_0_OR_GREATER
            delegate* unmanaged<void> handler;
            if (analog)
                handler = sprActive
                    ? (delegate* unmanaged<void>)&Ppu_Tick_Visible_PixelZone_Analog_Spr
                    : (delegate* unmanaged<void>)&Ppu_Tick_Visible_PixelZone_Analog_NoSpr;
            else
                handler = sprActive
                    ? (delegate* unmanaged<void>)&Ppu_Tick_Visible_PixelZone_Digital_Spr
                    : (delegate* unmanaged<void>)&Ppu_Tick_Visible_PixelZone_Digital_NoSpr;
#else
            delegate*<void> handler;
            if (analog)
                handler = sprActive
                    ? (delegate*<void>)&Ppu_Tick_Visible_PixelZone_Analog_Spr
                    : (delegate*<void>)&Ppu_Tick_Visible_PixelZone_Analog_NoSpr;
            else
                handler = sprActive
                    ? (delegate*<void>)&Ppu_Tick_Visible_PixelZone_Digital_Spr
                    : (delegate*<void>)&Ppu_Tick_Visible_PixelZone_Digital_NoSpr;
#endif
            for (int i = 0; i < 256; i++) ppuTickVisibleTable[i] = handler;
        }

        // Hot-path hook from PpuPhase4_Dot339: only re-populates table when
        // sprite-active state for the upcoming scanline differs from current.
        // ~99% of consecutive scanlines have the same state, so this short-
        // circuits the 256-pointer write almost every call.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdatePpuVisibleDispatchForNextScanline()
        {
            if (spriteAnyActive == _dispatchSpritesActive) return;
            ConfigurePpuVisibleDispatch();
        }

        // ════════════════════════════════════════════════════════════════
        // PixelZone hot handler — 4-way specialised by (Analog, Sprites):
        //   Digital_Spr  / Digital_NoSpr  — output mode × sprite activity
        //   Analog_Spr   / Analog_NoSpr
        //
        // Output mode (Digital vs Analog) is set at config time via AnalogEnabled.
        // Sprite mode (Spr vs NoSpr) is updated per-scanline in PpuPhase4_Dot339:
        //   when spriteAnyActive == false at scanline boundary, route to NoSpr
        //   variant for the next scanline (skips the entire sprite mux block).
        //
        // Source uses a generic struct constraint pattern (IPixelZoneMode) so
        // the 4 variants share one body; JIT specialises per TMode and folds
        // const branches at compile time. UnmanagedCallersOnly entry wrappers
        // are non-generic (required for function-pointer dispatch).
        // ════════════════════════════════════════════════════════════════

        private interface IPixelZoneMode
        {
            bool IsAnalog { get; }
            bool HasSprites { get; }
        }
        private struct PixelMode_Digital_Spr   : IPixelZoneMode { public bool IsAnalog { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => false; } public bool HasSprites { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => true;  } }
        private struct PixelMode_Digital_NoSpr : IPixelZoneMode { public bool IsAnalog { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => false; } public bool HasSprites { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => false; } }
        private struct PixelMode_Analog_Spr    : IPixelZoneMode { public bool IsAnalog { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => true;  } public bool HasSprites { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => true;  } }
        private struct PixelMode_Analog_NoSpr  : IPixelZoneMode { public bool IsAnalog { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => true;  } public bool HasSprites { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => false; } }

#if NET10_0_OR_GREATER
        [UnmanagedCallersOnly]
#endif
        static void Ppu_Tick_Visible_PixelZone_Digital_Spr()   => PixelZoneImpl<PixelMode_Digital_Spr>();
#if NET10_0_OR_GREATER
        [UnmanagedCallersOnly]
#endif
        static void Ppu_Tick_Visible_PixelZone_Digital_NoSpr() => PixelZoneImpl<PixelMode_Digital_NoSpr>();
#if NET10_0_OR_GREATER
        [UnmanagedCallersOnly]
#endif
        static void Ppu_Tick_Visible_PixelZone_Analog_Spr()    => PixelZoneImpl<PixelMode_Analog_Spr>();
#if NET10_0_OR_GREATER
        [UnmanagedCallersOnly]
#endif
        static void Ppu_Tick_Visible_PixelZone_Analog_NoSpr()  => PixelZoneImpl<PixelMode_Analog_NoSpr>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PixelZoneImpl<TMode>() where TMode : struct, IPixelZoneMode
        {
            bool isAnalog = default(TMode).IsAnalog;     // compile-time const
            bool hasSprites = default(TMode).HasSprites; // compile-time const

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

            PPU_DATA_Pipeline_Step1();

            if (oamCorruptDelay != 0) PpuPhase_HandleDelayedOamCorruption(true);

            PpuPhase4_HandleOamCorruptionIfNeeded();
            PpuPhase4_VisiblePixelZone(cx);

            if ((mcCpuClock & 3) == 3)
            {
                ShowBG_EvalDelay = ShowBackGround;
                ShowSpr_EvalDelay = ShowSprites;
            }

            if (!ShowBackGround && !ShowSprites) ppuAddressBus = vram_addr;

            if (ppu2001UpdateDelay > 0) PpuPhase_Apply2001Mask();
            if (ppu2001EmphasisDelay > 0) PpuPhase_Apply2001Emphasis();

            // ── Pipeline shift (palette index only, both modes) ──
            // Phase A5: dotColor pipeline retired with ScreenBuf1x. Both digital and analog
            // now consume only the palette-index pipeline; render-side does NesColors[] lookup.
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

            // ── Pixel composition (mode-specialised) ──
            {
                bool showBG = ShowBackGround;
                bool showSpr = ShowSprites;

                int bgColor = 0, bgPalette = 0;

                if (showBG && (cx > 8 || ShowBgLeft8))
                {
                    int bit = 15 - FineX;
                    bgColor = (((renderHigh >> bit) & 1) << 1) | ((renderLow >> bit) & 1);
                    { int ab = 7 - FineX;
                      bgPalette = (((renderAttrHigh >> ab) & 1) << 1) | ((renderAttrLow >> ab) & 1); }
                    if (bgColor == 0) bgPalette = 0;
                }

                // Sprite mux: completely elided in NoSpr variants (hasSprites=false → JIT prunes block).
                if (hasSprites && showSpr && (cx > 8 || ShowSprLeft8) && spriteAnyActive)
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

                // Phase A5: both modes compute only compositePalIdx; render-side
                // converts palette indices to RGB via Convert_PalIdxFrameToRGB.
                byte compositePalIdx = (byte)(ppu_ram[0x3f00] & 0x3f);
                if (showBG || showSpr)
                { int pa = (bgPalette << 2) | bgColor; if (bgColor == 0) pa = 0; compositePalIdx = (byte)(ppu_ram[0x3f00 + pa] & 0x3f); }
                else { if ((vram_addr & 0x3F1F) >= 0x3F00) { int pa = vram_addr & 0x1F; if ((pa & 3) == 0) pa &= 0x0F; compositePalIdx = (byte)(ppu_ram[0x3f00 + pa] & 0x3f); } }
                dotPalIdx = compositePalIdx;
            }

            // ── Sprite shift (cx <= 256 always true) ──
            {
                bool renderEnabled = ShowSprites || ShowBackGround;
                ulong v = *(ulong*)sprXCounter;
                if (skippedPreRenderDot341 || v == 0)
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
                        // Pre-combine (& 0xFE) with mask_0 → saves 1 AND per shift register.
                        ulong shift_mask = mask_0 & 0xFEFEFEFEFEFEFEFEUL;
                        ulong keep_mask = ~mask_0;
                        ulong sl = *(ulong*)sprShiftL;
                        ulong sh = *(ulong*)sprShiftH;
                        *(ulong*)sprShiftL = ((sl << 1) & shift_mask) | (sl & keep_mask);
                        *(ulong*)sprShiftH = ((sh << 1) & shift_mask) | (sh & keep_mask);
                    }
                }
            }

            Ppu2007_BusRead();

            // ── Draw (Phase A5: palette indices only) ──
            if (cx >= 4)
            {
                int pos = (scanline << 8) + (cx - 4);
                ntsc_rowPalettes[pos] = prevPrevPrevDotPalIdx;
            }

            ppuRenderingEnabled = ShowBackGround_Instant || ShowSprites_Instant;
        }

        // ════════════════════════════════════════════════════════════════
        // Ppu_Tick_Visible_SpriteFetch — visible, entry cx ∈ [258, 319]
        // (post-inc cx ∈ [259, 320], HBlank sprite fetch region).
        // Specialisations:
        //   - No scroll ops (entry never 256/257)
        //   - No wrap (entry < 340)
        //   - No events (scanline < nmiTriggerLine)
        //   - No odd-skip, no skippedPreRenderDot341 reset (cx range misses both)
        //   - Tile fetch gate → always false → whole tile fetch block removed
        //   - Pixel / sprite shift gates → always false → removed
        //   - Draw fires only at post-inc cx==259 (entry 258); keep runtime check
        //   - NTSC capture fires at post-inc cx==260 (entry 259); keep runtime check
        //   - Frame render → never → removed
        // ════════════════════════════════════════════════════════════════
#if NET10_0_OR_GREATER
        [UnmanagedCallersOnly]
#endif
        static void Ppu_Tick_Visible_SpriteFetch()
        {
            int cx = ppu_cycles_x;

            if (ppu2006UpdateDelay != 0 || ppu2005UpdateDelay != 0)
                PpuPhase2_DeferredUpdates(cx);

            if (--open_bus_decay_timer == 0) { open_bus_decay_timer = 77777; openbus = 0; }

            // (No scroll ops, no wrap, no events.)

            ppu_cycles_x = ++cx;

            PpuVisibleAuxBeforePhase4();
            PpuPhase4_SpriteFetch(cx);
            PpuDotAuxAfterPhase4();

            // (Tile fetch / pixel / sprite shift blocks removed — post-inc cx never in ranges.)

            Ppu2007_BusRead();

            // Draw: only post-inc cx==259 fires (entry 258).
            // Phase A5: palette indices only — render-side does palette→RGB.
            if (cx == 259)
            {
                int pos = (scanline << 8) + 255;
                ntsc_rowPalettes[pos] = prevPrevPrevDotPalIdx;
            }
            // Phase A1: analog scanline data is already in ntsc_rowPalettes;
            // Ntsc_CaptureScanline now only sets emphasis + advances per-row phase counters.
            if (AnalogEnabled && cx == 260)
                Ntsc_CaptureScanline(scanline, ppuEmphasis);

            ppuRenderingEnabled = ShowBackGround_Instant || ShowSprites_Instant;
        }

        // ════════════════════════════════════════════════════════════════
        // Ppu_Tick_Visible_Prefetch — visible, entry cx ∈ [320, 335]
        // (post-inc cx ∈ [321, 336], next-scanline background prefetch).
        // Specialisations:
        //   - No scroll / wrap / events / odd-skip
        //   - Tile fetch gate → always true → block runs unconditionally
        //   - Pixel / sprite shift / draw / NTSC capture / frame render → never → removed
        // ════════════════════════════════════════════════════════════════
#if NET10_0_OR_GREATER
        [UnmanagedCallersOnly]
#endif
        static void Ppu_Tick_Visible_Prefetch()
        {
            int cx = ppu_cycles_x;

            if (ppu2006UpdateDelay != 0 || ppu2005UpdateDelay != 0)
                PpuPhase2_DeferredUpdates(cx);

            if (--open_bus_decay_timer == 0) { open_bus_decay_timer = 77777; openbus = 0; }

            ppu_cycles_x = ++cx;

            PpuVisibleAuxBeforePhase4();
            if (cx == 322 && (ShowBackGround_Instant || ShowSprites_Instant))
                oamCopyBuffer = secondaryOAM[0];
            PpuDotAuxAfterPhase4();

            PpuBgTileFetchRange(cx);

            Ppu2007_BusRead();

            // (No pixel, no sprite shift, no draw, no NTSC capture, no frame render.)

            ppuRenderingEnabled = ShowBackGround_Instant || ShowSprites_Instant;
        }

        // ════════════════════════════════════════════════════════════════
        // Ppu_Tick_Visible_Dummy — visible, entry cx ∈ [336, 339]
        // (post-inc cx ∈ [337, 340], end-of-line dummy fetch region).
        // Specialisations:
        //   - No scroll / wrap / events / odd-skip
        //   - Tile fetch / pixel / sprite shift / draw / NTSC / frame render → all never → removed
        //   - Leanest possible visible handler — only universal per-dot state updates.
        // ════════════════════════════════════════════════════════════════
#if NET10_0_OR_GREATER
        [UnmanagedCallersOnly]
#endif
        static void Ppu_Tick_Visible_Dummy()
        {
            int cx = ppu_cycles_x;

            if (ppu2006UpdateDelay != 0 || ppu2005UpdateDelay != 0)
                PpuPhase2_DeferredUpdates(cx);

            if (--open_bus_decay_timer == 0) { open_bus_decay_timer = 77777; openbus = 0; }

            ppu_cycles_x = ++cx;

            PpuVisibleAuxBeforePhase4();
            if (cx == 339) PpuPhase4_Dot339();
            PpuPhase4_DummyNTFetch(cx);
            PpuDotAuxAfterPhase4();

            Ppu2007_BusRead();

            ppuRenderingEnabled = ShowBackGround_Instant || ShowSprites_Instant;
        }

        // ════════════════════════════════════════════════════════════════
        // Ppu_Tick_VisibleTail — visible tail slots only, entry cx ∈ {256, 257, 340}.
        // Post-inc cx ∈ {257, 258, 0}; this handler only needs Yinc, CopyHoriV, wrap,
        // and the final two delayed draw writes.
        // ════════════════════════════════════════════════════════════════
#if NET10_0_OR_GREATER
        [UnmanagedCallersOnly]
#endif
        static void Ppu_Tick_VisibleTail()
        {
            int cx = ppu_cycles_x;

            if (ppu2006UpdateDelay != 0 || ppu2005UpdateDelay != 0)
                PpuPhase2_DeferredUpdates(cx);

            if (--open_bus_decay_timer == 0) { open_bus_decay_timer = 77777; openbus = 0; }

            // Visible tail slots keep only the two scroll side effects.
            if (ShowBackGround || ShowSprites)
            {
                if (cx == 256)      Yinc();
                else if (cx == 257) CopyHoriV();
            }

            PpuAdvanceAndMaybeWrap(ref cx);

            PpuVisibleAuxBeforePhase4();
            if (cx == 0)
            {
                PpuPhase4_DummyNTFetch(0);
            }
            else
            {
                PpuPhase4_SpriteFetch(cx);
                if (cx == 257)
                {
                    sprSlotCount = evalSpriteCount;
                    sprZeroInSlots = evalSprite0Visible;
                }
            }
            PpuDotAuxAfterPhase4();

            Ppu2007_BusRead();

            // Only post-inc cx 257/258 produce the final delayed pixels.
            // Phase A5: palette indices only.
            if (scanline < 240 && (cx == 257 || cx == 258))
            {
                int pos = (scanline << 8) + (cx - 4);
                ntsc_rowPalettes[pos] = prevPrevPrevDotPalIdx;
            }

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

            PpuAdvanceAndMaybeWrap(ref cx);

            // Phase 3 events: always active on preRender (scanline >= nmiTriggerLine).
            // Post-wrap scanline may be 0 (visible) → runtime check kept.
            if (scanline >= nmiTriggerLine)
                PpuPhase3_Events(cx);

            PpuDotAuxBeforeStep1Core();
            // Odd-frame dot skip — fires only on preRenderLine at cx==340 pre-wrap.
            // Note: after wrap cx is 0 or unchanged; check against pre-wrap state via scanline guard.
            if (Region == RegionType.NTSC && oddSwap && (ShowBackGround || ShowSprites)
                && scanline == preRenderLine && cx == 340)
                PpuPhase_DoOddFrameSkip(ref cx);

            PpuDotAuxStep1(true);

            // Pre-render Phase4 plus wrap-to-dot0 handling.
            PpuPhase4_SpriteEvalAndInit();

            PpuDotAuxAfterPhase4();

            // Pre-render still needs BG tile fetch + sprite shift, but no pixel composition.
            Ppu_PreRender_RenderBlock(cx);

            Ppu2007_BusRead();

            ppuRenderingEnabled = ShowBackGround_Instant || ShowSprites_Instant;
        }

        // Shared by visible non-pixel handlers (SpriteFetch / Prefetch / Dummy / Tail).
        // PixelZone keeps its body fully inlined to avoid perturbing the hottest path.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PpuVisibleAuxBeforePhase4()
        {
            PpuDotAuxBeforeStep1Core();
            PpuDotAuxStep1(true);
            PpuPhase4_HandleOamCorruptionIfNeeded();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PpuAdvanceAndMaybeWrap(ref int cx)
        {
            ppu_cycles_x = ++cx;
            if (cx > 340)
            {
                ppu_cycles_x = cx = 0;
                scanline++;
                scanline &= (scanline - (preRenderLine + 1)) >> 31;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PpuDotAuxBeforeStep1Core()
        {
            ppuVSET_Latch1 = !ppuVSET;
            if (ppuVSET && !ppuVSET_Latch2) isVblank = true;
            if (ppu2002ReadPending) { ppu2002ReadPending = false; isVblank = false; }

            isSpriteOverflow_Delayed = isSpriteOverflow;

            MapperObj.PpuClock();
            ppuA12Prev = (ppuAddressBus & 0x1000) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PpuDotAuxStep1(bool activeScanlineCorruption)
        {
            if ((mcCpuClock & 3) != 3)
            {
                ShowBG_EvalDelay = ShowBackGround;
                ShowSpr_EvalDelay = ShowSprites;
            }

            PPU_DATA_Pipeline_Step1();

            if (oamCorruptDelay != 0) PpuPhase_HandleDelayedOamCorruption(activeScanlineCorruption);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PpuDotAuxAfterPhase4()
        {
            if ((mcCpuClock & 3) == 3)
            {
                ShowBG_EvalDelay = ShowBackGround;
                ShowSpr_EvalDelay = ShowSprites;
            }

            if (!ShowBackGround && !ShowSprites) ppuAddressBus = vram_addr;

            if (ppu2001UpdateDelay > 0) PpuPhase_Apply2001Mask();
            if (ppu2001EmphasisDelay > 0) PpuPhase_Apply2001Emphasis();

            // Phase A5: dotColor pipeline retired with ScreenBuf1x.
            prevPrevPrevDotPalIdx = prevPrevDotPalIdx; prevPrevDotPalIdx = prevDotPalIdx; prevDotPalIdx = dotPalIdx;
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

            PpuAdvanceAndMaybeWrap(ref cx);

            // Phase 3 events: scanline >= nmiTriggerLine is true throughout VBlank.
            if (scanline >= nmiTriggerLine)
                PpuPhase3_Events(cx);

            PpuDotAuxBeforeStep1Core();
            // Odd-skip: not here (preRender only).
            PpuDotAuxStep1(false);

            // No sprite eval (active scanline == false).

            PpuDotAuxAfterPhase4();

            // No active-scanline render block.

            Ppu2007_BusRead();

            // No DrawToScreen (visible gate false).

            // Frame render: fires at SL 240 cx 1 (first VBlank dot).
            if (scanline == 240 && cx == 1) PpuPhase_FrameRender();

            ppuRenderingEnabled = ShowBackGround_Instant || ShowSprites_Instant;
        }

        // Shared non-pixel BG fetch path used by visible prefetch and pre-render.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PpuBgTileFetchRange(int cx)
        {
            if ((cx < 1 || cx > 336) || (cx > 256 && cx < 321))
                return;

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

        // Pre-render only: BG fetch plus sprite shifter advance, no visible pixel composition.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Ppu_PreRender_RenderBlock(int cx)
        {
            PpuBgTileFetchRange(cx);

            if (cx < 1 || cx > 256)
                return;

            bool renderEnabled = ShowBackGround || ShowSprites;
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
