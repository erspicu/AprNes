using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        //MMC5 https://wiki.nesdev.com/w/index.php/MMC5 editing

        int CHR4_Bankselect1k = 0, CHR5_Bankselect1k = 0, CHR6_Bankselect1k = 0, CHR7_Bankselect1k = 0;
        int CHR2_Bankselect2k = 0, CHR3_Bankselect2k = 0;
        int PRG2_Bankselect = 0, PRG3_Bankselect = 0;
        int PRG_BankselectRam = 0;//, PRG0_BankselectRam = 0, PRG1_BankselectRam = 0, PRG2_BankselectRam = 0; 
        bool useRamBank0 = false, useRamBank1 = false, useRamBank2 = false;

        byte mapper005read_ExpansionROM(ushort address)//for register configure
        {
            return 0;
        }

        void mapper005write_ExpansionROM(ushort address, byte value)//for register configure
        {
            //register
            switch (address)
            {
                #region audio
                case 0x5000: break;
                case 0x5001: break;
                case 0x5002: break;
                case 0x5003: break;
                case 0x5004: break;
                case 0x5005: break;
                case 0x5006: break;
                case 0x5007: break;
                case 0x5010: break;
                case 0x5011: break;
                case 0x5015: break;
                #endregion
                case 0x5100: PRG_Bankmode = value & 3; break; //PRG mode 
                case 0x5101: CHR_Bankmode = value & 3; break; //CHR mode
                case 0x5102: break; //PRG RAM Protect 1 
                case 0x5103: break; //PRG RAM Protect 2 
                case 0x5104: break; //Extended RAM mode 
                case 0x5105: break; //Nametable mapping 
                case 0x5106: break; //Fill-mode tile 
                case 0x5107: break; //Fill-mode color
                case 0x5113: PRG_BankselectRam = value & 3; break;
                case 0x5114:
                    PRG0_Bankselect = value & 0x7f;
                    useRamBank0 = ((value & 0x80) == 0) ? true : false;
                    break;
                case 0x5115:
                    PRG1_Bankselect = value & 0x7f;
                    useRamBank1 = ((value & 0x80) == 0) ? true : false;
                    break;
                case 0x5116:
                    PRG2_Bankselect = value & 0x7f;
                    useRamBank2 = ((value & 0x80) == 0) ? true : false;
                    break;
                case 0x5117: PRG3_Bankselect = value & 0x7f; break;
                case 0x5120: CHR0_Bankselect1k = value; break;
                case 0x5121: CHR0_Bankselect2k = CHR1_Bankselect1k = value; break;
                case 0x5122: CHR2_Bankselect1k = value; break;
                case 0x5123: CHR0_Bankselect = CHR1_Bankselect2k = CHR3_Bankselect1k = value; break;
                case 0x5124: CHR4_Bankselect1k = value; break;
                case 0x5125: CHR2_Bankselect2k = CHR5_Bankselect1k = value; break;
                case 0x5126: CHR6_Bankselect1k = value; break;
                case 0x5127: CHR_Bankselect = CHR1_Bankselect = CHR3_Bankselect2k = CHR7_Bankselect1k = value; break;
                case 0x5128: CHR0_Bankselect1k = CHR4_Bankselect1k = value; break;
                case 0x5129: CHR0_Bankselect2k = CHR2_Bankselect2k = CHR1_Bankselect1k = CHR5_Bankselect1k = value; break;
                case 0x512a: CHR2_Bankselect1k = CHR6_Bankselect1k = value; break;
                case 0x512b: CHR_Bankselect = CHR0_Bankselect = CHR1_Bankselect = CHR1_Bankselect2k = CHR3_Bankselect2k = CHR3_Bankselect1k = CHR7_Bankselect1k = value; break;
                case 0x5130: break; //Upper CHR Bank bits 
                case 0x5200: break; //Vertical Split Mode
                case 0x5201: break; //Vertical Split Scroll
                case 0x5202: break; //Vertical Split Bank  
                case 0x5203: break; //IRQ Counter 
                case 0x5204: break; //IRQ Status
                case 0x5205: break; //??  Writes specify the eight-bit multiplicand; reads return the lower eight bits of the product
                case 0x5206: break; //??  Writes specify the eight-bit multiplier; reads return the upper eight bits of the product
            }
        }
        void mapper005write_RAM(ushort address, byte value)
        {
        }
        byte mapper005read_RAM(ushort address, byte value)
        {
            return 0;
        }
        byte mapper005read_RPG(ushort address)
        {
            if (PRG_Bankmode == 0) return 0;
            else if (PRG_Bankmode == 1)
            {
                if (address < 0xc000)
                {
                    if (address < 0xa000)
                    {
                        if (useRamBank0) return 0;
                        else return 0;
                    }
                    else
                    {
                        if (useRamBank1) return 0;
                        else return 0;
                    }
                }
                else return 0;
            }
            else if (PRG_Bankmode == 2)
            {
                if (address < 0xc000)
                {
                    if (address < 0xa000)
                    {
                        if (useRamBank0) return 0;
                        else return 0;
                    }
                    else
                    {
                        if (useRamBank1) return 0;
                        else return 0;
                    }
                }
                else if (address < 0xe000)
                {
                    if (useRamBank2) return 0;
                    else return 0;
                }
                else return 0;
            }
            else //3
            {
                if (address < 0xa000)
                {
                    if (useRamBank0) return 0;
                    else return 0;
                }
                else if (address < 0xc000)
                {
                    if (useRamBank1) return 0;
                    else return 0;

                }
                else if (address < 0xe000)
                {
                    if (useRamBank2) return 0;
                    else return 0;
                }
                else return 0;
            }
        }

        byte mapper005read_CHR(int address)
        {
            if (CHR_Bankmode == 0) return CHR_ROM[address + (CHR_Bankselect << 13)]; //8k
            else if (CHR_Bankmode == 1) //4k
            {
                if (address < 0x1000) return CHR_ROM[address + (CHR0_Bankselect << 12)];
                else return CHR_ROM[(address - 0x1000) + (CHR1_Bankselect << 12)];
            }
            else if (CHR_Bankmode == 2) //2k
            {
                if (address < 0x800) return CHR_ROM[address + (CHR0_Bankselect2k << 11)];
                else if (address < 0x1000) return CHR_ROM[(address - 0x800) + (CHR1_Bankselect2k << 11)];
                else if (address < 0x1800) return CHR_ROM[(address - 0x1000) + (CHR2_Bankselect2k << 11)];
                else return CHR_ROM[(address - 0x1800) + (CHR3_Bankselect2k << 11)];
            }
            else //1k
            {
                if (address < 0x400) return CHR_ROM[address + (CHR0_Bankselect1k << 10)];
                else if (address < 0x800) return CHR_ROM[(address - 0x400) + (CHR1_Bankselect1k << 10)];
                else if (address < 0xc00) return CHR_ROM[(address - 0x800) + (CHR2_Bankselect1k << 10)];
                else if (address < 0x1000) return CHR_ROM[(address - 0xc00) + (CHR3_Bankselect1k << 10)];
                else if (address < 0x1400) return CHR_ROM[(address - 0x1000) + (CHR4_Bankselect1k << 10)];
                else if (address < 0x1800) return CHR_ROM[(address - 0x1400) + (CHR5_Bankselect1k << 10)];
                else if (address < 0x1c00) return CHR_ROM[(address - 0x1800) + (CHR6_Bankselect1k << 10)];
                else return CHR_ROM[(address - 0x1c00) + (CHR7_Bankselect1k << 10)];
            }
        }
    }
}
