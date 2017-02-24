using System;

namespace AprNes
{
    unsafe public class Mapper007 : IMapper
    {
        //AxROM https://wiki.nesdev.com/w/index.php/AxROM need check !!!

        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int CHR_ROM_count;
        int PRG_ROM_count;
        int* Vertical;
        private int PRG_Bankselect;
        private bool ScreenSingle; //need fix!!!!!!
        private bool ScreenSpecial;//need fix!!!!!!

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram, int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM;
            CHR_ROM = _CHR_ROM;
            ppu_ram = _ppu_ram;
            CHR_ROM_count = _CHR_ROM_count;
            PRG_ROM_count = _PRG_ROM_count;
            Vertical = _Vertical;
        }

        public byte MapperR_ExpansionROM(ushort address)
        {
            throw new NotImplementedException();
        }

        public void MapperW_ExpansionROM(ushort address, byte value)
        {
            throw new NotImplementedException();
        }

        public void MapperW_RAM(ushort address, byte value)
        {
            throw new NotImplementedException();
        }

        public byte MapperR_RAM(ushort address)
        {
            throw new NotImplementedException();
        }

        public void MapperW_PRG(ushort address, byte value)
        {
            PRG_Bankselect = value & 0xf; // fixed 7 -> 0xf
            ScreenSingle = ScreenSpecial = ((value & 0x10) > 0) ? true : false;
        }

        public byte MapperR_RPG(ushort address)
        {
            return PRG_ROM[(address - 0x8000) + (PRG_Bankselect << 15)];
        }

        public byte MapperR_CHR(int address)
        {
            return CHR_ROM[address];
        }
    }
}
