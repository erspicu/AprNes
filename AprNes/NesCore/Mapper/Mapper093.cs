namespace AprNes
{
    // Sunsoft-2 (Fantasy Zone II variant) — Mapper 093
    // Write to $8000-$FFFF:
    //   bits[6:4] = PRG 16KB bank at $8000-$BFFF
    //   $C000-$FFFF fixed to last 16KB
    // CHR 8KB fixed (CHR-RAM or single bank). No IRQ.
    //
    // NESdev Mapper093: "74161/32" style, PRG bank in D6:D4

    unsafe public class Mapper093 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int prgBank;

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
            prgBank = 0;
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }

        public void MapperW_PRG(ushort address, byte value)
        {
            // bits[6:4] = PRG 16KB bank
            prgBank = (value >> 4) & 0x07;
        }

        public byte MapperR_RPG(ushort address)
        {
            int total16k = PRG_ROM_count;  // PRG_ROM_count = number of 16KB banks
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
            // CHR is fixed (no bankswitching) — just point to base
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
            }
            else
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = CHR_ROM + (i << 10);
            }
        }

        public byte MapperR_CHR(int address) { return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF]; }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }

        public void CpuCycle() { }
        public void CpuClockRise() { }
        public void NotifyA12(int addr, int ppuAbsCycle) { }
            public void Cleanup() { }
}
}
