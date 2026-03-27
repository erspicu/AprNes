namespace AprNes
{
    // NINA-03 / NINA-06 — Mapper 079 (AVE)
    // Write to $4100-$5FFF (address must satisfy (addr & 0xE100) == 0x4100):
    //   bit3     = PRG 32KB bank (1 bit)
    //   bits[2:0] = CHR 8KB bank
    // No IRQ. Mirroring fixed from header.

    unsafe public class Mapper079 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int prgBank;
        int chrBank;

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
            prgBank = 0; chrBank = 0;
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address)
        {
            return 0;
        }

        public void MapperW_ExpansionROM(ushort address, byte value)
        {
            // Only respond to addresses where (addr & 0xE100) == 0x4100
            if ((address & 0xE100) != 0x4100) return;
            prgBank = (value >> 3) & 0x01;   // bit 3 = PRG 32KB bank
            chrBank = value & 0x07;           // bits[2:0] = CHR 8KB bank
            UpdateCHRBanks();
        }

        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public void MapperW_PRG(ushort address, byte value) { }

        public byte MapperR_RPG(ushort address)
        {
            // PRG_ROM_count = number of 16KB banks; 32KB bank count = PRG_ROM_count/2
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
            int total8k = CHR_ROM_count;
            int bank = chrBank % total8k;
            byte* b = CHR_ROM + (bank << 13);
            for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = b + (i << 10);
        }

        public byte MapperR_CHR(int address) { return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF]; }
        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }

        public void CpuCycle() { }
        public void NotifyA12(int addr, int ppuAbsCycle) { }
            public void Cleanup() { }
}
}
