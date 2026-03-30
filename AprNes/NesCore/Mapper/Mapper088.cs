namespace AprNes
{
    // Namco 118 / 634 — Mapper 088 / Mapper 154 (Namco 129)
    // Same register structure as Namco108 (Mapper206), but:
    //   R0/R1: select 2KB CHR banks at $0000/$0800; bit6 forced=0 (low 64KB only)
    //   R2-R5: select 1KB CHR banks at $1000/$1400/$1800/$1C00; bit6 forced=1 (high 64KB)
    //   R6/R7: 8KB PRG banks at $8000/$A000
    //   $C000-$DFFF fixed to second-to-last 8KB
    //   $E000-$FFFF fixed to last 8KB
    // No IRQ. Mapper 088: hardwired mirroring from header.
    // Mapper 154 (IsMapper154=true): dynamic single-screen mirroring via bit6 of write value.
    //   bit6=0 → Screen A (single-screen page 0), bit6=1 → Screen B (single-screen page 1)
    unsafe public class Mapper088 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        public bool IsMapper154 = false;

        int cmdReg;
        // 8 registers: 0-5=CHR, 6-7=PRG
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
            // Set bit6 on R2-R5 by default (forced high for 1KB banks at $1000)
            reg[2] |= 0x40; reg[3] |= 0x40; reg[4] |= 0x40; reg[5] |= 0x40;
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { }  // No WRAM on Namco 108/088
        public byte MapperR_RAM(ushort address) { return NesCore.cpubus; }  // Open bus

        public void MapperW_PRG(ushort address, byte value)
        {
            // Mapper 154: every write checks bit6 for single-screen mirroring
            if (IsMapper154)
                *Vertical = (value & 0x40) != 0 ? 3 : 2;  // bit6=1→ScreenB(3), bit6=0→ScreenA(2)

            // All writes redirected to $8000/$8001 (addr & 0x8001)
            if ((address & 1) == 0)
            {
                // Command register — bits 6-7 always cleared (no chr_mode or prg_mode)
                cmdReg = value & 0x07;
            }
            else
            {
                int r = cmdReg;
                if (r <= 1)
                {
                    // R0/R1: 2KB CHR banks — bit6 cleared (select from low 64KB)
                    reg[r] = value & 0x3F;
                }
                else if (r <= 5)
                {
                    // R2-R5: 1KB CHR banks — force bit6=1 (select from high 64KB)
                    reg[r] = (value & 0x3F) | 0x40;
                }
                else
                {
                    // R6/R7: PRG banks
                    reg[r] = value & 0x3F;
                }
                UpdateCHRBanks();
            }
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + i * 1024;
                return;
            }
            int total1k = CHR_ROM_count * 8;

            // R0: 2KB at $0000 (chrBankPtrs 0,1) — use reg[0] as 1KB index, force even
            int b0 = (reg[0] & 0x3E) % total1k;  // clear bit0 for 2KB alignment
            NesCore.chrBankPtrs[0] = CHR_ROM + (b0 << 10);
            NesCore.chrBankPtrs[1] = CHR_ROM + ((b0 + 1) << 10);

            // R1: 2KB at $0800 (chrBankPtrs 2,3)
            int b1 = (reg[1] & 0x3E) % total1k;
            NesCore.chrBankPtrs[2] = CHR_ROM + (b1 << 10);
            NesCore.chrBankPtrs[3] = CHR_ROM + ((b1 + 1) << 10);

            // R2-R5: 1KB at $1000-$1C00 (chrBankPtrs 4-7)
            NesCore.chrBankPtrs[4] = CHR_ROM + ((reg[2] % total1k) << 10);
            NesCore.chrBankPtrs[5] = CHR_ROM + ((reg[3] % total1k) << 10);
            NesCore.chrBankPtrs[6] = CHR_ROM + ((reg[4] % total1k) << 10);
            NesCore.chrBankPtrs[7] = CHR_ROM + ((reg[5] % total1k) << 10);
        }

        public byte MapperR_RPG(ushort address)
        {
            int total8k = PRG_ROM_count * 2;
            if (address < 0xA000) return PRG_ROM[(address - 0x8000) + ((reg[6] % total8k) << 13)];
            if (address < 0xC000) return PRG_ROM[(address - 0xA000) + ((reg[7] % total8k) << 13)];
            if (address < 0xE000) return PRG_ROM[(address - 0xC000) + ((total8k - 2) << 13)];
            return               PRG_ROM[(address - 0xE000) + ((total8k - 1) << 13)];
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
