namespace AprNes
{
    // Mapper 241 — BxROM / Subor study cartridges
    // PRG: 32KB switchable at $8000-$FFFF; write anywhere in PRG range selects bank.
    // CHR: 8KB fixed (CHR-ROM or CHR-RAM).
    // Mirroring: fixed from header.
    // No IRQ.
    //
    // Games: Subor / 小霸王 learning cartridges (1-12), Russian study carts, 16-in-1 compilations.
    // Ref: Mesen2/Core/NES/Mappers/Unlicensed/Mapper241.h — trivial impl, value → SelectPrgPage.
    unsafe public class Mapper241 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int prgBank;

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM;
            CHR_ROM = _CHR_ROM;
            ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count;
            CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
            UpdateCHRBanks();
        }

        public void Reset()
        {
            prgBank = 0;
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }

        public void MapperW_PRG(ushort address, byte value)
        {
            // $8000-$FFFF: any write selects 32KB PRG bank (value is bank index)
            prgBank = value;
        }

        public byte MapperR_RPG(ushort address)
        {
            // PRG_ROM_count is 16KB units. Total 32K banks = count/2.
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
            // 8KB fixed CHR — map linearly
            for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = CHR_ROM + (i << 10);
        }

        public byte MapperR_CHR(int address)
        {
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
        public void CpuCycle() { }
        public void CpuClockRise() { }
        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void PpuClock() { }
        public void Cleanup() { }
    }
}
