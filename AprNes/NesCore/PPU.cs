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

        static int vram_addr_internal = 0, addr_range, vram_addr = 0, scrol_y = 0, FineX = 0;
        static bool vram_latch = false;
        static byte ppu_2007_buffer = 0, ppu_2007_temp = 0;
        static byte* spr_ram, ppu_ram;
        public static uint* ScreenBuf1x;
        static uint* NesColors; //, targetSize;
        static int* Buffer_BG_array;
        static byte spr_ram_add = 0;
        static bool NMI_set = false, IRQ_set = false;
        static Stopwatch StopWatch = new Stopwatch();
        static bool oddSwap = false;
        //https://wiki.nesdev.com/w/index.php/PPU_scrolling

        #region editing new way
        //打算把PPU的timing跟rendering整個做法重寫,尚未完成

        //Coarse X increment
        static void CXinc()
        {
            if ((vram_addr & 0x001F) == 31) // if coarse X == 31
            {
                vram_addr &= ~0x001F;// coarse X = 0
                vram_addr ^= 0x0400;// switch horizontal nametable
            }
            else
                vram_addr += 1;// increment coarse X
        }

        //Y increment
        static void Yinc()
        {
            if ((vram_addr & 0x7000) != 0x7000) // if fine Y < 7
                vram_addr += 0x1000;// increment fine Y
            else
            {
                vram_addr &= ~0x7000;// fine Y = 0
                int y = (vram_addr & 0x03E0) >> 5;// let y = coarse Y
                if (y == 29)
                {
                    y = 0; // coarse Y = 0
                    vram_addr ^= 0x0800;// switch vram_addr ertical nametable
                }
                else if (y == 31)
                    y = 0;// coarse Y = 0, nametable not switched
                else
                    y += 1;// increment coarse Y
                vram_addr = (vram_addr & ~0x03E0) | (y << 5); // put coarse Y back into vram_addr 
            }
        }
        static void getadd()
        {
            int tile_addr = 0x2000 | (vram_addr & 0x0FFF);
            int attr_addr = 0x23C0 | (vram_addr & 0x0C00) | ((vram_addr >> 4) & 0x38) | ((vram_addr >> 2) & 0x07);
        }
        //hori(v) = hori(t) update
        static void CopyHoriV()
        {
            vram_addr &= ~0x41f;
            vram_addr |= vram_addr_internal & 0x41f;
        }
        static int tileattr = 0, tilepat = 0, ioaddr = 0, pat_addr = 0;
        static uint bg_shift_pat = 0, bg_shift_attr = 0;

        static byte NTVal = 0;
        static byte ATVal = 0;
        static byte lowTile = 0;
        static byte highTile = 0;

        static void ppu_rendering_tick()
        {
            bool tile_decode_mode = ((0x10FFFF & (1u << (ppu_cycles_x / 16))) != 0) ? true : false;  // When x is 0..255, 320..335
            if (tile_decode_mode)
            {
                switch (ppu_cycles_x & 7)
                {
                    case 0: ioaddr = 0x2000 + (vram_addr & 0xFFF); break; //get NT address
                    case 1: NTVal = ppu_ram[ioaddr]; break; //get NT content
                    case 2: ioaddr = 0x23C0 | (vram_addr & 0xC00) | (vram_addr >> 4 & 0x38) | (vram_addr >> 2 & 0x7); break;//get AT address
                    case 3: ATVal = (byte)(ppu_ram[ioaddr] >> (((vram_addr >> 4 & 0x04) | (vram_addr & 0x02)))); break;//get AT content
                    case 4: ioaddr = BgPatternTableAddr | (NTVal << 4) | (vram_addr >> 12 & 7); break;//get low tile address
                    case 5: lowTile = MapperObj.MapperR_CHR(ioaddr); break;//get low tile content
                    case 6: ioaddr = BgPatternTableAddr | (NTVal << 4) | (vram_addr >> 12 & 7) | 8; break;//get high tile address
                    case 7: highTile = MapperObj.MapperR_CHR(ioaddr); break;//get high tile content
                }
            }
            else if (ppu_cycles_x < 320) { }
            //ignore unused NT fetches
        }
        static void ppu_rendering_pixel()
        {
        }
        static bool oddlatch = false;
        static int scanline_end = 341;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_step_new()
        {
            //for openbus value counter
            if (--open_bus_decay_timer == 0)
            {
                open_bus_decay_timer = 77777;
                openbus = 0;
            }
            if (scanline < 240 || scanline == 261)
            {
                if (ShowBackGround || ShowSprites) ppu_rendering_tick();
                if (scanline != 261 && ppu_cycles_x < 256) ppu_rendering_pixel();
            }
            ppu_cycles_x++;
            if (ppu_cycles_x == scanline_end)
            {
                ppu_cycles_x = 0;
                scanline_end = 341;
                if (++scanline == 262)
                {
                    scanline = 0;
                    if (oddlatch && ShowBackGround) scanline_end = 340;
                    oddlatch = !oddlatch;

                }
            }
        }
        #endregion

        static int open_bus_decay_timer = 77777;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_step()
        {
            if (--open_bus_decay_timer == 0)
            {
                open_bus_decay_timer = 77777;
                openbus = 0;
            }

            if (scanline < 240)
            {

                if (ppu_cycles_x == 254)
                {
                    if (ShowBackGround)
                        RenderBackGroundLine();
                    if (ShowSprites || ShowBackGround)
                        RenderSpritesLine();

                }
                else if (ppu_cycles_x == 256 && (ShowBackGround || ShowSprites)) UpdateVramRegister();
                else if (ppu_cycles_x == 260)
                {
                    // MMC3 IRQ: clock on visible scanlines (A12 rising edge during sprite fetches)
                    if ((ShowBackGround || ShowSprites) && mapper == 4)
                        (MapperObj as Mapper004).Mapper04step_IRQ();
                }
            }
            else if (scanline == 261 && ppu_cycles_x == 260)
            {
                // MMC3 IRQ: also clock on pre-render scanline 261 (hardware clocks here too)
                if ((ShowBackGround || ShowSprites) && mapper == 4)
                    (MapperObj as Mapper004).Mapper04step_IRQ();
            }
            else if (scanline == 240 && ppu_cycles_x == 1)
            {
                RenderScreen();
                if (LimitFPS) while (StopWatch.Elapsed.TotalSeconds < 0.01666) Thread.Sleep(1);//0.0167
                frame_count++;
                StopWatch.Restart();
            }
            ++ppu_cycles_x;

            if (scanline == 241 && ppu_cycles_x == 1)
            {
                if (!SuppressVbl)
                {
                    isVblank = true;
                }
                if (NMIable) NMIInterrupt();
            }

            if (scanline == 261 && ppu_cycles_x == 338)
            {
                oddSwap = !oddSwap;
                if (!oddSwap & ShowBackGround) ++ppu_cycles_x;
            }
            if (ppu_cycles_x == 341)
            {
                if (++scanline == 262) scanline = 0;
                ppu_cycles_x = 0;
            }

            if (scanline == 261)
            {
                if (ppu_cycles_x == 1) isVblank = isSprite0hit = isSpriteOverflow = false;
                else if (ppu_cycles_x == 304 && (ShowBackGround || ShowSprites)) vram_addr = vram_addr_internal;
            }

        }

        static bool NMIing = false;

        static ushort attrAddr, tileAddr, lowshift, highshift;
        static int current, pixel, vram_addr_limite, attr, attrbuf, array_loc;

        static int GetAttr()
        {

            vram_addr_limite = vram_addr & 0x3FF;
            attrAddr = (ushort)(0x23C0 | (vram_addr & 0xC00) | (((vram_addr_limite >> 2) & 0x07) | (((vram_addr_limite >> 4) & 0x38) | 0x3C0)));
            tileAddr = (ushort)((vram_addr & 0xc00) | 0x2000 | vram_addr_limite);
            array_loc = (ppu_ram[tileAddr] << 4) + BgPatternTableAddr + ((vram_addr >> 12) & 7);
            lowshift = (ushort)((lowshift << 8) | MapperObj.MapperR_CHR(array_loc));
            highshift = (ushort)((highshift << 8) | MapperObj.MapperR_CHR(array_loc + 8));
            if ((vram_addr & 0x1F) == 0x1F) vram_addr ^= 0x41F; else vram_addr++;
            return ((ppu_ram[attrAddr] >> (((vram_addr_limite >> 4) & 0x04) | (vram_addr_limite & 0x02))) & 0x03);
        }

        static void RenderBackGroundLine()
        {
            attr = GetAttr();
            attrbuf = GetAttr();
            for (int x = 0; x < 32; x++)
            {
                for (int loc = 0; loc < 8; loc++)
                {
                    current = 15 - loc - FineX;
                    array_loc = (scanline << 8) + ((x << 3) | loc);
                    int bgPixel = ((lowshift >> current) & 1) | (((highshift >> current) & 1) << 1);
                    // left 8 pixels clip: treat as transparent for BG when ShowBgLeft8 is off
                    bool inLeft8 = ((x << 3) | loc) < 8;
                    Buffer_BG_array[array_loc] = (!ShowBgLeft8 && inLeft8) ? 0 : bgPixel;
                    pixel = bgPixel;

                    if (!ShowBgLeft8 && inLeft8)
                        ScreenBuf1x[array_loc] = NesColors[ppu_ram[0x3f00] & 0x3f]; // universal BG color
                    else if (current >= 8)
                        ScreenBuf1x[array_loc] = NesColors[ppu_ram[((pixel == 0) ? 0x3f00 : 0x3f00 | (attr << 2)) | pixel] & 0x3f];
                    else
                        ScreenBuf1x[array_loc] = NesColors[ppu_ram[((pixel == 0) ? 0x3f00 : 0x3f00 | (attrbuf << 2)) | pixel] & 0x3f];
                }
                attr = attrbuf;
                attrbuf = GetAttr();
            }

            GetAttr();
        }

        static void RenderSpritesLine()
        {
            // Pass 1: scan OAM 0→63, pick first 8 sprites visible on this scanline.
            // NES secondary OAM evaluation: hardware only renders the first 8 per scanline.
            int* sel = stackalloc int[8];
            int selCount = 0, spriteCount = 0;
            int height = Spritesize8x16 ? 15 : 7;

            for (int oam_th = 0; oam_th < 64; oam_th++)
            {
                int raw_y = spr_ram[oam_th << 2];
                // OAM Y = display_top - 1, so display range is [raw_y+1, raw_y+1+height]
                // i.e., scanline > raw_y && scanline - raw_y <= height + 1
                if (scanline <= raw_y || scanline - raw_y > height + 1) continue;
                if (++spriteCount == 9) isSpriteOverflow = true;
                if (selCount < 8) sel[selCount++] = oam_th;
            }

            if (!ShowSprites) return;

            // Pass 2: render in reverse OAM order so lower index (higher priority) wins.
            for (int si = selCount - 1; si >= 0; si--)
            {
                int oam_th = sel[si];
                int oam_addr = oam_th << 2;
                int y_loc = spr_ram[oam_addr] + 1; // display top row (OAM Y is top-1)

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
                    if (Spritesize8x16) tile_th_t ^= 1; // 8x16 vflip: swap top/bottom tile
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
                    array_loc = (scanline << 8) + screenX;
                    if (oam_th == 0 && !isSprite0hit && pixel != 0 && Buffer_BG_array[array_loc] != 0 && screenX != 255 && ShowBackGround)
                        isSprite0hit = true;
                    if (pixel != 0 && (!ShowBackGround || Buffer_BG_array[array_loc] == 0 || !priority))
                        ScreenBuf1x[array_loc] = NesColors[ppu_ram[0x3f10 + ((sprite_attr & 3) << 2) | pixel] & 0x3f];
                }
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

        static void UpdateVramRegister()
        {
            if ((vram_addr & 0x1F) == 0x1F) vram_addr ^= 0x41F; else vram_addr++;
            if (ShowBackGround || ShowSprites)
            {
                if ((vram_addr & 0x7000) == 0x7000)
                {
                    vram_addr ^= 0x7000;
                    switch (vram_addr & 0x3E0)
                    {
                        case 0x3A0: vram_addr ^= 0xBA0; break;
                        case 0x3E0: vram_addr ^= 0x3E0; break;
                        default: vram_addr += 0x20; break;
                    }
                }
                else
                    vram_addr += 0x1000;
                vram_addr = (vram_addr & 0x7BE0) | (vram_addr_internal & 0x41F);
            }
        }

        static bool SuppressNmi = false, SuppressVbl = false;
        //ref http://wiki.nesdev.com/w/index.php/PPU_scrolling
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte ppu_r_2002() //ok
        {


            openbus = (byte)(((isVblank) ? 0x80 : 0) | ((isSprite0hit) ? 0x40 : 0) | ((isSpriteOverflow) ? 0x20 : 0) | (openbus & 0x1f));

            if (ppu_cycles_x == 1 && scanline == 240)
            {
                SuppressNmi = true;
                SuppressVbl = true;
            }
            else
            {
                SuppressNmi = false;
                SuppressVbl = false;
                isVblank = false;
                interrupt_vector = 0xfffe;
            }

            vram_latch = false;
            return openbus;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte ppu_r_2007()
        {

            return ppu_read_fun[vram_addr](vram_addr);

            //old way 暫時保留參考
            /*
             int vram_addr_wrap = 0;
             if ((vram_addr & 0x3F00) == 0x3F00)
             {
                 ppu_2007_temp = ppu_ram[vram_addr & ((vram_addr & 0x03) == 0 ? 0x0C : 0x1F) + 0x3f00];
                 vram_addr_wrap = vram_addr & 0x2FFF;
                 if (vram_addr_wrap < 0x2000) ppu_2007_buffer = MapperObj.MapperR_CHR(vram_addr_wrap);
                 else ppu_2007_buffer = ppu_ram[vram_addr_wrap];
             }
             else
             {
                 ppu_2007_temp = ppu_2007_buffer; //need read from buffer
                 vram_addr_wrap = vram_addr & 0x3FFF;
                 if (vram_addr_wrap < 0x2000) ppu_2007_buffer = MapperObj.MapperR_CHR(vram_addr_wrap);//Pattern Table 
                 else if (vram_addr_wrap < 0x3F00) ppu_2007_buffer = ppu_ram[vram_addr_wrap]; //Name Table & Attribute Table
                 else ppu_2007_buffer = ppu_ram[vram_addr_wrap & ((vram_addr_wrap & 0x03) == 0 ? 0x0C : 0x1F) + 0x3f00]; // //Sprite Palette & Image Palette   
             }
             vram_addr = (ushort)((vram_addr + VramaddrIncrement) & 0x7FFF);
             return openbus = ppu_2007_temp;*/
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
            NMIable = ((value & 0x80) > 0) ? true : false;
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

        static byte ppu_r_2004() //ok
        {
            if ((spr_ram_add + 3) % 3 == 0) open_bus_decay_timer = 77777;//fixed add
            return openbus = spr_ram[spr_ram_add] &= 0xE3; //fixed 
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

            //old way 暫時保留參考
            /*int vram_addr_wrap = 0;
            openbus = value;
            vram_addr_wrap = vram_addr & 0x3FFF;
            if (vram_addr_wrap < 0x2000)
            {
                if (CHR_ROM_count == 0) ppu_ram[vram_addr_wrap] = value;
            }
            else if (vram_addr_wrap < 0x3f00) //Name Table & Attribute Table
            {
                addr_range = vram_addr_wrap & 0xc00;
                if (ScreenSpecial)
                {
                    if (ScreenFour) ppu_ram[vram_addr_wrap] = value;
                    else if (ScreenSingle)
                    {
                        ppu_ram[0x2000 | (vram_addr_wrap & 0x3ff)] = ppu_ram[0x2400 | (vram_addr_wrap & 0x3ff)] = ppu_ram[0x2800 | (vram_addr_wrap & 0x3ff)] = ppu_ram[0x2c00 | (vram_addr_wrap & 0x3ff)] = value;
                    }
                }
                else
                {
                    if (*Vertical != 0)
                    {
                        if (addr_range < 0x800) ppu_ram[vram_addr_wrap] = ppu_ram[vram_addr_wrap | 0x800] = value;
                        else ppu_ram[vram_addr_wrap] = ppu_ram[vram_addr_wrap & 0x37ff] = value;
                    }
                    else
                    {
                        if (addr_range < 0x400) ppu_ram[vram_addr_wrap] = ppu_ram[vram_addr_wrap | 0x400] = value;
                        else if (addr_range < 0x800) ppu_ram[vram_addr_wrap] = ppu_ram[vram_addr_wrap & 0x3bff] = value;
                        else if (addr_range < 0xc00) ppu_ram[vram_addr_wrap] = ppu_ram[vram_addr_wrap | 0x400] = value;
                        else ppu_ram[vram_addr_wrap] = ppu_ram[vram_addr_wrap & 0x3bff] = value;
                    }
                }
            }
            else ppu_ram[(vram_addr_wrap & ((vram_addr_wrap & 0x03) == 0 ? 0x0C : 0x1F)) + 0x3f00] = value; //Sprite Palette & Image Palette
            vram_addr = (ushort)((vram_addr + VramaddrIncrement) & 0x7FFF);*/
        }

        static void ppu_w_4014(byte value)//DMA , fixex 2017.01.16 pass sprite_ram test
        {
            int oam_address = value << 8;
            for (int i = 0; i < 256; i++) spr_ram[spr_ram_add++] = NES_MEM[oam_address++];
            cpu_cycles += 512;
        }
    }
}
