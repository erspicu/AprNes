namespace AprNes
{
    // Jaleco JF-17 — Mapper 072
    // Write to $8000-$FFFF (latch-based, bus conflicts):
    //   When bit7=1 AND prgFlag was 0: latch PRG bank = bits[2:0]
    //   When bit6=1 AND chrFlag was 0: latch CHR bank = bits[3:0]
    //   prgFlag/chrFlag track previous write's bit7/bit6
    // PRG: $8000-$BFFF = switchable 16KB, $C000-$FFFF = fixed last 16KB
    // CHR: 8KB banked.
    // No IRQ. Mirroring from header.

    unsafe public class Mapper072 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int prgBank;
        int chrBank;
        bool prgFlag;
        bool chrFlag;

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
            prgBank = 0; chrBank = 0;
            prgFlag = false; chrFlag = false;
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }

        public void MapperW_PRG(ushort address, byte value)
        {
            // Bus conflicts: data ANDed with PRG ROM byte at same address
            // For simplicity, use value as-is (bus conflict emulation optional)
            if (!prgFlag && (value & 0x80) != 0)
                prgBank = value & 0x07;   // bits[2:0] = PRG 16KB bank

            if (!chrFlag && (value & 0x40) != 0)
                chrBank = value & 0x0F;   // bits[3:0] = CHR 8KB bank

            prgFlag = (value & 0x80) != 0;
            chrFlag = (value & 0x40) != 0;

            UpdateCHRBanks();
        }

        public byte MapperR_RPG(ushort address)
        {
            int total16k = PRG_ROM_count;
            if (address < 0xC000)
            {
                int bank = prgBank % total16k;
                return PRG_ROM[(address - 0x8000) + (bank << 14)];
            }
            else
            {
                return PRG_ROM[(address - 0xC000) + ((total16k - 1) << 14)];
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
            public void Cleanup() { }
}
}
