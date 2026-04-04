namespace AprNes
{
    // Crazy Climber / UnRom_180 — Mapper 180
    // UNROM variant: fixed FIRST 16KB at $8000-$BFFF, switchable LAST at $C000-$FFFF.
    // (opposite of standard Mapper002 where $8000 is switchable and $C000 is fixed)
    // CHR: 8KB CHR-RAM
    unsafe public class Mapper180 : IMapper
    {
        byte* PRG_ROM, ppu_ram;
        int PRG_ROM_count;
        int* Vertical;

        int prgBank; // 16KB bank at $C000-$FFFF

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;
        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void PpuClock() { }
        public void CpuCycle() { }
        public void CpuClockRise() { }

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count;
            Vertical = _Vertical;
        }

        public void Reset()
        {
            prgBank = 0;
            UpdateCHRBanks();
        }

        public void UpdateCHRBanks()
        {
            // 8KB CHR-RAM in ppu_ram[0..8191]
            for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + i * 1024;
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        public void MapperW_PRG(ushort address, byte value)
        {
            prgBank = value & 0x07;
        }

        public byte MapperR_RPG(ushort address)
        {
            int n16k = PRG_ROM_count; // PRG_ROM_count is already in 16KB units
            if (address < 0xC000)
                return PRG_ROM[(address - 0x8000)]; // first 16KB fixed (bank 0)
            return PRG_ROM[(address - 0xC000) + ((prgBank % n16k) << 14)];
        }

        // CHR-RAM
        public byte MapperR_CHR(int address) { return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF]; }
        public void MapperW_CHR(int addr, byte val) { NesCore.chrBankPtrs[(addr >> 10) & 7][addr & 0x3FF] = val; }
            public void Cleanup() { }
}
}
