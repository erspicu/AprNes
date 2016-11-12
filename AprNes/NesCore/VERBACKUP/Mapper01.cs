using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AprNes
{
    public partial class NesCore
    {
        //mmc1 editing

        //http://wiki.nesdev.com/w/index.php/MMC1

        int PRG_Bankmode;
        int CHR_Bankmode;
        int Mirroring_type;
        void mapper01write_ROM(ushort address, byte value)
        {
            if ((value & 0x80) != 0)
            {
                MapperShiftCount =0;

                MapperReg = 0xc;

                //PRG_Bankmode = 2;

                //Console.WriteLine("22222222");
                //Console.ReadLine();

                return;
            }
            MapperReg |= (value & 1) << MapperShiftCount;
            if (++MapperShiftCount < 5) return;

            if (address < 0xa000)
            {
                // $8000-$9FFF
                Mirroring_type = MapperReg & 3; //(0: one-screen, lower bank; 1: one-screen, upper bank; 2: vertical; 3: horizontal)

                if (Mirroring_type == 2)
                    Vertical = true;
                else if (Mirroring_type == 3)
                    Vertical = false;

                PRG_Bankmode = (MapperReg & 0xc) >> 2;
                //0, 1: switch 32 KB at $8000, ignoring low bit of bank number;
                //2: fix first bank at $8000 and switch 16 KB bank at $C000;
                //3: fix last bank at $C000 and switch 16 KB bank at $8000

                CHR_Bankmode = (MapperReg & 0x10) >> 4;
                //(0: switch 8 KB at a time; 1: switch two separate 4 KB banks)
            }
            else if (address < 0xc000)
            {
                //$A000-$BFFF
                CHR0_Bankselect = MapperReg;//(low bit ignored in 8 KB mode)
            }
            else if (address < 0xe000)
            {
                //$C000-$DFFF
                CHR1_Bankselect = MapperReg; // (ignored in 8 KB mode)
            }
            else
            {
                //$E000-$FFFF
                PRG_Bankselect = MapperReg & 0xf;
            }
            //init
            MapperShiftCount = MapperReg = 0;
        }

        int tmp_select = 0;
        byte mapper01read_RPG(ushort address) // need fix
        {

            if (PRG_Bankmode != 0)
            {
                //Console.WriteLine("!!!! not 0,1");
                // Console.ReadLine();
            }

            if (PRG_Bankmode == 0 || PRG_Bankmode == 1)
            {
                if (PRG_Bankselect == 0)
                {
                    tmp_select = PRG_ROM_count - 2;
                }
                else
                {
                    tmp_select = PRG_Bankselect;
                }
               // Console.WriteLine(PRG_Bankselect + " " + PRG_ROM[(address - 0x8000) + (tmp_select * 0x4000)].ToString("X2")); 
               // Console.ReadLine();
            
                /*
                if (PRG_Bankselect == 0)
                {
                    tmp_select = PRG_ROM_count - 2;
                }
                else
                {
                    tmp_select =  PRG_Bankselect;
                }*/

                return PRG_ROM[(address - 0x8000) + (tmp_select * 0x4000)];//32k
            }
            else if (PRG_Bankmode == 2)
            {
                if (address < 0xc000)
                    return PRG_ROM[address - 0x8000];//fixed
                else
                    return PRG_ROM[(address - 0xc000) + (PRG_Bankselect << 14)]; // switch
            }
            else
            {
                if (address < 0xc000)
                    return PRG_ROM[(address - 0x8000) + (PRG_Bankselect << 14)];//switch
                else
                    return PRG_ROM[(address - 0xc000) + ((PRG_ROM_count - 1) << 14)]; // fixed
            }
        }

        byte mapper01read_CHR(int address) //checking
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];
            if (CHR_Bankmode > 0)
            {

                //4K
                if (address < 0x1000)
                    return CHR_ROM[address + (CHR0_Bankselect << 12)];
                else
                    return CHR_ROM[(address - 0x1000) + (CHR1_Bankselect << 12)];
            }
            else
            {
                Console.WriteLine("need check !!!");
                Console.ReadLine();
                return CHR_ROM[address + 0x2000 * (CHR0_Bankselect >> 1)];//need check!! 
            }
        }

        void mapper01write_CHR(int address, byte value) //ok
        {
            if (CHR_ROM_count == 0) ppu_ram[address] = value;
        }

    }
}
