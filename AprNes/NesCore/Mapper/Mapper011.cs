using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        //Color Dreams https://wiki.nesdev.com/w/index.php/Color_Dreams need check! 
        void mapper011write_ROM(ushort address, byte value)
        {
            PRG_Bankselect = value & 3; //Select 32 KB PRG ROM bank for CPU $8000-$FFFF
            CHR_Bankselect = (value & 0xf0) >> 4;//Select 8 KB CHR ROM bank for PPU $0000-$1FFF
        }

        byte mapper011read_RPG(ushort address)
        {
            return PRG_ROM[(address - 0x8000) + (PRG_Bankselect << 15)];
        }

        byte mapper011read_CHR(int address)
        {
            return CHR_ROM[CHR_Bankselect << 13];
        }
    }
}
