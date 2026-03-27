namespace AprNes
{
    // Namco 108 (MMC3 subset) — no IRQ, hardwired mirroring
    // All $8000-$FFFF writes are redirected to $8000/$8001 (addr &= 0x8001).
    // CHR mode 0 only (bits 6-7 of command register always 0):
    //   cmd 0: 2K CHR at $0000  (chrBankPtrs[0,1])
    //   cmd 1: 2K CHR at $0800  (chrBankPtrs[2,3])
    //   cmd 2: 1K CHR at $1000  (chrBankPtrs[4])
    //   cmd 3: 1K CHR at $1400  (chrBankPtrs[5])
    //   cmd 4: 1K CHR at $1800  (chrBankPtrs[6])
    //   cmd 5: 1K CHR at $1C00  (chrBankPtrs[7])
    //   cmd 6: 8K PRG at $8000
    //   cmd 7: 8K PRG at $A000
    // PRG: $C000-$DFFF = second-to-last 8K, $E000-$FFFF = last 8K (fixed)
    unsafe public class Mapper206 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int CHR_ROM_count;
        int PRG_ROM_count;
        int* Vertical;

        int cmdReg;
        int[] chrBank = new int[6];  // indices 0-5 → CHR 1KB or 2KB blocks
        int prgBank8000, prgBankA000;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
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
            cmdReg = 0;
            for (int i = 0; i < 6; i++) chrBank[i] = 0;
            prgBank8000 = 0;
            prgBankA000 = 1;
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        public void MapperW_PRG(ushort address, byte value)
        {
            // All writes redirect to $8000 (even) or $8001 (odd)
            if ((address & 1) == 0)
            {
                cmdReg = value & 7;  // bits 6-7 forced 0: no chr_mode/prg_mode
            }
            else
            {
                if (cmdReg <= 5)
                {
                    chrBank[cmdReg] = value;
                    UpdateCHRBanks();
                }
                else if (cmdReg == 6) { prgBank8000 = value & 0x3F; }
                else                  { prgBankA000 = value & 0x3F; }
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            int total8k = PRG_ROM_count * 2;
            if (address < 0xA000) return PRG_ROM[(address - 0x8000) + ((prgBank8000 % total8k) << 13)];
            if (address < 0xC000) return PRG_ROM[(address - 0xA000) + ((prgBankA000 % total8k) << 13)];
            if (address < 0xE000) return PRG_ROM[(address - 0xC000) + ((total8k - 2) << 13)];
            return               PRG_ROM[(address - 0xE000) + ((total8k - 1) << 13)];
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
                return;
            }
            int total1k = CHR_ROM_count * 8;
            // cmd 0,1 → 2K each (left half): chrBank[0] selects 2K block (clear bit 0)
            int b0 = (chrBank[0] & ~1) % total1k;
            NesCore.chrBankPtrs[0] = CHR_ROM + (b0 << 10);
            NesCore.chrBankPtrs[1] = CHR_ROM + ((b0 + 1) << 10);
            int b1 = (chrBank[1] & ~1) % total1k;
            NesCore.chrBankPtrs[2] = CHR_ROM + (b1 << 10);
            NesCore.chrBankPtrs[3] = CHR_ROM + ((b1 + 1) << 10);
            // cmd 2-5 → 1K each (right half)
            NesCore.chrBankPtrs[4] = CHR_ROM + ((chrBank[2] % total1k) << 10);
            NesCore.chrBankPtrs[5] = CHR_ROM + ((chrBank[3] % total1k) << 10);
            NesCore.chrBankPtrs[6] = CHR_ROM + ((chrBank[4] % total1k) << 10);
            NesCore.chrBankPtrs[7] = CHR_ROM + ((chrBank[5] % total1k) << 10);
        }

        public byte MapperR_CHR(int address)
        {
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void CpuCycle() { }
        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;
    }
}
