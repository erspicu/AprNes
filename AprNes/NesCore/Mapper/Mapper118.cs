namespace AprNes
{
    // TxSROM — Mapper 118 (TKSROM and TLSROM)
    // Full MMC3 functionality, but nametable control is via CHR bank registers:
    //   CHR registers R0-R5 bit7 selects nametable CIRAM page (0 or 1)
    //   instead of the normal $A000 mirroring register.
    //
    // In CHR mode 0 (bit6 of $8000 = 0):
    //   R0 controls NT0,NT1; R1 controls NT2,NT3
    // In CHR mode 1 (bit6 of $8000 = 1):
    //   R2→NT0, R3→NT1, R4→NT2, R5→NT3
    //
    // NT page bit = value >> 7 (0 or 1), selects CIRAM bank 0 or 1.
    unsafe public class Mapper118 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram, NES_MEM;
        int CHR_ROM_count, PRG_ROM_count;
        int* Vertical;

        // MMC3 state
        bool IRQ_enable, IRQReset;
        int IRQlatchVal, IRQCounter;
        int BankReg;
        int lastA12, a12LowSince, lastNotifyTime;
        const int A12_FILTER = 16;

        int CHR0_Bankselect1k, CHR1_Bankselect1k, CHR2_Bankselect1k, CHR3_Bankselect1k;
        int CHR0_Bankselect2k, CHR1_Bankselect2k;
        int PRG0_Bankselect, PRG1_Bankselect;
        int PRG_Bankmode, CHR_Bankmode;

        public MapperA12Mode A12NotifyMode => MapperA12Mode.MMC3;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
            NES_MEM = NesCore.NES_MEM;
        }

        public void Reset()
        {
            IRQ_enable = false; IRQReset = false;
            IRQlatchVal = 0; IRQCounter = 0;
            BankReg = 0;
            lastA12 = 0; a12LowSince = -100; lastNotifyTime = -100;
            CHR0_Bankselect1k = 0; CHR1_Bankselect1k = 0;
            CHR2_Bankselect1k = 0; CHR3_Bankselect1k = 0;
            CHR0_Bankselect2k = 0; CHR1_Bankselect2k = 0;
            PRG0_Bankselect = 0; PRG1_Bankselect = 0;
            PRG_Bankmode = 0; CHR_Bankmode = 0;
            NesCore.ntChrOverrideEnabled = true;
            UpdateCHRBanks();
            UpdateNametables();
        }

        public byte MapperR_ExpansionROM(ushort address) { return 0; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NES_MEM[address]; }
        public void CpuCycle() { }

        public void NotifyA12(int address, int ppuAbsCycle)
        {
            int a12 = (address >> 12) & 1;
            int sinceLast = ppuAbsCycle - lastNotifyTime;
            if (sinceLast < 0) sinceLast += 341 * 262;
            lastNotifyTime = ppuAbsCycle;

            if (a12 != 0 && lastA12 == 0)
            {
                int elapsed = ppuAbsCycle - a12LowSince;
                if (elapsed < 0) elapsed += 341 * 262;
                if (elapsed >= A12_FILTER)
                    ClockIRQ();
            }
            else if (a12 == 0 && lastA12 != 0)
            {
                if (sinceLast < 341)
                    a12LowSince = ppuAbsCycle;
            }
            lastA12 = a12;
        }

        void ClockIRQ()
        {
            if (IRQCounter == 0 || IRQReset)
            {
                IRQCounter = IRQlatchVal;
                IRQReset = false;
            }
            else
            {
                IRQCounter--;
            }
            if (IRQCounter == 0 && IRQ_enable)
            {
                NesCore.statusmapperint = true;
                NesCore.UpdateIRQLine();
            }
        }

        public void MapperW_PRG(ushort address, byte value)
        {
            // TxSROM: on $8001 writes, update nametables based on current BankReg and new value
            if ((address & 0xE001) == 0x8001)
            {
                int ntPage = (value >> 7) & 1;  // 0 or 1

                if (CHR_Bankmode == 0)
                {
                    switch (BankReg)
                    {
                        case 0: SetNT(0, ntPage); SetNT(1, ntPage); break;
                        case 1: SetNT(2, ntPage); SetNT(3, ntPage); break;
                    }
                }
                else
                {
                    switch (BankReg)
                    {
                        case 2: SetNT(0, ntPage); break;
                        case 3: SetNT(1, ntPage); break;
                        case 4: SetNT(2, ntPage); break;
                        case 5: SetNT(3, ntPage); break;
                    }
                }
            }

            // Normal MMC3 register handling
            if ((address & 1) == 0)
            {
                if (address < 0xA000)
                {
                    BankReg = value & 7;
                    PRG_Bankmode = (value & 0x40) >> 6;
                    int newCHRMode = (value & 0x80) >> 7;
                    if (newCHRMode != CHR_Bankmode)
                    {
                        CHR_Bankmode = newCHRMode;
                        UpdateCHRBanks();
                        UpdateNametables();
                    }
                    else CHR_Bankmode = newCHRMode;
                }
                // $A000: TxSROM ignores mirroring writes
                else if (address < 0xC000) { }
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
                    bool chrChanged = false;
                    if (BankReg == 0) { CHR0_Bankselect2k = value; chrChanged = true; }
                    else if (BankReg == 1) { CHR1_Bankselect2k = value; chrChanged = true; }
                    else if (BankReg == 2) { CHR0_Bankselect1k = value; chrChanged = true; }
                    else if (BankReg == 3) { CHR1_Bankselect1k = value; chrChanged = true; }
                    else if (BankReg == 4) { CHR2_Bankselect1k = value; chrChanged = true; }
                    else if (BankReg == 5) { CHR3_Bankselect1k = value; chrChanged = true; }
                    else if (BankReg == 6) PRG0_Bankselect = value;
                    else PRG1_Bankselect = value;
                    if (chrChanged) UpdateCHRBanks();
                }
                else if (address < 0xC000) { /* PRG RAM protect — ignore */ }
                else if (address < 0xE000) IRQReset = true;
                else IRQ_enable = true;
            }
        }

        // ntSlot[0..3]: which CIRAM page (0 or 1) each NT slot maps to
        int[] ntSlot = new int[4];

        // Map TxSROM CIRAM page (0 or 1) to the ppu_ram offset that matches the write path.
        // The write path depends on *Vertical:
        //   AprNes V (*Vertical=0, NES V): writes $2000↔$2400; writes $2800↔$2C00
        //     → page 0 (NT0,NT1) reads from ppu_ram+0x2000; page 1 (NT2,NT3) from ppu_ram+0x2800
        //   AprNes H (*Vertical=1, NES H): writes $2000↔$2800; writes $2400↔$2C00
        //     → page 0 (NT0,NT2) reads from ppu_ram+0x2000; page 1 (NT1,NT3) from ppu_ram+0x2400
        //   Single-screen: all same page
        // We resolve this dynamically based on the final ntSlot configuration.
        byte* CIRAMPageForSlot(int slot)
        {
            // Determine base address for this NT slot based on current ntSlot[] and mirroring
            int page = ntSlot[slot];
            // For NES V (NT0=NT1, NT2=NT3): slots 0,1 → $2000; slots 2,3 → $2800
            // For NES H (NT0=NT2, NT1=NT3): slots 0,2 → $2000; slots 1,3 → $2400
            // For single-screen: all → $2000
            int p0 = ntSlot[0], p1 = ntSlot[1], p2 = ntSlot[2], p3 = ntSlot[3];
            if (p0 == 0 && p1 == 0 && p2 == 1 && p3 == 1)
            {
                // NES Vertical: page 0 slots at $2000, page 1 slots at $2800
                return ppu_ram + 0x2000 + (page == 0 ? 0 : 0x800);
            }
            else if (p0 == 0 && p1 == 1 && p2 == 0 && p3 == 1)
            {
                // NES Horizontal: page 0 slots at $2000, page 1 slots at $2400
                return ppu_ram + 0x2000 + (page == 0 ? 0 : 0x400);
            }
            else
            {
                // Single-screen or other: use $2000 for page 0, $2400 for page 1
                return ppu_ram + 0x2000 + (page << 10);
            }
        }

        void SetNT(int slot, int page)
        {
            ntSlot[slot] = page;
            // SyncVertical first so CIRAMPageForSlot uses the correct mirroring
            SyncVertical();
            for (int i = 0; i < 4; i++)
                NesCore.ntBankPtrs[i] = CIRAMPageForSlot(i);
        }

        void UpdateNametables()
        {
            if (CHR_Bankmode == 0)
            {
                ntSlot[0] = ntSlot[1] = (CHR0_Bankselect2k >> 7) & 1;
                ntSlot[2] = ntSlot[3] = (CHR1_Bankselect2k >> 7) & 1;
            }
            else
            {
                ntSlot[0] = (CHR0_Bankselect1k >> 7) & 1;
                ntSlot[1] = (CHR1_Bankselect1k >> 7) & 1;
                ntSlot[2] = (CHR2_Bankselect1k >> 7) & 1;
                ntSlot[3] = (CHR3_Bankselect1k >> 7) & 1;
            }
            SyncVertical();
            for (int i = 0; i < 4; i++)
                NesCore.ntBankPtrs[i] = CIRAMPageForSlot(i);
        }

        // Sync *Vertical so that PPU writes also go to the correct CIRAM pages.
        // AprNes *Vertical=0 → NES Vertical (NT0=NT1 share page, NT2=NT3 share page)
        //         i.e., write to $2000 mirrors to $2400; write to $2800 mirrors to $2C00
        // AprNes *Vertical=1 → NES Horizontal (NT0=NT2 share page, NT1=NT3 share page)
        //         i.e., write to $2000 mirrors to $2800; write to $2400 mirrors to $2C00
        // AprNes *Vertical>=2 → single-screen (all slots mirror together)
        //
        // TxSROM p=(0,0,1,1) → NT0=NT1=page0, NT2=NT3=page1 → NES Vertical → *Vertical=0
        // TxSROM p=(0,1,0,1) → NT0=NT2=page0, NT1=NT3=page1 → NES Horizontal → *Vertical=1
        // TxSROM p=(0,0,0,0) → all page0 → single-A → *Vertical=2
        // TxSROM p=(1,1,1,1) → all page1 → single-B → *Vertical=3
        void SyncVertical()
        {
            int p0 = ntSlot[0], p1 = ntSlot[1], p2 = ntSlot[2], p3 = ntSlot[3];
            if (p0 == 0 && p1 == 0 && p2 == 0 && p3 == 0) *Vertical = 2;       // single-A
            else if (p0 == 1 && p1 == 1 && p2 == 1 && p3 == 1) *Vertical = 3;  // single-B
            else if (p0 == 0 && p1 == 0 && p2 == 1 && p3 == 1) *Vertical = 0;  // NES Vertical (NT0=NT1, NT2=NT3)
            else if (p0 == 0 && p1 == 1 && p2 == 0 && p3 == 1) *Vertical = 1;  // NES Horizontal (NT0=NT2, NT1=NT3)
            else *Vertical = 0;  // fallback
        }

        public byte MapperR_RPG(ushort address)
        {
            if (PRG_Bankmode == 0)
            {
                if (address < 0xA000) return PRG_ROM[(address - 0x8000) + (PRG0_Bankselect << 13)];
                else if (address < 0xC000) return PRG_ROM[(address - 0xA000) + (PRG1_Bankselect << 13)];
                else if (address < 0xE000) return PRG_ROM[(address - 0xC000) + (((PRG_ROM_count << 1) - 2) << 13)];
                else return PRG_ROM[(address - 0xE000) + (((PRG_ROM_count << 1) - 1) << 13)];
            }
            else
            {
                if (address < 0xA000) return PRG_ROM[(address - 0x8000) + (((PRG_ROM_count << 1) - 2) << 13)];
                else if (address < 0xC000) return PRG_ROM[(address - 0xA000) + (PRG1_Bankselect << 13)];
                else if (address < 0xE000) return PRG_ROM[(address - 0xC000) + (PRG0_Bankselect << 13)];
                else return PRG_ROM[(address - 0xE000) + (((PRG_ROM_count << 1) - 1) << 13)];
            }
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + i * 1024;
                return;
            }
            // Mask bank values to valid range: total 1KB pages = CHR_ROM_count * 8
            int total1k = CHR_ROM_count * 8;
            int mask1k = total1k - 1;  // assume power of 2
            if (CHR_Bankmode == 0)
            {
                int b0 = (CHR0_Bankselect2k & 0xFE) & mask1k;
                int b1 = (CHR1_Bankselect2k & 0xFE) & mask1k;
                NesCore.chrBankPtrs[0] = CHR_ROM + (b0 << 10);
                NesCore.chrBankPtrs[1] = CHR_ROM + ((b0 | 1) << 10);
                NesCore.chrBankPtrs[2] = CHR_ROM + (b1 << 10);
                NesCore.chrBankPtrs[3] = CHR_ROM + ((b1 | 1) << 10);
                NesCore.chrBankPtrs[4] = CHR_ROM + ((CHR0_Bankselect1k & mask1k) << 10);
                NesCore.chrBankPtrs[5] = CHR_ROM + ((CHR1_Bankselect1k & mask1k) << 10);
                NesCore.chrBankPtrs[6] = CHR_ROM + ((CHR2_Bankselect1k & mask1k) << 10);
                NesCore.chrBankPtrs[7] = CHR_ROM + ((CHR3_Bankselect1k & mask1k) << 10);
            }
            else
            {
                int b0 = (CHR0_Bankselect2k & 0xFE) & mask1k;
                int b1 = (CHR1_Bankselect2k & 0xFE) & mask1k;
                NesCore.chrBankPtrs[0] = CHR_ROM + ((CHR0_Bankselect1k & mask1k) << 10);
                NesCore.chrBankPtrs[1] = CHR_ROM + ((CHR1_Bankselect1k & mask1k) << 10);
                NesCore.chrBankPtrs[2] = CHR_ROM + ((CHR2_Bankselect1k & mask1k) << 10);
                NesCore.chrBankPtrs[3] = CHR_ROM + ((CHR3_Bankselect1k & mask1k) << 10);
                NesCore.chrBankPtrs[4] = CHR_ROM + (b0 << 10);
                NesCore.chrBankPtrs[5] = CHR_ROM + ((b0 | 1) << 10);
                NesCore.chrBankPtrs[6] = CHR_ROM + (b1 << 10);
                NesCore.chrBankPtrs[7] = CHR_ROM + ((b1 | 1) << 10);
            }
        }

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
    }
}
