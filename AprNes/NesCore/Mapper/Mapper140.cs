namespace AprNes
{
    // Jaleco JF-11 / JF-14 — Mapper 140
    // Register at $6000-$7FFF (write-only):
    //   bits[5:4] = PRG 32KB bank select
    //   bits[3:0] = CHR 8KB bank select
    // PRG: 32KB swappable at $8000-$FFFF
    // CHR: 8KB swappable at PPU $0000-$1FFF
    // Mirroring: fixed from header
    unsafe public class Mapper140 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int prgBank;  // 32KB bank select (bits 3:0)
        int chrBank;  // 8KB CHR bank select (bits 7:4)

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;
        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void CpuCycle() { }
        public void CpuClockRise() { }

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
        }

        public void Reset()
        {
            prgBank = 0; chrBank = 0;
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }

        // RAM area ($6000-$7FFF) acts as register for this mapper
        public void MapperW_RAM(ushort address, byte value)
        {
            // Write to $6000-$7FFF = register write
            prgBank = (value >> 4) & 0x03;
            chrBank = value & 0x0F;
            UpdateCHRBanks();
        }

        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        // No PRG writes ($8000-$FFFF are read-only)
        public void MapperW_PRG(ushort address, byte value) { }

        public byte MapperR_RPG(ushort address)
        {
            int total32k = PRG_ROM_count / 2;
            if (total32k == 0) total32k = 1;
            return PRG_ROM[(address - 0x8000) + ((prgBank % total32k) << 15)];
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + i * 1024;
                return;
            }
            int total8k = CHR_ROM_count;
            byte* b = CHR_ROM + ((chrBank % total8k) << 13);
            for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = b + i * 1024;
        }

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
            public void Cleanup() { }
}
}
