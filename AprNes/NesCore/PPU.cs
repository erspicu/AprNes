using System;
using XBRz_speed;
using ScalexFilter;
using NativeWIN32API;
using System.Diagnostics;
using System.Threading;
using System.Drawing;
using System.Drawing.Imaging;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        public int frame_count = 0, ScreenSize = 1;
        public bool LimitFPS = true;
        int ppu_cycles = 0, scanline = 0; // 241;

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

        //ppu ctrl 0x2000
        int BaseNameTableAddr = 0, VramaddrIncrement = 1, SpPatternTableAddr = 0, BgPatternTableAddr = 0;
        bool Spritesize8x16 = false, NMIable = false;

        //ppu mask 0x2001
        bool ShowBackGround = false, ShowSprites = false;

        //ppu status 0x2002.
        bool isSpriteOverflow = false, isSprite0hit = false, isVblank = false;

        int vram_addr_internal = 0, vram_addr_tmp = 0, addr_range, vram_addr = 0, scrol_y = 0, FineX = 0;
        bool vram_latch = false;
        byte spr_ram_add = 0, ppu_2007_buffer = 0, ppu_2007_temp = 0;
        byte* spr_ram, ppu_ram;
        uint* ScreenBuf1x, ScreenBuf2x, ScreenBuf3x, ScreenBuf4x, ScreenBuf5x, ScreenBuf6x, ScreenBuf8x, ScreenBuf9x, NesColors; //, targetSize;
        int* Buffer_BG_array;

      
        bool NMI_set = false, IRQ_set = false;

        Stopwatch StopWatch = new Stopwatch();

        bool oddSwap = false;
        void ppu_step()
        {
           // debug();

          //  StepsLog.WriteLine("1:"+ scanline+ " "+ vram_addr.ToString("x4") + " " + vram_addr_internal.ToString("x4"));

            if (scanline < 240)
            {
                if (ppu_cycles == 254)
                {
                    if (ShowBackGround) RenderBackGroundLine();
                    if (ShowSprites) RenderSpritesLine();
                }
                else if (ppu_cycles == 256 && ShowBackGround) UpdateVramRegister();
                else if (ppu_cycles == 260)
                {
                    if (SpPatternTableAddr == 0x1000 && BgPatternTableAddr == 0) mapper04step_IRQ();
                }
            }
            else if (scanline == 240 && ppu_cycles == 1)
            {
                //if (!SuppressVbl) isVblank = true;
                //if (NMIable && !SuppressNmi) NMI_set = true;
                RenderScreen();
                if (LimitFPS) while (StopWatch.Elapsed.TotalSeconds < 0.01666) Thread.Sleep(1);//0.0167
                frame_count++;
                StopWatch.Restart();
            }
            else if (scanline == 261)
            {
                if (ppu_cycles == 1) isVblank = isSprite0hit = isSpriteOverflow = false;
                else if (ppu_cycles == 304 && (ShowBackGround || ShowSprites)) vram_addr = vram_addr_internal;
            }

            ++ppu_cycles;


            
            if (scanline == 241 && ppu_cycles == 1)
            {
                if (!SuppressVbl) isVblank = true;
                if (NMIable && !SuppressNmi) NMIInterrupt(); //NMI_set = true;
            }


            if (scanline == 261 && ppu_cycles == 338)
            {
                oddSwap = !oddSwap;
                if (!oddSwap & ShowBackGround) ++ppu_cycles;
            }
            if (ppu_cycles == 341)
            {
                ppu_cycles = 0;
                if (++scanline == 262) scanline = 0;
            }

          //  StepsLog.WriteLine("2:" + scanline + " " + vram_addr.ToString("x4") + " " + vram_addr_internal.ToString("x4"));

        }

        ushort attrAddr, tileAddr, lowshift, highshift;
        int current, pixel, vram_addr_limite, attr, attrbuf, array_loc;
        int GetAttr()
        {

            vram_addr_limite = vram_addr & 0x3FF;
            attrAddr = (ushort)(0x23C0 | (vram_addr & 0xC00) | (((vram_addr_limite >> 2) & 0x07) | (((vram_addr_limite >> 4) & 0x38) | 0x3C0)));
            tileAddr = (ushort)((vram_addr & 0xc00) | 0x2000 | vram_addr_limite);
            array_loc = (ppu_ram[tileAddr] << 4) + BgPatternTableAddr + ((scanline + scrol_y) & 7);
            lowshift = (ushort)((lowshift << 8) | MapperRouterR_CHR(array_loc));
            highshift = (ushort)((highshift << 8) | MapperRouterR_CHR(array_loc + 8));
            if ((vram_addr & 0x1F) == 0x1F) vram_addr ^= 0x41F; else vram_addr++;
            return ((ppu_ram[attrAddr] >> (((vram_addr_limite >> 4) & 0x04) | (vram_addr_limite & 0x02))) & 0x03);
        }

        /*void _RenderBackGroundLine()
        {

            attr = attrbuf;
            attrbuf = GetAttr();
            for (int loc = 0; loc < 8; loc++)
            {
                current = 15 - loc - FineX;
                array_loc = (scanline << 8) + ((th_x << 3) | loc);
                pixel = Buffer_BG_array[array_loc] = ((lowshift >> current) & 1) | (((highshift >> current) & 1) << 1);
                if (current >= 8) ScreenBuf1x[array_loc] = NesColors[ppu_ram[((pixel == 0) ? 0x3f00 : 0x3f00 | (attr << 2)) | pixel] & 0x3f];
                else ScreenBuf1x[array_loc] = NesColors[ppu_ram[((pixel == 0) ? 0x3f00 : 0x3f00 | (attrbuf << 2)) | pixel] & 0x3f];
            }
        }*/

        void RenderBackGroundLine()
        {
            attr = GetAttr();
            attrbuf = GetAttr();
            for (int x = 0; x < 32; x++)
            {
                for (int loc = 0; loc < 8; loc++)
                {
                    current = 15 - loc - FineX;
                    array_loc = (scanline << 8) + ((x << 3) | loc);
                    pixel = Buffer_BG_array[array_loc] = ((lowshift >> current) & 1) | (((highshift >> current) & 1) << 1);
                    if (current >= 8) ScreenBuf1x[array_loc] = NesColors[ppu_ram[((pixel == 0) ? 0x3f00 : 0x3f00 | (attr << 2)) | pixel] & 0x3f];
                    else ScreenBuf1x[array_loc] = NesColors[ppu_ram[((pixel == 0) ? 0x3f00 : 0x3f00 | (attrbuf << 2)) | pixel] & 0x3f];
                }
                attr = attrbuf;
                attrbuf = GetAttr();
            }

            GetAttr();
        }
        void RenderSpritesLine()
        {
            int spriteCount = 0, line_t, loc_t, oam_addr = 0, line, tile_th_t, y_loc = 0, offset, mask;
            byte tile_th, tile_hbyte, tile_lbyte;
            bool flip_x = false;
            for (int oam_th = 63; oam_th >= 0; oam_th--)
            {
                oam_addr = oam_th << 2;
                y_loc = spr_ram[oam_addr] + 1;
                if (Spritesize8x16)
                {
                    if (scanline < y_loc || scanline > (y_loc + 15)) continue;
                    byte byte0 = spr_ram[oam_addr | 1];
                    tile_th = (byte)((byte0 & 0xfe) >> 0);
                    if ((byte0 & 1) > 0) offset = 256; else offset = 0;
                }
                else
                {
                    if (scanline < y_loc || scanline > (y_loc + 7)) continue;
                    tile_th = spr_ram[oam_addr | 1];
                    offset = SpPatternTableAddr >> 4;
                }
                byte sprite_attr = spr_ram[oam_addr | 2];
                byte x_loc = spr_ram[oam_addr | 3];
                bool priority = ((sprite_attr & 0x20) > 0) ? true : false;
                if (scanline >= y_loc && scanline <= (y_loc + 7))
                {
                    tile_th_t = tile_th + offset;
                    line = scanline - y_loc;
                }
                else
                {
                    tile_th_t = tile_th + offset + 1;
                    line = (scanline - y_loc) - 8;
                }
                if ((sprite_attr & 0x80) > 0) line_t = (7 - line); else line_t = line;
                tile_hbyte = MapperRouterR_CHR((tile_th_t << 4) | (line_t + 8));
                tile_lbyte = MapperRouterR_CHR((tile_th_t << 4) | line_t);
                flip_x = ((sprite_attr & 0x40) > 0) ? true : false;
                for (int loc = 0; loc < 8; loc++)
                {
                    if ((x_loc + loc) > 255) continue;
                    if (flip_x) loc_t = (7 - loc); else loc_t = loc;
                    mask = 1 << (7 - loc_t);
                    pixel = (((tile_hbyte & mask) << 1) + (tile_lbyte & mask)) >> ((7 - loc_t));
                    array_loc = ((scanline) << 8) + (x_loc + loc);
                    if (oam_th == 0 && !isSprite0hit && pixel != 0 && Buffer_BG_array[array_loc] != 0) isSprite0hit = true;
                    if ((pixel != 0 && !priority) || (pixel != 0 && priority && Buffer_BG_array[array_loc] == 0))
                        ScreenBuf1x[array_loc] = NesColors[ppu_ram[0x3f10 + ((sprite_attr & 3) << 2) | pixel] & 0x3f];
                }
                if ((++spriteCount) == 9) isSpriteOverflow = true;
            }
        }

        public bool screen_lock = false;
        void RenderScreen()
        {
            
            screen_lock = true;
            if (ScreenSize != 1)
                switch (ScreenSize)
                {
                    case 2: HS_XBRz.ScaleImage2X(ScreenBuf1x, ScreenBuf2x); break;
                    case 3: HS_XBRz.ScaleImage3X(ScreenBuf1x, ScreenBuf3x); break;
                    case 4: HS_XBRz.ScaleImage4X(ScreenBuf1x, ScreenBuf4x); break;
                    case 5: HS_XBRz.ScaleImage5X(ScreenBuf1x, ScreenBuf5x); break;
                    case 6: HS_XBRz.ScaleImage6X(ScreenBuf1x, ScreenBuf6x); break;

                    case 8:
                        HS_XBRz.ScaleImage4X(ScreenBuf1x, ScreenBuf4x);
                        ScalexTool.toScale2x_dx(ScreenBuf4x, 1024, 960 , ScreenBuf8x);
                        break;//4x2
                         
                    case 9:
                        HS_XBRz.ScaleImage3X(ScreenBuf1x, ScreenBuf3x);
                        ScalexTool.toScale3x_dx(ScreenBuf3x, 768, 720, ScreenBuf9x);
                        break;//3x3

                }
            NativeGDI.DrawImageHighSpeedtoDevice();
            screen_lock = false;
        }

        void UpdateVramRegister()
        {
            if ((vram_addr & 0x1F) == 0x1F) vram_addr ^= 0x41F; else vram_addr++;
            if (ShowBackGround || ShowSprites)
            {
                if ((vram_addr & 0x7000) == 0x7000)
                {
                    //int tmp = vram_addr & 0x3E0;
                    //vram_addr &= 0xFFF;
                    //switch (tmp)
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

        bool SuppressNmi = false, SuppressVbl = false;
        //ref http://wiki.nesdev.com/w/index.php/PPU_scrolling
        byte ppu_r_2002() //ok
        {
            byte ppu_ststus = (byte)(((isVblank) ? 0x80 : 0) | ((isSprite0hit) ? 0x40 : 0) | ((isSpriteOverflow) ? 0x20 : 0));
            if (ppu_cycles == 1 && scanline == 240)
            {
                SuppressNmi = true;
                SuppressVbl = true;
            }
            else
            {
                SuppressNmi = false;
                SuppressVbl = false;
                isVblank = false;
            }

            vram_latch = false;

            //vram_latch = true;

            return ppu_ststus;
        }

        byte ppu_r_2007()
        {
            if ((vram_addr & 0x3F00) == 0x3F00)
            {
                if ((vram_addr & 3) == 0) ppu_2007_temp = ppu_ram[vram_addr & 0x3fff]; // !! need check 
                else ppu_2007_temp = ppu_ram[(vram_addr % 0x3f00) | 0x3f00];
                vram_addr_tmp = vram_addr & 0x2FFF;
                if (vram_addr_tmp < 0x2000) ppu_2007_buffer = MapperRouterR_CHR(vram_addr_tmp);
                else ppu_2007_buffer = ppu_ram[vram_addr_tmp];
            }
            else
            {
                ppu_2007_temp = ppu_2007_buffer; //need read from buffer
                vram_addr_tmp = vram_addr & 0x3FFF;
                if (vram_addr_tmp < 0x2000) ppu_2007_buffer = MapperRouterR_CHR(vram_addr_tmp);//Pattern Table 
                else if (vram_addr_tmp < 0x3F00) ppu_2007_buffer = ppu_ram[vram_addr_tmp]; //Name Table & Attribute Table
                else//Sprite Palette & Image Palette
                {
                    vram_addr = (vram_addr + VramaddrIncrement) & 0x7FFF;
                    if ((vram_addr_tmp & 3) == 0) return ppu_ram[0x3F00];
                    else return ppu_ram[(vram_addr_tmp % 0x3f00) | 0x3f00];
                }
            }
            vram_addr = (ushort)((vram_addr + VramaddrIncrement) & 0x7FFF);
            return ppu_2007_temp;
        }

        void ppu_w_2000(byte value) //ok
        {

            //StepsLog.WriteLine("2000:" + " " + value.ToString("x2"));

            // t: ...BA.. ........ = d: ......BA
            vram_addr_internal = (ushort)((vram_addr_internal & 0x73ff) | ((value & 3) << 10)); // 0xx73ff
            BaseNameTableAddr = 0x2000 | ((value & 3) << 10);
            VramaddrIncrement = ((value & 4) > 0) ? 32 : 1;
            SpPatternTableAddr = ((value & 8) > 0) ? 0x1000 : 0;
            BgPatternTableAddr = ((value & 0x10) > 0) ? 0x1000 : 0;
            Spritesize8x16 = ((value & 0x20) > 0) ? true : false;
            NMIable = ((value & 0x80) > 0) ? true : false;

            // StepsLog.WriteLine("2000:" + " " + vram_addr_internal.ToString("x4") + " " + vram_addr.ToString("x4" ));
        }

        void ppu_w_2001(byte value) //ok
        {
            ShowBackGround = ((value & 0x8) > 0) ? true : false;
            ShowSprites = ((value & 0x10) > 0) ? true : false;
        }

        void ppu_w_2003(byte value) //ok
        {
            spr_ram_add = value;
        }

        void ppu_w_2004(byte value) //ok
        {
            spr_ram[spr_ram_add++] = value;
        }

        void ppu_w_2005(byte value) //ok
        {



            // Console.WriteLine("2005:"+value.ToString("x2"));

            if (vram_latch)
            {
                scrol_y = value;
                vram_addr_internal = (vram_addr_internal & 0x0C1F) | ((value & 0x7) << 12) | ((value & 0xF8) << 2);


                //if(value != 0 )
                //Console.WriteLine("Y:"+value);
            }
            else
            {//first
                vram_addr_internal = (vram_addr_internal & 0x7fe0) | ((value & 0xf8) >> 3);
                FineX = value & 0x07;

                // Console.WriteLine("X:"+value);
            }
            vram_latch = !vram_latch;

            //tepsLog.WriteLine("2005:" + " " + vram_addr_internal.ToString("x4") + " " + vram_addr.ToString("x4"));

        }

        void ppu_w_2006(byte value)//ok
        {
            // StepsLog.WriteLine("2006:" + " " + value.ToString("x2"));

            // Console.WriteLine("2006:" + value.ToString("x2"));

            if (!vram_latch) //first
                vram_addr_internal = (vram_addr_internal & 0x00FF) | ((value & 0x3F) << 8);
            else
            {
                vram_addr_internal = (vram_addr_internal & 0x7F00) | value;
                vram_addr = vram_addr_internal;
            }
            vram_latch = !vram_latch;

            // StepsLog.WriteLine("2006:" + " " + vram_addr_internal.ToString("x4") + " " + vram_addr.ToString("x4"));
        }

        void ppu_w_2007(byte value)
        {
            vram_addr_tmp = vram_addr & 0x3FFF;
            if (vram_addr_tmp < 0x2000)
            {
                if (CHR_ROM_count == 0) ppu_ram[vram_addr_tmp] = value;
                //MapperRouterW_CHR(vram_addr_tmp, value);
            }
            else if (vram_addr_tmp < 0x3f00) //Name Table & Attribute Table
            {
                addr_range = vram_addr_tmp & 0xc00;

                if (ScreenSpecial)
                {
                    if (ScreenFour) ppu_ram[vram_addr_tmp] = value;
                    else if (ScreenSingle)
                    {
                        ppu_ram[0x2000 | (vram_addr_tmp & 0x3ff)] = ppu_ram[0x2400 | (vram_addr_tmp & 0x3ff)] = ppu_ram[0x2800 | (vram_addr_tmp & 0x3ff)] = ppu_ram[0x2c00 | (vram_addr_tmp & 0x3ff)] = value;

                    }
                }
                else
                {

                    if (Vertical)
                    {
                        if (addr_range < 0x800) ppu_ram[vram_addr_tmp] = ppu_ram[vram_addr_tmp | 0x800] = value;
                        else ppu_ram[vram_addr_tmp] = ppu_ram[vram_addr_tmp & 0x37ff] = value;
                    }
                    else
                    {
                        if (addr_range < 0x400) ppu_ram[vram_addr_tmp] = ppu_ram[vram_addr_tmp | 0x400] = value;
                        else if (addr_range < 0x800) ppu_ram[vram_addr_tmp] = ppu_ram[vram_addr_tmp & 0x3bff] = value;
                        else if (addr_range < 0xc00) ppu_ram[vram_addr_tmp] = ppu_ram[vram_addr_tmp | 0x400] = value;
                        else ppu_ram[vram_addr_tmp] = ppu_ram[vram_addr_tmp & 0x3bff] = value;
                    }
                }
            }
            else //Sprite Palette & Image Palette
            {
                if ((vram_addr_tmp & 3) == 0) ppu_ram[vram_addr_tmp | 0x10] = ppu_ram[vram_addr_tmp & 0x3FEF] = value; //mirror 3f00 = 3f10 , 3f04 = 3f14 , 3f08 = 3f18 , 3f0c = 3f1c
                else ppu_ram[(vram_addr_tmp % 0x3f00) + 0x3f00] = value;
            }
            vram_addr = (ushort)((vram_addr + VramaddrIncrement) & 0x7FFF);
        }
        //int dma_cost = 0;
        void ppu_w_4014(byte value)//DMA 
        {
            //Console.WriteLine(value.ToString("x2"));
            //dma_cost = 512;
            int start_addr = value << 8;
            for (int i = 0; i < 256; i++) spr_ram[i] = NES_MEM[start_addr | i];
            cpu_cycles += 512;
        }
        public Bitmap GetScreenFrame()
        {
            switch (ScreenSize)
            {
                case 1: return new Bitmap(256 * 1, 240 * 1, 256 * 1 * 4, PixelFormat.Format32bppRgb, (IntPtr)ScreenBuf1x);
                case 2: return new Bitmap(256 * 2, 240 * 2, 256 * 2 * 4, PixelFormat.Format32bppRgb, (IntPtr)ScreenBuf2x);
                case 3: return new Bitmap(256 * 3, 240 * 3, 256 * 3 * 4, PixelFormat.Format32bppRgb, (IntPtr)ScreenBuf3x);
                case 4: return new Bitmap(256 * 4, 240 * 4, 256 * 4 * 4, PixelFormat.Format32bppRgb, (IntPtr)ScreenBuf4x);
                case 5: return new Bitmap(256 * 5, 240 * 5, 256 * 5 * 4, PixelFormat.Format32bppRgb, (IntPtr)ScreenBuf5x);
                case 6: return new Bitmap(256 * 6, 240 * 6, 256 * 6 * 4, PixelFormat.Format32bppRgb, (IntPtr)ScreenBuf6x);
                case 8: return new Bitmap(256 * 8, 240 * 8, 256 * 8 * 4, PixelFormat.Format32bppRgb, (IntPtr)ScreenBuf8x);
                case 9: return new Bitmap(256 * 9, 240 * 9, 256 * 9 * 4, PixelFormat.Format32bppRgb, (IntPtr)ScreenBuf9x);
            }
            return null;
        }
    }
}
