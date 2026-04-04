namespace AprNes
{
    // Konami VRC1 — Mapper 075
    // PRG: 4×8KB; $8000/$A000/$C000 swappable, $E000 fixed to last bank
    // CHR: 2×4KB ($0000 and $1000)
    // Mirror: $9000 bit0 (0=Vertical, 1=Horizontal)
    // High CHR bits: $9000 bit1 = high bit of CHR $0000 bank, bit2 = high bit of CHR $1000 bank
    unsafe public class Mapper075 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        // PRG registers: 3 swappable 8KB banks
        int prgBank0, prgBank1, prgBank2;

        // CHR: two 4KB banks. Each is 5 bits: 1 high bit from $9000 + 4 low bits from $E000/$F000
        int[] chrBank = new int[2];

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;
        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void PpuClock() { }
        public void CpuCycle() { }
        public void CpuClockRise() { }

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
        }

        public void Reset()
        {
            prgBank0 = 0; prgBank1 = 0; prgBank2 = 0;
            chrBank[0] = 0; chrBank[1] = 0;
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        public void MapperW_PRG(ushort address, byte value)
        {
            switch (address & 0xF000)
            {
                case 0x8000:
                    prgBank0 = value & 0xFF;
                    break;
                case 0x9000:
                    // bit0: mirroring (0=Vertical, 1=Horizontal)
                    *Vertical = ((value & 1) == 0) ? 1 : 0;
                    // bit1: high bit of CHR bank 0 ($0000)
                    chrBank[0] = (chrBank[0] & 0x0F) | ((value & 0x02) << 3);
                    // bit2: high bit of CHR bank 1 ($1000)
                    chrBank[1] = (chrBank[1] & 0x0F) | ((value & 0x04) << 2);
                    UpdateCHRBanks();
                    break;
                case 0xA000:
                    prgBank1 = value & 0xFF;
                    break;
                case 0xC000:
                    prgBank2 = value & 0xFF;
                    break;
                case 0xE000:
                    // low 4 bits of CHR bank 0
                    chrBank[0] = (chrBank[0] & 0x10) | (value & 0x0F);
                    UpdateCHRBanks();
                    break;
                case 0xF000:
                    // low 4 bits of CHR bank 1
                    chrBank[1] = (chrBank[1] & 0x10) | (value & 0x0F);
                    UpdateCHRBanks();
                    break;
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            int total8k = PRG_ROM_count * 2;
            if (address < 0xA000) return PRG_ROM[(address - 0x8000) + ((prgBank0 % total8k) << 13)];
            if (address < 0xC000) return PRG_ROM[(address - 0xA000) + ((prgBank1 % total8k) << 13)];
            if (address < 0xE000) return PRG_ROM[(address - 0xC000) + ((prgBank2 % total8k) << 13)];
            // $E000-$FFFF fixed to last bank
            return PRG_ROM[(address - 0xE000) + ((total8k - 1) << 13)];
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + i * 1024;
                return;
            }
            int total4k = CHR_ROM_count * 2;
            // CHR bank 0: 4KB at PPU $0000
            int b0 = (chrBank[0] % total4k) << 12;
            NesCore.chrBankPtrs[0] = CHR_ROM + b0;
            NesCore.chrBankPtrs[1] = CHR_ROM + b0 + 0x400;
            NesCore.chrBankPtrs[2] = CHR_ROM + b0 + 0x800;
            NesCore.chrBankPtrs[3] = CHR_ROM + b0 + 0xC00;
            // CHR bank 1: 4KB at PPU $1000
            int b1 = (chrBank[1] % total4k) << 12;
            NesCore.chrBankPtrs[4] = CHR_ROM + b1;
            NesCore.chrBankPtrs[5] = CHR_ROM + b1 + 0x400;
            NesCore.chrBankPtrs[6] = CHR_ROM + b1 + 0x800;
            NesCore.chrBankPtrs[7] = CHR_ROM + b1 + 0xC00;
        }

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
            public void Cleanup() { }
}
}
