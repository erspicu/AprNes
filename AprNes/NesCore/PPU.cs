using System;
using System.Diagnostics;
using System.Threading;
using System.Runtime.CompilerServices;

namespace AprNes
{

    //把system與UI有關的顯示處理切割出去到NES Core外層

    unsafe static public partial class NesCore
    {
        static public int frame_count = 0;
        static public bool LimitFPS = true;
        static public int ppu_cycles_x = 0, scanline = -1; // 241;

        //Palette ref http://www.thealmightyguru.com/Games/Hacking/Wiki/index.php?title=NES_Palette & http://www.dev.bowdenweb.com/nes/nes-color-palette.html
        static readonly uint[] NesColorsData =  {
            0xFF7C7C7C,0xFF0000FC,0xFF0000BC,0xFF4428BC,0xFF940084,0xFFA80020,0xFFA81000,0xFF881400,
            0xFF503000,0xFF007800,0xFF006800,0xFF005800,0xFF004058,0xFF000000,0xFF000000,0xFF000000,
            0xFFBCBCBC,0xFF0078F8,0xFF0058F8,0xFF6844FC,0xFFD800CC,0xFFE40058,0xFFF83800,0xFFE45C10,
            0xFFAC7C00,0xFF00B800,0xFF00A800,0xFF00A844,0xFF008888,0xFF000000,0xFF000000,0xFF000000,
            0xFFF8F8F8,0xFF3CBCFC,0xFF6888FC,0xFF9878F8,0xFFF878F8,0xFFF85898,0xFFF87858,0xFFFCA044,
            0xFFF8B800,0xFFB8F818,0xFF58D854,0xFF58F898,0xFF00E8D8,0xFF787878,0xFF000000,0xFF000000,
            0xFFFCFCFC,0xFFA4E4FC,0xFFB8B8F8,0xFFD8B8F8,0xFFF8B8F8,0xFFF8A4C0,0xFFF0D0B0,0xFFFCE0A8,
            0xFFF8D878,0xFFD8F878,0xFFB8F8B8,0xFFB8F8D8,0xFF00FCFC,0xFFF8D8F8,0xFF000000,0xFF000000 };

        //table form blargg_ppu  power_up_palette.asm test rom source
        static readonly byte[] defaultPal = { 0x09, 0x01, 0x00, 0x01, 0x00, 0x02, 0x02, 0x0D, 0x08, 0x10, 0x08, 0x24, 0x00, 0x00, 0x04, 0x2C, 0x09, 0x01, 0x34, 0x03, 0x00, 0x04, 0x00, 0x14, 0x08, 0x3A, 0x00, 0x02, 0x00, 0x20, 0x2C, 0x08 };

        //ppu ctrl 0x2000
        static int BaseNameTableAddr = 0, VramaddrIncrement = 1, SpPatternTableAddr = 0, BgPatternTableAddr = 0;
        static bool Spritesize8x16 = false, NMIable = false;

        //ppu mask 0x2001
        public static bool ShowBackGround = false, ShowSprites = false;
        static bool ShowBgLeft8 = true, ShowSprLeft8 = true; // bit1/bit2: show in leftmost 8 pixels

        //ppu status 0x2002.
        static bool isSpriteOverflow = false, isSprite0hit = false, isVblank = false;

        static int vram_addr_internal = 0, vram_addr = 0, scrol_y = 0, FineX = 0;
        static bool vram_latch = false;
        static byte ppu_2007_buffer = 0, ppu_2007_temp = 0;
        static byte* spr_ram, ppu_ram;
        static public uint* ScreenBuf1x;
        static uint* NesColors; //, targetSize;
        static int* Buffer_BG_array;
        static byte spr_ram_add = 0;

        static Stopwatch StopWatch = new Stopwatch();
        static bool oddSwap = false;
        //https://wiki.nesdev.com/w/index.php/PPU_scrolling

        #region cycle-accurate PPU

        // Coarse X increment
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

        // hori(v) = hori(t)
        static void CopyHoriV()
        {
            vram_addr = (vram_addr & ~0x041F) | (vram_addr_internal & 0x041F);
        }

        // ---- Tile fetch state ----
        static byte NTVal = 0, ATVal = 0, lowTile = 0, highTile = 0;
        static int ioaddr = 0;

        // ---- BG shift registers (16-bit, two tiles: high=current, low=next) ----
        static ushort lowshift = 0, highshift = 0;

