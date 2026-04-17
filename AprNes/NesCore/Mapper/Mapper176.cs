namespace AprNes
{
    // Mapper 176 — FK23C (Waixing / 外星科技 multicart chip)
    //
    // FK23C is an MMC3 super-set used by Chinese pirate multicarts (3-in-1, 4-in-1,
    // 12-in-1, FK23Cxxxx family). This implementation covers the core banking modes
    // used by typical multicarts. Omitted (not needed for tested ROMs):
    //   - Extended MMC3 mode (10 registers instead of 8) — falls back to standard MMC3
    //   - WRAM banking at $4000-$7FFF (uses plain $6000-$7FFF WRAM)
    //   - $5101-specific toggle semantics
    //
    // Registers at $5010-$5FFF (addr & 0x5010 == 0x5010):
    //   addr & 3:
    //     0: prgBankingMode (bits 0-2) | outerChrBankSize (bit 4) | selectChrRam (bit 5)
    //        | mmc3ChrMode (!bit 6) | prgBaseBits (bits 7,3 → bits 8,7)
    //     1: prgBaseBits (bits 0-6 → bits 0-6)
    //     2: chrBaseBits (full) | prgBaseBits bit 9 (bit 6 of value)
    //     3: extendedMmc3Mode (bit 1) | cnromChrMode (bit 2 or 6)
    //
    // PRG banking modes:
    //   0-2: Standard MMC3 + prgBaseBits (inner mask narrows with mode)
    //   3:   SelectPrgPage2x (both halves mirror prgBaseBits<<1)
    //   4:   SelectPrgPage4x (whole 32KB = (prgBaseBits & 0xFFE)<<1)
    //
    // CHR modes: MMC3 (standard) or CNROM (outerChrBankSize controls inner mask).
    //
    // Ref: Mesen2 Waixing/Fk23C.h (407 lines).
    unsafe public class Mapper176 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram, NES_MEM;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        // MMC3 state
        bool IRQ_enable, IRQReset;
        byte IRQlatchVal, IRQCounter;
        int BankReg;
        int m2Filter;
        int CHR0_1k, CHR1_1k, CHR2_1k, CHR3_1k;
        int CHR0_2k, CHR1_2k;
        int PRG0, PRG1;
        int PRG_Bankmode, CHR_Bankmode;
        bool invertPrgA14, invertChrA12;

        // FK23C state
        byte prgBankingMode;
        bool outerChrBankSize;
        bool selectChrRam;
        bool mmc3ChrMode = true;
        bool cnromChrMode;
        int  prgBaseBits;
        byte chrBaseBits;
        byte cnromChrReg;
        bool fk23RegistersEnabled = true;  // default enabled (most carts)

        public MapperA12Mode A12NotifyMode => MapperA12Mode.MMC3;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram, int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical; NES_MEM = NesCore.NES_MEM;
            UpdateState();
        }

        public void Reset()
        {
            prgBankingMode = 0;
            outerChrBankSize = false;
            selectChrRam = false;
            mmc3ChrMode = true;
            cnromChrMode = false;
            // Subtype 1 default: 1024KB PRG == CHR → prgBaseBits = 0x20, else 0
            int prgBytes = PRG_ROM_count * 16384;
            int chrBytes = CHR_ROM_count * 8192;
            prgBaseBits = (prgBytes == 1024 * 1024 && prgBytes == chrBytes) ? 0x20 : 0;
            chrBaseBits = 0;
            cnromChrReg = 0;
            invertPrgA14 = false;
            invertChrA12 = false;
            IRQ_enable = IRQReset = false;
            IRQCounter = 0;
            IRQlatchVal = 0;
            BankReg = 0;
            m2Filter = 0;
            fk23RegistersEnabled = true;
            UpdateState();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { WriteRegister(address, value); }
        public byte MapperR_RAM(ushort address) { return NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NES_MEM[address] = value; }

        public void MapperW_PRG(ushort address, byte value) { WriteRegister(address, value); }

        void WriteRegister(int addr, byte value)
        {
            if (addr < 0x8000)
            {
                // FK23C control at $5010-$5FFF (Mesen2: (addr & 0x5010) == 0x5010)
                if (!fk23RegistersEnabled) return;
                if ((addr & 0x5010) != 0x5010) return;

                switch (addr & 0x03)
                {
                    case 0:
                        prgBankingMode = (byte)(value & 0x07);
                        outerChrBankSize = (value & 0x10) != 0;
                        selectChrRam = (value & 0x20) != 0;
                        mmc3ChrMode = (value & 0x40) == 0;
                        prgBaseBits = (prgBaseBits & ~0x180) | ((value & 0x80) << 1) | ((value & 0x08) << 4);
                        break;
                    case 1:
                        prgBaseBits = (prgBaseBits & ~0x7F) | (value & 0x7F);
                        break;
                    case 2:
                        prgBaseBits = (prgBaseBits & ~0x200) | ((value & 0x40) << 3);
                        chrBaseBits = value;
                        cnromChrReg = 0;
                        break;
                    case 3:
                        cnromChrMode = (value & 0x44) != 0;
                        break;
                }
                UpdateState();
                return;
            }

            // $8000+ — standard MMC3 + CNROM CHR bit on certain ranges
            if (cnromChrMode && (addr <= 0x9FFF || addr >= 0xC000))
            {
                cnromChrReg = (byte)(value & 0x03);
                UpdateState();
                // Fall through? Mesen2 does NOT fall through — the switch below still runs.
            }

            switch (addr & 0xE001)
            {
                case 0x8000:
                    invertPrgA14 = (value & 0x40) != 0;
                    invertChrA12 = (value & 0x80) != 0;
                    BankReg = value & 0x07;
                    UpdateState();
                    break;
                case 0x8001:
                {
                    // Standard MMC3 mapping (8 regs for non-extended mode)
                    int reg = BankReg & 0x07;
                    switch (reg)
                    {
                        case 0: CHR0_2k = value; break;
                        case 1: CHR1_2k = value; break;
                        case 2: CHR0_1k = value; break;
                        case 3: CHR1_1k = value; break;
                        case 4: CHR2_1k = value; break;
                        case 5: CHR3_1k = value; break;
                        case 6: PRG0 = value; break;
                        case 7: PRG1 = value; break;
                    }
                    UpdateState();
                    break;
                }
                case 0xA000:
                    *Vertical = ((value & 1) > 0) ? 0 : 1;
                    break;
                case 0xA001:
                    // Simplified WRAM protect (ignored here)
                    break;
                case 0xC000:
                    IRQlatchVal = value;
                    break;
                case 0xC001:
                    IRQCounter = 0xFF;
                    IRQReset = true;
                    break;
                case 0xE000:
                    IRQ_enable = false;
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                    break;
                case 0xE001:
                    IRQ_enable = true;
                    break;
            }
        }

        void UpdateState()
        {
            UpdatePRG();
            UpdateCHRBanks();
        }

        void UpdatePRG()
        {
            int total8k = PRG_ROM_count * 2;
            if (total8k < 1) total8k = 1;

            // Modes 0-2: standard MMC3 + base bits (inner mask narrows with mode)
            if (prgBankingMode <= 2)
            {
                int swap = invertPrgA14 ? 2 : 0;
                int innerMask = 0x3F >> prgBankingMode;
                int outer = (prgBaseBits << 1) & ~innerMask;
                _prg[0 ^ swap] = ((PRG0 & innerMask) | outer) % total8k;
                _prg[1]        = ((PRG1 & innerMask) | outer) % total8k;
                _prg[2 ^ swap] = ((0xFE & innerMask)  | outer) % total8k;
                _prg[3]        = ((0xFF & innerMask)  | outer) % total8k;
            }
            else if (prgBankingMode == 3)
            {
                // 2× mirrored 16KB
                int basep = (prgBaseBits << 1) % total8k;
                _prg[0] = basep;     _prg[1] = (basep + 1) % total8k;
                _prg[2] = basep;     _prg[3] = (basep + 1) % total8k;
            }
            else // mode 4
            {
                int basep = ((prgBaseBits & 0xFFE) << 1) % total8k;
                _prg[0] = basep;     _prg[1] = (basep + 1) % total8k;
                _prg[2] = (basep + 2) % total8k;
                _prg[3] = (basep + 3) % total8k;
            }
        }
        int[] _prg = new int[4]; // 8KB slots for $8000/$A000/$C000/$E000

        public byte MapperR_RPG(ushort address)
        {
            int slot = ((address >> 13) & 3);  // 0..3
            int bank = _prg[slot];
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

            if (!mmc3ChrMode)
            {
                // CNROM mode: all 8 slots from a single 8KB bank (cnromChrReg + chrBaseBits)
                int innerMask = cnromChrMode ? (outerChrBankSize ? 1 : 3) : 0;
                for (int i = 0; i < 8; i++)
                {
                    int bank = ((((cnromChrReg & innerMask) | chrBaseBits) << 3) + i) % total1k;
                    NesCore.chrBankPtrs[i] = CHR_ROM + (bank << 10);
                }
                return;
            }

            // MMC3 CHR mode + outerChrBankSize masking
            int swap = invertChrA12 ? 4 : 0;
            int innerM = outerChrBankSize ? 0x7F : 0xFF;
            int outer = (chrBaseBits << 3) & ~innerM;

            int[] banks = new int[8];
            banks[0 ^ swap] = ((CHR0_2k & 0xFE) & innerM) | outer;
            banks[1 ^ swap] = ((CHR0_2k | 0x01) & innerM) | outer;
            banks[2 ^ swap] = ((CHR1_2k & 0xFE) & innerM) | outer;
            banks[3 ^ swap] = ((CHR1_2k | 0x01) & innerM) | outer;
            banks[4 ^ swap] = (CHR0_1k & innerM) | outer;
            banks[5 ^ swap] = (CHR1_1k & innerM) | outer;
            banks[6 ^ swap] = (CHR2_1k & innerM) | outer;
            banks[7 ^ swap] = (CHR3_1k & innerM) | outer;

            for (int i = 0; i < 8; i++)
                NesCore.chrBankPtrs[i] = CHR_ROM + ((banks[i] % total1k) << 10);
        }

        public byte MapperR_CHR(int address)
        {
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }

        public void CpuCycle() { }
        public void CpuClockRise() { }
        public void NotifyA12(int addr, int ppuAbsCycle) { }

        public void PpuClock()
        {
            bool a12Now = (NesCore.ppuAddressBus & 0x1000) != 0;
            if (!a12Now) { if (m2Filter < 16) m2Filter++; }
            if (!NesCore.ppuA12Prev && a12Now && m2Filter >= 10) Mmc3_StepIRQ();
            if (a12Now) m2Filter = 0;
        }

        void Mmc3_StepIRQ()
        {
            if (IRQReset)
            {
                IRQCounter = IRQlatchVal;
                IRQReset = false;
                if (IRQCounter == 0 && IRQ_enable) { NesCore.statusmapperint = true; NesCore.UpdateIRQLine(); }
            }
            else
            {
                IRQCounter--;
                if (IRQCounter == 0 && IRQ_enable) { NesCore.statusmapperint = true; NesCore.UpdateIRQLine(); }
                else if (IRQCounter == 255)
                {
                    IRQCounter = IRQlatchVal;
                    if (IRQCounter == 0 && IRQ_enable) { NesCore.statusmapperint = true; NesCore.UpdateIRQLine(); }
                }
            }
        }

        public void Cleanup() { }
    }
}
