namespace AprNes
{
    // Sunsoft Mapper #4 — AfterBurner II (J), Maharaja (J)
    // PRG: switchable 16K at $8000 ($F000 reg, bits 2-0), fixed last 16K at $C000
    // CHR: 4×2K banks at PPU $0000-$1FFF ($8000/$9000/$A000/$B000 regs)
    // NT:  $C000/$D000 select 1K CHR pages (bit 7 forced) for nametables 0/1
    //      $E000 bit 4 = 1 → enable CHR-as-nametable (ntChrOverrideEnabled)
    // Mirror: $E000 bits 0-1 (0=Vertical, 1=Horizontal, 2=single-A, 3=single-B)
    // No IRQ.
    unsafe public class Mapper068 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        byte[] chrRegs = new byte[4];  // 2K CHR bank selectors ($8000-$B000)
        byte ntReg0, ntReg1;           // 1K NT CHR page regs; bit 7 forced ($C000/$D000)
        bool useChrForNT;              // $E000 bit 4
        int mirrorMode;                // $E000 bits 0-1
        int prgBank;                   // $F000 bits 2-0

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
            prgBank = 0;
            for (int i = 0; i < 4; i++) chrRegs[i] = 0;
            ntReg0 = ntReg1 = 0x80;
            useChrForNT = false;
            mirrorMode = 0;
            *Vertical = 1; // Vertical mirroring (bit 0 of $E000 default = 0 → Horizontal in doc; using H=1 in AprNes)
            // Note: actual power-on mirror from iNES header; will be overridden on first $E000 write
            NesCore.ntChrOverrideEnabled = false;
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return 0; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }

        public void MapperW_PRG(ushort address, byte value)
        {
            switch (address & 0xF000)
            {
                case 0x8000: chrRegs[0] = value; UpdateCHRBanks(); break;
                case 0x9000: chrRegs[1] = value; UpdateCHRBanks(); break;
                case 0xA000: chrRegs[2] = value; UpdateCHRBanks(); break;
                case 0xB000: chrRegs[3] = value; UpdateCHRBanks(); break;
                case 0xC000: ntReg0 = (byte)(value | 0x80); UpdateNTBanks(); break;
                case 0xD000: ntReg1 = (byte)(value | 0x80); UpdateNTBanks(); break;
                case 0xE000:
                    mirrorMode = value & 3;
                    useChrForNT = (value & 0x10) != 0;
                    // AprNes convention: 0=V, 1=H, 2=single-A, 3=single-B
                    *Vertical = mirrorMode == 0 ? 1 : mirrorMode == 1 ? 0 : mirrorMode == 2 ? 2 : 3;
                    NesCore.ntChrOverrideEnabled = false; // recomputed in UpdateNTBanks
                    UpdateNTBanks();
                    break;
                case 0xF000:
                    prgBank = value & 0x07;
                    break;
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            // $C000-$FFFF: fixed last 16K bank
            if (address >= 0xC000)
                return PRG_ROM[(address - 0xC000) + (PRG_ROM_count - 1) * 0x4000];
            // $8000-$BFFF: switchable 16K bank
            return PRG_ROM[(address - 0x8000) + (prgBank % PRG_ROM_count) * 0x4000];
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
                return;
            }
            int total2k = CHR_ROM_count * 4;  // total 2K CHR banks (CHR_ROM_count in 8KB units)
            for (int i = 0; i < 4; i++)
            {
                int page2k = chrRegs[i] % total2k;
                NesCore.chrBankPtrs[i * 2]     = CHR_ROM + (page2k << 11);
                NesCore.chrBankPtrs[i * 2 + 1] = CHR_ROM + (page2k << 11) + 0x400;
            }
        }

        void UpdateNTBanks()
        {
            if (!useChrForNT || CHR_ROM_count == 0)
            {
                NesCore.ntChrOverrideEnabled = false;
                return;
            }
            int total1k = CHR_ROM_count * 8;  // total 1K CHR pages
            byte* base0 = CHR_ROM + ((ntReg0 % total1k) << 10);
            byte* base1 = CHR_ROM + ((ntReg1 % total1k) << 10);

            // Assign per-nametable pointers based on mirrorMode
            switch (mirrorMode)
            {
                case 0: // Vertical: $2000/$2800=NT0, $2400/$2C00=NT1
                    NesCore.ntBankPtrs[0] = base0;
                    NesCore.ntBankPtrs[1] = base1;
                    NesCore.ntBankPtrs[2] = base0;
                    NesCore.ntBankPtrs[3] = base1;
                    break;
                case 1: // Horizontal: $2000/$2400=NT0, $2800/$2C00=NT1
                    NesCore.ntBankPtrs[0] = base0;
                    NesCore.ntBankPtrs[1] = base0;
                    NesCore.ntBankPtrs[2] = base1;
                    NesCore.ntBankPtrs[3] = base1;
                    break;
                case 2: // Single-A
                    NesCore.ntBankPtrs[0] = NesCore.ntBankPtrs[1] =
                    NesCore.ntBankPtrs[2] = NesCore.ntBankPtrs[3] = base0;
                    break;
                case 3: // Single-B
                    NesCore.ntBankPtrs[0] = NesCore.ntBankPtrs[1] =
                    NesCore.ntBankPtrs[2] = NesCore.ntBankPtrs[3] = base1;
                    break;
            }
            NesCore.ntChrOverrideEnabled = true;
        }

        public byte MapperR_CHR(int address) { return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF]; }
        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
        public void CpuCycle() { }
        public void NotifyA12(int addr, int ppuAbsCycle) { }
    }
}
