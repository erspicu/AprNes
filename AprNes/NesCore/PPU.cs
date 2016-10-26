using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using XBRz_speed;
using NativeWIN32API;
using System.Diagnostics;
using System.Threading;



namespace AprNes
{
    public partial class NesCore
    {
        int ppu_cycles = 0;
        int scanline = 241;
        public int frame_count = 0;

        //NES Palette 
        //ref http://www.thealmightyguru.com/Games/Hacking/Wiki/index.php?title=NES_Palette
        //ref  http://www.dev.bowdenweb.com/nes/nes-color-palette.html
        public uint[] NesColors = new uint[] { 
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
        bool Greyscale = false;//bit 0
        bool ShowBgLeftMost = false;//bit 1
        bool ShowSpLeftMost = false;//bit 2
        bool ShowBackGround = false;//bit 3
        bool ShowSprites = false;//bit 4
        bool EmphasizeRed = false;//bit 5
        bool EmphasizeGreen = false; //bit 6
        bool EmphasizeBlue = false; //bit 7

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

        public byte[] pre_Dec_tiles = new byte[512 * 64]; //共512個tiles , 每個tile由64bytes記錄
        public void DecodeTiles()
        {
            //處理tiles迴圈
            for (int tile = 0; tile < 512; tile++)
            {
                //解碼處理每個tile的線
                for (int line = 0; line < 8; line++)
                {
                    byte low = CHR_ROM[tile * 16 + line];
                    byte high = CHR_ROM[tile * 16 + line + 8];

                    //解碼每一條線的每個bits計算
                    for (int k = 0; k < 8; k++)
                    {
                        byte mask = (byte)(1 << (7 - k));
                        byte pixel = (byte)((((high & mask) << 1) + (low & mask)) >> ((7 - k)));
                        pre_Dec_tiles[64 * tile + line * 8 + k] = pixel;
                    }
                }
            }
        }

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

        int[] AttributeLocation = new int[0x400];
        int[] AttributeShift = new int[0x400];
        ushort attrAddr, attrAddrBuf;

        ushort tileAddr, tileAddrBuf;

        int attr, attrbuf;

        int shift;
        byte low, high;
        ushort lowshift, highshift;
        int current;
        int pixel;
        public void RenderBackGroundLine()
        {

            //-----------------------------------------------------------
            attrAddr = (ushort)(0x23C0 | (vram_addr & 0xC00) | AttributeLocation[vram_addr & 0x3FF]);
            shift = AttributeShift[vram_addr & 0x3FF];
            attr = ((ppu_ram[attrAddr] >> shift) & 0x03);

            tileAddr = (ushort)((vram_addr & 0xc00) | 0x2000 | (vram_addr & 0x3FF));

            low = CHR_ROM[ppu_ram[tileAddr] * 16 + BgPatternTableAddr + ((scanline + scrol_y) % 8)];
            high = CHR_ROM[ppu_ram[tileAddr] * 16 + BgPatternTableAddr + 8 + ((scanline + scrol_y) % 8)];
            lowshift = low;
            highshift = high;

            if ((vram_addr & 0x1F) == 0x1F) vram_addr ^= 0x41F; else vram_addr++;
            //-----------------------------------------------------------
            attrAddrBuf = (ushort)(0x23C0 | (vram_addr & 0xC00) | AttributeLocation[vram_addr & 0x3FF]);
            shift = AttributeShift[vram_addr & 0x3FF];
            attrbuf = ((ppu_ram[attrAddrBuf] >> shift) & 0x03);

            tileAddrBuf = (ushort)((vram_addr & 0xc00) | 0x2000 | (vram_addr & 0x3FF));
            low = CHR_ROM[ppu_ram[tileAddrBuf] * 16 + BgPatternTableAddr + ((scanline + scrol_y) % 8)];
            high = CHR_ROM[ppu_ram[tileAddrBuf] * 16 + BgPatternTableAddr + 8 + ((scanline + scrol_y) % 8)];

            lowshift = (ushort)((lowshift << 8) | low);
            highshift = (ushort)((highshift << 8) | high);
            if ((vram_addr & 0x1F) == 0x1F) vram_addr ^= 0x41F; else vram_addr++;
            //-----------------------------------------------------------

            for (int x = 0; x < 32; x++)
            {
                for (int loc = 0; loc < 8; loc++)
                {
                    current = 15 - loc - FineX;

                    pixel = Buffer_BG_array[x * 8 + loc][scanline] = ((lowshift >> current) & 1) | (((highshift >> current) & 1) << 1);
                    if (current >= 8)
                    {
                        int pal_offset = (pixel == 0) ? 0x3f00 : 0x3f00 + attr * 4;
                        Buffer_Screen_array[x * 8 + loc][scanline] = NesColors[ppu_ram[pal_offset + pixel]];
                    }
                    else
                    {
                        int pal_offset = (pixel == 0) ? 0x3f00 : 0x3f00 + attrbuf * 4;
                        Buffer_Screen_array[x * 8 + loc][scanline] = NesColors[ppu_ram[pal_offset + pixel]];
                    }
                }

                attr = attrbuf;

                //-----------------------------------------------------------
                attrAddrBuf = (ushort)(0x23C0 | (vram_addr & 0xC00) | AttributeLocation[vram_addr & 0x3FF]);
                shift = AttributeShift[vram_addr & 0x3FF];
                attrbuf = ((ppu_ram[attrAddrBuf] >> shift) & 0x03);

                tileAddrBuf = (ushort)((vram_addr & 0xc00) | 0x2000 | (vram_addr & 0x3FF));

                low = CHR_ROM[ppu_ram[tileAddrBuf] * 16 + BgPatternTableAddr + ((scanline + scrol_y) % 8)];
                high = CHR_ROM[ppu_ram[tileAddrBuf] * 16 + BgPatternTableAddr + 8 + ((scanline + scrol_y) % 8)];

                lowshift = (ushort)((lowshift << 8) | low);
                highshift = (ushort)((highshift << 8) | high);

                if ((vram_addr & 0x1F) == 0x1F) vram_addr ^= 0x41F; else vram_addr++;
                //-----------------------------------------------------------
            }
        }

        public void RenderSpritesLine()
        {

            int offset = 0;
            if (SpPatternTableAddr == 0x1000)
                offset = 256;

            int spriteCount = 0;
            for (int oam_th = 63; oam_th >= 0; oam_th--)
            {
                int y_loc = spr_ram[oam_th * 4] + 1;

                if (!Spritesize8x16)
                {
                    if (scanline >= y_loc && scanline <= (y_loc + 7))
                    {

                        byte tile_th = spr_ram[oam_th * 4 + 1];
                        byte sprite_attr = spr_ram[oam_th * 4 + 2];
                        byte x_loc = spr_ram[oam_th * 4 + 3];

                        int spr_color = sprite_attr & 3;

                        bool priority = ((sprite_attr & 0x20) > 0) ? true : false;
                        bool flip_x = ((sprite_attr & 0x40) > 0) ? true : false;
                        bool flip_y = ((sprite_attr & 0x80) > 0) ? true : false;

                        int pal_offset = 0x3f10 + spr_color * 4;
                        int line = scanline - y_loc;
                        byte p;

                        for (int loc = 0; loc < 8; loc++)
                        {

                            if ((x_loc + loc) > 255)
                                continue;

                            if (!flip_x && !flip_y)
                            {
                                p = pre_Dec_tiles[64 * (tile_th + offset) + loc + line * 8];

                                if (oam_th == 0 && !isSprite0hit && p != 0 && Buffer_BG_array[x_loc + loc][scanline] != 0) isSprite0hit = true;

                                if ((p != 0 && !priority) || (p != 0 && priority && Buffer_BG_array[x_loc + loc][scanline] == 0))
                                    Buffer_Screen_array[x_loc + loc][scanline] = NesColors[ppu_ram[pal_offset + p]];
                            }
                            else if (flip_x && !flip_y)
                            {
                                p = pre_Dec_tiles[64 * (tile_th + offset) + (7 - loc) + line * 8];

                                if (oam_th == 0 && !isSprite0hit && p != 0 && Buffer_BG_array[x_loc + loc][scanline] != 0) isSprite0hit = true;

                                if ((p != 0 && !priority) || (p != 0 && priority && Buffer_BG_array[x_loc + loc][scanline] == 0))
                                    Buffer_Screen_array[x_loc + loc][scanline] = NesColors[ppu_ram[pal_offset + p]];
                            }
                            else if (!flip_x && flip_y)
                            {
                                p = pre_Dec_tiles[64 * (tile_th + offset) + loc + (7 - line) * 8];

                                if (oam_th == 0 && !isSprite0hit && p != 0 && Buffer_BG_array[x_loc + loc][scanline] != 0) isSprite0hit = true;

                                if ((p != 0 && !priority) || (p != 0 && priority && Buffer_BG_array[x_loc + loc][scanline] == 0))
                                    Buffer_Screen_array[x_loc + loc][scanline] = NesColors[ppu_ram[pal_offset + p]];
                            }
                            else
                            {
                                p = pre_Dec_tiles[64 * (tile_th + offset) + (7 - loc) + (7 - line) * 8];

                                if (oam_th == 0 && !isSprite0hit && p != 0 && Buffer_BG_array[x_loc + loc][scanline] != 0) isSprite0hit = true;

                                if ((p != 0 && !priority) || (p != 0 && priority && Buffer_BG_array[x_loc + loc][scanline] == 0))
                                    Buffer_Screen_array[x_loc + loc][scanline] = NesColors[ppu_ram[pal_offset + p]];
                            }
                        }
                        spriteCount++;
                        if (spriteCount == 9)
                            isSpriteOverflow = true;
                    }

                }
                else
                {
                    if (scanline >= y_loc && scanline <= (y_loc + 15))
                    {

                        byte byte0 = spr_ram[oam_th * 4 + 1];

                        byte tile_th = (byte)((byte0 & 0xfe) >> 0);

                        if ((byte0 & 1) > 0)
                            offset = 256;
                        else
                            offset = 0;

                        byte sprite_attr = spr_ram[oam_th * 4 + 2];
                        byte x_loc = spr_ram[oam_th * 4 + 3];

                        int spr_color = sprite_attr & 3;

                        bool priority = ((sprite_attr & 0x20) > 0) ? true : false;
                        bool flip_x = ((sprite_attr & 0x40) > 0) ? true : false;
                        bool flip_y = ((sprite_attr & 0x80) > 0) ? true : false;

                        int pal_offset = 0x3f10 + spr_color * 4;

                        byte p;

                        if (scanline >= y_loc && scanline <= (y_loc + 7))
                        {
                            int line = scanline - y_loc;

                            for (int loc = 0; loc < 8; loc++)
                            {

                                if ((x_loc + loc) > 255)
                                    continue;


                                if (!flip_x && !flip_y)
                                {
                                    p = pre_Dec_tiles[64 * (tile_th + offset) + loc + line * 8];

                                    if (oam_th == 0 && !isSprite0hit && p != 0 && Buffer_BG_array[x_loc + loc][scanline] != 0) isSprite0hit = true;
                                    if ((p != 0 && !priority) || (p != 0 && priority && Buffer_BG_array[x_loc + loc][scanline] == 0))
                                        Buffer_Screen_array[x_loc + loc][scanline] = NesColors[ppu_ram[pal_offset + p]];
                                }
                                else if (flip_x && !flip_y)
                                {
                                    p = pre_Dec_tiles[64 * (tile_th + offset) + (7 - loc) + line * 8];
                                    if (oam_th == 0 && !isSprite0hit && p != 0 && Buffer_BG_array[x_loc + loc][scanline] != 0) isSprite0hit = true;
                                    if ((p != 0 && !priority) || (p != 0 && priority && Buffer_BG_array[x_loc + loc][scanline] == 0))
                                        Buffer_Screen_array[x_loc + loc][scanline] = NesColors[ppu_ram[pal_offset + p]];
                                }
                                else if (!flip_x && flip_y)
                                {
                                    p = pre_Dec_tiles[64 * (tile_th + offset) + loc + (7 - line) * 8];
                                    if (oam_th == 0 && !isSprite0hit && p != 0 && Buffer_BG_array[x_loc + loc][scanline] != 0) isSprite0hit = true;
                                    if ((p != 0 && !priority) || (p != 0 && priority && Buffer_BG_array[x_loc + loc][scanline] == 0))
                                        Buffer_Screen_array[x_loc + loc][scanline] = NesColors[ppu_ram[pal_offset + p]];
                                }
                                else
                                {
                                    p = pre_Dec_tiles[64 * (tile_th + offset) + (7 - loc) + (7 - line) * 8];
                                    if (oam_th == 0 && !isSprite0hit && p != 0 && Buffer_BG_array[x_loc + loc][scanline] != 0) isSprite0hit = true;
                                    if ((p != 0 && !priority) || (p != 0 && priority && Buffer_BG_array[x_loc + loc][scanline] == 0))
                                        Buffer_Screen_array[x_loc + loc][scanline] = NesColors[ppu_ram[pal_offset + p]];
                                }
                            }
                        }
                        else
                        {
                            int line = (scanline - y_loc) - 8;

                            for (int loc = 0; loc < 8; loc++)
                            {
                                if ((x_loc + loc) > 255)
                                    continue;

                                if (!flip_x && !flip_y)
                                {
                                    p = pre_Dec_tiles[64 * (tile_th + offset + 1) + loc + line * 8];
                                    if (oam_th == 0 && !isSprite0hit && p != 0 && Buffer_BG_array[x_loc + loc][scanline] != 0) isSprite0hit = true;
                                    if ((p != 0 && !priority) || (p != 0 && priority && Buffer_BG_array[x_loc + loc][scanline] == 0))
                                        Buffer_Screen_array[x_loc + loc][scanline] = NesColors[ppu_ram[pal_offset + p]];
                                }
                                else if (flip_x && !flip_y)
                                {
                                    p = pre_Dec_tiles[64 * (tile_th + offset + 1) + (7 - loc) + line * 8];
                                    if (oam_th == 0 && !isSprite0hit && p != 0 && Buffer_BG_array[x_loc + loc][scanline] != 0) isSprite0hit = true;
                                    if ((p != 0 && !priority) || (p != 0 && priority && Buffer_BG_array[x_loc + loc][scanline] == 0))
                                        Buffer_Screen_array[x_loc + loc][scanline] = NesColors[ppu_ram[pal_offset + p]];
                                }
                                else if (!flip_x && flip_y)
                                {
                                    p = pre_Dec_tiles[64 * (tile_th + offset + 1) + loc + (7 - line) * 8];
                                    if (oam_th == 0 && !isSprite0hit && p != 0 && Buffer_BG_array[x_loc + loc][scanline] != 0) isSprite0hit = true;
                                    if ((p != 0 && !priority) || (p != 0 && priority && Buffer_BG_array[x_loc + loc][scanline] == 0))
                                        Buffer_Screen_array[x_loc + loc][scanline] = NesColors[ppu_ram[pal_offset + p]];
                                }
                                else
                                {
                                    p = pre_Dec_tiles[64 * (tile_th + offset + 1) + (7 - loc) + (7 - line) * 8];

                                    if (oam_th == 0 && !isSprite0hit && p != 0 && Buffer_BG_array[x_loc + loc][scanline] != 0) isSprite0hit = true;
                                    if ((p != 0 && !priority) || (p != 0 && priority && Buffer_BG_array[x_loc + loc][scanline] == 0))
                                        Buffer_Screen_array[x_loc + loc][scanline] = NesColors[ppu_ram[pal_offset + p]];
                                }
                            }
                        }
                        spriteCount++;
                        if (spriteCount == 9)
                            isSpriteOverflow = true;
                    }
                }
            }
        }

        public void RenderScreen()
        {
            //1x spped test
            /*for (int x = 255; x >= 0; x--)
                for (int y = 239; y >= 0; y--)
                    ScreenBuffer1x[y * 256 + x] = Buffer_Screen_array[x][y];*/

            HS_XBRz.ScaleImage2X(Buffer_Screen_array, ScreenBuffer2x, 256, 240);
            NativeGDI.DrawImageHighSpeedtoDevice();
        }

        public void UpdateVramRegister()
        {

            if ((vram_addr & 0x1F) == 0x1F) vram_addr ^= 0x41F; else vram_addr++;

            //fixed
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
                    ppu_2007_temp = ppu_ram[vram_addr];
                else
                    ppu_2007_temp = ppu_ram[(vram_addr % 0x3f00) | 0x3f00];

                vram_addr_temp_access1 = (ushort)(vram_addr & 0x2FFF);
                if (vram_addr_temp_access1 < 0x2000)
                {
                    ppu_2007_buffer = CHR_ROM[vram_addr_temp_access1];
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
                    ppu_2007_buffer = CHR_ROM[vram_addr_temp_access1];
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
            Greyscale = ((value & 0x1) > 0) ? true : false;
            ShowBgLeftMost = ((value & 0x2) > 0) ? true : false;
            ShowSpLeftMost = ((value & 0x4) > 0) ? true : false;
            ShowBackGround = ((value & 0x8) > 0) ? true : false;
            ShowSprites = ((value & 0x10) > 0) ? true : false;
            EmphasizeRed = ((value & 0x20) > 0) ? true : false;
            EmphasizeGreen = ((value & 0x40) > 0) ? true : false;
            EmphasizeBlue = ((value & 0x80) > 0) ? true : false;
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
                //t: ....... ...HGFED = d: HGFED...                
                vram_addr_temp = (ushort)((vram_addr_temp & 0x7fe0) | (((int)value & 0xf8) >> 3));

                //x:              CBA = d: .....CBA
                FineX = (byte)((int)value & 0x07);
            }
            else
            {
                scrol_y = value;
                //t: CBA..HG FED..... = d: HGFEDCBA
                vram_addr_temp = (ushort)((vram_addr_temp & 0x0C1F) | (((int)value & 0x7) << 12) | (((int)value & 0xF8) << 2));
            }
            vram_latch = !vram_latch;
        }

        public void ppu_w_2006(byte value)//ok
        {
            if (!vram_latch) //first
            {
                //t: .FEDCBA ........ = d: ..FEDCBA
                vram_addr_temp = (ushort)((vram_addr_temp & 0x00FF) | (((int)value & 0x3F) << 8));

                //t: X...... ........ = 0
            }
            else
            {
                //t: ....... HGFEDCBA = d: HGFEDCBA
                vram_addr_temp = (ushort)((vram_addr_temp & 0x7F00) | (int)value);

                //v                   = t
                vram_addr = vram_addr_temp;
            }
            vram_latch = !vram_latch;
        }

        public void ppu_w_2007(byte value)
        {
            int vram_adder_tmp_access = vram_addr & 0x3FFF;

            if (vram_adder_tmp_access < 0x2000)
            {
                //Pattern Table 
                //MessageBox.Show("no support write to Pattern Table !");
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
    }
}
