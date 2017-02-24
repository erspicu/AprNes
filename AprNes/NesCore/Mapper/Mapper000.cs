using System;

namespace AprNes
{
    unsafe public class Mapper000 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int CHR_ROM_count;

        //NROM ok!
        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,int _PRG_ROM_count, int _CHR_ROM_count ,int * _Vertical)
        {
            PRG_ROM = _PRG_ROM;
            CHR_ROM = _CHR_ROM;
            ppu_ram = _ppu_ram;
            CHR_ROM_count = _CHR_ROM_count;
        }

        public byte MapperR_ExpansionROM(ushort address) { return 0; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return 0; }
        public void MapperW_PRG(ushort address, byte value) { }

        public byte MapperR_RPG(ushort address)
        {
            return PRG_ROM[address - 0x8000];
        }

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];
            return CHR_ROM[address];
        }
    }
}
