namespace AprNes
{
    // Jaleco JF-09 / JF-10 / JF-18 — Mapper 087
    // Write to $6000-$7FFF:
    //   bit1 = CHR bank bit 1
    //   bit0 = CHR bank bit 0  (bits are NOT swapped per NESdev)
    //   actual CHR bank = ((data & 2) >> 1) | ((data & 1) << 1)
    //   (swap: D0→bank bit1, D1→bank bit0)
    // NESdev: "CHR bank = D1D0" but in practice: CHR = (d>>1&1)|(d<<1&2)
    // PRG 32KB fixed. CHR 8KB banked. No IRQ.

    unsafe public class Mapper087 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int chrBank;

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
        }

        public void Reset()
        {
            chrBank = 0;
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }

        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value)
        {
            // $6000-$7FFF write: swap bits D0 and D1 to get CHR bank
            chrBank = ((value & 2) >> 1) | ((value & 1) << 1);
            UpdateCHRBanks();
        }

        public void MapperW_PRG(ushort address, byte value) { }  // No PRG bank switching

        public byte MapperR_RPG(ushort address)
        {
            // PRG 32KB fixed — handle both 16KB and 32KB ROMs
            int offset = address - 0x8000;
            if (PRG_ROM_count == 1)
            {
                // 32KB ROM
                return PRG_ROM[offset];
            }
            else
            {
                // Mirror if needed
                return PRG_ROM[offset % (PRG_ROM_count << 15)];
            }
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
                return;
            }
            int total8k = CHR_ROM_count;
            int bank = chrBank % total8k;
            byte* b = CHR_ROM + (bank << 13);
            for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = b + (i << 10);
        }

        public byte MapperR_CHR(int address) { return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF]; }
        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }

        public void CpuCycle() { }
        public void CpuClockRise() { }
        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void PpuClock() { }
            public void Cleanup() { }
}
}
