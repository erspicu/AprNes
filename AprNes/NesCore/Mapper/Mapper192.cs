namespace AprNes
{
    // Mapper 074 — 漢化版 MMC3 variant
    // Identical to MMC3 (Mapper 4) except: CHR bank numbers 0x08 and 0x09 map to
    // 2KB of CHR-RAM (banks 8 and 9 are RAM, all other banks are CHR-ROM).
    // Typical games: 重裝機兵 (Metal Max), 吞食天地 II etc. — Chinese font data
    // written into the CHR-RAM slice at runtime.
    // Ref: Mesen2 MMC3_ChrRam(0x08, 0x09, 2)
    //
    // IMPLEMENTATION: copy of Mapper004.cs verbatim + CHR-RAM redirect in
    // UpdateCHRBanks + MapperR_CHR + Cleanup. All MMC3 timing (A12 IRQ, PRG/CHR
    // bankmodes) preserved.
    unsafe public class Mapper192 : IMapper
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

        // ── Variant-specific: 2KB CHR-RAM redirecting banks 0x08 and 0x09 ──
        const int CHR_RAM_FIRST = 0x08;
        const int CHR_RAM_LAST  = 0x0B;
        const int CHR_RAM_SIZE  = 4096;
        byte* chrRamBuffer = null;

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

            // Allocate CHR-RAM buffer. Zero-initialized.
            if (chrRamBuffer == null)
            {
                chrRamBuffer = (byte*)NesCore.AllocUnmanaged(CHR_RAM_SIZE);
                for (int i = 0; i < CHR_RAM_SIZE; i++) chrRamBuffer[i] = 0;
            }
        }

        public void Reset() { }
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

        void Mmc3_StepIRQ()
        {
            if (IRQReset)
            {
                IRQCounter = IRQlatchVal;
                IRQReset = false;
                if (IRQCounter == 0 && IRQ_enable)
                {
                    NesCore.statusmapperint = true;
                    NesCore.UpdateIRQLine();
                }
            }
            else
            {
                IRQCounter--;
                if (IRQCounter == 0 && IRQ_enable)
                {
                    NesCore.statusmapperint = true;
                    NesCore.UpdateIRQLine();
                }
                else if (IRQCounter == 255)
                {
                    IRQCounter = IRQlatchVal;
                    if (IRQCounter == 0 && IRQ_enable)
                    {
                        NesCore.statusmapperint = true;
                        NesCore.UpdateIRQLine();
                    }
                }
            }
        }

        public byte MapperR_ExpansionROM(ushort address) { return 0; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
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

        // Per-1KB-slot pointer: check variant range first, else CHR-ROM.
        byte* BankPtr(int bank1k)
        {
            if (bank1k >= CHR_RAM_FIRST && bank1k <= CHR_RAM_LAST)
                return chrRamBuffer + ((bank1k - CHR_RAM_FIRST) << 10);
            return CHR_ROM + (bank1k << 10);
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                // Pure CHR-RAM (no CHR-ROM): flat 8KB layout via ppu_ram (same as Mapper004).
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + i * 1024;
                return;
            }
            if (CHR_Bankmode == 0)
            {
                NesCore.chrBankPtrs[0] = BankPtr(CHR0_Bankselect2k & 0xFE);
                NesCore.chrBankPtrs[1] = BankPtr(CHR0_Bankselect2k | 1);
                NesCore.chrBankPtrs[2] = BankPtr(CHR1_Bankselect2k & 0xFE);
                NesCore.chrBankPtrs[3] = BankPtr(CHR1_Bankselect2k | 1);
                NesCore.chrBankPtrs[4] = BankPtr(CHR0_Bankselect1k);
                NesCore.chrBankPtrs[5] = BankPtr(CHR1_Bankselect1k);
                NesCore.chrBankPtrs[6] = BankPtr(CHR2_Bankselect1k);
                NesCore.chrBankPtrs[7] = BankPtr(CHR3_Bankselect1k);
            }
            else
            {
                NesCore.chrBankPtrs[0] = BankPtr(CHR0_Bankselect1k);
                NesCore.chrBankPtrs[1] = BankPtr(CHR1_Bankselect1k);
                NesCore.chrBankPtrs[2] = BankPtr(CHR2_Bankselect1k);
                NesCore.chrBankPtrs[3] = BankPtr(CHR3_Bankselect1k);
                NesCore.chrBankPtrs[4] = BankPtr(CHR0_Bankselect2k & 0xFE);
                NesCore.chrBankPtrs[5] = BankPtr(CHR0_Bankselect2k | 1);
                NesCore.chrBankPtrs[6] = BankPtr(CHR1_Bankselect2k & 0xFE);
                NesCore.chrBankPtrs[7] = BankPtr(CHR1_Bankselect2k | 1);
            }
        }

        public byte MapperR_CHR(int address)
        {
            // Delegate via chrBankPtrs set by UpdateCHRBanks (same as Mapper004 mode 0/1 both).
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val)
        {
            if (CHR_ROM_count == 0) { ppu_ram[addr] = val; return; }
            // CHR-RAM writes — write into chrRamBuffer if target slot points there.
            // Identify slot and offset:
            int slot = (addr >> 10) & 7;
            byte* slotPtr = NesCore.chrBankPtrs[slot];
            // If slotPtr is inside chrRamBuffer [0, CHR_RAM_SIZE), write; else ignore (CHR-ROM read-only).
            if (slotPtr >= chrRamBuffer && slotPtr < chrRamBuffer + CHR_RAM_SIZE)
                slotPtr[addr & 0x3FF] = val;
        }

        public void Cleanup()
        {
            if (chrRamBuffer != null)
            {
                NesCore.FreeUnmanaged((System.IntPtr)chrRamBuffer);
                chrRamBuffer = null;
            }
        }
    }
}
