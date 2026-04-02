namespace AprNes
{
    unsafe public class Mapper003 : IMapper
    {
        //CNROM
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int CHR_ROM_count;
        int PRG_ROM_count;
        int* Vertical;
        int CHR_Bankselect;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram, int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM;
            CHR_ROM = _CHR_ROM;
            ppu_ram = _ppu_ram;
            CHR_ROM_count = _CHR_ROM_count;
            PRG_ROM_count = _PRG_ROM_count;
            Vertical = _Vertical;
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        public void MapperW_PRG(ushort address, byte value)
        {
            CHR_Bankselect = value & 3;
            UpdateCHRBanks();
        }

        public byte MapperR_RPG(ushort address)
        {
            return PRG_ROM[address - 0x8000];
        }

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];
            return CHR_ROM[address + ((CHR_Bankselect) << 13)];
        }

        public void UpdateCHRBanks()
        {
            byte* b = CHR_ROM_count == 0 ? ppu_ram : CHR_ROM + (CHR_Bankselect << 13);
            for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = b + i * 1024;
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
        public void Reset() { }
        public void CpuCycle() { }
        public void CpuClockRise() { }
        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;
        public void NotifyA12(int addr, int ppuAbsCycle) { }
            public void Cleanup() { }
}
}
