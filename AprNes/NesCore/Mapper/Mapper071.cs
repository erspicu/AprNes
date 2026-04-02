namespace AprNes
{
    // Camerica / Codemasters BF909x — Mapper 071
    // PRG: 16KB switchable at $8000, last 16KB fixed at $C000
    // CHR: 8KB CHR-RAM (no CHR-ROM banking)
    // BF9097 variant (SubMapper 1 / auto-detect via $9000 write):
    //   $9000 write bit4 controls single-screen mirroring (1=ScreenA, 0=ScreenB)
    // Non-BF9097: all $8000-$FFFF writes select PRG bank
    // BF9097:     $C000-$FFFF = PRG bank, $8000-$BFFF = mirroring control
    unsafe public class Mapper071 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int CHR_ROM_count;
        int PRG_ROM_count;
        int* Vertical;
        int prgBank;
        bool bf9097Mode;

        public int Submapper; // 1 = BF9097

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram, int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM;
            CHR_ROM = _CHR_ROM;
            ppu_ram = _ppu_ram;
            CHR_ROM_count = _CHR_ROM_count;
            PRG_ROM_count = _PRG_ROM_count;
            Vertical = _Vertical;
        }

        public void Reset()
        {
            prgBank = 0;
            bf9097Mode = (Submapper == 1);
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        public void MapperW_PRG(ushort address, byte value)
        {
            // $9000 write auto-enables BF9097 mode (Firehawk detection)
            if (address >= 0x9000 && address < 0xA000)
                bf9097Mode = true;

            if (address >= 0xC000 || !bf9097Mode)
            {
                // PRG bank select
                prgBank = value;
            }
            else if (bf9097Mode)
            {
                // BF9097 mirroring: bit4 = 1→ScreenA, 0→ScreenB
                *Vertical = (value & 0x10) != 0 ? 2 : 3;
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            int n16k = PRG_ROM_count;
            if (address < 0xC000)
                return PRG_ROM[(address - 0x8000) + ((prgBank % n16k) << 14)];
            return PRG_ROM[(address - 0xC000) + ((n16k - 1) << 14)];
        }

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void UpdateCHRBanks()
        {
            byte* b = CHR_ROM_count == 0 ? ppu_ram : CHR_ROM;
            for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = b + i * 1024;
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
        public void CpuCycle() { }
        public void CpuClockRise() { }
        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;
        public void NotifyA12(int addr, int ppuAbsCycle) { }
            public void Cleanup() { }
}
}
