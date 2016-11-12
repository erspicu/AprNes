﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using XBRz_speed;
using NativeWIN32API;
using System.Diagnostics;
using System.Threading;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AprNes
{
    public partial class NesCore
    {
        int ppu_cycles = 0;
        int scanline = 241;
        public int frame_count = 0;


        public int ScreenSize = 1;


        //NES Palette 
        //ref http://www.thealmightyguru.com/Games/Hacking/Wiki/index.php?title=NES_Palette
        //ref  http://www.dev.bowdenweb.com/nes/nes-color-palette.html
        static readonly uint[] NesColors =  { 
            0xFF7C7C7C,0xFF0000FC,0xFF0000BC,0xFF4428BC,0xFF940084,0xFFA80020,0xFFA81000,0xFF881400,
            0xFF503000,0xFF007800,0xFF006800,0xFF005800,0xFF004058,0xFF000000,0xFF000000,0xFF000000,
            0xFFBCBCBC,0xFF0078F8,0xFF0058F8,0xFF6844FC,0xFFD800CC,0xFFE40058,0xFFF83800,0xFFE45C10,
            0xFFAC7C00,0xFF00B800,0xFF00A800,0xFF00A844,0xFF008888,0xFF000000,0xFF000000,0xFF000000,
            0xFFF8F8F8,0xFF3CBCFC,0xFF6888FC,0xFF9878F8,0xFFF878F8,0xFFF85898,0xFFF87858,0xFFFCA044,
            0xFFF8B800,0xFFB8F818,0xFF58D854,0xFF58F898,0xFF00E8D8,0xFF787878,0xFF000000,0xFF000000,
            0xFFFCFCFC,0xFFA4E4FC,0xFFB8B8F8,0xFFD8B8F8,0xFFF8B8F8,0xFFF8A4C0,0xFFF0D0B0,0xFFFCE0A8,
            0xFFF8D878,0xFFD8F878,0xFFB8F8B8,0xFFB8F8D8,0xFF00FCFC,0xFFF8D8F8,0xFF000000,0xFF000000 };

        //ppu ctrl 0x2000
        int BaseNameTableAddr = 0;
        int VramaddrIncrement = 1;
        int SpPatternTableAddr = 0;
        public int BgPatternTableAddr = 0;
        bool Spritesize8x16 = false;
        bool NMIable = false;

        //ppu mask 0x2001
        bool ShowBackGround = false;//bit 3
        bool ShowSprites = false;//bit 4

        //ppu status 0x2002
        bool isSpriteOverflow = false;//bit 5
        bool isSprite0hit = false;//bit 6
        bool isVblank = false; // bit 7

        int vram_addr_temp = 0;
        int vram_addr = 0;
        bool vram_latch = false;

        byte FineX = 0;

        public byte[] spr_ram = new byte[256];
        byte spr_ram_add = 0;

        public byte[] ppu_ram = new byte[0x4000];

        byte ppu_2007_buffer = 0;
        byte ppu_2007_temp = 0;

        ushort vram_addr_temp_access1 = 0;

        uint[][] Buffer_Screen_array = new uint[256][];
        int[][] Buffer_BG_array = new int[256][];

        uint[] ScreenBuffer1x = new uint[256 * 240];
        uint[] ScreenBuffer2x = new uint[512 * 480];
        uint[] ScreenBuffer3x = new uint[768 * 720];
        uint[] ScreenBuffer4x = new uint[1024 * 960];
        uint[] ScreenBuffer5x = new uint[1280 * 1200];

        int scrol_y = 0;

        bool NMI_set = false;
        public bool LimitFPS = true;

        Stopwatch StopWatch = new Stopwatch();
        public void ppu_step()
        {
            switch (scanline)
            {
                case 240:
                    {
                        if (ppu_cycles == 1)
                        {
                            if (!SuppressVbl)
                                isVblank = true;

                            if (NMIable && !SuppressNmi)
                                NMI_set = true;

                            RenderScreen();

                            if (LimitFPS)
                                while (StopWatch.Elapsed.TotalSeconds < 0.01665)  //0.0167
                                    Thread.Sleep(0);

                            StopWatch.Restart();
                            frame_count++;
                        }
                    }
                    break;

                case 260: // End of vblank
                    {
                        if (ppu_cycles == 1)
                        {
                            // Clear VBlank flag
                            isVblank = false;
                        }
                        else if (ppu_cycles == 341)
                        {
                            scanline = -1;
                            ppu_cycles = 1;
                            return;
                        }
                    }
                    break;

                case -1:
                    {
                        if (ppu_cycles == 1)
                        {
                            isSprite0hit = isSpriteOverflow = false;
                        }
                        else if (ppu_cycles == 304)
                        {
                            if (ShowBackGround || ShowSprites)
                                vram_addr = vram_addr_temp;
                        }
                    }
                    break;

                default:
                    if (scanline > -1 && scanline < 240)
                    {
                        switch (ppu_cycles)
                        {
                            case 254:
                                {
                                    if (ShowBackGround)
                                        RenderBackGroundLine();

                                    if (ShowSprites)
                                        RenderSpritesLine();
                                }
                                break;

                            case 256:
                                if (ShowBackGround)
                                    UpdateVramRegister();
                                break;

                            case 260:
                                //for MMC3
                                break;
                        }
                    }
                    break;
            }

            if (ppu_cycles == 341)
            {
                ppu_cycles = 0;
                scanline++;
            }

            ppu_cycles++;
        }


        ushort attrAddr, attrAddrBuf;
        ushort tileAddr, tileAddrBuf;
        int attr, attrbuf;
        byte low, high;
        ushort lowshift, highshift;
        int current;
        int pixel;

        int vram_addr_limite;
        public void RenderBackGroundLine()
        {

            //-----------------------------------------------------------
            vram_addr_limite = vram_addr & 0x3FF;
            attrAddr = (ushort)(0x23C0 | (vram_addr & 0xC00) | (((vram_addr_limite >> 2) & 0x07) | (((vram_addr_limite >> 4) & 0x38) | 0x3C0)));
            attr = ((ppu_ram[attrAddr] >> (((vram_addr_limite >> 4) & 0x04) | (vram_addr_limite & 0x02))) & 0x03);
            tileAddr = (ushort)((vram_addr & 0xc00) | 0x2000 | vram_addr_limite);

            low = MapperRouterR_CHR((ppu_ram[tileAddr] << 4) + BgPatternTableAddr + ((scanline + scrol_y) & 7));
            high = MapperRouterR_CHR((ppu_ram[tileAddr] << 4) + BgPatternTableAddr + 8 + ((scanline + scrol_y) & 7));
            lowshift = low;
            highshift = high;

            if ((vram_addr & 0x1F) == 0x1F) vram_addr ^= 0x41F; else vram_addr++;
            //-----------------------------------------------------------
            vram_addr_limite = vram_addr & 0x3FF;
            attrAddrBuf = (ushort)(0x23C0 | (vram_addr & 0xC00) | (((vram_addr_limite >> 2) & 0x07) | (((vram_addr_limite >> 4) & 0x38) | 0x3C0)));
            attrbuf = ((ppu_ram[attrAddrBuf] >> (((vram_addr_limite >> 4) & 0x04) | (vram_addr_limite & 0x02))) & 0x03);

            tileAddrBuf = (ushort)((vram_addr & 0xc00) | 0x2000 | vram_addr_limite);
            low = MapperRouterR_CHR((ppu_ram[tileAddrBuf] << 4) + BgPatternTableAddr + ((scanline + scrol_y) & 7));
            high = MapperRouterR_CHR((ppu_ram[tileAddrBuf] << 4) + BgPatternTableAddr + 8 + ((scanline + scrol_y) & 7));

            lowshift = (ushort)((lowshift << 8) | low);
            highshift = (ushort)((highshift << 8) | high);
            if ((vram_addr & 0x1F) == 0x1F) vram_addr ^= 0x41F; else vram_addr++;
            //-----------------------------------------------------------

            for (int x = 0; x < 32; x++)
            {
                for (int loc = 0; loc < 8; loc++)
                {
                    current = 15 - loc - FineX;

                    pixel = Buffer_BG_array[(x << 3) + loc][scanline] = ((lowshift >> current) & 1) | (((highshift >> current) & 1) << 1);
                    if (current >= 8)
                    {
                        int pal_offset = (pixel == 0) ? 0x3f00 : 0x3f00 + (attr << 2);
                        Buffer_Screen_array[(x << 3) + loc][scanline] = NesColors[ppu_ram[pal_offset + pixel]];
                    }
                    else
                    {
                        int pal_offset = (pixel == 0) ? 0x3f00 : 0x3f00 + (attrbuf << 2);
                        Buffer_Screen_array[(x << 3) + loc][scanline] = NesColors[ppu_ram[pal_offset + pixel]];
                    }
                }

                attr = attrbuf;

                //-----------------------------------------------------------
                vram_addr_limite = vram_addr & 0x3FF;
                attrAddrBuf = (ushort)(0x23C0 | (vram_addr & 0xC00) | (((vram_addr_limite >> 2) & 0x07) | (((vram_addr_limite >> 4) & 0x38) | 0x3C0)));
                attrbuf = ((ppu_ram[attrAddrBuf] >> (((vram_addr_limite >> 4) & 0x04) | (vram_addr_limite & 0x02))) & 0x03);

                tileAddrBuf = (ushort)((vram_addr & 0xc00) | 0x2000 | vram_addr_limite);

                low = MapperRouterR_CHR((ppu_ram[tileAddrBuf] << 4) + BgPatternTableAddr + ((scanline + scrol_y) & 7));
                high = MapperRouterR_CHR((ppu_ram[tileAddrBuf] << 4) + BgPatternTableAddr + 8 + ((scanline + scrol_y) & 7));

                lowshift = (ushort)((lowshift << 8) | low);
                highshift = (ushort)((highshift << 8) | high);
                if ((vram_addr & 0x1F) == 0x1F) vram_addr ^= 0x41F; else vram_addr++;
                //-----------------------------------------------------------
            }
        }

        public void RenderSpritesLine()
        {
            int spriteCount = 0, line_t, loc_t, oam_addr = 0, line, tile_th_t, y_loc = 0, offset, mask;
            byte tile_th;
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
                for (int loc = 0; loc < 8; loc++)
                {
                    if ((x_loc + loc) > 255) continue;
                    if ((sprite_attr & 0x40) > 0) loc_t = (7 - loc); else loc_t = loc;
                    mask = 1 << (7 - loc_t);
                    pixel = (((MapperRouterR_CHR((tile_th_t << 4) + line_t + 8) & mask) << 1) + (MapperRouterR_CHR((tile_th_t << 4) + line_t) & mask)) >> ((7 - loc_t));
                    if (oam_th == 0 && !isSprite0hit && pixel != 0 && Buffer_BG_array[x_loc + loc][scanline] != 0) isSprite0hit = true;
                    if ((pixel != 0 && !priority) || (pixel != 0 && priority && Buffer_BG_array[x_loc + loc_t][scanline] == 0))
                        Buffer_Screen_array[x_loc + loc_t][scanline] = NesColors[ppu_ram[0x3f10 + ((sprite_attr & 3) << 2) + pixel]];
                }
                if ((++spriteCount) == 9) isSpriteOverflow = true;
            }
        }

        public bool screen_lock = false;
        public void RenderScreen()
        {

            screen_lock = true;
            switch (ScreenSize)
            {
                case 1:
                    for (int x = 255; x >= 0; x--)
                        for (int y = 239; y >= 0; y--)
                            ScreenBuffer1x[(y << 8) + x] = Buffer_Screen_array[x][y];
                    break;
                case 2: HS_XBRz.ScaleImage2X(Buffer_Screen_array, ScreenBuffer2x, 256, 240); break;
                case 3: HS_XBRz.ScaleImage3X(Buffer_Screen_array, ScreenBuffer3x, 256, 240); break;
                case 4: HS_XBRz.ScaleImage4X(Buffer_Screen_array, ScreenBuffer4x, 256, 240); break;
                case 5: HS_XBRz.ScaleImage5X(Buffer_Screen_array, ScreenBuffer5x, 256, 240); break;
            }
            NativeGDI.DrawImageHighSpeedtoDevice();
            screen_lock = false;
        }

        public void UpdateVramRegister()
        {

            if ((vram_addr & 0x1F) == 0x1F) vram_addr ^= 0x41F; else vram_addr++;
            if (ShowBackGround || ShowSprites)
            {
                if ((vram_addr & 0x7000) == 0x7000)
                {
                    int tmp = vram_addr & 0x3E0;
                    vram_addr &= 0xFFF;

                    switch (tmp)
                    {
                        case 0x3A0: vram_addr ^= 0xBA0; break;
                        case 0x3E0: vram_addr ^= 0x3E0; break;
                        default: vram_addr += 0x20; break;
                    }
                }
                else
                    vram_addr += 0x1000;
                vram_addr = (ushort)((vram_addr & 0x7BE0) | (vram_addr_temp & 0x41F));
            }
        }

        bool SuppressNmi = false;
        bool SuppressVbl = false;

        //ref http://wiki.nesdev.com/w/index.php/PPU_scrolling
        public byte ppu_r_2002() //ok
        {
            byte t = (byte)(((isVblank) ? 0x80 : 0) | ((isSprite0hit) ? 0x40 : 0) | ((isSpriteOverflow) ? 0x20 : 0));

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
            return t;
        }

        public byte ppu_r_2007()
        {
            if ((vram_addr & 0x3F00) == 0x3F00)
            {
                if ((vram_addr & 3) == 0)
                    ppu_2007_temp = ppu_ram[vram_addr & 0x3fff]; // !! need check 
                else
                    ppu_2007_temp = ppu_ram[(vram_addr % 0x3f00) | 0x3f00];

                vram_addr_temp_access1 = (ushort)(vram_addr & 0x2FFF);
                if (vram_addr_temp_access1 < 0x2000)
                {
                    ppu_2007_buffer = MapperRouterR_CHR(vram_addr_temp_access1); 
                        //CHR_ROM[vram_addr_temp_access1];
                }
                else
                {
                    ppu_2007_buffer = ppu_ram[vram_addr_temp_access1];
                }
            }
            else
            {
                //need read from buffer
                ppu_2007_temp = ppu_2007_buffer;

                vram_addr_temp_access1 = (ushort)(vram_addr & 0x3FFF);
                if (vram_addr_temp_access1 < 0x2000)
                {
                    //Pattern Table 
                    ppu_2007_buffer = MapperRouterR_CHR(vram_addr_temp_access1);
                        //CHR_ROM[vram_addr_temp_access1];
                }
                else if (vram_addr_temp_access1 < 0x3F00)
                {
                    //Name Table & Attribute Table
                    ppu_2007_buffer = ppu_ram[vram_addr_temp_access1];
                }
                else
                {
                    vram_addr = (ushort)((vram_addr + VramaddrIncrement) & 0x7FFF);
                    //Sprite Palette & Image Palette
                    if ((vram_addr_temp_access1 & 3) == 0)
                        return ppu_ram[0x3F00];
                    else
                        return ppu_ram[(vram_addr_temp_access1 % 0x3f00) | 0x3f00];
                }
            }
            vram_addr = (ushort)((vram_addr + VramaddrIncrement) & 0x7FFF);
            return ppu_2007_temp;
        }

        public void ppu_w_2000(byte value) //ok
        {
            // t: ...BA.. ........ = d: ......BA
            vram_addr_temp = (ushort)((vram_addr_temp & 0x73ff) | ((value & 3) << 10)); // 0xx73ff

            BaseNameTableAddr = 0x2000 | ((value & 3) << 10);
            VramaddrIncrement = ((value & 4) > 0) ? 32 : 1;
            SpPatternTableAddr = ((value & 8) > 0) ? 0x1000 : 0;
            BgPatternTableAddr = ((value & 0x10) > 0) ? 0x1000 : 0;
            Spritesize8x16 = ((value & 0x20) > 0) ? true : false;
            NMIable = ((value & 0x80) > 0) ? true : false;
        }

        public void ppu_w_2001(byte value) //ok
        {
            ShowBackGround = ((value & 0x8) > 0) ? true : false;
            ShowSprites = ((value & 0x10) > 0) ? true : false;
        }

        public void ppu_w_2003(byte value) //ok
        {
            spr_ram_add = value;
        }

        public void ppu_w_2004(byte value) //ok
        {
            spr_ram[spr_ram_add++] = value;
        }

        public void ppu_w_2005(byte value) //ok
        {
            if (!vram_latch) //first
            {
                vram_addr_temp = (ushort)((vram_addr_temp & 0x7fe0) | (((int)value & 0xf8) >> 3));
                FineX = (byte)((int)value & 0x07);
            }
            else
            {
                scrol_y = value;
                vram_addr_temp = (ushort)((vram_addr_temp & 0x0C1F) | (((int)value & 0x7) << 12) | (((int)value & 0xF8) << 2));
            }
            vram_latch = !vram_latch;
        }

        public void ppu_w_2006(byte value)//ok
        {
            if (!vram_latch) //first
            {
                vram_addr_temp = (ushort)((vram_addr_temp & 0x00FF) | (((int)value & 0x3F) << 8));
            }
            else
            {
                vram_addr_temp = (ushort)((vram_addr_temp & 0x7F00) | (int)value);
                vram_addr = vram_addr_temp;
            }
            vram_latch = !vram_latch;
        }

        public void ppu_w_2007(byte value)
        {
            int vram_adder_tmp_access = vram_addr & 0x3FFF;

            if (vram_adder_tmp_access < 0x2000)
            {
                MapperRouterW_CHR(vram_adder_tmp_access, value);
            }
            else if (vram_adder_tmp_access < 0x3f00)
            {
                //Name Table & Attribute Table
                if (Vertical)
                {
                    switch (vram_adder_tmp_access & 0xc00)
                    {
                        //2000 -> 2800
                        case 0x0:
                        case 0x100:
                        case 0x200:
                        case 0x300:
                            ppu_ram[vram_adder_tmp_access] = ppu_ram[vram_adder_tmp_access | 0x800] = value;
                            break;

                        //2400 -> 2c00
                        case 0x400:
                        case 0x500:
                        case 0x600:
                        case 0x700:
                            ppu_ram[vram_adder_tmp_access] = ppu_ram[vram_adder_tmp_access | 0x800] = value;
                            break;

                        // 2800 -> 2000
                        case 0x800:
                        case 0x900:
                        case 0xa00:
                        case 0xb00:
                            ppu_ram[vram_adder_tmp_access] = ppu_ram[vram_adder_tmp_access & 0x37ff] = value;
                            break;

                        // 2c00 -> 2400
                        case 0xc00:
                        case 0xd00:
                        case 0xe00:
                        case 0xf00:
                            ppu_ram[vram_adder_tmp_access] = ppu_ram[vram_adder_tmp_access & 0x37ff] = value;
                            break;
                    }
                }
                else
                {
                    switch (vram_adder_tmp_access & 0xc00)
                    {
                        case 0x0:
                        case 0x100:
                        case 0x200:
                        case 0x300: ppu_ram[vram_adder_tmp_access] = ppu_ram[vram_adder_tmp_access | 0x400] = value;
                            break;

                        case 0x400:
                        case 0x500:
                        case 0x600:
                        case 0x700:
                            ppu_ram[vram_adder_tmp_access] = ppu_ram[vram_adder_tmp_access & 0x3bff] = value;
                            break;

                        case 0x800:
                        case 0x900:
                        case 0xa00:
                        case 0xb00:
                            ppu_ram[vram_adder_tmp_access] = ppu_ram[vram_adder_tmp_access | 0x400] = value; break;

                        case 0xc00:
                        case 0xd00:
                        case 0xe00:
                        case 0xf00:
                            ppu_ram[vram_adder_tmp_access] = ppu_ram[vram_adder_tmp_access & 0x3bff] = value; break;
                    }
                }
            }
            else
            {
                //Sprite Palette & Image Palette
                if ((vram_adder_tmp_access & 3) == 0) //mirror 3f00 = 3f10 , 3f04 = 3f14 , 3f08 = 3f18 , 3f0c = 3f1c
                    ppu_ram[vram_adder_tmp_access | 0x10] = ppu_ram[vram_adder_tmp_access & 0x3FEF] = value;
                else
                    ppu_ram[(vram_adder_tmp_access % 0x3f00) + 0x3f00] = value;
            }
            vram_addr = (ushort)((vram_addr + VramaddrIncrement) & 0x7FFF);
        }

        int dma_cost = 0;
        public void ppu_w_4014(byte value)//DMA 
        {
            dma_cost = 512;
            int start_addr = value << 8;
            for (int i = 0; i < 256; i++)
                spr_ram[i] = NES_MEM[start_addr | i];
        }



        public Bitmap GetScreenFrame()
        {
            switch (ScreenSize)
            {
                case 1: return new Bitmap(256, 240, 256 * 4, PixelFormat.Format32bppRgb , Marshal.UnsafeAddrOfPinnedArrayElement(ScreenBuffer1x, 0));
                case 2: return new Bitmap(256 * 2, 240 * 2, 256 * 2 * 4, PixelFormat.Format32bppRgb , Marshal.UnsafeAddrOfPinnedArrayElement(ScreenBuffer2x, 0));
                case 3: return new Bitmap(256 * 3, 240 * 3, 256 * 3 * 4, PixelFormat.Format32bppRgb, Marshal.UnsafeAddrOfPinnedArrayElement(ScreenBuffer3x, 0));
                case 4: return new Bitmap(256 * 4, 240 * 4, 256 * 4 * 4, PixelFormat.Format32bppRgb, Marshal.UnsafeAddrOfPinnedArrayElement(ScreenBuffer4x, 0));
                case 5: return new Bitmap(256 * 5, 240 * 5, 256 * 5 * 4, PixelFormat.Format32bppRgb, Marshal.UnsafeAddrOfPinnedArrayElement(ScreenBuffer5x, 0));
            }
            return null;
        }


    }
}
