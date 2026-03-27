namespace AprNes
{
    // Konami VRC2b — Contra (J), Getsufuu Maden (J)
    // Identical to VRC2a except:
    //   - Address lines: chip-A0 = CPU-bit0, chip-A1 = CPU-bit1 (standard, not swapped)
    //   - CHR bank index uses all 8 bits (no >> 1 shift)
    unsafe public class Mapper023 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int prgBank0, prgBank1;
        byte[] chrLo = new byte[8];  // low  4 bits of each 1K CHR bank index
        byte[] chrHi = new byte[8];  // high 5 bits of each 1K CHR bank index

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
            prgBank0 = prgBank1 = 0;
            for (int i = 0; i < 8; i++) { chrLo[i] = chrHi[i] = 0; }
            UpdateCHRBanks();
        }

        // VRC2b: chip-A0 = cpu-bit0, chip-A1 = cpu-bit1 (standard)
        static int A0(ushort addr) => addr & 1;
        static int A1(ushort addr) => (addr >> 1) & 1;

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }

        public void MapperW_PRG(ushort address, byte value)
        {
            int group = (address >> 12) & 0xF;
            int a0 = A0(address);
            int a1 = A1(address);

            if      (group == 0x8) { prgBank0 = value & 0x1F; }
            else if (group == 0x9) { *Vertical = (value & 1) == 0 ? 1 : 0; }
            else if (group == 0xA) { prgBank1 = value & 0x1F; }
            else if (group >= 0xB && group <= 0xE)
            {
                int reg = (group - 0xB) * 2 + a1;
                if (a0 == 0) chrLo[reg] = (byte)(value & 0x0F);
                else         chrHi[reg] = (byte)(value & 0x1F);
                UpdateCHRBanks();
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            int n = PRG_ROM_count * 2;  // total 8K banks
            if (address < 0xA000) return PRG_ROM[(address - 0x8000) + (prgBank0 % n) * 0x2000];
            if (address < 0xC000) return PRG_ROM[(address - 0xA000) + (prgBank1 % n) * 0x2000];
            if (address < 0xE000) return PRG_ROM[(address - 0xC000) + (n - 2) * 0x2000];
            return PRG_ROM[(address - 0xE000) + (n - 1) * 0x2000];
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
                return;
            }
            int total1k = CHR_ROM_count * 8;
            for (int i = 0; i < 8; i++)
            {
                // VRC2b: full 8-bit index (no shift)
                int page = chrLo[i] | (chrHi[i] << 4);
                NesCore.chrBankPtrs[i] = CHR_ROM + ((page % total1k) << 10);
            }
        }

        public byte MapperR_CHR(int address) { return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF]; }
        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
        public void CpuCycle() { }
        public void NotifyA12(int addr, int ppuAbsCycle) { }
            public void Cleanup() { }
}
}
