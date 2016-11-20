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

        //NROM ok!
        unsafe public class Mapper000 : CMapper
        {
            public Mapper000(byte* prgROM, byte* chrROM) : base(prgROM, chrROM) { }

            public override byte Read_CHR(int address)
            {
                return CHR_ROM[address];
            }

            public override byte Read_PRG(ushort address)
            {
                return PRG_ROM[address - 0x8000];
            }

            public override void Write_Rom(ushort address, byte value)
            {
                return;
            }
        }
    }
}