        // ---- Attribute 3-stage pipeline ----
        // Phase-3 shifts ATVal into p1; phase-7 render reads p3 (2 groups later).
        // This correctly delays attribute by 2 fetch groups with no index drift.
        static byte bg_attr_p1 = 0, bg_attr_p2 = 0, bg_attr_p3 = 0;

        // Render 8 BG pixels at screen positions [ppu_cycles_x-7 .. ppu_cycles_x]
        // using shift registers BEFORE reload (high byte = current tile data).
        static void RenderBGTile()
        {
            byte renderAttr = bg_attr_p3;
            byte nextAttr   = bg_attr_p2;

            int baseX = ppu_cycles_x - 7;
            int scanOff = scanline << 8;
            for (int loc = 0; loc < 8; loc++)
            {
                int screenX = baseX + loc;
                if (screenX > 255) break;

                bool inLeft8 = screenX < 8;
                int bit = 15 - loc - FineX;           // 1..15 always (FineX 0..7, loc 0..7)
                byte attrUse = (bit >= 8) ? renderAttr : nextAttr;
                int bgPixel = ((lowshift >> bit) & 1) | (((highshift >> bit) & 1) << 1);

                int slot = scanOff + screenX;
                Buffer_BG_array[slot] = (!ShowBgLeft8 && inLeft8) ? 0 : bgPixel;

                if (!ShowBgLeft8 && inLeft8)
                    ScreenBuf1x[slot] = NesColors[ppu_ram[0x3f00] & 0x3f];
                else if (bgPixel == 0)
                    ScreenBuf1x[slot] = NesColors[ppu_ram[0x3f00] & 0x3f];
                else
                    ScreenBuf1x[slot] = NesColors[ppu_ram[(0x3f00 | (attrUse << 2)) + bgPixel] & 0x3f];

                // Sprite 0 hit detection (per-pixel, cycle-accurate)
                if (sprite0_on_line && !isSprite0hit && screenX != 255)
                {
                    int sprCol = screenX - sprite0_line_x;
                    if (sprCol >= 0 && sprCol < 8 && bgPixel != 0
                        && !(!ShowBgLeft8 && inLeft8) && !(!ShowSprLeft8 && inLeft8))
                    {
                        int loc_t = sprite0_flip_x ? (7 - sprCol) : sprCol;
                        int mask = 1 << (7 - loc_t);
                        int sprPixel = (((sprite0_tile_high & mask) << 1) + (sprite0_tile_low & mask)) >> (7 - loc_t);
                        if (sprPixel != 0)
                            isSprite0hit = true;
                    }
                }
            }
        }

