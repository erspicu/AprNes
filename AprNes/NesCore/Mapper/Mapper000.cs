using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        //NROM ok!
        byte mapper000read_RPG(ushort address)
        {
            return PRG_ROM[address - 0x8000];
        }

        byte mapper000read_CHR(int address)
        {
            return CHR_ROM[address];
        }
    }
}
