using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AprNes
{
    unsafe public partial class NesCoreSpeed
    {
        // PPU memory
        static byte* spr_ram_S;          // 256-byte OAM
        static public byte* ppu_ram_S;   // 16KB PPU VRAM (allocated in Main_S.cs)
        static public uint* ScreenBuf1x_S;
        static uint* NesColors_S;
        static byte* Buffer_BG_array_S;  // SP-5: 256*240 BG opacity bytes (byte not int = 4x smaller, better cache)

        // PPU control ($2000)
        static int VramaddrIncrement_S = 1;
        static int SpPatternTableAddr_S = 0, BgPatternTableAddr_S = 0;
        static bool Spritesize8x16_S = false, NMIable_S = false;

        // PPU mask ($2001)
        static bool ShowBackGround_S = false, ShowSprites_S = false;
        static bool ShowBgLeft8_S = true, ShowSprLeft8_S = true;

        // PPU status ($2002)
        static bool isSpriteOverflow_S = false, isSprite0hit_S = false, isVblank_S = false;

        // Scroll / address registers
        static int vram_addr_internal_S = 0, vram_addr_S = 0, FineX_S = 0;
        static bool vram_latch_S = false;
        static byte ppu_2007_buffer_S = 0;
        static byte spr_ram_add_S = 0;
        static bool oddSwap_S = false;
        static public int frame_count_S = 0;

        // NES color palette
        static readonly uint[] NesColorsData_S = {
            0xFF7C7C7C,0xFF0000FC,0xFF0000BC,0xFF4428BC,0xFF940084,0xFFA80020,0xFFA81000,0xFF881400,
            0xFF503000,0xFF007800,0xFF006800,0xFF005800,0xFF004058,0xFF000000,0xFF000000,0xFF000000,
            0xFFBCBCBC,0xFF0078F8,0xFF0058F8,0xFF6844FC,0xFFD800CC,0xFFE40058,0xFFF83800,0xFFE45C10,
            0xFFAC7C00,0xFF00B800,0xFF00A800,0xFF00A844,0xFF008888,0xFF000000,0xFF000000,0xFF000000,
            0xFFF8F8F8,0xFF3CBCFC,0xFF6888FC,0xFF9878F8,0xFFF878F8,0xFFF85898,0xFFF87858,0xFFFCA044,
            0xFFF8B800,0xFFB8F818,0xFF58D854,0xFF58F898,0xFF00E8D8,0xFF787878,0xFF000000,0xFF000000,
            0xFFFCFCFC,0xFFA4E4FC,0xFFB8B8F8,0xFFD8B8F8,0xFFF8B8F8,0xFFF8A4C0,0xFFF0D0B0,0xFFFCE0A8,
            0xFFF8D878,0xFFD8F878,0xFFB8F8B8,0xFFB8F8D8,0xFF00FCFC,0xFFF8D8F8,0xFF000000,0xFF000000 };

        // NES colors GCHandle for pinning
        static System.Runtime.InteropServices.GCHandle NesColorsHandle_S;

        static void init_ppu_S()
        {
            // Allocate OAM
            spr_ram_S = (byte*)Marshal.AllocHGlobal(256);
            for (int i = 0; i < 256; i++) spr_ram_S[i] = 0;

            // Allocate screen buffer (256*240 pixels)
            ScreenBuf1x_S = (uint*)Marshal.AllocHGlobal(256 * 240 * 4);

            // SP-5: Allocate BG array as bytes (4x smaller than int array)
            Buffer_BG_array_S = (byte*)Marshal.AllocHGlobal(256 * 240);

            // Pin colors array
            NesColorsHandle_S = System.Runtime.InteropServices.GCHandle.Alloc(NesColorsData_S,
                System.Runtime.InteropServices.GCHandleType.Pinned);
            NesColors_S = (uint*)NesColorsHandle_S.AddrOfPinnedObject();

            // Reset state
            ppu_scanline_S = 0; ppu_x_S = 0;
            isVblank_S = isSprite0hit_S = isSpriteOverflow_S = false;
            vram_addr_S = vram_addr_internal_S = 0;
            vram_latch_S = false; oddSwap_S = false;
            frame_count_S = 0;
            VramaddrIncrement_S = 1;
            SpPatternTableAddr_S = 0; BgPatternTableAddr_S = 0;
            Spritesize8x16_S = false; NMIable_S = false;
            ShowBackGround_S = false; ShowSprites_S = false;
            ShowBgLeft8_S = true; ShowSprLeft8_S = true;
            ppu_2007_buffer_S = 0;
            spr_ram_add_S = 0;

            // Initialize default palette
            for (int i = 0; i < 0x20; i++) ppu_ram_S[0x3F00 + i] = 0;
        }

        static void cleanup_ppu_S()
        {
            if (spr_ram_S != null) { Marshal.FreeHGlobal((System.IntPtr)spr_ram_S); spr_ram_S = null; }
            if (ScreenBuf1x_S != null) { Marshal.FreeHGlobal((System.IntPtr)ScreenBuf1x_S); ScreenBuf1x_S = null; }
            if (Buffer_BG_array_S != null) { Marshal.FreeHGlobal((System.IntPtr)Buffer_BG_array_S); Buffer_BG_array_S = null; }
            if (NesColorsHandle_S.IsAllocated) { NesColorsHandle_S.Free(); NesColors_S = null; }
        }

        // ----------------------------------------------------------------
        // Loopy scroll helpers

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CXinc_S()
        {
            if ((vram_addr_S & 0x001F) == 31) { vram_addr_S &= ~0x001F; vram_addr_S ^= 0x0400; }
            else vram_addr_S++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Yinc_S()
        {
            if ((vram_addr_S & 0x7000) != 0x7000)
                vram_addr_S += 0x1000;
            else
            {
                vram_addr_S &= ~0x7000;
                int y = (vram_addr_S & 0x03E0) >> 5;
                if (y == 29) { y = 0; vram_addr_S ^= 0x0800; }
                else if (y == 31) y = 0;
                else y++;
                vram_addr_S = (vram_addr_S & ~0x03E0) | (y << 5);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CopyHoriV_S()
        {
            vram_addr_S = (vram_addr_S & ~0x041F) | (vram_addr_internal_S & 0x041F);
        }

        // ----------------------------------------------------------------
        // Scanline renderer

        static void RenderScanline_S()
        {
            int sl = ppu_scanline_S;
            int scanOff = sl << 8;
            bool rendering = ShowBackGround_S || ShowSprites_S;

            // SP-5: Clear BG byte array (256 bytes = 32 × 8-byte writes, 4x faster than int array clear)
            ulong* bgDst = (ulong*)(Buffer_BG_array_S + scanOff);
            for (int i = 0; i < 32; i++) bgDst[i] = 0;

            if (ShowBackGround_S)
            {
                int fineX = FineX_S;
                uint bgcolor = NesColors_S[ppu_ram_S[0x3F00] & 0x3F];

                // SP-7: Precompute all 16 BG palette entries (4 palettes × 4 pixels) once per scanline.
                // Eliminates ~260 ppu_ram lookups per scanline by reading 16 entries upfront.
                uint* palCache = stackalloc uint[16];
                for (int p = 0; p < 4; p++)
                {
                    int b = 0x3F00 | (p << 2);
                    palCache[(p << 2) | 0] = bgcolor; // pixel 0 = universal BG color
                    palCache[(p << 2) | 1] = NesColors_S[ppu_ram_S[b + 1] & 0x3F];
                    palCache[(p << 2) | 2] = NesColors_S[ppu_ram_S[b + 2] & 0x3F];
                    palCache[(p << 2) | 3] = NesColors_S[ppu_ram_S[b + 3] & 0x3F];
                }

                // SP-26: Restructured tile loop — eliminate bounds checks for tiles 1-31 (fast path).
                // Tile 0: skip first fineX pixels (negative screenX). Tile 32: stop at fineX (>255 overflow).
                // ShowBgLeft8_S applied as post-process to avoid per-pixel branch in the hot loop.
                for (int tile = 0; tile < 33; tile++)
                {
                    // Fetch NT byte
                    int nt_addr = 0x2000 | (vram_addr_S & 0x0FFF);
                    byte ntVal = ppu_ram_S[nt_addr & 0x3FFF];

                    // Fetch AT byte
                    int at_addr = 0x23C0 | (vram_addr_S & 0x0C00) | ((vram_addr_S >> 4) & 0x38) | ((vram_addr_S >> 2) & 0x07);
                    byte atVal = (byte)((ppu_ram_S[at_addr & 0x3FFF] >> (((vram_addr_S >> 4) & 0x04) | (vram_addr_S & 0x02))) & 0x03);

                    // Fetch CHR (fine Y from bits 12-14 of vram_addr)
                    int fineY = (vram_addr_S >> 12) & 7;
                    int chrAddr = BgPatternTableAddr_S | (ntVal << 4) | fineY;
                    // SP-1: direct CHR bank pointer access
                    byte* tileBase = chrBankPtrs_S[chrAddr >> 10] + (chrAddr & 0x3FF);
                    byte lowTile  = tileBase[0];
                    byte highTile = tileBase[8];

                    // SP-7: Use precomputed palette — no branch needed (pixel 0 → bgcolor in cache)
                    uint* tilePal = palCache + (atVal << 2);
                    int baseX = tile * 8 - fineX;

                    if ((uint)(tile - 1) < 31u)
                    {
                        // SP-26 fast path: tiles 1-31 always have screenX in [1..255] — no bounds check
                        for (int px = 0, bit = 7; px < 8; px++, bit--)
                        {
                            int screenX = baseX + px;
                            int bgPixel = ((lowTile >> bit) & 1) | (((highTile >> bit) & 1) << 1);
                            Buffer_BG_array_S[scanOff + screenX] = (byte)bgPixel;
                            ScreenBuf1x_S[scanOff + screenX] = tilePal[bgPixel];
                        }
                    }
                    else if (tile == 0)
                    {
                        // Tile 0: first fineX pixels have negative screenX — skip them
                        for (int px = fineX, bit = 7 - fineX; px < 8; px++, bit--)
                        {
                            int screenX = px - fineX;
                            int bgPixel = ((lowTile >> bit) & 1) | (((highTile >> bit) & 1) << 1);
                            Buffer_BG_array_S[scanOff + screenX] = (byte)bgPixel;
                            ScreenBuf1x_S[scanOff + screenX] = tilePal[bgPixel];
                        }
                    }
                    else
                    {
                        // Tile 32: only first fineX pixels are valid (screenX 256-fineX .. 255)
                        for (int px = 0, bit = 7; px < fineX; px++, bit--)
                        {
                            int screenX = baseX + px;
                            int bgPixel = ((lowTile >> bit) & 1) | (((highTile >> bit) & 1) << 1);
                            Buffer_BG_array_S[scanOff + screenX] = (byte)bgPixel;
                            ScreenBuf1x_S[scanOff + screenX] = tilePal[bgPixel];
                        }
                    }

                    CXinc_S();
                }

                // ShowBgLeft8_S post-process: overwrite left 8 pixels with bgcolor if BG left-8 disabled
                if (!ShowBgLeft8_S)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        Buffer_BG_array_S[scanOff + x] = 0;
                        ScreenBuf1x_S[scanOff + x] = bgcolor;
                    }
                }

                // Advance Y
                Yinc_S();
            }
            else
            {
                uint bgcolor = NesColors_S[ppu_ram_S[0x3F00] & 0x3F];
                for (int i = 0; i < 256; i++) ScreenBuf1x_S[scanOff + i] = bgcolor;
            }

            // Horizontal scroll reset
            if (rendering) CopyHoriV_S();

            // Sprite rendering
            if (ShowSprites_S) RenderSpritesLine_S();

            // Sprite 0 hit check
            if (!isSprite0hit_S && ShowBackGround_S && ShowSprites_S)
                CheckSprite0Hit_S();
        }

        static void RenderSpritesLine_S()
        {
            // Pass 1: scan OAM 0→63, pick first 8 sprites visible on this scanline.
            int* sel = stackalloc int[8];
            int selCount = 0;
            int height = Spritesize8x16_S ? 15 : 7;
            bool renderingEnabled = ShowBackGround_S || ShowSprites_S;

            if (renderingEnabled)
            {
                for (int oam_th = 0; oam_th < 64; oam_th++)
                {
                    int render_y = spr_ram_S[oam_th << 2] + 1;
                    if (ppu_scanline_S < render_y || ppu_scanline_S - render_y > height) continue;
                    if (selCount < 8) sel[selCount++] = oam_th;
                    else { isSpriteOverflow_S = true; break; }
                }
            }

            if (!ShowSprites_S) return;

            // Per-pixel sprite winner buffers
            uint* sprColor    = stackalloc uint[256];
            byte* sprPriority = stackalloc byte[256];
            byte* sprSet      = stackalloc byte[256];
            // SP-5b: Clear sprSet with 32 ulong writes (4x fewer iterations than byte loop)
            ulong* sprSetU = (ulong*)sprSet; for (int i = 0; i < 32; i++) sprSetU[i] = 0;

            // SP-21: Precompute sprite palette (4 palettes × 4 entries = 16 lookups once per scanline)
            uint* sprPalCache = stackalloc uint[16];
            for (int p = 0; p < 4; p++)
            {
                int b = 0x3F10 | (p << 2);
                sprPalCache[(p << 2) | 1] = NesColors_S[ppu_ram_S[b + 1] & 0x3F];
                sprPalCache[(p << 2) | 2] = NesColors_S[ppu_ram_S[b + 2] & 0x3F];
                sprPalCache[(p << 2) | 3] = NesColors_S[ppu_ram_S[b + 3] & 0x3F];
            }

            // Pass 2: evaluate sprites in reverse OAM order so lower-index overrides higher
            for (int si = selCount - 1; si >= 0; si--)
            {
                int oam_th = sel[si];
                int oam_addr = oam_th << 2;
                int y_loc = spr_ram_S[oam_addr] + 1;

                int offset, tile_th_t, line, line_t;
                byte tile_th;

                if (Spritesize8x16_S)
                {
                    byte byte0 = spr_ram_S[oam_addr | 1];
                    tile_th = (byte)(byte0 & 0xFE);
                    offset = (byte0 & 1) != 0 ? 256 : 0;
                }
                else
                {
                    tile_th = spr_ram_S[oam_addr | 1];
                    offset = SpPatternTableAddr_S >> 4;
                }

                byte sprite_attr = spr_ram_S[oam_addr | 2];
                byte x_loc = spr_ram_S[oam_addr | 3];
                bool priority = (sprite_attr & 0x20) != 0;

                if (ppu_scanline_S <= y_loc + 7)
                {
                    tile_th_t = tile_th + offset;
                    line = ppu_scanline_S - y_loc;
                }
                else
                {
                    tile_th_t = tile_th + offset + 1;
                    line = ppu_scanline_S - y_loc - 8;
                }

                if ((sprite_attr & 0x80) != 0)
                {
                    line_t = 7 - line;
                    if (Spritesize8x16_S) tile_th_t ^= 1;
                }
                else line_t = line;

                int aHigh = (tile_th_t << 4) | (line_t + 8);
                int aLow  = (tile_th_t << 4) | line_t;
                byte tile_hbyte = chrBankPtrs_S[aHigh >> 10][aHigh & 0x3FF];
                byte tile_lbyte = chrBankPtrs_S[aLow  >> 10][aLow  & 0x3FF];
                bool flip_x = (sprite_attr & 0x40) != 0;
                // SP-21: use precomputed palette base pointer for this sprite
                uint* thisSprPal = sprPalCache + ((sprite_attr & 3) << 2);

                for (int loc = 0; loc < 8; loc++)
                {
                    int screenX = x_loc + loc;
                    if (screenX > 255) continue;
                    if (!ShowSprLeft8_S && screenX < 8) continue;
                    int loc_t = flip_x ? (7 - loc) : loc;
                    int mask = 1 << (7 - loc_t);
                    int pixel = (((tile_hbyte & mask) << 1) + (tile_lbyte & mask)) >> (7 - loc_t);
                    if (pixel == 0) continue;

                    sprSet[screenX]      = 1;
                    sprPriority[screenX] = (byte)(priority ? 1 : 0);
                    sprColor[screenX]    = thisSprPal[pixel]; // SP-21: cached palette lookup
                }
            }

            // SP-20: P33 port — ulong 8-byte block-scan skips all-zero blocks in composite pass
            int scanOff = ppu_scanline_S << 8;
            {
                int sx = 0;
                while (sx <= 248)
                {
                    if (*(ulong*)(sprSet + sx) == 0) { sx += 8; continue; }
                    int blockEnd = sx + 8;
                    for (; sx < blockEnd; sx++)
                    {
                        if (sprSet[sx] == 0) continue;
                        int array_loc = scanOff + sx;
                        if (!ShowBackGround_S || Buffer_BG_array_S[array_loc] == 0 || sprPriority[sx] == 0)
                            ScreenBuf1x_S[array_loc] = sprColor[sx];
                    }
                }
                for (; sx < 256; sx++)
                {
                    if (sprSet[sx] == 0) continue;
                    int array_loc = scanOff + sx;
                    if (!ShowBackGround_S || Buffer_BG_array_S[array_loc] == 0 || sprPriority[sx] == 0)
                        ScreenBuf1x_S[array_loc] = sprColor[sx];
                }
            }
        }

        static void CheckSprite0Hit_S()
        {
            int sl = ppu_scanline_S;
            int y_loc = spr_ram_S[0] + 1;
            int height = Spritesize8x16_S ? 15 : 7;
            if (sl < y_loc || sl - y_loc > height) return;

            int x_loc = spr_ram_S[3];
            byte sprite_attr = spr_ram_S[2];
            bool flip_x = (sprite_attr & 0x40) != 0;

            int line = sl - y_loc;
            byte tile_th = spr_ram_S[1];
            int offset, tile_th_t, line_t;
            if (Spritesize8x16_S)
            {
                offset = (tile_th & 1) != 0 ? 256 : 0;
                tile_th = (byte)(tile_th & 0xFE);
                if (line > 7) { tile_th_t = tile_th + offset + 1; line = line - 8; } else tile_th_t = tile_th + offset;
            }
            else
            {
                offset = SpPatternTableAddr_S >> 4;
                tile_th_t = tile_th + offset;
            }
            if ((sprite_attr & 0x80) != 0) { line_t = 7 - line; if (Spritesize8x16_S) tile_th_t ^= 1; }
            else line_t = line;

            int sp0High = (tile_th_t << 4) | (line_t + 8);
            int sp0Low  = (tile_th_t << 4) | line_t;
            byte tile_high = chrBankPtrs_S[sp0High >> 10][sp0High & 0x3FF];
            byte tile_low  = chrBankPtrs_S[sp0Low  >> 10][sp0Low  & 0x3FF];

            int scanOff = sl << 8;
            for (int px = 0; px < 8; px++)
            {
                int screenX = x_loc + px;
                if (screenX >= 255 || screenX < 0) continue;
                if (!ShowSprLeft8_S && screenX < 8) continue;
                if (!ShowBgLeft8_S && screenX < 8) continue;
                int loc_t = flip_x ? (7 - px) : px;
                int bit = 7 - loc_t;
                int sprPixel = ((tile_low >> bit) & 1) | (((tile_high >> bit) & 1) << 1);
                if (sprPixel != 0 && Buffer_BG_array_S[scanOff + screenX] != 0)
                {
                    isSprite0hit_S = true;
                    return;
                }
            }
        }

        // ----------------------------------------------------------------
        // end_scanline_S: called by tick_S() in MEM_S.cs when ppu_x_S wraps

        static void end_scanline_S()
        {
            bool rendering = ShowBackGround_S || ShowSprites_S;

            if (ppu_scanline_S >= 0 && ppu_scanline_S < 240)
            {
                if (rendering)
                    RenderScanline_S();
                else
                {
                    int scanOff = ppu_scanline_S << 8;
                    uint c = NesColors_S[ppu_ram_S[0x3F00] & 0x3F];
                    for (int i = 0; i < 256; i++) { ScreenBuf1x_S[scanOff + i] = c; Buffer_BG_array_S[scanOff + i] = 0; }
                }
                // Clock MMC3 scanline IRQ counter once per visible scanline when rendering is active
                // (approximates A12 rising-edge per-scanline clocking for Mapper 004)
                if (rendering) Mapper004Ref_S?.Mapper04step_IRQ_S(); // SP-12b: cached ref, no `as` cast
            }
            else if (ppu_scanline_S == 240)
            {
                RenderScreen_S();
            }
            else if (ppu_scanline_S == 241)
            {
                isVblank_S = true;
                if (NMIable_S) nmi_pending_S = true;
                frame_count_S++;
            }
            else if (ppu_scanline_S == 261)
            {
                isVblank_S = isSprite0hit_S = isSpriteOverflow_S = false;
                // Copy vert(t) to vert(v)
                if (rendering)
                    vram_addr_S = (vram_addr_S & ~0x7BE0) | (vram_addr_internal_S & 0x7BE0);
                // Odd frame skip
                oddSwap_S = !oddSwap_S;
                if (!oddSwap_S && rendering) ppu_x_S++; // skip one dot
            }

            ppu_scanline_S++;
            if (ppu_scanline_S >= 262) ppu_scanline_S = 0;
        }

        // ----------------------------------------------------------------
        // PPU register read/write functions

        static byte ppu_r_2002_S()
        {
            byte val = (byte)(((isVblank_S) ? 0x80 : 0) | ((isSprite0hit_S) ? 0x40 : 0) | ((isSpriteOverflow_S) ? 0x20 : 0) | (openbus_S & 0x1F));
            isVblank_S = false;
            nmi_pending_S = false;  // suppress NMI if $2002 read cancels VBL
            vram_latch_S = false;
            return openbus_S = val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte ppu_r_2004_S()
        {
            return openbus_S = spr_ram_S[spr_ram_add_S];
        }

        static byte ppu_r_2007_S()
        {
            int addr = vram_addr_S & 0x3FFF;
            byte result;
            if (addr < 0x3F00)
            {
                result = ppu_2007_buffer_S;
                ppu_2007_buffer_S = ppu_ram_S[addr];
            }
            else
            {
                ppu_2007_buffer_S = ppu_ram_S[addr & 0x3EFF];
                result = ppu_ram_S[addr];
            }
            vram_addr_S = (vram_addr_S + VramaddrIncrement_S) & 0x7FFF;
            return result;
        }

        static void ppu_w_2000_S(byte value)
        {
            openbus_S = value;
            vram_addr_internal_S = (vram_addr_internal_S & ~0x0C00) | ((value & 3) << 10);
            VramaddrIncrement_S = ((value & 4) > 0) ? 32 : 1;
            SpPatternTableAddr_S = ((value & 8) > 0) ? 0x1000 : 0;
            BgPatternTableAddr_S = ((value & 0x10) > 0) ? 0x1000 : 0;
            Spritesize8x16_S = ((value & 0x20) > 0);
            bool wasNMIable = NMIable_S;
            NMIable_S = ((value & 0x80) > 0);
            if (!wasNMIable && NMIable_S && isVblank_S) nmi_pending_S = true;
            if (wasNMIable && !NMIable_S) nmi_pending_S = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_w_2001_S(byte value)
        {
            openbus_S = value;
            ShowBgLeft8_S  = (value & 0x02) != 0;
            ShowSprLeft8_S = (value & 0x04) != 0;
            ShowBackGround_S = (value & 0x08) != 0;
            ShowSprites_S    = (value & 0x10) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_w_2003_S(byte value)
        {
            openbus_S = value;
            spr_ram_add_S = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_w_2004_S(byte value)
        {
            openbus_S = value;
            // During rendering, writes don't modify OAM; OAMADDR increments by 4
            if ((ppu_scanline_S < 240 || ppu_scanline_S == 261) && ppu_scanline_S >= 0 && (ShowBackGround_S || ShowSprites_S))
            {
                spr_ram_add_S = (byte)((spr_ram_add_S + 4) & 0xFC);
            }
            else
            {
                spr_ram_S[spr_ram_add_S++] = value;
            }
        }

        static void ppu_w_2005_S(byte value)
        {
            openbus_S = value;
            if (vram_latch_S)
            {
                // Second write: Y scroll
                vram_addr_internal_S = (vram_addr_internal_S & 0x0C1F) | ((value & 0x7) << 12) | ((value & 0xF8) << 2);
            }
            else
            {
                // First write: X scroll
                vram_addr_internal_S = (vram_addr_internal_S & 0x7FE0) | ((value & 0xF8) >> 3);
                FineX_S = value & 0x07;
            }
            vram_latch_S = !vram_latch_S;
        }

        static void ppu_w_2006_S(byte value)
        {
            openbus_S = value;
            if (!vram_latch_S)
            {
                // First write: high byte
                vram_addr_internal_S = (vram_addr_internal_S & 0x00FF) | ((value & 0x3F) << 8);
            }
            else
            {
                // Second write: low byte, copy t to v
                vram_addr_internal_S = (vram_addr_internal_S & 0x7F00) | value;
                vram_addr_S = vram_addr_internal_S;
            }
            vram_latch_S = !vram_latch_S;
        }

        static void ppu_w_2007_S(byte value)
        {
            int addr = vram_addr_S & 0x3FFF;
            if (addr >= 0x3F00)
            {
                // Palette
                addr &= 0x1F;
                if ((addr & 0x13) == 0x10) addr &= ~0x10; // mirror $3F10/$3F14/$3F18/$3F1C
                ppu_ram_S[0x3F00 | addr] = value;
            }
            else if (addr >= 0x2000)
            {
                // Nametable write with mirroring (matches old-core ppu_write_fun logic)
                int wrap  = addr & 0x2FFF; // $3000-$3EFF mirrors $2000-$2EFF
                int range = wrap & 0x0C00;
                int mirror = *Vertical_S;
                if (mirror >= 2)
                {
                    // One-screen: all 4 NTs map to same 1KB
                    int rel = wrap & 0x3FF;
                    ppu_ram_S[0x2000 + rel] = ppu_ram_S[0x2400 + rel] =
                    ppu_ram_S[0x2800 + rel] = ppu_ram_S[0x2C00 + rel] = value;
                }
                else if (mirror == 1) // Vertical mirroring ($2000=$2800, $2400=$2C00)
                {
                    if (range < 0x800) ppu_ram_S[wrap] = ppu_ram_S[wrap | 0x800]  = value;
                    else               ppu_ram_S[wrap] = ppu_ram_S[wrap & 0x37FF] = value;
                }
                else // Horizontal mirroring ($2000=$2400, $2800=$2C00)
                {
                    if      (range < 0x400) ppu_ram_S[wrap] = ppu_ram_S[wrap | 0x400]  = value;
                    else if (range < 0x800) ppu_ram_S[wrap] = ppu_ram_S[wrap & 0x3BFF] = value;
                    else if (range < 0xC00) ppu_ram_S[wrap] = ppu_ram_S[wrap | 0x400]  = value;
                    else                    ppu_ram_S[wrap] = ppu_ram_S[wrap & 0x3BFF] = value;
                }
            }
            else
            {
                ppu_ram_S[addr] = value;
            }
            vram_addr_S = (vram_addr_S + VramaddrIncrement_S) & 0x7FFF;
        }

        static void ppu_w_4014_S(byte value)
        {
            int oam_address = value << 8;
            for (int i = 0; i < 256; i++) spr_ram_S[spr_ram_add_S++] = Mem_r_S((ushort)(oam_address++));
            cpu_cycles_S += 513;
        }
    }
}
