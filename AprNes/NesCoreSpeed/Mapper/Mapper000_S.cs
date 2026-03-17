namespace AprNes
{
    unsafe public class Mapper000_S : IMapper_S
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
                               int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            UpdateChrPtrs();
            UpdatePrgPtrs();
        }

        void UpdateChrPtrs()
        {
            byte* b = (CHR_ROM_count > 0) ? CHR_ROM : ppu_ram;
            for (int i = 0; i < 8; i++) NesCoreSpeed.chrBankPtrs_S[i] = b + i * 1024;
        }

        void UpdatePrgPtrs()
        {
            // NROM: mirror if 16K, direct if 32K
            if (PRG_ROM_count == 1)
            {
                for (int i = 0; i < 4; i++) NesCoreSpeed.prgBankPtrs_S[4 + i] = PRG_ROM + i * 8192;
                for (int i = 0; i < 4; i++) NesCoreSpeed.prgBankPtrs_S[i]     = PRG_ROM + i * 8192; // mirror
            }
            else
            {
                for (int i = 0; i < 8; i++) NesCoreSpeed.prgBankPtrs_S[i] = PRG_ROM + i * 8192;
            }
        }

        public byte MapperR_PRG(ushort address)
        {
            // NROM: 16K or 32K, mirrored if needed
            int off = address - 0x8000;
            if (PRG_ROM_count == 1) off &= 0x3FFF;
            return PRG_ROM[off];
        }

        public void MapperW_PRG(ushort address, byte value) { }

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address & 0x1FFF];
            return CHR_ROM[address & 0x1FFF];
        }

        public byte MapperR_RAM(ushort address)  { return NesCoreSpeed.NES_MEM_S[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCoreSpeed.NES_MEM_S[address] = value; }
        public byte MapperR_EXP(ushort address)  { return 0; }
        public void MapperW_EXP(ushort address, byte value) { }
    }
}
