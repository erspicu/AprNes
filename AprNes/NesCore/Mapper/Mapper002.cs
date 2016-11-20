using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        //UNROM
        void mapper002write_ROM(ushort address, byte value)
        {
            PRG_Bankselect = value & 7;
        }

        byte mapper002read_RPG(ushort address)
        {
            if (address < 0xc000) return PRG_ROM[(address - 0x8000) + (PRG_Bankselect << 14)];//siwtch
            else return PRG_ROM[(address - 0xc000) + Rom_offset]; // fixed 
        }

        byte mapper002read_CHR(int address)
        {
            return ppu_ram[address];
        }

        //UNROM
        unsafe public class Mapper002 : CMapper
        {
            public Mapper002(byte* prgROM, byte* chrROM) : base(prgROM, chrROM) { }

            public override byte Read_CHR(int address)
            {
                return NesCore.ppu_ram[address];
            }

            public override byte Read_PRG(ushort address)
            {
                if (address < 0xc000) return PRG_ROM[(address - 0x8000) + (PRG_Bankselect << 14)];//siwtch
                else return PRG_ROM[(address - 0xc000) + Rom_offset]; // fixed 
            }

            public override void Write_Rom(ushort address, byte value)
            {
                PRG_Bankselect = value & 7;
            }
        }
    }
}
