using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AprNes
{
    public partial class NesCore
    {
        //nrom ok!
        byte mapper00read_RPG(ushort address)
        {
            return PRG_ROM[address - 0x8000];
        }

        byte mapper00read_CHR(int address)
        {
            return CHR_ROM[address];
        }
    }
}
