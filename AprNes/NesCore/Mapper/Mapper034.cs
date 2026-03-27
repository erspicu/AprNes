namespace AprNes
{
    // Nina-1 — Deadly Towers (U), Impossible Mission II (U), Mashou (J)
    // Two sub-mappers in one:
    //   Deadly Towers:        $8000-$FFFF → Select 32K PRG bank
    //   Impossible Mission II: $7FFD → 32K PRG, $7FFE → 4K CHR@$0000, $7FFF → 4K CHR@$1000
    // No IRQ, no mirroring control (fixed from header).
    // CHR-RAM supported (Deadly Towers / Mashou have no CHR-ROM).
    unsafe public class Mapper034 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int CHR_ROM_count, PRG_ROM_count;
        int* Vertical;

        int prgBank;
        int chrBank0, chrBank1;  // 4K CHR bank indices

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM;
            CHR_ROM = _CHR_ROM;
            ppu_ram = _ppu_ram;
            CHR_ROM_count = _CHR_ROM_count;
            PRG_ROM_count = _PRG_ROM_count;
            Vertical = _Vertical;
            UpdateCHRBanks();
        }

        public void Reset()
        {
            prgBank = 0;
            chrBank0 = chrBank1 = 0;
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        public void MapperW_RAM(ushort address, byte value)
        {
            // $7FFD/$7FFE/$7FFF: Impossible Mission II bank registers
            // Also write through to RAM (WritePrgRam equivalent — game may read back)
            NesCore.NES_MEM[address] = value;
            if      (address == 0x7FFD) { prgBank  = value & 0x01; }
            else if (address == 0x7FFE) { chrBank0 = value & 0x0F; UpdateCHRBanks(); }
            else if (address == 0x7FFF) { chrBank1 = value & 0x0F; UpdateCHRBanks(); }
        }

        public void MapperW_PRG(ushort address, byte value)
        {
            // $8000-$FFFF: Deadly Towers / Mashou 32K PRG select (CHR-RAM variant only)
            // Impossible Mission II uses $7FFD-$7FFF instead; ignore $8000+ writes for it
            if (CHR_ROM_count == 0) prgBank = value;
        }

        public byte MapperR_RPG(ushort address)
        {
            // 32K bank mapped at $8000-$FFFF
            int total32k = PRG_ROM_count / 2;
            if (total32k < 1) total32k = 1;
            int bank = prgBank % total32k;
            return PRG_ROM[(address - 0x8000) + (bank << 15)];
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
                return;
            }
            int total4k = CHR_ROM_count * 2;
            byte* b0 = CHR_ROM + ((chrBank0 % total4k) << 12);
            byte* b1 = CHR_ROM + ((chrBank1 % total4k) << 12);
            NesCore.chrBankPtrs[0] = b0;
            NesCore.chrBankPtrs[1] = b0 + 1024;
            NesCore.chrBankPtrs[2] = b0 + 2048;
            NesCore.chrBankPtrs[3] = b0 + 3072;
            NesCore.chrBankPtrs[4] = b1;
            NesCore.chrBankPtrs[5] = b1 + 1024;
            NesCore.chrBankPtrs[6] = b1 + 2048;
            NesCore.chrBankPtrs[7] = b1 + 3072;
        }

        public byte MapperR_CHR(int address)
        {
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
        public void CpuCycle() { }
        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;
        public void NotifyA12(int addr, int ppuAbsCycle) { }
            public void Cleanup() { }
}
}
