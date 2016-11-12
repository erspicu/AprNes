using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AprNes
{
    public partial class NesCore
    {
        //unrom ok!
        void mapper02write_ROM(ushort address, byte value)
        {
            PRG_Bankselect = value & 7;
        }

        byte mapper02read_RPG(ushort address)
        {
            if (address < 0xc000)
                return PRG_ROM[(address - 0x8000) + (PRG_Bankselect << 14)];//siwtch
            else
                return PRG_ROM[(address - 0xc000) + Rom_offset]; // fixed 
        }

        byte mapper02read_CHR(int address)
        {
            return ppu_ram[address];
        }

        void mapper02write_CHR(int address, byte value)
        {
            ppu_ram[address] = value;
        }
    }
}
