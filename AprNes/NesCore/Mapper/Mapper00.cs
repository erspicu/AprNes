using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AprNes
{
    public partial class NesCore
    {
        //do nothing about write

        public byte mapper00read(ushort address)
        {
            return PRG_ROM[address - 0x8000];
        }
    }
}
