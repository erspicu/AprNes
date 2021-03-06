﻿

namespace AprNes
{
    unsafe public partial class NesCore
    {
        //GxROM http://wiki.nesdev.com/w/index.php/GxROM ok
        static void mapper066write_ROM(ushort address, byte value)
        {
            CHR_Bankselect = value & 3; // Select 8 KB CHR ROM bank for PPU $0000-$1FFF
            PRG_Bankselect = (value & 0x30) >> 4; //select 32 KB PRG ROM bank for CPU $8000-$FFFF
        }

        static byte mapper066read_RPG(ushort address)
        {
            return PRG_ROM[(address - 0x8000) + (PRG_Bankselect << 15)];
        }

        static byte mapper066read_CHR(int address)
        {
            return CHR_ROM[ address + (CHR_Bankselect <<13)  ];
        }
    }
}

