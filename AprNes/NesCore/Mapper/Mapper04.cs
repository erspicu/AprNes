using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AprNes
{
    public partial class NesCore
    {
        //editing
        //MMC3
        void mapper04write_ROM(ushort address, byte value)
        {
        }
        byte mapper04read_RPG(ushort address)
        {
            return 0;
        }
        byte mapper04read_CHR(int address)
        {
            return 0;
        }
        void mapper04write_CHR(int address, byte value)
        {
        }
    }
}
