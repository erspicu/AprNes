using System;
using System.Runtime.CompilerServices;

namespace AprNes
{

    //把system與UI有關的顯示處理切割出去到NES Core外層

    unsafe static public partial class NesCore
    {
        static public volatile int frame_count = 0;
        static public int ppu_cycles_x = 0, scanline = -1; // 241;

        // Palette ref: http://www.thealmightyguru.com/Games/Hacking/Wiki/index.php?title=NES_Palette
        // Fills NesColors (uint*) and default palette into ppu_ram[0x3F00..0x3F1F] (byte*)
        static void initPalette()
        {
            NesColors[ 0]=0xFF7C7C7C; NesColors[ 1]=0xFF0000FC; NesColors[ 2]=0xFF0000BC; NesColors[ 3]=0xFF4428BC;
            NesColors[ 4]=0xFF940084; NesColors[ 5]=0xFFA80020; NesColors[ 6]=0xFFA81000; NesColors[ 7]=0xFF881400;
            NesColors[ 8]=0xFF503000; NesColors[ 9]=0xFF007800; NesColors[10]=0xFF006800; NesColors[11]=0xFF005800;
            NesColors[12]=0xFF004058; NesColors[13]=0xFF000000; NesColors[14]=0xFF000000; NesColors[15]=0xFF000000;
            NesColors[16]=0xFFBCBCBC; NesColors[17]=0xFF0078F8; NesColors[18]=0xFF0058F8; NesColors[19]=0xFF6844FC;
            NesColors[20]=0xFFD800CC; NesColors[21]=0xFFE40058; NesColors[22]=0xFFF83800; NesColors[23]=0xFFE45C10;
            NesColors[24]=0xFFAC7C00; NesColors[25]=0xFF00B800; NesColors[26]=0xFF00A800; NesColors[27]=0xFF00A844;
            NesColors[28]=0xFF008888; NesColors[29]=0xFF000000; NesColors[30]=0xFF000000; NesColors[31]=0xFF000000;
            NesColors[32]=0xFFF8F8F8; NesColors[33]=0xFF3CBCFC; NesColors[34]=0xFF6888FC; NesColors[35]=0xFF9878F8;
            NesColors[36]=0xFFF878F8; NesColors[37]=0xFFF85898; NesColors[38]=0xFFF87858; NesColors[39]=0xFFFCA044;
            NesColors[40]=0xFFF8B800; NesColors[41]=0xFFB8F818; NesColors[42]=0xFF58D854; NesColors[43]=0xFF58F898;
            NesColors[44]=0xFF00E8D8; NesColors[45]=0xFF787878; NesColors[46]=0xFF000000; NesColors[47]=0xFF000000;
            NesColors[48]=0xFFFCFCFC; NesColors[49]=0xFFA4E4FC; NesColors[50]=0xFFB8B8F8; NesColors[51]=0xFFD8B8F8;
            NesColors[52]=0xFFF8B8F8; NesColors[53]=0xFFF8A4C0; NesColors[54]=0xFFF0D0B0; NesColors[55]=0xFFFCE0A8;
            NesColors[56]=0xFFF8D878; NesColors[57]=0xFFD8F878; NesColors[58]=0xFFB8F8B8; NesColors[59]=0xFFB8F8D8;
            NesColors[60]=0xFF00FCFC; NesColors[61]=0xFFF8D8F8; NesColors[62]=0xFF000000; NesColors[63]=0xFF000000;

            // table from blargg_ppu power_up_palette.asm
            ppu_ram[0x3F00]=0x09; ppu_ram[0x3F01]=0x01; ppu_ram[0x3F02]=0x00; ppu_ram[0x3F03]=0x01;
            ppu_ram[0x3F04]=0x00; ppu_ram[0x3F05]=0x02; ppu_ram[0x3F06]=0x02; ppu_ram[0x3F07]=0x0D;
            ppu_ram[0x3F08]=0x08; ppu_ram[0x3F09]=0x10; ppu_ram[0x3F0A]=0x08; ppu_ram[0x3F0B]=0x24;
            ppu_ram[0x3F0C]=0x00; ppu_ram[0x3F0D]=0x00; ppu_ram[0x3F0E]=0x04; ppu_ram[0x3F0F]=0x2C;
            ppu_ram[0x3F10]=0x09; ppu_ram[0x3F11]=0x01; ppu_ram[0x3F12]=0x34; ppu_ram[0x3F13]=0x03;
            ppu_ram[0x3F14]=0x00; ppu_ram[0x3F15]=0x04; ppu_ram[0x3F16]=0x00; ppu_ram[0x3F17]=0x14;
            ppu_ram[0x3F18]=0x08; ppu_ram[0x3F19]=0x3A; ppu_ram[0x3F1A]=0x00; ppu_ram[0x3F1B]=0x02;
            ppu_ram[0x3F1C]=0x00; ppu_ram[0x3F1D]=0x20; ppu_ram[0x3F1E]=0x2C; ppu_ram[0x3F1F]=0x08;
        }

        //ppu ctrl 0x2000
        static int BaseNameTableAddr = 0, VramaddrIncrement = 1, SpPatternTableAddr = 0, BgPatternTableAddr = 0;
        static bool Spritesize8x16 = false, NMIable = false;

        //ppu mask 0x2001
        public static bool ShowBackGround = false, ShowSprites = false;
        static bool ShowBgLeft8 = true, ShowSprLeft8 = true; // bit1/bit2: show in leftmost 8 pixels
        static byte ppuEmphasis = 0; // $2001[7:5] emphasis bits (for NTSC signal amplitude)

        // NTSC 類比模式：每條掃描線的原始調色盤索引緩衝區（256 bytes，0x00-0x3F）
        // 由 RenderBGTile 和 RenderSpritesLine 在 AnalogEnabled=true 時填入
        static byte[] ntscScanBuf = new byte[256];

        //ppu status 0x2002.
        static bool isSpriteOverflow = false, isSprite0hit = false, isVblank = false;

        static int vram_addr_internal = 0, vram_addr = 0, scrol_y = 0, FineX = 0;
        static bool vram_latch = false;
        static byte ppu_2007_buffer = 0, ppu_2007_temp = 0;
        static int ppu2007ReadCooldown = 0; // 6 PPU dots cooldown after $2007 read (Mesen2: _ignoreVramRead)
        static byte* spr_ram;
        static public byte* ppu_ram;

        // OAM corruption: when rendering is disabled mid-scanline, the PPU's internal
        // secondary OAM addressing glitches and marks rows for corruption.
        // When rendering re-enables (or at next frame start), first 8 bytes of OAM
        // are copied over each marked row.
        static byte* corruptOamRow; // 32 bytes, 0=clean 1=corrupt
        static bool prevRenderingEnabled = false;
        static public uint* ScreenBuf1x;
        static uint* NesColors; //, targetSize;
        static int* Buffer_BG_array;
        static uint* palCacheR; // 4 pre-computed palette colors for renderAttr
        static uint* palCacheN; // 4 pre-computed palette colors for nextAttr
        static byte spr_ram_add = 0;

