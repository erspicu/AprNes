namespace AprNes
{
    // Namco 118 DxROM — Mapper 095
    // Namco108 family: same register mechanism as Mapper088/206,
    // but CHR registers R0/R1 bit5 selects nametable mirroring per-half.
    // PRG: two switchable 8KB at $8000/$A000, fixed last two at $C000/$E000
    // CHR: two 2KB banks at $0000/$0800 (R0/R1), four 1KB banks at $1000-$1C00 (R2-R5)
    //      R0/R1 bit5 → nametable for left/right half
    unsafe public class Mapper095 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int cmdReg;
        int[] reg = new int[8];

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;
        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void CpuCycle() { }

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
        }

        public void Reset()
        {
            cmdReg = 0;
            for (int i = 0; i < 8; i++) reg[i] = 0;
            UpdateBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return 0; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        public void MapperW_PRG(ushort address, byte value)
        {
            // Namco108: all writes redirected to $8000/$8001 (addr & 0x8001)
            int regAddr = address & 0x8001;
            if (regAddr == 0x8000)
            {
                cmdReg = value & 0x07; // no CHR/PRG mode bits
            }
            else // 0x8001
            {
                reg[cmdReg] = value & 0x3F;
                UpdateBanks();
                // NT mirroring update on data writes (from R0/R1 bit5)
                int nt1 = (reg[0] >> 5) & 0x01;
                int nt2 = (reg[1] >> 5) & 0x01;
                SetNametables(nt1, nt1, nt2, nt2);
            }
        }

        void SetNametables(int nt0, int nt1, int nt2, int nt3)
        {
            // nt values: 0 = CIRAM page 0 ($2000), 1 = CIRAM page 1 ($2400)
            NesCore.ntBankPtrs[0] = ppu_ram + 0x2000 + (nt0 << 10);
            NesCore.ntBankPtrs[1] = ppu_ram + 0x2000 + (nt1 << 10);
            NesCore.ntBankPtrs[2] = ppu_ram + 0x2000 + (nt2 << 10);
            NesCore.ntBankPtrs[3] = ppu_ram + 0x2000 + (nt3 << 10);
            NesCore.ntChrOverrideEnabled = true;

            // Sync *Vertical so PPU writes go to the right CIRAM
            if (nt0 == 0 && nt1 == 0 && nt2 == 1 && nt3 == 1)
                *Vertical = 0; // Vertical (NES V)
            else if (nt0 == 0 && nt1 == 1 && nt2 == 0 && nt3 == 1)
                *Vertical = 1; // Horizontal
            else if (nt0 == 0 && nt1 == 0 && nt2 == 0 && nt3 == 0)
                *Vertical = 2; // Single-A
            else
                *Vertical = 3; // Single-B or other
        }

        public void UpdateCHRBanks() { UpdateBanks(); }
        void UpdateBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + i * 1024;
                return;
            }
            int n1k = CHR_ROM_count * 8;

            // R0: 2KB at $0000 (slots 0,1) — bit5 used for NT, clear from CHR index
            int b0 = (reg[0] & 0x1E) % n1k; // bit5 = NT select, bit0 = forced 0 for 2KB
            NesCore.chrBankPtrs[0] = CHR_ROM + (b0 << 10);
            NesCore.chrBankPtrs[1] = CHR_ROM + ((b0 + 1) << 10);

            // R1: 2KB at $0800 (slots 2,3)
            int b1 = (reg[1] & 0x1E) % n1k;
            NesCore.chrBankPtrs[2] = CHR_ROM + (b1 << 10);
            NesCore.chrBankPtrs[3] = CHR_ROM + ((b1 + 1) << 10);

            // R2-R5: 1KB at $1000-$1C00 (slots 4-7), force bit6=1 (upper half)
            NesCore.chrBankPtrs[4] = CHR_ROM + (((reg[2] | 0x40) % n1k) << 10);
            NesCore.chrBankPtrs[5] = CHR_ROM + (((reg[3] | 0x40) % n1k) << 10);
            NesCore.chrBankPtrs[6] = CHR_ROM + (((reg[4] | 0x40) % n1k) << 10);
            NesCore.chrBankPtrs[7] = CHR_ROM + (((reg[5] | 0x40) % n1k) << 10);
        }

        public byte MapperR_RPG(ushort address)
        {
            int n8k = PRG_ROM_count * 2;
            if (address < 0xA000) return PRG_ROM[(address - 0x8000) + ((reg[6] % n8k) << 13)];
            if (address < 0xC000) return PRG_ROM[(address - 0xA000) + ((reg[7] % n8k) << 13)];
            if (address < 0xE000) return PRG_ROM[(address - 0xC000) + ((n8k - 2) << 13)];
            return PRG_ROM[(address - 0xE000) + ((n8k - 1) << 13)];
        }

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
    }
}
