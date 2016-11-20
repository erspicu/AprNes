using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        public int[] Mapper_Allow = new int[] { 0, 1, 2, 3, 4, 7, 11, 66 }; //5,7,11,66,71

        int PRG_Bankselect = 0;
        int CHR_Bankselect = 0;

        int CHR0_Bankselect = 0;
        int CHR1_Bankselect = 0;

        //for some mapper common use
        int MapperRegBuffer = 0;
        int MapperShiftCount = 0;
        int Rom_offset = 0;

        byte MapperRouterR_ExpansionROM(ushort address)
        {
            switch (mapper)
            {
                case 5: return mapper005read_ExpansionROM(address);
                default: return 0;
            }
        }

        void MapperRouterW_ExpansionROM(ushort address, byte value)
        {
            switch (mapper)
            {
                case 5: mapper005write_ExpansionROM(address, value); break;
                default: break;
            }
        }

        void MapperRouterW_RAM(ushort address, byte value)
        {
            switch (mapper)
            {
                case 5: mapper005write_RAM(address, value); break;
                default: NES_MEM[address] = value; break;
            }
        }

        byte MapperRouterR_RAM(ushort address)
        {
            switch (mapper)
            {
                case 5: return mapper005read_RPG(address);
                default: return NES_MEM[address];
            }
        }

        void MapperRouterW_PRG(ushort address, byte value)
        {
            switch (mapper)
            {
                case 0: break;//NROM , nothing 
                case 1: mapper001write_ROM(address, value); break; //MMC1
                case 2: mapper002write_ROM(address, value); break; //UNROM
                case 3: mapper003write_ROM(address, value); break; //CNROM
                case 4: mapper004write_ROM(address, value); break; //MMC3
                case 5: break;//MMC5 , nothing
                case 7: mapper007write_ROM(address, value); break;
                case 11: mapper011write_ROM(address, value); break;
                case 66: mapper066write_ROM(address, value); break;
                case 71: mapper071write_ROM(address, value); break;
                default: break;
            }
        }

        byte MapperRouterR_RPG(ushort address)
        {
            switch (mapper)
            {
                case 0: return mapper000read_RPG(address);
                case 1: return mapper001read_RPG(address);
                case 2: return mapper002read_RPG(address);
                case 3: return mapper003read_RPG(address);
                case 4: return mapper004read_RPG(address);
                case 5: return mapper005read_RPG(address);
                case 7: return mapper007read_RPG(address);
                case 11: return mapper011read_RPG(address);
                case 66: return mapper066read_RPG(address);
                case 71: return mapper071read_RPG(address);
                default: return 0;
            }
        }

        byte MapperRouterR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];
            switch (mapper)
            {
                case 0: return mapper000read_CHR(address);
                case 1: return mapper001read_CHR(address);
                case 2: return mapper002read_CHR(address);
                case 3: return mapper003read_CHR(address);
                case 4: return mapper004read_CHR(address);
                case 5: return mapper005read_CHR(address);
                case 7: return mapper007read_CHR(address);
                case 11: return mapper011read_CHR(address);
                case 66: return mapper066read_CHR(address);
                case 71: return mapper071read_CHR(address);
                default: return 0;
            }
        }
    }
}