        static bool oddSwap = false;
        static bool ppuRenderingEnabled = false; // Delayed rendering enable (Mesen2 model: updated at end of PPU dot)
        static bool nmi_output_prev = false;  // NMI edge detection: previous NMI output level
        static long nmi_delay_cycle = -1;     // CPU cycle that detected NMI edge (-1 = inactive)
                                              // Promotes to nmi_pending when cpuCycleCount > nmi_delay_cycle
        //https://wiki.nesdev.com/w/index.php/PPU_scrolling

        #region cycle-accurate PPU

        // Coarse X increment
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CXinc()
        {
            if ((vram_addr & 0x001F) == 31)
            {
                vram_addr &= ~0x001F;
                vram_addr ^= 0x0400;
            }
            else
                vram_addr += 1;
        }

        // Y increment
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Yinc()
        {
            if ((vram_addr & 0x7000) != 0x7000)
                vram_addr += 0x1000;
            else
            {
                vram_addr &= ~0x7000;
                int y = (vram_addr & 0x03E0) >> 5;
                if (y == 29)
                {
                    y = 0;
                    vram_addr ^= 0x0800;
                }
                else if (y == 31)
                    y = 0;
                else
                    y += 1;
                vram_addr = (vram_addr & ~0x03E0) | (y << 5);
            }
        }

        // $2007 access increment: during rendering → CXinc + Yinc; otherwise → +1/+32
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Increment2007()
        {
            if ((ShowBackGround || ShowSprites) && (scanline < 240 || scanline == 261))
            {
                CXinc();
                Yinc();
            }
            else
            {
                vram_addr = (ushort)((vram_addr + VramaddrIncrement) & 0x7FFF);
            }
            if (mapperNeedsA12) NotifyMapperA12(vram_addr);
        }

        // hori(v) = hori(t)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CopyHoriV()
        {
            vram_addr = (vram_addr & ~0x041F) | (vram_addr_internal & 0x041F);
        }

        // ---- Tile fetch state ----
        static byte NTVal = 0, ATVal = 0, lowTile = 0, highTile = 0;
        static int ioaddr = 0;

        // ---- BG shift registers (16-bit, two tiles: high=current, low=next) ----
        static ushort lowshift = 0, highshift = 0;

        // ---- Per-dot shifted BG registers for sprite 0 hit (serial in: 0=low, 1=high) ----
        static ushort lowshift_s0 = 0, highshift_s0 = 0;

        // ---- Attribute 3-stage pipeline ----
        // Phase-3 shifts ATVal into p1; phase-7 render reads p3 (2 groups later).
        // This correctly delays attribute by 2 fetch groups with no index drift.
        static byte bg_attr_p1 = 0, bg_attr_p2 = 0, bg_attr_p3 = 0;

        // Render 8 BG pixels at screen positions [ppu_cycles_x-7 .. ppu_cycles_x]
        // using shift registers BEFORE reload (high byte = current tile data).
        static void RenderBGTile(int cx)
        {
            byte renderAttr = bg_attr_p3;
            byte nextAttr   = bg_attr_p2;

            // Refresh palette caches for this tile (static alloc, no heap activity)
            int baseAddrR = 0x3f00 | (renderAttr << 2);
            int baseAddrN = 0x3f00 | (nextAttr   << 2);
            uint bgColor = NesColors[ppu_ram[0x3f00] & 0x3f];
            palCacheR[0] = palCacheN[0] = bgColor;
            palCacheR[1] = NesColors[ppu_ram[baseAddrR + 1] & 0x3f];
            palCacheR[2] = NesColors[ppu_ram[baseAddrR + 2] & 0x3f];
            palCacheR[3] = NesColors[ppu_ram[baseAddrR + 3] & 0x3f];
            palCacheN[1] = NesColors[ppu_ram[baseAddrN + 1] & 0x3f];
            palCacheN[2] = NesColors[ppu_ram[baseAddrN + 2] & 0x3f];
            palCacheN[3] = NesColors[ppu_ram[baseAddrN + 3] & 0x3f];

            int baseX = cx - 7;
            int scanOff = scanline << 8;
            int fineX = FineX;
            bool bgLeft8 = ShowBgLeft8;
            ushort ls = lowshift;
            ushort hs = highshift;
            for (int loc = 0; loc < 8; loc++)
            {
                int screenX = baseX + loc;

                int bit = 15 - loc - fineX;
                byte attrUse = (bit >= 8) ? renderAttr : nextAttr;
                int bgPixel = ((ls >> bit) & 1) | (((hs >> bit) & 1) << 1);

                bool masked = !bgLeft8 && screenX < 8;
                int slot = scanOff + screenX;
                Buffer_BG_array[slot] = masked ? 0 : bgPixel;
                uint* pal = (attrUse == renderAttr) ? palCacheR : palCacheN;
                ScreenBuf1x[slot] = masked ? bgColor : pal[bgPixel];

                // NTSC: 寫入原始調色盤索引（不經 NesColors RGB 轉換）
                if (AnalogEnabled && (uint)screenX < 256)
                {
                    int padrBase = (attrUse == renderAttr) ? baseAddrR : baseAddrN;
                    ntscScanBuf[screenX] = (masked || bgPixel == 0)
                        ? (byte)(ppu_ram[0x3f00] & 0x3f)
                        : (byte)(ppu_ram[padrBase + bgPixel] & 0x3f);
                }
            }
        }

