namespace AprNes
{
    // Sunsoft-1 (FC-08) — Mapper 184
    // Write to $6000-$7FFF:
    //   bits[2:0]  = CHR 4KB bank for $0000-$0FFF (lower)
    //   bits[6:4]  = CHR 4KB bank for $1000-$1FFF (upper), with bit7 always set (0x80 | bank)
    //   "The most significant bit of H is always set in hardware."
    // PRG 32KB fixed at $8000. No IRQ. Uses CHR-ROM.

    unsafe public class Mapper184 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int chrLoBankIdx;   // 4KB bank for $0000-$0FFF
        int chrHiBankIdx;   // 4KB bank for $1000-$1FFF (always has bit7 set = 0x80 | ...)

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
            chrLoBankIdx = 0;
            chrHiBankIdx = 0;
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }

        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value)
        {
            // $6000-$7FFF write: CHR bank selection
            chrLoBankIdx  = value & 0x07;             // bits[2:0] = lower 4KB bank
            chrHiBankIdx  = 0x80 | ((value >> 4) & 0x07);  // 0x80 | bits[6:4] = upper 4KB bank
            UpdateCHRBanks();
        }

        public void MapperW_PRG(ushort address, byte value) { }

        public byte MapperR_RPG(ushort address)
        {
            int offset = address - 0x8000;
            int size = PRG_ROM_count << 15;
            return PRG_ROM[offset % size];
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
                return;
            }
            // Total 4KB banks = CHR_ROM_count * 2 (CHR_ROM_count is in 8KB units)
            int total4k = CHR_ROM_count * 2;

            int lo = chrLoBankIdx % total4k;
            int hi = chrHiBankIdx % total4k;

            // Lower 4KB ($0000-$0FFF): 4 × 1KB pages
            for (int i = 0; i < 4; i++)
                NesCore.chrBankPtrs[i] = CHR_ROM + (lo << 12) + (i << 10);
            // Upper 4KB ($1000-$1FFF): 4 × 1KB pages
            for (int i = 0; i < 4; i++)
                NesCore.chrBankPtrs[4 + i] = CHR_ROM + (hi << 12) + (i << 10);
        }

        public byte MapperR_CHR(int address) { return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF]; }
        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }

        public void CpuCycle() { }
        public void NotifyA12(int addr, int ppuAbsCycle) { }
            public void Cleanup() { }
}
}
