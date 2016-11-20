using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        //CNROM
        void mapper003write_ROM(ushort address, byte value)
        {
            CHR_Bankselect = value & 3;
        }

        byte mapper003read_RPG(ushort address)
        {
            return PRG_ROM[address - 0x8000];
        }

        byte mapper003read_CHR(int address)
        {
            return CHR_ROM[address + (CHR_Bankselect << 13)];
        }

        //CNROM

        unsafe public class Mapper003 : CMapper
        {
            public Mapper003(byte* prgROM, byte* chrROM) : base(prgROM, chrROM) { }

            public override byte Read_CHR(int address)
            {
                return CHR_ROM[address + (CHR_Bankselect << 13)];
            }

            public override byte Read_PRG(ushort address)
            {
                return PRG_ROM[address - 0x8000];
            }

            public override void Write_Rom(ushort address, byte value)
            {
                CHR_Bankselect = value & 3;
            }
        }
    }
}