        // Per-8-cycle tile fetch: runs each PPU cycle on visible/pre-render scanlines when rendering enabled.
        // BG tiles fetched at cycles 0-255 (visible) and 320-335 (next-scanline prefetch).
        // A12 notifications: BG at phases 0 (NT addr, A12=0) and 4 (CHR low addr, A12=BG table bit12),
        // sprites at phases 0 (garbage NT, A12=0) and 3 (sprite CHR, A12=sprite table bit12).
        static void ppu_rendering_tick(int cx)
        {
            if (cx < 256 || (cx >= 320 && cx < 336))
            {
                int phase = cx & 7;
                if (phase == 0) {
                    ioaddr = 0x2000 | (vram_addr & 0x0FFF);
                    if (mapperA12IsMmc3) NotifyMapperA12(ioaddr);  // NT addr, A12=0
                } else if (phase == 1) {
                    if (ntChrOverrideEnabled)
                        NTVal = ntBankPtrs[(ioaddr >> 10) & 3][ioaddr & 0x3FF];
                    else
                        NTVal = ppu_ram[ioaddr];
                } else if (phase == 2) {
                    ioaddr = 0x23C0 | (vram_addr & 0x0C00) | ((vram_addr >> 4) & 0x38) | ((vram_addr >> 2) & 0x07);
                } else if (phase == 3) {
                    if (ntChrOverrideEnabled)
                        ATVal = (byte)((ntBankPtrs[(ioaddr >> 10) & 3][ioaddr & 0x3FF] >> (((vram_addr >> 4) & 0x04) | (vram_addr & 0x02))) & 0x03);
                    else
                        ATVal = (byte)((ppu_ram[ioaddr] >> (((vram_addr >> 4) & 0x04) | (vram_addr & 0x02))) & 0x03);
                    bg_attr_p3 = bg_attr_p2; bg_attr_p2 = bg_attr_p1; bg_attr_p1 = ATVal;
                } else if (phase == 4) {
                    ioaddr = BgPatternTableAddr | (NTVal << 4) | ((vram_addr >> 12) & 7);
                    if (mapperNeedsA12) NotifyMapperA12(ioaddr);  // CHR low addr
                } else if (phase == 5) {
                    lowTile = chrBankPtrs[(ioaddr >> 10) & 7][ioaddr & 0x3FF];
                } else if (phase == 6) {
                    ioaddr = BgPatternTableAddr | (NTVal << 4) | ((vram_addr >> 12) & 7) | 8;
                    if (mapperNeedsA12 && !mapperA12IsMmc3) NotifyMapperA12(ioaddr);  // MMC2/MMC4: CHR high triggers latch
                } else {
                    highTile = chrBankPtrs[(ioaddr >> 10) & 7][ioaddr & 0x3FF];
                    // Render 8 pixels using shift registers BEFORE reload (visible only, BG on)
                    if (scanline < 240 && cx < 256 && ShowBackGround)
                        RenderBGTile(cx);
                    // Load shift registers (high = old-low = previous tile, low = new tile)
                    lowshift  = (ushort)((lowshift  << 8) | lowTile);
                    highshift = (ushort)((highshift << 8) | highTile);
                    // Sync per-dot shadow registers: load new tile into low byte
                    // (the per-dot shifting already shifted the old data up by 8 bits)
                    lowshift_s0  = (ushort)((lowshift_s0  & 0xFF00) | lowTile);
                    highshift_s0 = (ushort)((highshift_s0 & 0xFF00) | highTile);
                    CXinc();
                }
            }
            else if (cx == 256)
            {
                Yinc();
            }
            else if (cx >= 257 && cx < 320)
            {
                if (cx == 257) { CopyHoriV(); spr_ram_add = 0; }

                // Latch sprite size at dot 261 (sprite 0 CHR low fetch address).
                // On real hardware, the Spritesize8x16 value at CHR fetch time determines
                // tile addressing. Mid-HBlank $2000 writes after this dot won't affect
                // the current scanline's sprite 0 tile data.
                if (cx == 261) spriteSizeLatchedForFetch = Spritesize8x16;

                if (mapperA12IsMmc3)
                {
                    int phase = (cx - 257) & 7;
                    if (phase == 0) NotifyMapperA12(0x2000);                // garbage NT addr, A12=0
                    else if (phase == 3) NotifyMapperA12(SpPatternTableAddr); // sprite CHR addr (pre-output)
                }
                else if (mapperNeedsA12 && !mapperA12IsMmc3)
                {
                    // MMC2/MMC4: per-sprite CHR high address for right-latch detection
                    int phase9 = (cx - 257) & 7;
                    int slot9  = (cx - 257) >> 3;
                    if (phase9 == 0) NotifyMapperA12(0x2000);
                    else if (phase9 == 5 && slot9 < 8)
                    {
                        byte tileNum = secondaryOAM[slot9 * 4 + 1];
                        NotifyMapperA12(SpPatternTableAddr | (tileNum << 4) | 8);
                    }
                }
            }

            // Dot 321: latch secondary OAM[0] into oamCopyBuffer for dots 321-340
            if (cx == 321 && scanline < 240 && (ShowBackGround || ShowSprites))
                oamCopyBuffer = secondaryOAM[0];

            // Pre-render scanline: continuous vert(v) = vert(t) copy at cycles 280-304
            if (scanline == 261 && cx >= 280 && cx <= 304)
            {
                vram_addr = (vram_addr & ~0x7BE0) | (vram_addr_internal & 0x7BE0);
            }

            // Garbage NT fetches at dots 336-339: notify A12=0 to create falling edge
            // after BG prefetch CHR (A12=1 for BG=$1000), needed for scanline-boundary timing
            if (mapperNeedsA12 && cx == 336)
                NotifyMapperA12(0x2000);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void NotifyMapperA12(int address)
        {
            MapperObj.NotifyA12(address, scanline * 341 + ppu_cycles_x);
        }

        static void ppu_step_new()
        {
            // $2007 read cooldown (suppresses rapid consecutive reads)
            if (ppu2007ReadCooldown > 0) ppu2007ReadCooldown--;

            // Open bus decay
            if (--open_bus_decay_timer == 0)
            {
                open_bus_decay_timer = 77777;
                openbus = 0;
            }

            bool renderingEnabled = ShowBackGround || ShowSprites;
            // ppuRenderingEnabled is the delayed version (Mesen2 model: updated at end of previous dot).
            // Used for tile fetch gating — the mask write takes effect 1 PPU dot later for rendering.

            // Shadow ppu_cycles_x to local for JIT enregistration
            int cx = ppu_cycles_x;

            // At dot 0 of visible scanlines: precompute sprite 0 data for hit detection.
            // Must run BEFORE the hit check so sprite0_on_line is valid at dot 0.
            // On real hardware, sprite evaluation happens during the previous scanline.
            if (scanline >= 0 && scanline < 240 && cx == 0)
                PrecomputeSprite0Line();

            // Per-pixel sprite 0 hit detection using per-dot shifted shadow registers.
            // Condition order: cheapest short-circuits first.
            // sprite0_on_line / !isSprite0hit eliminate most scanlines instantly.
            // Range check (cx in [sprite0_line_x, +8)) narrows to 8 dots from 256,
            // avoiding the inner sprCol calculation for the other 248 dots per scanline.
            if (sprite0_on_line && !isSprite0hit
                && cx >= sprite0_line_x && cx < sprite0_line_x + 8
                && cx < 256
                && scanline >= 0 && scanline < 240
                && ShowBackGround && ShowSprites)
            {
                bool inLeft8 = cx < 8;
                if (!(!ShowBgLeft8 && inLeft8) && !(!ShowSprLeft8 && inLeft8) && cx != 255)
                {
                    int sprCol = cx - sprite0_line_x;
                    int bit = 15 - FineX;
                    int bgPixel = ((lowshift_s0 >> bit) & 1) | (((highshift_s0 >> bit) & 1) << 1);
                    if (bgPixel != 0)
                    {
                        int loc_t = sprite0_flip_x ? (7 - sprCol) : sprCol;
                        int mask = 1 << (7 - loc_t);
                        int sprPx = (((sprite0_tile_high & mask) << 1) + (sprite0_tile_low & mask)) >> (7 - loc_t);
                        if (sprPx != 0)
                            isSprite0hit = true;
                    }
                }
            }
            // Per-dot BG shift register shifting (shadow registers for sprite 0 hit).
            // "Output then shift" model: hit check reads BEFORE shift, shift happens after.
            // Hardware: shift registers are FROZEN when rendering is disabled (both BG+sprites off).
            // When rendering is enabled, shift with serial input: low plane = 0, high plane = 1.
            // AccuracyCoin "Stale BG Shift Registers" confirms: regs not clocked when off → stale data preserved.
            // AccuracyCoin "Rendering Flag" confirms: regs empty if never loaded → no false hit.
            // Use delayed ppuRenderingEnabled: shift registers are clocked by the same signal as tile fetches.
            if (ppuRenderingEnabled)
            {
                if ((scanline >= 0 && scanline < 240 && cx < 256)
                    || ((scanline < 240 || scanline == 261)
                        && cx >= 320 && cx < 336))
                {
                    lowshift_s0 <<= 1;
                    highshift_s0 = (ushort)((highshift_s0 << 1) | 1);
                }
            }

            if (scanline < 240 || scanline == 261)
            {
                if (ppuRenderingEnabled)
                    ppu_rendering_tick(cx);

                // Per-dot sprite evaluation (visible scanlines only)
                if (AccuracyOptA)
                {
                    if (scanline >= 0 && scanline < 240 && ppuRenderingEnabled)
                    {
                        // Dots 1-64: clear secondary OAM (write $FF, 2 dots per byte)
                        if (cx >= 1 && cx <= 64)
                        {
                            oamCopyBuffer = 0xFF;
                            if ((cx & 1) == 0)
                                secondaryOAM[(cx >> 1) - 1] = 0xFF;
                        }
                        // Dot 65: initialize evaluation FSM
                        else if (cx == 65)
                        {
                            sprite0_eval_addr = spr_ram_add;
                            SpriteEvalInit();
                            SpriteEvalTick();
                        }
                        // Dots 66-256: per-dot evaluation
                        else if (cx >= 66 && cx <= 256)
                        {
                            SpriteEvalTick();
                            if (cx == 256) SpriteEvalEnd();
                        }
                    }
                    // Pre-render line: save sprite0_eval_addr at dot 65
                    else if (scanline == 261 && cx == 65 && ppuRenderingEnabled)
                    {
                        sprite0_eval_addr = spr_ram_add;
                    }
                }

                if (scanline >= 0 && scanline < 240)
                {
                    // At start of each visible scanline: always zero Buffer_BG_array to prevent
                    // stale data from prior frames causing incorrect sprite priority decisions.
                    if (cx == 0)
                    {
                        int scanOff = scanline << 8;
                        int* bgp = Buffer_BG_array + scanOff;
                        for (int* bge = bgp + 256; bgp < bge; bgp++) *bgp = 0;
                        if (!ShowBackGround)
                        {
                            uint bgColor = NesColors[ppu_ram[0x3f00] & 0x3f];
                            uint* sp = ScreenBuf1x + scanOff;
                            for (uint* se = sp + 256; sp < se; sp++) *sp = bgColor;
                            // NTSC: BG 關閉時填入背景色調色盤索引
                            if (AnalogEnabled)
                            {
                                byte bgIdx = (byte)(ppu_ram[0x3f00] & 0x3f);
                                for (int i = 0; i < 256; i++) ntscScanBuf[i] = bgIdx;
                            }
                        }
                        PrecomputeOverflow();
                    }

                    // Per-cycle sprite overflow flag (set at the exact evaluation cycle)
                    if (spriteOverflowCycle >= 0 && cx == spriteOverflowCycle)
                        isSpriteOverflow = true;

                    // Sprite evaluation + rendering at cycle 257 (after BG tiles complete at cycle 255)
                    if (cx == 257)
                        RenderSpritesLine();

                }

                // Pre-render line: compute pre-render sprite data at dot 257
                if (scanline == 261 && cx == 257 && ppuRenderingEnabled)
                    PrecomputePreRenderSprites();

            }

            // Screen output at scanline 240 cycle 1 (matches ppu_step timing)
            if (scanline == 240 && cx == 1)
            {
                RenderScreen();
                frame_count++;
                // StopWatch 持續計時，不在此 Restart（供 deadline 絕對計時使用）
            }

            // Update delayed rendering flag (Mesen2: _renderingEnabled updated at end of dot)
            ppuRenderingEnabled = renderingEnabled;

            // Advance cycle counter; writeback to static
            ppu_cycles_x = ++cx;

            // VBlank start at scanline 241, cycle 1 (post-increment)
            if (scanline == 241 && cx == 1)
            {
                if (!SuppressVbl)
                {
                    isVblank = true;
                }
                SuppressVbl = false;
            }

            // Pre-render: clear PPU status flags
            // M2 duty cycle effect: sprite flags are read at M2 fall (~1.875 PPU dots after M2 rise)
            // So sprite flags appear to clear ~2 dots earlier than VBL flag
            if (scanline == 261 && cx == 1)
                isSprite0hit = isSpriteOverflow = false;
            if (scanline == 261 && cx == 2)
                isVblank = false;

            // Odd frame skip: on odd frames with rendering enabled, skip last idle cycle of pre-render
            if (scanline == 261 && cx == 339)
            {
                oddSwap = !oddSwap;
                if (!oddSwap && (ShowBackGround || ShowSprites)) ppu_cycles_x = ++cx;
            }

            // Advance scanline
            if (cx == 341)
            {
                if (++scanline == 262)
                {
                    scanline = 0;
                    // Process OAM corruption at frame start if rendering is enabled
                    if (ShowBackGround || ShowSprites)
                        ProcessOamCorruption();
                }
                ppu_cycles_x = 0;
            }
        }

        #endregion

        static int open_bus_decay_timer = 77777;

        // Pre-computed sprite 0 data for per-pixel hit detection during BG rendering
        static bool sprite0_on_line;
        static int sprite0_line_x;
        static byte sprite0_tile_low, sprite0_tile_high;
        static bool sprite0_flip_x;
        static int sprite0_eval_addr;  // OAMADDR at start of sprite evaluation (dot 65), for next scanline's sprite 0
        static bool spriteSizeLatchedForFetch; // Spritesize8x16 latched at dot 261 (sprite 0 CHR fetch timing)

        // ========== Secondary OAM and Per-dot Sprite Evaluation FSM ==========
        static byte* secondaryOAM; // 8 sprites × 4 bytes
        static byte oamCopyBuffer;                  // Last byte read during evaluation ($2004 returns this)
        static byte spriteEvalAddrH;                // Primary OAM sprite index (0-63)
        static byte spriteEvalAddrL;                // Byte offset within sprite (0-3)
        static byte secOAMAddr;                     // Write position in secondary OAM (0-31)
        static bool spriteInRange;                  // Current sprite Y is in range
        static bool sprite0Added;                   // First evaluated sprite was in range
        static bool oamCopyDone;                    // Evaluation wrapped around all 64 sprites
        static byte overflowBugCounter;             // For sprite overflow hardware bug
        static int evalSpriteCount;                 // Number of sprites found (0-8)
        static bool evalSprite0Visible;             // Sprite 0 found in secondary OAM

        // Pre-render line sprite data (loaded at pre-render dot 257 for scanline 0)
        static bool prerender_sprite0_valid;
        static int prerender_sprite0_x;
        static byte prerender_sprite0_tile_low, prerender_sprite0_tile_high;
        static bool prerender_sprite0_flip_x;

        // Pre-computed sprite overflow cycle for cycle-accurate overflow flag timing
        static int spriteOverflowCycle;

        // OAM corruption: mark which row gets corrupted when rendering is disabled mid-scanline
        static void SetOamCorruptionFlags()
        {
            if (ppu_cycles_x >= 0 && ppu_cycles_x < 64)
            {
                // Secondary OAM clear phase: every 2 dots shifts corruption down 1 row
                corruptOamRow[ppu_cycles_x >> 1] = 1;
            }
            else if (ppu_cycles_x >= 256 && ppu_cycles_x < 320)
            {
                // Sprite tile fetch phase: 8-dot segments
                int rel = ppu_cycles_x - 256;
                int baseIdx = rel >> 3;
                int offset = rel & 0x07;
                if (offset > 3) offset = 3;
                corruptOamRow[baseIdx * 4 + offset] = 1;
            }
        }

        // OAM corruption: copy first 8 bytes of OAM over each marked row
        static void ProcessOamCorruption()
        {
            for (int i = 0; i < 32; i++)
            {
                if (corruptOamRow[i] != 0)
                {
                    if (i > 0)
                    {
                        for (int j = 0; j < 8; j++)
                            spr_ram[i * 8 + j] = spr_ram[j];
                    }
                    corruptOamRow[i] = 0;
                }
            }
        }

        // Pre-compute sprite 0 tile data for the current scanline so hit detection
        // can happen per-pixel inside RenderBGTile() at the correct PPU cycle.
        static void PrecomputeSprite0Line()
        {
            sprite0_on_line = false;
            if (isSprite0hit) return;
            if (!ShowBackGround && !ShowSprites) return;

            // Sprite 0 is the first sprite processed during evaluation on the PREVIOUS
            // scanline. Use the saved OAMADDR from that evaluation (dot 65).
            // For misaligned OAM, bytes wrap: addrH increments when addrL wraps past 3.
            int addrH = (sprite0_eval_addr >> 2) & 0x3F;
            int addrL = sprite0_eval_addr & 0x03;

            // Read 4 bytes as hardware does: Y, tile, attr, X — with addrL wrapping
            byte sprY    = spr_ram[(byte)(addrH * 4 + addrL)]; addrL++; if (addrL >= 4) { addrH = (addrH + 1) & 0x3F; addrL = 0; }
            byte sprTile = spr_ram[(byte)(addrH * 4 + addrL)]; addrL++; if (addrL >= 4) { addrH = (addrH + 1) & 0x3F; addrL = 0; }
            byte sprAttr = spr_ram[(byte)(addrH * 4 + addrL)]; addrL++; if (addrL >= 4) { addrH = (addrH + 1) & 0x3F; addrL = 0; }
            byte sprX    = spr_ram[(byte)(addrH * 4 + addrL)];

            int y_loc = sprY + 1; // NES hardware: sprites display at OAM_Y + 1
            // Use latched sprite size from dot 261 of previous scanline's HBlank.
            // This matches real hardware where sprite 0's CHR fetch uses the size
            // at fetch time, not the value at dot 0 of the next scanline.
            bool sprSize16 = spriteSizeLatchedForFetch;
            int height = sprSize16 ? 15 : 7;
            if (scanline < y_loc || scanline - y_loc > height)
            {
                // Scanline 0: use pre-render line sprite 0 data if available.
                if (scanline == 0 && prerender_sprite0_valid)
                {
                    sprite0_on_line = true;
                    sprite0_line_x = prerender_sprite0_x;
                    sprite0_tile_low = prerender_sprite0_tile_low;
                    sprite0_tile_high = prerender_sprite0_tile_high;
                    sprite0_flip_x = prerender_sprite0_flip_x;
                }
                return;
            }

            sprite0_on_line = true;
            sprite0_line_x = sprX;

            sprite0_flip_x = (sprAttr & 0x40) != 0;
            int offset, tile_th_t, line, line_t;
            byte tile_th;

            if (sprSize16)
            {
                tile_th = (byte)(sprTile & 0xfe);
                offset = (sprTile & 1) != 0 ? 256 : 0;
            }
            else
            {
                tile_th = sprTile;
                offset = SpPatternTableAddr >> 4;
            }

            if (scanline <= y_loc + 7)
            {
                tile_th_t = tile_th + offset;
                line = scanline - y_loc;
            }
            else
            {
                tile_th_t = tile_th + offset + 1;
                line = scanline - y_loc - 8;
            }

            if ((sprAttr & 0x80) != 0)
            {
                line_t = 7 - line;
                if (sprSize16) tile_th_t ^= 1;
            }
            else line_t = line;

            { int a = (tile_th_t << 4) | (line_t + 8); sprite0_tile_high = chrBankPtrs[(a >> 10) & 7][a & 0x3FF]; }
            { int a = (tile_th_t << 4) | line_t;       sprite0_tile_low  = chrBankPtrs[(a >> 10) & 7][a & 0x3FF]; }
        }

        // Pre-compute the PPU cycle at which sprite overflow flag should be set.
        // Simulates NES sprite evaluation timing (dots 65-256) with the hardware
        // overflow bug: after finding 8 sprites, byte offset m cycles 0→1→2→3,
        // reading tile/attr/X bytes as Y coordinates.
        static void PrecomputeOverflow()
        {
            spriteOverflowCycle = -1;
            if (!ShowBackGround && !ShowSprites) return;

            int height = Spritesize8x16 ? 15 : 7;
            int evalCycle = 65;
            int foundCount = 0;
            int m = 0; // byte offset for overflow bug

            for (int n = 0; n < 64 && evalCycle <= 256; n++)
            {
                if (foundCount < 8)
                {
                    // Normal evaluation: always read byte 0 (Y)
                    int oam_y = spr_ram[n << 2];
                    if (scanline >= oam_y && scanline - oam_y <= height)
                    {
                        foundCount++;
                        evalCycle += 8; // in-range: 2 read + 6 copy
                    }
                    else
                    {
                        evalCycle += 2; // not in range
                    }
                }
                else
                {
                    // Overflow bug evaluation: read byte m (cycles through 0,1,2,3)
                    int oam_y = spr_ram[(n << 2) + m];
                    if (scanline >= oam_y && scanline - oam_y <= height)
                    {
                        spriteOverflowCycle = evalCycle;
                        return;
                    }
                    m = (m + 1) & 3; // bug: increment byte offset on miss
                    evalCycle += 2;
                }
            }
        }

        // Initialize sprite evaluation state at dot 65 of visible scanlines
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SpriteEvalInit()
        {
            sprite0Added = false;
            spriteInRange = false;
            secOAMAddr = 0;
            overflowBugCounter = 0;
            oamCopyDone = false;
            spriteEvalAddrH = (byte)((spr_ram_add >> 2) & 0x3F);
            spriteEvalAddrL = (byte)(spr_ram_add & 0x03);
        }

        // Per-dot sprite evaluation: odd dots read, even dots write/check
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SpriteEvalTick()
        {
            bool isOdd = (ppu_cycles_x & 1) != 0;

            if (isOdd)
            {
                // Odd cycle: read from primary OAM
                oamCopyBuffer = spr_ram[(byte)(spriteEvalAddrH * 4 + spriteEvalAddrL)];
                // Attribute byte bits 2-4 don't exist in hardware; masked on internal bus
                if (spriteEvalAddrL == 2) oamCopyBuffer &= 0xE3;
            }
            else
            {
                // Even cycle: write/check
                SpriteEvalWrite();
            }
        }

        static void SpriteEvalWrite()
        {
            int height = Spritesize8x16 ? 16 : 8;

            if (secOAMAddr >= 0x20)
            {
                // Secondary OAM full
                oamCopyBuffer = secondaryOAM[secOAMAddr & 0x1F]; // read instead of write

                if (oamCopyDone)
                {
                    // Already found 8+ sprites, just advance
                    spriteEvalAddrH = (byte)((spriteEvalAddrH + 1) & 0x3F);
                    spriteEvalAddrL = 0;
                }
                else if (spriteInRange)
                {
                    // Found 9th+ sprite: overflow flag set by PrecomputeOverflow() at exact cycle
                    spriteEvalAddrL++;
                    if (spriteEvalAddrL >= 4)
                    {
                        spriteEvalAddrH = (byte)((spriteEvalAddrH + 1) & 0x3F);
                        spriteEvalAddrL = 0;
                    }
                    if (overflowBugCounter == 0)
                        overflowBugCounter = 3;
                    else if (--overflowBugCounter == 0)
                    {
                        oamCopyDone = true;
                        spriteEvalAddrL = 0;
                    }
                    spriteInRange = false; // reset for next check
                }
                else
                {
                    // Check in-range for overflow bug (reads wrong byte offset)
                    if (scanline >= oamCopyBuffer && scanline < oamCopyBuffer + height)
                        spriteInRange = true;
                    // Advance both H and L (hardware bug: L increments on miss)
                    spriteEvalAddrH = (byte)((spriteEvalAddrH + 1) & 0x3F);
                    spriteEvalAddrL = (byte)((spriteEvalAddrL + 1) & 0x03);
                    if (spriteEvalAddrH == 0) oamCopyDone = true;
                }
            }
            else
            {
                // Check in-range if not already tracking
                if (!spriteInRange)
                {
                    if (scanline >= oamCopyBuffer && scanline < oamCopyBuffer + height)
                        spriteInRange = !oamCopyDone;
                }

                if (oamCopyDone)
                {
                    // All 64 sprites checked, fewer than 8 in range:
                    // even dots read from secondary OAM into oamCopyBuffer (no write)
                    oamCopyBuffer = secondaryOAM[secOAMAddr];
                    // Still advance through primary OAM (hardware continues cycling)
                    spriteEvalAddrH = (byte)((spriteEvalAddrH + 1) & 0x3F);
                    spriteEvalAddrL = 0;
                }
                else
                {
                    // Write to secondary OAM (attribute already masked at read time)
                    secondaryOAM[secOAMAddr] = oamCopyBuffer;

                    if (spriteInRange)
                    {
                        // First in-range sprite at the very first evaluation is sprite 0
                        if (ppu_cycles_x == 66) sprite0Added = true;

                        spriteEvalAddrL++;
                        secOAMAddr++;

                        if (spriteEvalAddrL >= 4)
                        {
                            spriteEvalAddrH = (byte)((spriteEvalAddrH + 1) & 0x3F);
                            spriteEvalAddrL = 0;
                            if (spriteEvalAddrH == 0) oamCopyDone = true;
                        }

                        if ((secOAMAddr & 0x03) == 0)
                        {
                            // Done copying 4 bytes of this sprite
                            spriteInRange = false;
                        }
                    }
                    else
                    {
                        // Not in range: skip to next sprite
                        spriteEvalAddrH = (byte)((spriteEvalAddrH + 1) & 0x3F);
                        spriteEvalAddrL = 0;
                        if (spriteEvalAddrH == 0) oamCopyDone = true;
                    }
                }
            }

            // Update primary OAM read address (for $2003 visibility)
            spr_ram_add = (byte)((spriteEvalAddrL & 0x03) | (spriteEvalAddrH << 2));
        }

        // Finalize evaluation at dot 256
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SpriteEvalEnd()
        {
            evalSprite0Visible = sprite0Added;
            evalSpriteCount = (secOAMAddr + 3) >> 2;
            if (evalSpriteCount > 8) evalSpriteCount = 8;
        }

        // Pre-render line: check secondary OAM entries against (261 & 255) = 5
        // and store sprite 0 data for scanline 0 rendering
        static void PrecomputePreRenderSprites()
        {
            prerender_sprite0_valid = false;
            int effectiveScanline = 261 & 255; // = 5
            int height = Spritesize8x16 ? 16 : 8;

            // Check first entry in secondary OAM (potential sprite 0)
            byte sprY = secondaryOAM[0];
            if (sprY >= 240) return; // $FF or invalid

            if (effectiveScanline >= sprY && effectiveScanline < sprY + height)
            {
                byte sprTile = secondaryOAM[1];
                byte sprAttr = secondaryOAM[2];
                byte sprX = secondaryOAM[3];

                prerender_sprite0_valid = true;
                prerender_sprite0_x = sprX;
                prerender_sprite0_flip_x = (sprAttr & 0x40) != 0;

                int line = effectiveScanline - sprY;
                int offset, tile_th_t, line_t;
                byte tile_th;

                if (Spritesize8x16)
                {
                    tile_th = (byte)(sprTile & 0xfe);
                    offset = (sprTile & 1) != 0 ? 256 : 0;
                }
                else
                {
                    tile_th = sprTile;
                    offset = SpPatternTableAddr >> 4;
                }

                if (line <= 7)
                {
                    tile_th_t = tile_th + offset;
                }
                else
                {
                    tile_th_t = tile_th + offset + 1;
                    line -= 8;
                }

                if ((sprAttr & 0x80) != 0)
                {
                    line_t = 7 - line;
                    if (Spritesize8x16) tile_th_t ^= 1;
                }
                else line_t = line;

                { int a = (tile_th_t << 4) | (line_t + 8); prerender_sprite0_tile_high = chrBankPtrs[(a >> 10) & 7][a & 0x3FF]; }
                { int a = (tile_th_t << 4) | line_t;       prerender_sprite0_tile_low  = chrBankPtrs[(a >> 10) & 7][a & 0x3FF]; }
            }
        }

        static int pixel, array_loc;

        static void RenderSpritesLine()
        {
            // Pass 1: scan OAM 0→63, pick first 8 sprites visible on this scanline.
            // NES hardware only performs sprite evaluation when rendering is enabled.
            // Overflow detection is handled by PrecomputeOverflow() at cycle-accurate timing.
            int* sel = stackalloc int[8];
            int selCount = 0;
            int height = Spritesize8x16 ? 15 : 7;
            bool renderingEnabled = ShowBackGround || ShowSprites;

            if (renderingEnabled)
            {
                for (int oam_th = 0; oam_th < 64; oam_th++)
                {
                    // Selection for rendering uses Y+1 (sprites display one scanline later)
                    int render_y = spr_ram[oam_th << 2] + 1;
                    if (scanline < render_y || scanline - render_y > height) continue;
                    if (selCount < 8) sel[selCount++] = oam_th;
                }
            }

            if (!ShowSprites) return;

            // Per-pixel sprite winner buffers.
            // NES hardware picks ONE winning sprite per pixel (lowest OAM index with opaque pixel).
            // That winner's priority bit then decides the BG/sprite composite for ALL sprites at that pixel.
            // This implements the "sprite priority quirk": a behind-BG mask sprite (low OAM index,
            // priority=1) can suppress a front sprite (high OAM index, priority=0) at the same pixel.
            uint* sprColor    = stackalloc uint[256];
            byte* sprPriority = stackalloc byte[256]; // 1 = behind BG, 0 = in front
            byte* sprSet      = stackalloc byte[256]; // 1 = a winning sprite pixel exists here
            byte* sprPalIdx   = stackalloc byte[256]; // NTSC: raw palette index of winning sprite
            for (int i = 0; i < 256; i++) sprSet[i] = 0;

            // Pass 2: evaluate sprites in reverse OAM order so lower-index sprites overwrite higher,
            // making the lowest-index sprite the final winner at each pixel.
            for (int si = selCount - 1; si >= 0; si--)
            {
                int oam_th = sel[si];
                int oam_addr = oam_th << 2;
                int y_loc = spr_ram[oam_addr] + 1; // NES hardware: sprites display at OAM_Y + 1

                int offset, tile_th_t, line, line_t;
                byte tile_th;

                if (Spritesize8x16)
                {
                    byte byte0 = spr_ram[oam_addr | 1];
                    tile_th = (byte)(byte0 & 0xfe);
                    offset = (byte0 & 1) != 0 ? 256 : 0;
                }
                else
                {
                    tile_th = spr_ram[oam_addr | 1];
                    offset = SpPatternTableAddr >> 4;
                }

                byte sprite_attr = spr_ram[oam_addr | 2];
                byte x_loc = spr_ram[oam_addr | 3];
                bool priority = (sprite_attr & 0x20) != 0;

                if (scanline <= y_loc + 7)
                {
                    tile_th_t = tile_th + offset;
                    line = scanline - y_loc;
                }
                else
                {
                    tile_th_t = tile_th + offset + 1;
                    line = scanline - y_loc - 8;
                }

                if ((sprite_attr & 0x80) != 0)
                {
                    line_t = 7 - line;
                    if (Spritesize8x16) tile_th_t ^= 1;
                }
                else line_t = line;

                int th_a = (tile_th_t << 4) | (line_t + 8); byte tile_hbyte = chrBankPtrs[(th_a >> 10) & 7][th_a & 0x3FF];
                int tl_a = (tile_th_t << 4) | line_t;       byte tile_lbyte = chrBankPtrs[(tl_a >> 10) & 7][tl_a & 0x3FF];
                bool flip_x = (sprite_attr & 0x40) != 0;

                for (int loc = 0; loc < 8; loc++)
                {
                    int screenX = x_loc + loc;
                    if (screenX > 255) continue;
                    if (!ShowSprLeft8 && screenX < 8) continue;
                    int loc_t = flip_x ? (7 - loc) : loc;
                    int mask = 1 << (7 - loc_t);
                    pixel = (((tile_hbyte & mask) << 1) + (tile_lbyte & mask)) >> (7 - loc_t);
                    if (pixel == 0) continue;

                    array_loc = (scanline << 8) + screenX;

                    // Record as winner at this column (lower OAM index will overwrite later)
                    sprSet[screenX]      = 1;
                    sprPriority[screenX] = (byte)(priority ? 1 : 0);
                    byte sprRawPal       = (byte)(ppu_ram[0x3f10 + ((sprite_attr & 3) << 2) | pixel] & 0x3f);
                    sprColor[screenX]    = NesColors[sprRawPal];
                    sprPalIdx[screenX]   = sprRawPal; // NTSC: preserve raw palette index
                }
            }

            // Pass 3: composite — draw winning sprite pixel only if:
            //   BG is disabled, OR BG pixel is transparent, OR winning sprite is front-priority.
            // A behind-BG winner (priority=1) with opaque BG blocks ALL sprites at that pixel,
            // correctly implementing the mask-sprite trick used by SMB3.
            int scanOff = scanline << 8;
            // P33: ulong 8-byte block-scan — skip all-zero blocks 8 pixels at a time,
            // fall back to scalar only for blocks that contain sprite pixels.
            {
                int sx = 0;
                while (sx <= 248) // 256 - 8
                {
                    if (*(ulong*)(sprSet + sx) == 0) { sx += 8; continue; }
                    int blockEnd = sx + 8;
                    for (; sx < blockEnd; sx++)
                    {
                        if (sprSet[sx] == 0) continue;
                        array_loc = scanOff + sx;
                        if (!ShowBackGround || Buffer_BG_array[array_loc] == 0 || sprPriority[sx] == 0)
                        {
                            ScreenBuf1x[array_loc] = sprColor[sx];
                            if (AnalogEnabled) ntscScanBuf[sx] = sprPalIdx[sx]; // NTSC: override with sprite palette
                        }
                    }
                }
                for (; sx < 256; sx++)
                {
                    if (sprSet[sx] == 0) continue;
                    array_loc = scanOff + sx;
                    if (!ShowBackGround || Buffer_BG_array[array_loc] == 0 || sprPriority[sx] == 0)
                    {
                        ScreenBuf1x[array_loc] = sprColor[sx];
                        if (AnalogEnabled) ntscScanBuf[sx] = sprPalIdx[sx]; // NTSC: override with sprite palette
                    }
                }
            }

            // NTSC: 訊號解碼（使用已填好的 ntscScanBuf 原始調色盤索引）
            if (AnalogEnabled)
                Ntsc.DecodeScanline(scanline, ntscScanBuf, ppuEmphasis);
        }

        static public bool screen_lock = false;
        static void RenderScreen()
        {
            screen_lock = true;
            if (AnalogEnabled && NesCore.UltraAnalog && NesCore.CrtEnabled) CrtScreen.Render(); // Stage 2：linearBuffer → AnalogScreenBuf（Level 3 + CRT）
            VideoOutput?.Invoke(null, null);
            screen_lock = false;
            _event.WaitOne();
        }

        static bool SuppressVbl = false;
        //ref http://wiki.nesdev.com/w/index.php/PPU_scrolling
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte ppu_r_2002()
        {
            bool vblFlag = isVblank;

            // VBL suppression at exact VBL set dot (sl=241, cx=1):
            // VBL was just set by tick; suppress flag read and NMI
            if (scanline == 241 && ppu_cycles_x == 1)
            {
                vblFlag = false;
            }

            openbus = (byte)((vblFlag ? 0x80 : 0) | ((isSprite0hit) ? 0x40 : 0) | ((isSpriteOverflow) ? 0x20 : 0) | (openbus & 0x1f));

            isVblank = false;
            nmi_delay_cycle = -1;      // Cancel not-yet-promoted NMI (same-cycle $2002 read)
            nmi_output_prev = false;   // Reset edge state to prevent false rising edge on next tick
            // Note: nmi_pending is NOT cleared — once promoted, $2002 can't cancel it
            vram_latch = false;
            return openbus;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte ppu_r_2007()
        {
            if (ppu2007ReadCooldown > 0)
                return openbus; // suppress rapid consecutive $2007 reads
            byte result = ppu_read_fun[vram_addr](vram_addr);
            ppu2007ReadCooldown = 6; // 6 PPU dots ≈ 2 CPU cycles
            return result;
        }

        static byte openbus;
        static byte cpubus;  // CPU data bus value (last byte read/written by CPU)

        static void ppu_w_2000(byte value) //ok
        {
            openbus = value;

            // t: ...BA.. ........ = d: ......BA
            vram_addr_internal = (ushort)((vram_addr_internal & 0x73ff) | ((value & 3) << 10)); // 0xx73ff
            BaseNameTableAddr = 0x2000 | ((value & 3) << 10);
            VramaddrIncrement = ((value & 4) > 0) ? 32 : 1;
            SpPatternTableAddr = ((value & 8) > 0) ? 0x1000 : 0;
            BgPatternTableAddr = ((value & 0x10) > 0) ? 0x1000 : 0;
            Spritesize8x16 = ((value & 0x20) > 0) ? true : false;
            bool wasNMIable = NMIable;
            NMIable = ((value & 0x80) > 0) ? true : false;

            // NMI edge detection for $2000 writes:
            // Falling edge (disable NMI): cancel nmi_delay (not yet promoted)
            //   but NOT nmi_pending — once promoted, NMI cannot be cancelled by disable
            // Rising edge (enable NMI): let next tick() detect it (natural delay)
            bool nmi_output = isVblank && NMIable;
            if (!nmi_output && nmi_output_prev)
            {
                nmi_delay_cycle = -1;
                nmi_output_prev = false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_w_2001(byte value) //ok
        {
            openbus = value;

            ShowBgLeft8  = (value & 0x02) != 0; // bit1: show BG in leftmost 8 pixels
            ShowSprLeft8 = (value & 0x04) != 0; // bit2: show sprites in leftmost 8 pixels
            ShowBackGround = (value & 0x08) != 0;
            ShowSprites    = (value & 0x10) != 0;
            ppuEmphasis    = (byte)((value >> 5) & 0x7); // bits 5-7: RGB emphasis

            bool newRenderingEnabled = ShowBackGround || ShowSprites;
            if (prevRenderingEnabled != newRenderingEnabled && scanline >= 0 && scanline < 240)
            {
                if (newRenderingEnabled)
                {
                    ProcessOamCorruption();
                    // Mid-scanline rendering re-enable: sprite 0 wasn't precomputed at dot 0
                    // (rendering was off then). Re-run now so hit detection can work.
                    // Sprite counters were frozen, so sprite appears at the current dot.
                    if (!sprite0_on_line && !isSprite0hit && ppu_cycles_x > 0)
                    {
                        PrecomputeSprite0Line();
                        if (sprite0_on_line)
                            sprite0_line_x = ppu_cycles_x;
                    }
                }
                else
                    SetOamCorruptionFlags();
            }
            prevRenderingEnabled = newRenderingEnabled;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_w_2003(byte value) //ok
        {
            openbus = value;
            spr_ram_add = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_w_2004(byte value) //ok
        {
            openbus = value;
            // During rendering (visible + pre-render), writes don't modify OAM; OAMADDR increments by 4 and aligns to 4-byte boundary
            if ((scanline < 240 || scanline == 261) && scanline >= 0 && (ShowBackGround || ShowSprites))
            {
                spr_ram_add = (byte)((spr_ram_add + 4) & 0xFC);
            }
            else
            {
                spr_ram[spr_ram_add++] = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte ppu_r_2004()
        {
            byte val;
            bool renderingOn = ShowBackGround || ShowSprites;
            if (scanline >= 0 && scanline < 240 && renderingOn)
            {
                // Timing offset: tick() processes 3 PPU dots BEFORE the bus read,
                // so at cx=N the last processed dot was N-1. Shift ranges by +1.
                if (ppu_cycles_x >= 2 && ppu_cycles_x <= 65)
                {
                    // Secondary OAM clear phase (hardware dots 1-64): always $FF
                    val = 0xFF;
                }
                else if (ppu_cycles_x >= 66 && ppu_cycles_x <= 257)
                {
                    // Sprite evaluation phase (hardware dots 65-256): last byte read from primary OAM
                    val = oamCopyBuffer;
                }
                else if (ppu_cycles_x >= 258 && ppu_cycles_x <= 321)
                {
                    // Sprite tile fetch (hardware dots 257-320): reads from secondary OAM
                    // Pattern per 8-dot group: Y, tile, attr, X, X, X, X, X
                    int offset = ppu_cycles_x - 258;
                    int spriteIdx = offset >> 3;
                    int step = offset & 0x07;
                    int byteIdx = step > 3 ? 3 : step;
                    val = secondaryOAM[spriteIdx * 4 + byteIdx];
                }
                else
                {
                    // Dots 0-1 and 322-340: return oamCopyBuffer
                    val = oamCopyBuffer;
                }
            }
            else
            {
                val = spr_ram[spr_ram_add];
                if ((spr_ram_add & 3) == 2) val &= 0xE3; // mask unimplemented bits of attribute byte
            }
            open_bus_decay_timer = 77777;
            return openbus = val;
        }

        static void ppu_w_2005(byte value) //ok
        {
            openbus = value;
            if (vram_latch)
            {
                scrol_y = value & 7;
                vram_addr_internal = (vram_addr_internal & 0x0C1F) | ((value & 0x7) << 12) | ((value & 0xF8) << 2);
            }
            else
            {//first
                vram_addr_internal = (vram_addr_internal & 0x7fe0) | ((value & 0xf8) >> 3);
                FineX = value & 0x07;
            }
            vram_latch = !vram_latch;
        }
        static void ppu_w_2006(byte value)//ok
        {
            openbus = value;
            if (!vram_latch) //first
                vram_addr_internal = (vram_addr_internal & 0x00FF) | ((value & 0x3F) << 8);
            else
            {
                vram_addr_internal = (vram_addr_internal & 0x7F00) | value;
                vram_addr = vram_addr_internal;
                if (mapperNeedsA12) NotifyMapperA12(vram_addr);
            }
            vram_latch = !vram_latch;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_w_2007(byte value)
        {
            open_bus_decay_timer = 77777;
            ppu_write_fun[vram_addr](value);
        }

        static void ppu_w_4014(byte value)//DMA , fixex 2017.01.16 pass sprite_ram test
        {
            // Set OAM DMA flags — deferred to next read cycle via ProcessPendingDma()
            spriteDmaTransfer = true;
            spriteDmaOffset = value;
            dmaNeedHalt = true;
        }
    }
}
