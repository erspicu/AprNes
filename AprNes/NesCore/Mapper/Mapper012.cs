namespace AprNes
{
    // Mapper 012 — DBDROM / Dragon Ball Z 5 (Ch hack) / Kirakira Star Night DX
    // MMC3 + extra register at $4020-$5FFF (caught via MapperW_ExpansionROM, $4100-$5FFF
    // actually — $4020-$40FF is open bus in AprNes but games typically write to $5xxx).
    //
    // The extra register chrSelection controls bank high bit:
    //   bit 0 set → slot 0-3 ($0000-$0FFF) CHR banks OR'd with 0x100
    //   bit 4 set → slot 4-7 ($1000-$1FFF) CHR banks OR'd with 0x100
    //
    // Ref: Mesen2 Mmc3Variants/MMC3_12.h
    //
    // IMPLEMENTATION: copy of Mapper004 + per-slot-group bank high-bit injection.
    unsafe public class Mapper012 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram, NES_MEM;
        int CHR_ROM_count;
        int PRG_ROM_count;
        int* Vertical;

        bool IRQ_enable = false, IRQReset = false;
        byte IRQlatchVal = 0, IRQCounter = 0;
        int BankReg = 0;
        int m2Filter = 0;
        int CHR0_Bankselect1k = 0, CHR1_Bankselect1k = 0, CHR2_Bankselect1k = 0, CHR3_Bankselect1k = 0;
        int CHR0_Bankselect2k = 0, CHR1_Bankselect2k = 0;
        int PRG0_Bankselect = 0, PRG1_Bankselect = 0;
        int PRG_Bankmode;
        int CHR_Bankmode;

        // Mapper 012 specific
        byte chrSelection = 0;

        public MapperA12Mode A12NotifyMode => MapperA12Mode.MMC3;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram, int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM;
            CHR_ROM = _CHR_ROM;
            ppu_ram = _ppu_ram;
            CHR_ROM_count = _CHR_ROM_count;
            PRG_ROM_count = _PRG_ROM_count;
            Vertical = _Vertical;
            NES_MEM = NesCore.NES_MEM;
        }

        public void Reset() { chrSelection = 0; }
        public void CpuCycle() { }
        public void CpuClockRise() { }
        public void NotifyA12(int address, int ppuAbsCycle) { }

        public void PpuClock()
        {
            bool a12Now = (NesCore.ppuAddressBus & 0x1000) != 0;
            if (!a12Now) { if (m2Filter < 16) m2Filter++; }
            if (!NesCore.ppuA12Prev && a12Now && m2Filter >= 10) Mmc3_StepIRQ();
            if (a12Now) m2Filter = 0;
        }

        // Mapper 012 forces MMC3 Rev A IRQ semantics (Mesen2 ForceMmc3RevAIrqs override).
        void Mmc3_StepIRQ()
        {
            int oldCounter = IRQCounter;
            bool wasReset = IRQReset;
            bool reload = (IRQCounter == 0 || IRQReset);
            if (reload) IRQCounter = IRQlatchVal;
            else        IRQCounter--;
            IRQReset = false;

            // Rev A: fire when counter reaches 0 AND (old non-zero OR explicit reload via $C001)
            if (IRQCounter == 0 && IRQ_enable && (oldCounter != 0 || wasReset))
            {
                NesCore.statusmapperint = true;
                NesCore.UpdateIRQLine();
            }
        }

        public byte MapperR_ExpansionROM(ushort address) { return 0; }

        public void MapperW_ExpansionROM(ushort address, byte value)
        {
            // $4100-$5FFF window (AprNes routing): writes update the CHR bank high-bit register
            if (address >= 0x4100 && address <= 0x5FFF)
            {
                chrSelection = value;
                UpdateCHRBanks();
            }
        }

        public void MapperW_RAM(ushort address, byte value) { NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NES_MEM[address]; }

        public void MapperW_PRG(ushort address, byte value)
        {
            if ((address & 1) == 0)
            {
                if (address < 0xa000)
                {
                    BankReg = value & 7;
                    PRG_Bankmode = (value & 0x40) >> 6;
                    int newCHRMode = (value & 0x80) >> 7;
                    if (newCHRMode != CHR_Bankmode) { CHR_Bankmode = newCHRMode; UpdateCHRBanks(); }
                    else CHR_Bankmode = newCHRMode;
                }
                else if (address < 0xc000) *Vertical = ((value & 1) > 0) ? 0 : 1;
                else if (address < 0xe000) IRQlatchVal = value;
                else
                {
                    IRQ_enable = false;
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                }
            }
            else
            {
                if (address < 0xa000)
                {
                    bool chrChanged = false;
                    if      (BankReg == 0) { CHR0_Bankselect2k = value; chrChanged = true; }
                    else if (BankReg == 1) { CHR1_Bankselect2k = value; chrChanged = true; }
                    else if (BankReg == 2) { CHR0_Bankselect1k = value; chrChanged = true; }
                    else if (BankReg == 3) { CHR1_Bankselect1k = value; chrChanged = true; }
                    else if (BankReg == 4) { CHR2_Bankselect1k = value; chrChanged = true; }
                    else if (BankReg == 5) { CHR3_Bankselect1k = value; chrChanged = true; }
                    else if (BankReg == 6) PRG0_Bankselect = value;
                    else PRG1_Bankselect = value;
                    if (chrChanged) UpdateCHRBanks();
                }
                else if (address < 0xc000) return;
                else if (address < 0xe000)
                {
                    IRQCounter = 0xFF;
                    IRQReset = true;
                }
                else IRQ_enable = true;
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            if (PRG_Bankmode == 0)
            {
                if      (address < 0xa000) return PRG_ROM[(address - 0x8000) + (PRG0_Bankselect << 13)];
                else if (address < 0xc000) return PRG_ROM[(address - 0xa000) + (PRG1_Bankselect << 13)];
                else if (address < 0xe000) return PRG_ROM[(address - 0xc000) + (((PRG_ROM_count << 1) - 2) << 13)];
                else                       return PRG_ROM[(address - 0xe000) + (((PRG_ROM_count << 1) - 1) << 13)];
            }
            else
            {
                if      (address < 0xa000) return PRG_ROM[(address - 0x8000) + (((PRG_ROM_count << 1) - 2) << 13)];
                else if (address < 0xc000) return PRG_ROM[(address - 0xa000) + (PRG1_Bankselect << 13)];
                else if (address < 0xe000) return PRG_ROM[(address - 0xc000) + (PRG0_Bankselect << 13)];
                else                       return PRG_ROM[(address - 0xe000) + (((PRG_ROM_count << 1) - 1) << 13)];
            }
        }

        byte* BankPtr(int bank1k)
        {
            if (CHR_ROM_count == 0) return ppu_ram + ((bank1k & 7) << 10);
            int total1k = CHR_ROM_count * 8;
            return CHR_ROM + ((bank1k % total1k) << 10);
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + i * 1024;
                return;
            }
            // Mapper 012: per-slot-group high bit injection
            //   bit 0 of chrSelection → slot 0..3 ($0000-$0FFF) gets bank |= 0x100
            //   bit 4 of chrSelection → slot 4..7 ($1000-$1FFF) gets bank |= 0x100
            int lowerOr = (chrSelection & 0x01) != 0 ? 0x100 : 0;
            int upperOr = (chrSelection & 0x10) != 0 ? 0x100 : 0;

            if (CHR_Bankmode == 0) // 2KB×2 at $0000-$0FFF, 1KB×4 at $1000-$1FFF
            {
                NesCore.chrBankPtrs[0] = BankPtr((CHR0_Bankselect2k & 0xFE) | lowerOr);
                NesCore.chrBankPtrs[1] = BankPtr((CHR0_Bankselect2k | 1)    | lowerOr);
                NesCore.chrBankPtrs[2] = BankPtr((CHR1_Bankselect2k & 0xFE) | lowerOr);
                NesCore.chrBankPtrs[3] = BankPtr((CHR1_Bankselect2k | 1)    | lowerOr);
                NesCore.chrBankPtrs[4] = BankPtr(CHR0_Bankselect1k | upperOr);
                NesCore.chrBankPtrs[5] = BankPtr(CHR1_Bankselect1k | upperOr);
                NesCore.chrBankPtrs[6] = BankPtr(CHR2_Bankselect1k | upperOr);
                NesCore.chrBankPtrs[7] = BankPtr(CHR3_Bankselect1k | upperOr);
            }
            else // 1KB×4 at $0000-$0FFF, 2KB×2 at $1000-$1FFF (slot positions unchanged)
            {
                NesCore.chrBankPtrs[0] = BankPtr(CHR0_Bankselect1k | lowerOr);
                NesCore.chrBankPtrs[1] = BankPtr(CHR1_Bankselect1k | lowerOr);
                NesCore.chrBankPtrs[2] = BankPtr(CHR2_Bankselect1k | lowerOr);
                NesCore.chrBankPtrs[3] = BankPtr(CHR3_Bankselect1k | lowerOr);
                NesCore.chrBankPtrs[4] = BankPtr((CHR0_Bankselect2k & 0xFE) | upperOr);
                NesCore.chrBankPtrs[5] = BankPtr((CHR0_Bankselect2k | 1)    | upperOr);
                NesCore.chrBankPtrs[6] = BankPtr((CHR1_Bankselect2k & 0xFE) | upperOr);
                NesCore.chrBankPtrs[7] = BankPtr((CHR1_Bankselect2k | 1)    | upperOr);
            }
        }

        public byte MapperR_CHR(int address)
        {
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
        public void Cleanup() { }
    }
}
