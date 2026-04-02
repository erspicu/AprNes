namespace AprNes
{
    // Irem G-101 — Image Fight (J/U), Daiku no Gen San (J/2), Hammerin' Harry (E)
    // PRG: two switchable 8K banks + fixed last two 8K banks ($E000=N-1 always)
    //   Mode 0 (default): $8000=prgReg0, $A000=prgReg1, $C000=N-2, $E000=N-1
    //   Mode 1:           $8000=N-2,     $A000=prgReg1, $C000=prgReg0, $E000=N-1
    // CHR: 8×1K banks via $B000-$B007 (addr & 7 selects bank)
    // Mirror: $9000 bit 0 (0=Vertical, 1=Horizontal)
    // No IRQ.
    // SubMapper 1 (Major League): PRG mode locked to 0, mirroring fixed to single-A
    unsafe public class Mapper032 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int prgReg0, prgReg1;
        int prgMode;          // 0 or 1 (set via $9000 bit 1)
        public bool majorLeague = false;  // SubMapper 1: lock mode 0 + single-A mirror

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
            prgReg0 = prgReg1 = 0;
            prgMode = 0;
            if (majorLeague) *Vertical = 2;  // single-A (CIRAM A10 tied high)
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }

        public void MapperW_PRG(ushort address, byte value)
        {
            switch (address & 0xF000)
            {
                case 0x8000:
                    prgReg0 = value & 0x1F;
                    break;
                case 0x9000:
                    if (!majorLeague) prgMode = (value >> 1) & 1;
                    if (!majorLeague) *Vertical = (value & 1) == 1 ? 0 : 1; // 1=H, 0=V
                    break;
                case 0xA000:
                    prgReg1 = value & 0x1F;
                    break;
                case 0xB000:
                    int bank = address & 7;
                    int total1k = CHR_ROM_count > 0 ? CHR_ROM_count * 8 : 8;
                    NesCore.chrBankPtrs[bank] = CHR_ROM_count > 0
                        ? CHR_ROM + ((value % total1k) << 10)
                        : ppu_ram + (bank << 10);
                    break;
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            int n = PRG_ROM_count * 2;  // total 8K banks
            if (address >= 0xE000) return PRG_ROM[(address - 0xE000) + (n - 1) * 0x2000];
            if (address >= 0xC000)
            {
                int b = prgMode == 0 ? n - 2 : prgReg0 % n;
                return PRG_ROM[(address - 0xC000) + b * 0x2000];
            }
            if (address >= 0xA000) return PRG_ROM[(address - 0xA000) + (prgReg1 % n) * 0x2000];
            // $8000-$9FFF
            {
                int b = prgMode == 0 ? prgReg0 % n : n - 2;
                return PRG_ROM[(address - 0x8000) + b * 0x2000];
            }
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
                NesCore.chrBankPtrs[i] = CHR_ROM + ((i % total1k) << 10);
        }

        public byte MapperR_CHR(int address) { return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF]; }
        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
        public void CpuCycle() { }
        public void CpuClockRise() { }
        public void NotifyA12(int addr, int ppuAbsCycle) { }
            public void Cleanup() { }
}
}