        // Per-8-cycle tile fetch: runs each PPU cycle on visible/pre-render scanlines when rendering enabled.
        // BG tiles fetched at cycles 0-255 (visible) and 320-335 (next-scanline prefetch).
        // A12 transitions detected at CHR address setup cycles (phase 4 and 6).
        static void ppu_rendering_tick()
        {
            if (ppu_cycles_x < 256 || (ppu_cycles_x >= 320 && ppu_cycles_x < 336))
            {
                switch (ppu_cycles_x & 7)
                {
                    case 0:
                        ioaddr = 0x2000 | (vram_addr & 0x0FFF);
                        break;
                    case 1:
                        NTVal = ppu_ram[ioaddr];
                        break;
                    case 2:
                        ioaddr = 0x23C0 | (vram_addr & 0x0C00) | ((vram_addr >> 4) & 0x38) | ((vram_addr >> 2) & 0x07);
                        break;
                    case 3:
                        ATVal = (byte)((ppu_ram[ioaddr] >> (((vram_addr >> 4) & 0x04) | (vram_addr & 0x02))) & 0x03);
                        bg_attr_p3 = bg_attr_p2; bg_attr_p2 = bg_attr_p1; bg_attr_p1 = ATVal;
                        break;
                    case 4:
                        ioaddr = BgPatternTableAddr | (NTVal << 4) | ((vram_addr >> 12) & 7);
                        break;
                    case 5:
                        lowTile = MapperObj.MapperR_CHR(ioaddr);
                        break;
                    case 6:
                        ioaddr = BgPatternTableAddr | (NTVal << 4) | ((vram_addr >> 12) & 7) | 8;
                        break;
                    case 7:
                        highTile = MapperObj.MapperR_CHR(ioaddr);
                        // Render 8 pixels using shift registers BEFORE reload (visible only, BG on)
                        if (scanline < 240 && ppu_cycles_x < 256 && ShowBackGround)
                            RenderBGTile();
                        // Load shift registers (high = old-low = previous tile, low = new tile)
                        lowshift  = (ushort)((lowshift  << 8) | lowTile);
                        highshift = (ushort)((highshift << 8) | highTile);
                        CXinc();
                        break;
                }
            }
            else if (ppu_cycles_x == 256)
            {
                Yinc();
            }
            else if (ppu_cycles_x == 257)
            {
                CopyHoriV();
            }

            // Pre-render scanline: continuous vert(v) = vert(t) copy at cycles 280-304
            if (scanline == 261 && ppu_cycles_x >= 280 && ppu_cycles_x <= 304)
                vram_addr = (vram_addr & ~0x7BE0) | (vram_addr_internal & 0x7BE0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_step_new()
        {
            // Open bus decay
            if (--open_bus_decay_timer == 0)
            {
                open_bus_decay_timer = 77777;
                openbus = 0;
            }

            bool renderingEnabled = ShowBackGround || ShowSprites;

            if (scanline < 240 || scanline == 261)
            {
                if (renderingEnabled)
                    ppu_rendering_tick();

                if (scanline >= 0 && scanline < 240)
                {
                    // At start of each visible scanline: always zero Buffer_BG_array to prevent
                    // stale data from prior frames causing incorrect sprite priority decisions.
                    if (ppu_cycles_x == 0)
                    {
                        int scanOff = scanline << 8;
                        for (int i = 0; i < 256; i++)
                            Buffer_BG_array[scanOff + i] = 0;
                        if (!ShowBackGround)
                        {
                            uint bgColor = NesColors[ppu_ram[0x3f00] & 0x3f];
                            for (int i = 0; i < 256; i++)
                                ScreenBuf1x[scanOff + i] = bgColor;
                        }
                        PrecomputeSprite0Line();
                    }

                    // Sprite evaluation + rendering at cycle 257 (after BG tiles complete at cycle 255)
                    if (ppu_cycles_x == 257)
                        RenderSpritesLine();

                    // MMC3 IRQ: clock on visible scanlines at cycle 260 (A12 rising edge, sprite fetch region)
                    if (ppu_cycles_x == 260 && renderingEnabled && mapper == 4)
                        (MapperObj as Mapper004).Mapper04step_IRQ();
                }
            }

            // MMC3 IRQ: also clock on pre-render scanline 261 at cycle 260
            if (scanline == 261 && ppu_cycles_x == 260 && renderingEnabled && mapper == 4)
                (MapperObj as Mapper004).Mapper04step_IRQ();

            // Screen output at scanline 240 cycle 1 (matches ppu_step timing)
            if (scanline == 240 && ppu_cycles_x == 1)
            {
                RenderScreen();
                if (LimitFPS) while (StopWatch.Elapsed.TotalSeconds < 0.01666) Thread.Sleep(1);
                frame_count++;
                StopWatch.Restart();
            }

            // Advance cycle counter
            ppu_cycles_x++;

            // VBlank start at scanline 241, cycle 1 (post-increment)
            if (scanline == 241 && ppu_cycles_x == 1)
            {
                if (!SuppressVbl)
                {
                    isVblank = true;
                    if (NMIable) nmi_pending = true;
                    if (NMIable) dbgWrite("VBL_SET: sl=241 cx=1 NMIable=True nmi_pending=" + nmi_pending);
                }
                else if (NMIable)
                    dbgWrite("VBL_SUPPRESSED: sl=241 cx=1");
                SuppressVbl = false;
            }

            // Pre-render: clear PPU status flags at cycle 1 (post-increment)
            if (scanline == 261 && ppu_cycles_x == 1)
                isVblank = isSprite0hit = isSpriteOverflow = false;

            // Odd frame skip: on odd frames with rendering enabled, skip last idle cycle of pre-render
            if (scanline == 261 && ppu_cycles_x == 339)
            {
                oddSwap = !oddSwap;
                if (!oddSwap && (ShowBackGround || ShowSprites)) ppu_cycles_x++;
            }

            // Advance scanline
            if (ppu_cycles_x == 341)
            {
                if (++scanline == 262) scanline = 0;
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

        // Pre-compute sprite 0 tile data for the current scanline so hit detection
        // can happen per-pixel inside RenderBGTile() at the correct PPU cycle.
        static void PrecomputeSprite0Line()
        {
            sprite0_on_line = false;
            if (isSprite0hit) return;
            if (!ShowBackGround || !ShowSprites) return;

            int raw_y = spr_ram[0];
            int height = Spritesize8x16 ? 15 : 7;
            if (scanline < raw_y || scanline - raw_y > height) return;

            sprite0_on_line = true;
            sprite0_line_x = spr_ram[3];

            byte sprite_attr = spr_ram[2];
            sprite0_flip_x = (sprite_attr & 0x40) != 0;

            int y_loc = raw_y;
            int offset, tile_th_t, line, line_t;
            byte tile_th;

            if (Spritesize8x16)
            {
                byte byte0 = spr_ram[1];
                tile_th = (byte)(byte0 & 0xfe);
                offset = (byte0 & 1) != 0 ? 256 : 0;
            }
            else
            {
                tile_th = spr_ram[1];
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

            if ((sprite_attr & 0x80) != 0)
            {
                line_t = 7 - line;
                if (Spritesize8x16) tile_th_t ^= 1;
            }
            else line_t = line;

            sprite0_tile_high = MapperObj.MapperR_CHR((tile_th_t << 4) | (line_t + 8));
            sprite0_tile_low = MapperObj.MapperR_CHR((tile_th_t << 4) | line_t);
        }

        static int pixel, array_loc;

        static void RenderSpritesLine()
        {
            // Pass 1: scan OAM 0→63, pick first 8 sprites visible on this scanline.
            // NES hardware only performs sprite evaluation when rendering is enabled.
            int* sel = stackalloc int[8];
            int selCount = 0, spriteCount = 0;
            int height = Spritesize8x16 ? 15 : 7;
            bool renderingEnabled = ShowBackGround || ShowSprites;

            if (renderingEnabled)
            {
                for (int oam_th = 0; oam_th < 64; oam_th++)
                {
                    int raw_y = spr_ram[oam_th << 2];
                    if (scanline < raw_y || scanline - raw_y > height) continue;
                    if (++spriteCount == 9) isSpriteOverflow = true;
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
            for (int i = 0; i < 256; i++) sprSet[i] = 0;

            // Pass 2: evaluate sprites in reverse OAM order so lower-index sprites overwrite higher,
            // making the lowest-index sprite the final winner at each pixel.
            for (int si = selCount - 1; si >= 0; si--)
            {
                int oam_th = sel[si];
                int oam_addr = oam_th << 2;
                int y_loc = spr_ram[oam_addr];

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

                byte tile_hbyte = MapperObj.MapperR_CHR((tile_th_t << 4) | (line_t + 8));
                byte tile_lbyte = MapperObj.MapperR_CHR((tile_th_t << 4) | line_t);
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
                    sprColor[screenX]    = NesColors[ppu_ram[0x3f10 + ((sprite_attr & 3) << 2) | pixel] & 0x3f];
                }
            }

            // Pass 3: composite — draw winning sprite pixel only if:
            //   BG is disabled, OR BG pixel is transparent, OR winning sprite is front-priority.
            // A behind-BG winner (priority=1) with opaque BG blocks ALL sprites at that pixel,
            // correctly implementing the mask-sprite trick used by SMB3.
            int scanOff = scanline << 8;
            for (int screenX = 0; screenX < 256; screenX++)
            {
                if (sprSet[screenX] == 0) continue;
                array_loc = scanOff + screenX;
                if (!ShowBackGround || Buffer_BG_array[array_loc] == 0 || sprPriority[screenX] == 0)
                    ScreenBuf1x[array_loc] = sprColor[screenX];
            }
        }

        static public bool screen_lock = false;
        static void RenderScreen()
        {
            screen_lock = true;
            VideoOutput?.Invoke(null, null);
            screen_lock = false;
            _event.WaitOne();
        }

        static bool SuppressVbl = false;
        //ref http://wiki.nesdev.com/w/index.php/PPU_scrolling
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte ppu_r_2002() //ok
        {
            // VBL look-ahead: check if VBL will start during this instruction's PPU cycles.
            // Without this, $2002 reads are "stale" (see PPU state from before this instruction),
            // causing sync_vbl to converge incorrectly. This lightweight check avoids
            // running full ppu_step_new (which would trigger mapper IRQs mid-instruction).
            // Use cpu_cycles*3: scan ALL PPU clocks of the instruction. On real NES, the
            // $2002 read happens on the LAST CPU cycle (e.g. cycle 4 of BIT abs), so VBL
            // events anywhere in the instruction are visible to the read. The first BIT in
            // sync_vbl's fine loop must "own" all VBL events within its execution range.
            bool lookahead_hit = false;
            if (!isVblank && cpu_cycles > 1)
            {
                int ppu_remaining = cpu_cycles * 3;
                int cx = ppu_cycles_x;
                int sl = scanline;
                for (int i = 0; i < ppu_remaining; i++)
                {
                    cx++;
                    if (cx == 341) { if (++sl == 262) sl = 0; cx = 0; }
                    if (sl == 241 && cx == 1)
                    {
                        lookahead_hit = true;
                        dbgWrite("LOOKAHEAD: VBL predicted at ppu=(" + scanline + "," + ppu_cycles_x + ") cpu_cycles=" + cpu_cycles + " SuppressVbl=" + SuppressVbl + " NMIable=" + NMIable);
                        if (!SuppressVbl)
                        {
                            isVblank = true;
                            if (NMIable) nmi_pending = true;
                        }
                        break;
                    }
                }
            }

            openbus = (byte)(((isVblank) ? 0x80 : 0) | ((isSprite0hit) ? 0x40 : 0) | ((isSpriteOverflow) ? 0x20 : 0) | (openbus & 0x1f));
            // Log $2002 reads only when VBL detected (reduce sync_vbl noise)
            if (lookahead_hit || (isVblank && scanline == 241))
                dbgWrite("R2002: sl=" + scanline + " cx=" + ppu_cycles_x + " val=$" + openbus.ToString("X2") + " isVbl=" + isVblank + " lookahead=" + lookahead_hit + " PC=$" + r_PC.ToString("X4"));

            if (ppu_cycles_x == 1 && scanline == 241)
            {
                SuppressVbl = true;
            }
            else if (lookahead_hit)
            {
                // Look-ahead predicted VBL at (241,1) which is coming during this
                // instruction's PPU catch-up. The read already "consumed" VBL (returned $80).
                // Set SuppressVbl so ppu_step_new doesn't re-set isVblank when it reaches (241,1).
                SuppressVbl = true;
                isVblank = false;
            }
            else
            {
                SuppressVbl = false;
                isVblank = false;
            }

            vram_latch = false;
            return openbus;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte ppu_r_2007()
        {
            return ppu_read_fun[vram_addr](vram_addr);
        }

        static byte openbus;

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
            // Rising edge: enabling NMI while VBL flag is already set fires NMI immediately
            if (!wasNMIable && NMIable && isVblank) nmi_pending = true;
            if (wasNMIable != NMIable)
                dbgWrite("W2000_NMI: " + (NMIable ? "ON" : "OFF") + " sl=" + scanline + " cx=" + ppu_cycles_x + " isVbl=" + isVblank + " PC=$" + r_PC.ToString("X4"));
        }

        static void ppu_w_2001(byte value) //ok
        {
            openbus = value;

            ShowBgLeft8  = (value & 0x02) != 0; // bit1: show BG in leftmost 8 pixels
            ShowSprLeft8 = (value & 0x04) != 0; // bit2: show sprites in leftmost 8 pixels
            ShowBackGround = (value & 0x08) != 0;
            ShowSprites    = (value & 0x10) != 0;
        }

        static void ppu_w_2003(byte value) //ok
        {
            openbus = value;
            spr_ram_add = value;
        }

        static void ppu_w_2004(byte value) //ok
        {
            openbus = value;
            spr_ram[spr_ram_add++] = value;
        }

        static byte ppu_r_2004()
        {
            byte val = spr_ram[spr_ram_add];
            if ((spr_ram_add & 3) == 2) val &= 0xE3; // mask unimplemented bits of attribute byte only
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
            int oam_address = value << 8;
            for (int i = 0; i < 256; i++) spr_ram[spr_ram_add++] = NES_MEM[oam_address++];
            // OAM DMA: 1 dummy cycle (halt) + 256 × 2 (read/write) = 513 cycles
            // On real NES, odd-cycle start adds 1 more (514), but 513 is the base.
            cpu_cycles += 513;
        }
    }
}
