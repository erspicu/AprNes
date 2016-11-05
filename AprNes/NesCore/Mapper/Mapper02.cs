using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AprNes
{
    public partial class NesCore
    {
        public void mapper02write(ushort address, byte value)
        {
            RPG_Bankselect = value & 7;

        }

        public byte mapper02read(ushort address)
        {

            if (address < 0xc000)
                return PRG_ROM[(address - 0x8000) + 0x4000 * RPG_Bankselect];//siwtch
            else
                return PRG_ROM[(address - 0xc000) + 0x4000 * 7]; // fixed 
        }
    }
}
