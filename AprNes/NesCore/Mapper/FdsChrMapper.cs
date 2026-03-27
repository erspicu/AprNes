namespace AprNes
{
    /// <summary>
    /// Minimal IMapper shim for FDS mode.
    /// Only provides CHR-RAM read/write for PPU function pointer setup.
    /// All CPU-side methods are no-ops (overridden by FDS.cs function pointers).
    /// </summary>
    unsafe public class FdsChrMapper : IMapper
    {
        byte* ppu_ram;

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            ppu_ram = _ppu_ram;
        }

        public void Reset() { }

        // CPU-side: all no-ops (FDS.cs overrides function pointers)
        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return 0; }
        public void MapperW_RAM(ushort address, byte value) { }
        public byte MapperR_RPG(ushort address) { return 0; }
        public void MapperW_PRG(ushort address, byte value) { }

        // CHR-RAM: identity-mapped 8KB
        public byte MapperR_CHR(int address) { return ppu_ram[address & 0x1FFF]; }
        public void MapperW_CHR(int addr, byte val) { ppu_ram[addr & 0x1FFF] = val; }

        public void UpdateCHRBanks()
        {
            for (int i = 0; i < 8; i++)
                NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
        }

        public void CpuCycle() { }
        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void Cleanup() { }
    }
}
