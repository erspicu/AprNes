using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AprNes
{
    public partial class NesCore
    {
        //cnrom ok!
        void mapper03write_ROM(ushort address, byte value)
        {
            CHR_Bankselect = value & 3;
        }

        byte mapper03read_RPG(ushort address)
        {
            return PRG_ROM[address - 0x8000];
        }

        byte mapper03read_CHR(int address)
        {
            return CHR_ROM[address + (CHR_Bankselect << 13)];
        }
    }
}
