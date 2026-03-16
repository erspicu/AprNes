namespace AprNes
{
    // Color Dreams (Mapper 011): write $8000-$FFFF sets PRG (bits 0-1) and CHR (bits 4-7) banks
    unsafe public class Mapper011_S : IMapper_S
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int prgBank = 0, chrBank = 0;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
                               int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            prgBank = 0; chrBank = 0;
        }

        public byte MapperR_PRG(ushort address)
        {
            return PRG_ROM[(prgBank * 0x8000) + (address - 0x8000)];
        }

        public void MapperW_PRG(ushort address, byte value)
        {
            prgBank = value & 3;
            chrBank = (value >> 4) & 0xF;
        }

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address & 0x1FFF];
            return CHR_ROM[(chrBank * 0x2000) + (address & 0x1FFF)];
        }

        public byte MapperR_RAM(ushort address)  { return NesCoreSpeed.NES_MEM_S[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCoreSpeed.NES_MEM_S[address] = value; }
        public byte MapperR_EXP(ushort address)  { return 0; }
        public void MapperW_EXP(ushort address, byte value) { }
    }
}
