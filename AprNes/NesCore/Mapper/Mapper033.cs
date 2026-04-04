namespace AprNes
{
    // Taito TC0190 — Akira (J), Don Doko Don (J), Insector X (J)
    // PRG: 2×8K switchable at $8000/$A000 ($8000/$8001 regs, bits 5-0)
    //      2×8K fixed: $C000=N-2, $E000=N-1
    // CHR: $8002/$8003 each select a 2K bank (2×1K ptrs) at PPU $0000/$0800
    //      $A000-$A003 each select 1K banks at PPU $1000-$1C00
    // Mirror: $8000 bit 6 (0=Vertical, 1=Horizontal)
    // Address decode: addr & 0xA003 (mirrors within 4K pages, uses bits 15,13,1,0)
    // No IRQ.
    unsafe public class Mapper033 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int prgBank0, prgBank1;     // 8K bank selectors for $8000/$A000
        byte[] chrReg = new byte[6]; // [0..1]=2K banks ($8002/$8003), [2..5]=1K banks ($A000-$A003)

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
            for (int i = 0; i < 6; i++) chrReg[i] = 0;
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }

        public void MapperW_PRG(ushort address, byte value)
        {
            switch (address & 0xA003)
            {
                case 0x8000:
                    prgBank0 = value & 0x3F;
                    *Vertical = (value & 0x40) != 0 ? 1 : 0; // bit6: 1=H, 0=V
                    break;
                case 0x8001:
                    prgBank1 = value & 0x3F;
                    break;
                case 0x8002:
                    chrReg[0] = value;  // 2K at PPU $0000 → expands to 1K pages value*2, value*2+1
                    UpdateCHRBanks();
                    break;
                case 0x8003:
                    chrReg[1] = value;  // 2K at PPU $0800
                    UpdateCHRBanks();
                    break;
                case 0xA000: case 0xA001: case 0xA002: case 0xA003:
                    chrReg[2 + (address & 3)] = value;  // 1K each at PPU $1000-$1C00
                    UpdateCHRBanks();
                    break;
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            int total8k = PRG_ROM_count * 2;
            int bank;
            if      (address < 0xA000) bank = prgBank0 % total8k;
            else if (address < 0xC000) bank = prgBank1 % total8k;
            else if (address < 0xE000) bank = total8k - 2;  // fixed N-2
            else                       bank = total8k - 1;  // fixed N-1
            return PRG_ROM[(address & 0x1FFF) + (bank << 13)];
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
                return;
            }
            int total1k = CHR_ROM_count * 8;
            // $0000-$07FF: 2K bank from chrReg[0] (value selects 2K, so *2 for 1K index)
            int p0 = (chrReg[0] * 2) % total1k;
            NesCore.chrBankPtrs[0] = CHR_ROM + (p0       << 10);
            NesCore.chrBankPtrs[1] = CHR_ROM + ((p0 + 1) << 10);
            // $0800-$0FFF: 2K bank from chrReg[1]
            int p1 = (chrReg[1] * 2) % total1k;
            NesCore.chrBankPtrs[2] = CHR_ROM + (p1       << 10);
            NesCore.chrBankPtrs[3] = CHR_ROM + ((p1 + 1) << 10);
            // $1000-$1FFF: 4×1K banks from chrReg[2..5]
            for (int i = 0; i < 4; i++)
                NesCore.chrBankPtrs[4 + i] = CHR_ROM + ((chrReg[2 + i] % total1k) << 10);
        }

        public byte MapperR_CHR(int address) { return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF]; }
        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
        public void CpuCycle() { }
        public void CpuClockRise() { }
        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void PpuClock() { }
            public void Cleanup() { }
}
}
