﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        //MMC3 https://wiki.nesdev.com/w/index.php/MMC3
        bool IRQ_enable = false, IRQReset = false, IRQResteVbl = false;
        int IRQlatchVal = 0, IRQCounter = 0, BankReg = 0;
        int CHR0_Bankselect1k = 0, CHR1_Bankselect1k = 0, CHR2_Bankselect1k = 0, CHR3_Bankselect1k = 0;
        int CHR0_Bankselect2k = 0, CHR1_Bankselect2k = 0;
        int PRG0_Bankselect = 0, PRG1_Bankselect = 0;
        void mapper004write_ROM(ushort address, byte value)
        {   //$8000-$9FFF, $A000-$BFFF, $C000-$DFFF, and $E000-$FFFF
            if ((address & 1) == 0)//even
            {
                if (address < 0xa000)//$8000-$9FFF (Bank select)
                {
                    BankReg = value & 7; // Specify which bank register to update on next write to Bank Data register
                    PRG_Bankmode = (value & 0x40) >> 6;
                    CHR_Bankmode = (value & 0x80) >> 7;
                }
                else if (address < 0xc000) Vertical = ((value & 1) > 0) ? false : true; //(0: vertical; 1: horizontal) $A000-$BFFF (Mirroring)
                else if (address < 0xe000) IRQlatchVal = value;//$C000-$DFFF (IRQ latch) 
                else//$E000-$FFFF IRQ disable
                {
                    IRQ_enable = false;
                    IRQCounter = IRQlatchVal;
                }
            }
            else//odd
            {
                if (address < 0xa000) //$8000-$9FFF (Bank data)
                {
                    if (BankReg == 0) CHR0_Bankselect2k = value; //0: Select 2 KB CHR bank at PPU $0000-$07FF (or $1000-$17FF);
                    else if (BankReg == 1) CHR1_Bankselect2k = value; //1: Select 2 KB CHR bank at PPU $0800-$0FFF (or $1800-$1FFF);
                    else if (BankReg == 2) CHR0_Bankselect1k = value; //2: Select 1 KB CHR bank at PPU $1000-$13FF (or $0000-$03FF);
                    else if (BankReg == 3) CHR1_Bankselect1k = value; //3: Select 1 KB CHR bank at PPU $1400-$17FF (or $0400-$07FF);
                    else if (BankReg == 4) CHR2_Bankselect1k = value; //4: Select 1 KB CHR bank at PPU $1800-$1BFF (or $0800-$0BFF);
                    else if (BankReg == 5) CHR3_Bankselect1k = value; //5: Select 1 KB CHR bank at PPU $1C00-$1FFF (or $0C00-$0FFF);
                    else if (BankReg == 6) PRG0_Bankselect = value;//6: Select 8 KB PRG ROM bank at $8000-$9FFF (or $C000-$DFFF);
                    else PRG1_Bankselect = value; //7: Select 8 KB PRG ROM bank at $A000-$BFFF
                }
                else if (address < 0xc000) return; //$A000-$BFFF (PRG RAM protect) nothing do
                else if (address < 0xe000)//$C000-$DFFF (IRQ reload)
                {
                    IRQCounter |= 0x80;
                    if (scanline < 240) IRQReset = true;
                    else
                    {
                        IRQResteVbl = true;
                        IRQReset = false;
                    }
                }
                else IRQ_enable = true; //$E000-$FFFF (IRQ enable) 
            }
        }
        byte mapper004read_RPG(ushort address)
        {
            if (PRG_Bankmode == 0) //0: $8000-$9FFF swappable, $C000-$DFFF fixed to second-last bank;
            {
                if (address < 0xa000) return PRG_ROM[(address - 0x8000) + (PRG0_Bankselect << 13)]; //$8000-$9FFF swap ok
                else if (address < 0xc000) return PRG_ROM[(address - 0xa000) + (PRG1_Bankselect << 13)]; //$A000-$BFFF swap ok
                else if (address < 0xe000) return PRG_ROM[(address - 0xc000) + (((PRG_ROM_count << 1) - 2) << 13)]; //$C000-$DFFF fixed
                else return PRG_ROM[(address - 0xe000) + (((PRG_ROM_count << 1) - 1) << 13)]; ;//$E000-$FFFF fixed

            }
            else //1: $C000-$DFFF swappable, $8000-$9FFF fixed to second-last bank
            {
                if (address < 0xa000) return PRG_ROM[(address - 0x8000) + (((PRG_ROM_count << 1) - 2) << 13)]; //$8000-$9FFF fixed
                else if (address < 0xc000) return PRG_ROM[(address - 0xa000) + (PRG1_Bankselect << 13)]; //$A000-$BFFF swap ok
                else if (address < 0xe000) return PRG_ROM[(address - 0xc000) + (PRG0_Bankselect << 13)]; //$C000-$DFFF swap ok
                else return PRG_ROM[(address - 0xe000) + (((PRG_ROM_count << 1) - 1) << 13)];//$E000-$FFFF fixed
            }
        }
        byte mapper004read_CHR(int address)
        {
            if (CHR_Bankmode == 0) //0: two 2 KB banks at $0000-$0FFF,four 1 KB banks at $1000-$1FFF; ok
            {
                if (address < 0x1000)//2k * 2
                {
                    if (address < 0x400) return CHR_ROM[address + ((CHR0_Bankselect2k & 0xfe) << 10)];
                    else if (address < 0x800) return CHR_ROM[(address - 0x400) + ((CHR0_Bankselect2k | 1) << 10)];
                    else if (address < 0xc00) return CHR_ROM[(address - 0x800) + ((CHR1_Bankselect2k & 0xfe) << 10)];
                    else return CHR_ROM[(address - 0xc00) + ((CHR1_Bankselect2k | 1) << 10)];
                }
                else //1k *4
                {
                    if (address < 0x1400) return CHR_ROM[(address - 0x1000) + (CHR0_Bankselect1k << 10)];
                    else if (address < 0x1800) return CHR_ROM[(address - 0x1400) + (CHR1_Bankselect1k << 10)];
                    else if (address < 0x1c00) return CHR_ROM[(address - 0x1800) + (CHR2_Bankselect1k << 10)];
                    else return CHR_ROM[(address - 0x1c00) + (CHR3_Bankselect1k << 10)];
                }
            }
            else //1: two 2 KB banks at $1000-$1FFF,four 1 KB banks at $0000-$0FFF
            {
                if (address < 0x1000) //1k*4
                {
                    if (address < 0x400) return CHR_ROM[address + (CHR0_Bankselect1k << 10)];
                    else if (address < 0x800) return CHR_ROM[(address - 0x400) + (CHR1_Bankselect1k << 10)];
                    else if (address < 0xc00) return CHR_ROM[(address - 0x800) + (CHR2_Bankselect1k << 10)];
                    else return CHR_ROM[(address - 0xc00) + (CHR3_Bankselect1k << 10)];
                }
                else //2k * 2
                {
                    if (address < 0x1400) return CHR_ROM[(address - 0x1000) + ((CHR0_Bankselect2k & 0xfe) << 10)];
                    else if (address < 0x1800) return CHR_ROM[(address - 0x1400) + ((CHR0_Bankselect2k | 1) << 10)];
                    else if (address < 0x1c00) return CHR_ROM[(address - 0x1800) + ((CHR1_Bankselect2k & 0xfe) << 10)];
                    else return CHR_ROM[(address - 0x1c00) + ((CHR1_Bankselect2k | 1) << 10)];
                }
            }
        }

        void mapper04step_IRQ()
        {
            if (ShowBackGround || ShowSprites)
            {
                if (IRQResteVbl)
                {
                    IRQCounter = IRQlatchVal;
                    IRQResteVbl = false;
                }
                if (IRQReset)
                {
                    IRQCounter = IRQlatchVal;
                    IRQReset = false;
                }
                else if (IRQCounter > 0) IRQCounter--;
            }
            if (IRQCounter == 0)
            {
                if (IRQ_enable) IRQ_set = true;
                IRQReset = true;
            }
        }
    }
}
