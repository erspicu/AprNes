using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        //GxROM http://wiki.nesdev.com/w/index.php/GxROM need check!
        void mapper066write_ROM(ushort address, byte value)
        {
            CHR_Bankselect = value & 3; // Select 8 KB CHR ROM bank for PPU $0000-$1FFF
            PRG_Bankselect = (value & 0x30) >> 4; //select 32 KB PRG ROM bank for CPU $8000-$FFFF
        }

        byte mapper066read_RPG(ushort address)
        {
            return PRG_ROM[(address - 0x8000) + (PRG_Bankselect << 15)];
        }

        byte mapper066read_CHR(int address)
        {
            return CHR_ROM[CHR_ROM_count << 13];
        }
    }

    unsafe public class Mapper066 : CMapper
    {
        //GxROM http://wiki.nesdev.com/w/index.php/GxROM need check!
        public Mapper066(byte* prgROM, byte* chrROM) : base(prgROM, chrROM) { }

        public override byte Read_CHR(int address)
        {
            return CHR_ROM[NesCore.CHR_ROM_count << 13];
        }

        public override byte Read_PRG(ushort address)
        {
            return PRG_ROM[(address - 0x8000) + (NesCore.PRG_Bankselect << 15)];
        }

        public override void Write_Rom(ushort address, byte value)
        {
            NesCore.PRG_Bankselect = value & 3; //Select 32 KB PRG ROM bank for CPU $8000-$FFFF
            NesCore.CHR_Bankselect = (value & 0xf0) >> 4;//Select 8 KB CHR ROM bank for PPU $0000-$1FFF
        }
    }
}

