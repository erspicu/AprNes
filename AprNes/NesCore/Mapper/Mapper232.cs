namespace AprNes
{
    // Camerica BF9096 Quattro — Mapper 232
    // PRG: two 16KB banks; $8000-$BFFF = swappable, $C000-$FFFF = fixed to last of outer group
    //   $8000-$9FFF write: outer bank = bits[4:3] (submapper 1: bits swapped via Aladdin variant)
    //   $C000-$DFFF write: inner bank = bits[1:0]
    //   Final bank0 addr = (outerBank << 2) | innerBank
    //   Final bank1 addr = (outerBank << 2) | 3  (last of group)
    // CHR: 8KB fixed to bank 0
    // Mirroring: from header (no register control in standard variant)
    unsafe public class Mapper232 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int prgBlock;   // outer bank (2 bits), from $8000-$9FFF write
        int prgPage;    // inner bank (2 bits), from $C000-$DFFF write

        public bool IsAladdinVariant = false;  // submapper 1 = Aladdin Deck Enhancer (bit swap)

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;
        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void CpuCycle() { }

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
        }

        public void Reset()
        {
            prgBlock = 0; prgPage = 0;
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return 0; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        public void MapperW_PRG(ushort address, byte value)
        {
            if (address >= 0xC000)
            {
                // Inner bank select: bits[1:0]
                prgPage = value & 0x03;
            }
            else
            {
                // Outer bank select
                if (IsAladdinVariant)
                    prgBlock = ((value >> 4) & 0x01) | ((value >> 2) & 0x02);
                else
                    prgBlock = (value >> 3) & 0x03;
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            int total16k = PRG_ROM_count;  // PRG_ROM_count = number of 16KB pages
            // bank0 = swappable, bank1 = fixed to last of outer group
            int bank0 = (prgBlock << 2) | prgPage;
            int bank1 = (prgBlock << 2) | 3;
            bank0 %= total16k; bank1 %= total16k;

            if (address < 0xC000) return PRG_ROM[(address - 0x8000) + (bank0 << 14)];
            return PRG_ROM[(address - 0xC000) + (bank1 << 14)];
        }

        public void UpdateCHRBanks()
        {
            byte* b = CHR_ROM_count == 0 ? ppu_ram : CHR_ROM;
            for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = b + i * 1024;
        }

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];
            return CHR_ROM[address];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
    }
}
