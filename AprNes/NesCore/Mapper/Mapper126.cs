namespace AprNes
{
    // Mapper 126 — PowerJoy multicart (MMC3 + 4 exReg at $6000-$7FFF)
    //
    // Standard MMC3 with four extended registers selected by addr&3, writable
    // at $6000-$7FFF. exRegs[0]/exRegs[2] compose an outer bank prefix for
    // PRG (bit 7) and CHR (bits 7-9). exRegs[3] controls PRG-lock mode and
    // CHR lock and register write-lock (bit 7).
    //
    // PRG transform (per MMC3 page P):
    //   innerMask = ((~reg0 >> 2) & 0x10) | 0x0F    // bit 4 gated by ~reg0.6
    //   outer     = (reg0 & (0x06 | (reg0 & 0x40) >> 6)) << 4 | (reg0 & 0x10) << 3
    //   final     = (P & innerMask) | outer
    //
    // PRG-lock (exRegs[3] & 0x03) applied ONLY to the MMC3 swappable slot
    // (slot = prgMode << 1 — 0 when prgMode=0, 2 when prgMode=1):
    //   0: normal per-slot mapping
    //   3: 32KB linear (pages page..page+3)
    //   1 or 2: 16KB mirror (pages page, page+1, page, page+1)
    //
    // CHR-outer (always applied unless locked):
    //   reg=exRegs[0]; reg2=exRegs[2]
    //   outer = (~reg & 0x80 & reg2) | (reg<<4 & 0x80 & reg)
    //         | (reg<<3 & 0x100)     | (reg<<5 & 0x200)
    //   innerMask = reg0.7 ? 0x7F : passthrough
    //
    // CHR-lock (exRegs[3] & 0x10): 8KB linear fixed starting at
    //   outer | ((exRegs[2] & 0x0F) << 3)
    //
    // exReg write gate:
    //   addr&3 == 1 or 2: always writable
    //   addr&3 == 0 or 3: writable only if exRegs[3].7 == 0
    //
    // Ref: Mesen2 Mmc3Variants/MMC3_126.h
    unsafe public class Mapper126 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram, NES_MEM;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        bool IRQ_enable, IRQReset;
        byte IRQlatchVal, IRQCounter;
        int BankReg;
        int m2Filter;
        int CHR0_1k, CHR1_1k, CHR2_1k, CHR3_1k;
        int CHR0_2k, CHR1_2k;
        int PRG0, PRG1;
        int PRG_Bankmode, CHR_Bankmode;

        readonly byte[] exRegs = new byte[4];
        readonly int[] _prg = new int[4];

        public MapperA12Mode A12NotifyMode => MapperA12Mode.MMC3;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical; NES_MEM = NesCore.NES_MEM;
            UpdatePRG();
            UpdateCHRBanks();
        }

        public void Reset()
        {
            for (int i = 0; i < 4; i++) exRegs[i] = 0;
            UpdatePRG();
            UpdateCHRBanks();
        }

        public void CpuCycle() { }
        public void CpuClockRise() { }
        public void NotifyA12(int a, int c) { }

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

        public byte MapperR_ExpansionROM(ushort address) { return 0; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }

        public byte MapperR_RAM(ushort address) { return NES_MEM[address]; }

        public void MapperW_RAM(ushort address, byte value)
        {
            int idx = address & 3;
            bool locked = (exRegs[3] & 0x80) != 0;
            bool canWrite = (idx == 1 || idx == 2) || !locked;
            if (canWrite && exRegs[idx] != value)
            {
                exRegs[idx] = value;
                UpdatePRG();
                UpdateCHRBanks();
            }
            // WRAM pass-through (AddRegisterRange in Mesen2 augments, not replaces, the WRAM write)
            NES_MEM[address] = value;
        }

        public void MapperW_PRG(ushort address, byte value)
        {
            if ((address & 1) == 0)
            {
                if (address < 0xA000)
                {
                    BankReg = value & 7;
                    int newPrgMode = (value & 0x40) >> 6;
                    int newChrMode = (value & 0x80) >> 7;
                    if (newPrgMode != PRG_Bankmode) { PRG_Bankmode = newPrgMode; UpdatePRG(); }
                    if (newChrMode != CHR_Bankmode) { CHR_Bankmode = newChrMode; UpdateCHRBanks(); }
                }
                else if (address < 0xC000) *Vertical = ((value & 1) > 0) ? 0 : 1;
                else if (address < 0xE000) IRQlatchVal = value;
                else
                {
                    IRQ_enable = false;
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                }
            }
            else
            {
                if (address < 0xA000)
                {
                    bool chrChanged = false, prgChanged = false;
                    if      (BankReg == 0) { CHR0_2k = value; chrChanged = true; }
                    else if (BankReg == 1) { CHR1_2k = value; chrChanged = true; }
                    else if (BankReg == 2) { CHR0_1k = value; chrChanged = true; }
                    else if (BankReg == 3) { CHR1_1k = value; chrChanged = true; }
                    else if (BankReg == 4) { CHR2_1k = value; chrChanged = true; }
                    else if (BankReg == 5) { CHR3_1k = value; chrChanged = true; }
                    else if (BankReg == 6) { PRG0 = value; prgChanged = true; }
                    else                   { PRG1 = value; prgChanged = true; }
                    if (chrChanged) UpdateCHRBanks();
                    if (prgChanged) UpdatePRG();
                }
                else if (address < 0xC000) return;
                else if (address < 0xE000) { IRQCounter = 0xFF; IRQReset = true; }
                else IRQ_enable = true;
            }
        }

        int TransformPrgPage(int page)
        {
            int reg0 = exRegs[0];
            int innerMask = ((~reg0 >> 2) & 0x10) | 0x0F;
            int outer = ((reg0 & (0x06 | ((reg0 & 0x40) >> 6))) << 4) | ((reg0 & 0x10) << 3);
            return (page & innerMask) | outer;
        }

        int GetChrOuterBank()
        {
            int reg  = exRegs[0];
            int reg2 = exRegs[2];
            return
                ((~reg) & 0x80 & reg2) |
                ((reg << 4) & 0x80 & reg) |
                ((reg << 3) & 0x0100) |
                ((reg << 5) & 0x0200);
        }

        void UpdatePRG()
        {
            int last_m1 = (PRG_ROM_count << 1) - 2;
            int last    = (PRG_ROM_count << 1) - 1;
            int[] mmc3 = new int[4];
            if (PRG_Bankmode == 0)
            {
                mmc3[0] = PRG0;    mmc3[1] = PRG1;
                mmc3[2] = last_m1; mmc3[3] = last;
            }
            else
            {
                mmc3[0] = last_m1; mmc3[1] = PRG1;
                mmc3[2] = PRG0;    mmc3[3] = last;
            }

            int lockMode = exRegs[3] & 0x03;
            if (lockMode == 0)
            {
                for (int i = 0; i < 4; i++) _prg[i] = TransformPrgPage(mmc3[i]);
            }
            else
            {
                int swappableSlot = PRG_Bankmode << 1;
                int basePage = TransformPrgPage(mmc3[swappableSlot]);
                if (lockMode == 0x03)
                {
                    _prg[0] = basePage;
                    _prg[1] = basePage + 1;
                    _prg[2] = basePage + 2;
                    _prg[3] = basePage + 3;
                }
                else
                {
                    _prg[0] = basePage;
                    _prg[1] = basePage + 1;
                    _prg[2] = basePage;
                    _prg[3] = basePage + 1;
                }
            }

            int total8k = PRG_ROM_count * 2;
            if (total8k < 1) total8k = 1;
            for (int i = 0; i < 4; i++) _prg[i] = ((_prg[i] % total8k) + total8k) % total8k;
        }

        public byte MapperR_RPG(ushort address)
        {
            int slot = (address >> 13) & 3;
            return PRG_ROM[(address & 0x1FFF) + (_prg[slot] << 13)];
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
                return;
            }
            int total1k = CHR_ROM_count * 8;
            int outer = GetChrOuterBank();

            if ((exRegs[3] & 0x10) != 0)
            {
                int page = outer | ((exRegs[2] & 0x0F) << 3);
                for (int i = 0; i < 8; i++)
                {
                    int bank = (page + i) % total1k;
                    NesCore.chrBankPtrs[i] = CHR_ROM + (bank << 10);
                }
                return;
            }

            int innerMask = (exRegs[0] & 0x80) != 0 ? 0x7F : 0x7FFFFFFF;

            int[] banks = new int[8];
            if (CHR_Bankmode == 0)
            {
                banks[0] = (CHR0_2k & 0xFE);
                banks[1] = (CHR0_2k | 0x01);
                banks[2] = (CHR1_2k & 0xFE);
                banks[3] = (CHR1_2k | 0x01);
                banks[4] = CHR0_1k;
                banks[5] = CHR1_1k;
                banks[6] = CHR2_1k;
                banks[7] = CHR3_1k;
            }
            else
            {
                banks[0] = CHR0_1k;
                banks[1] = CHR1_1k;
                banks[2] = CHR2_1k;
                banks[3] = CHR3_1k;
                banks[4] = (CHR0_2k & 0xFE);
                banks[5] = (CHR0_2k | 0x01);
                banks[6] = (CHR1_2k & 0xFE);
                banks[7] = (CHR1_2k | 0x01);
            }
            for (int i = 0; i < 8; i++)
            {
                int bank = (outer | (banks[i] & innerMask)) % total1k;
                NesCore.chrBankPtrs[i] = CHR_ROM + (bank << 10);
            }
        }

        public byte MapperR_CHR(int address)
        {
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val)
        {
            if (CHR_ROM_count == 0) ppu_ram[addr] = val;
        }

        public void Cleanup() { }
    }
}
