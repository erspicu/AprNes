namespace AprNes
{
    // Declares how a mapper wants PPU A12 notifications routed.
    // Set once at init; PPU reads NesCore.mapperA12Mode rather than checking mapper numbers.
    public enum MapperA12Mode
    {
        None   = 0,  // no A12 notification needed (most mappers)
        MMC3   = 1,  // MMC3-style: NT fetch + sprite CHR phase 3
        MMC2_4 = 2,  // MMC2/MMC4-style: BG CHR high + sprite CHR phase 5 latch
    }

    unsafe interface IMapper
    {
        MapperA12Mode A12NotifyMode { get; }
        void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM , byte* _ppu_ram ,int _PRG_ROM_count, int _CHR_ROM_count ,int * _Vertical);
        byte MapperR_ExpansionROM(ushort address);
        void MapperW_ExpansionROM(ushort address, byte value);
        void MapperW_RAM(ushort address, byte value);
        byte MapperR_RAM(ushort address);
        void MapperW_PRG(ushort address, byte value);
        byte MapperR_RPG(ushort address);
        byte MapperR_CHR(int address);
        void MapperW_CHR(int addr, byte val);
        void UpdateCHRBanks();
        void Reset();
        void CpuCycle();
        void NotifyA12(int addr, int ppuAbsCycle);
    }
}
