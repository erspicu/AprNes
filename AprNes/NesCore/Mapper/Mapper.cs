using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AprNes
{
    public partial class NesCore
    {
        int[] Mapper_Allow = new int[] { 0, 1, 2, 3 };

        int PRG_Bankselect = 0;
        int CHR_Bankselect = 0;

        int CHR0_Bankselect = 0;
        int CHR1_Bankselect = 0;

        //for some mapper common use
        int MapperRegBuffer = 0;
        int MapperShiftCount = 0;
        int Rom_offset = 0;

        void MapperRouterW(ushort address, byte value)
        {
            switch (mapper)
            {
                case 0: break;//NROM , nothing 
                case 1: mapper01write_ROM(address, value); break; //MMC1
                case 2: mapper02write_ROM(address, value); break; //UNROM
                case 3: mapper03write_ROM(address, value); break; //CNROM
                case 4: mapper04write_ROM(address, value); break; //MMC3
                default: break;
            }
        }

        byte MapperRouterR_RPG(ushort address)
        {
            switch (mapper)
            {
                case 0: return mapper00read_RPG(address);
                case 1: return mapper01read_RPG(address);
                case 2: return mapper02read_RPG(address);
                case 3: return mapper03read_RPG(address);
                case 4: return mapper04read_RPG(address);
                default: return 0;
            }
        }

        byte MapperRouterR_CHR(int address)
        {
            switch (mapper)
            {
                case 0: return mapper00read_CHR(address);
                case 1: return mapper01read_CHR(address);
                case 2: return mapper02read_CHR(address);
                case 3: return mapper03read_CHR(address);
                case 4: return mapper03read_CHR(address);
                default: return 0;
            }
        }

        void MapperRouterW_CHR(int address, byte vaalue)
        {
            switch (mapper)
            {
                case 1: mapper01write_CHR(address, vaalue); break;
                case 2: mapper02write_CHR(address, vaalue); break;
                case 4: mapper04write_CHR(address, vaalue); break;
                default: break;
            }
        }
    }
}
