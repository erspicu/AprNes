using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        //AxROM https://wiki.nesdev.com/w/index.php/AxROM need check !!!
        void mapper007write_ROM(ushort address, byte value)
        {
            PRG_Bankselect = value & 7;
            //ScreenSingle = ((value & 0x10) > 0) ? true : false;
        }

        byte mapper007read_RPG(ushort address)
        {
            return PRG_ROM[(address - 0x8000) + (PRG_Bankselect << 15)];
        }

        byte mapper007read_CHR(int address)
        {
            return CHR_ROM[address];
        }
    }

    unsafe public class Mapper007 : CMapper
    {
        //AxROM https://wiki.nesdev.com/w/index.php/AxROM need check !!!
        public Mapper007(byte* prgROM, byte* chrROM) : base(prgROM, chrROM) { }

        public override byte Read_CHR(int address)
        {
            return CHR_ROM[address];
        }

        public override byte Read_PRG(ushort address)
        {
            return PRG_ROM[(address - 0x8000) + (NesCore.PRG_Bankselect << 15)];
        }

        public override void Write_Rom(ushort address, byte value)
        {
            NesCore.PRG_Bankselect = value & 7;
            //ScreenSingle = ((value & 0x10) > 0) ? true : false;
        }
    }
}
