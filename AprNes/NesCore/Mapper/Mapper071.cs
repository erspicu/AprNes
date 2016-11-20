using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        //Camerica http://wiki.nesdev.com/w/index.php?title=INES_Mapper_071 need check!
        void mapper071write_ROM(ushort address, byte value)
        {
            //Select 16 KiB PRG ROM bank for CPU $8000-$BFFF
            if (address >= 0xc000 && address <= 0xffff) PRG_Bankselect = (value & 0xf);
        }

        byte mapper071read_RPG(ushort address)
        {
            if (address < 0xc000) return PRG_ROM[(address - 0x8000) + (PRG_Bankselect << 14)]; // swap
            else return PRG_ROM[(address - 0xc000) + (PRG_ROM_count << 14)];
        }

        byte mapper071read_CHR(int address)
        {
            return CHR_ROM[address];
        }
    }

    unsafe public class Mapper071 : CMapper
    {
        //Camerica http://wiki.nesdev.com/w/index.php?title=INES_Mapper_071 need check!
        public Mapper071(byte* prgROM, byte* chrROM) : base(prgROM, chrROM) { }

        public override byte Read_CHR(int address)
        {
            return CHR_ROM[address];
        }

        public override byte Read_PRG(ushort address)
        {
            if (address < 0xc000) return PRG_ROM[(address - 0x8000) + (NesCore.PRG_Bankselect << 14)]; // swap
            else return PRG_ROM[(address - 0xc000) + (NesCore.PRG_ROM_count << 14)];
        }

        public override void Write_Rom(ushort address, byte value)
        {
            //Select 16 KiB PRG ROM bank for CPU $8000-$BFFF
            if (address >= 0xc000 && address <= 0xffff) NesCore.PRG_Bankselect = (value & 0xf);
        }
    }
}
