using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        //AxROM https://wiki.nesdev.com/w/index.php/AxROM need check !!!
        void mapper007write_ROM(ushort address, byte value)
        {
            PRG_Bankselect = value & 7;
            ScreenSingle = ScreenSpecial = ((value & 0x10) > 0) ? true : false;
        }

        byte mapper007read_RPG(ushort address)
        {
            return PRG_ROM[(address - 0x8000) + (PRG_Bankselect << 15)];
        }

        byte mapper007read_CHR(int address)
        {
            return CHR_ROM[address ];
        }
    }
}
