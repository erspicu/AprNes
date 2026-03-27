namespace AprNes
{
    // Namco 175/340 — Mapper 210
    // Similar to Mapper019 (Namco163) but without audio expansion.
    // SubMapper 1 = Namco175: write-protect via $C000
    // SubMapper 2 = Namco340: mirroring via $E000 bits[7:6]
    // In both cases: no NT-from-CHR (CHR regs 0-7 always address CHR-ROM),
    //               no audio expansion, no NT regs at $C000-$D800 for Namco175.
    // PRG: 4×8KB; banks 0/1/2 via $E000/$E800/$F000, bank3 fixed last
    // CHR: 8×1KB via $8000-$B800
    unsafe public class Mapper210 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        public int Submapper = 0; // 1=Namco175, 2=Namco340
        bool autoDetect;          // true when Submapper==0 or -1 (unknown)

        int[] prgBank = new int[3];
        byte[] chrReg = new byte[8];

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;
        public void NotifyA12(int addr, int ppuAbsCycle) { }

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
        }

        public void Reset()
        {
            for (int i = 0; i < 3; i++) prgBank[i] = 0;
            for (int i = 0; i < 8; i++) chrReg[i] = 0;
            autoDetect = (Submapper <= 0); // auto-detect when unknown
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public void CpuCycle() { }

        public void MapperW_PRG(ushort address, byte value)
        {
            switch (address & 0xF800)
            {
                case 0x8000: case 0x8800: case 0x9000: case 0x9800:
                {
                    int idx = (address - 0x8000) >> 11;
                    chrReg[idx] = value;
                    UpdateCHRBanks();
                    break;
                }
                case 0xA000: case 0xA800: case 0xB000: case 0xB800:
                {
                    int idx = ((address - 0xA000) >> 11) + 4;
                    chrReg[idx] = value;
                    UpdateCHRBanks();
                    break;
                }
                case 0xC000:
                    // Namco175: write-protect reg (we accept but ignore write protection for simplicity)
                    break;
                case 0xE000:
                    // Auto-detect Namco340: bit 7 or bit 6 set in $E000 write
                    if (autoDetect && (value & 0xC0) != 0)
                    {
                        Submapper = 2;
                        autoDetect = false;
                    }
                    prgBank[0] = value & 0x3F;
                    if (Submapper == 2) // Namco340: mirroring
                    {
                        switch ((value >> 6) & 0x03)
                        {
                            case 0: *Vertical = 2; break; // Single-A
                            case 1: *Vertical = 1; break; // Vertical
                            case 2: *Vertical = 0; break; // Horizontal
                            case 3: *Vertical = 3; break; // Single-B
                        }
                    }
                    break;
                case 0xE800:
                    prgBank[1] = value & 0x3F;
                    break;
                case 0xF000:
                    prgBank[2] = value & 0x3F;
                    break;
                case 0xF800:
                    // Mapper019 uses this for audio RAM addr; Mapper210 ignores
                    break;
            }
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + i * 1024;
                return;
            }
            int n1k = CHR_ROM_count * 8;
            for (int i = 0; i < 8; i++)
                NesCore.chrBankPtrs[i] = CHR_ROM + ((chrReg[i] % n1k) << 10);
        }

        public byte MapperR_RPG(ushort address)
        {
            int n8k = PRG_ROM_count * 2;
            if (address < 0xA000) return PRG_ROM[(address - 0x8000) + (prgBank[0] % n8k) * 0x2000];
            if (address < 0xC000) return PRG_ROM[(address - 0xA000) + (prgBank[1] % n8k) * 0x2000];
            if (address < 0xE000) return PRG_ROM[(address - 0xC000) + (prgBank[2] % n8k) * 0x2000];
            return PRG_ROM[(address - 0xE000) + (n8k - 1) * 0x2000];
        }

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
    }
}
