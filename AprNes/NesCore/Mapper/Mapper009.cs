namespace AprNes
{
    // MMC2 — used exclusively by Punch-Out!! (U/JU/E)
    // PRG: 8K swappable at $8000; last 3×8K fixed at $A000/$C000/$E000
    // CHR: Two 4K banks, each with $FD/$FE sub-page selected by PPU latch
    // Latch trigger (via NotifyA12 from PPU):
    //   $0FD8       → leftLatch  = $FD (index 0)
    //   $0FE8       → leftLatch  = $FE (index 1)
    //   $1FD8-$1FDF → rightLatch = $FD (index 0)
    //   $1FE8-$1FEF → rightLatch = $FE (index 1)
    // Bank switch deferred by 1 notification (matches Mesen2 _needChrUpdate model)
    unsafe public class Mapper009 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int CHR_ROM_count;
        int PRG_ROM_count;
        int* Vertical;

        int prgBank;
        int leftFD, leftFE;    // 4K CHR bank indices for left half ($0000-$0FFF)
        int rightFD, rightFE;  // 4K CHR bank indices for right half ($1000-$1FFF)
        int leftLatch;         // 0=$FD active, 1=$FE active; power-on = 1 ($FE)
        int rightLatch;
        bool needChrUpdate;    // deferred: apply chrBankPtrs on next NotifyA12 call

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
            prgBank = 0;
            leftFD = leftFE = rightFD = rightFE = 0;
            leftLatch = 1;   // $FE on power-on per spec
            rightLatch = 1;
            needChrUpdate = false;
        }

        public byte MapperR_ExpansionROM(ushort address) { return 0; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        public void MapperW_PRG(ushort address, byte value)
        {
            switch (address & 0xF000)
            {
                case 0xA000: prgBank = value & 0x0F; break;
                case 0xB000: leftFD  = value & 0x1F; if (leftLatch  == 0) UpdateLeftBank();  break;
                case 0xC000: leftFE  = value & 0x1F; if (leftLatch  == 1) UpdateLeftBank();  break;
                case 0xD000: rightFD = value & 0x1F; if (rightLatch == 0) UpdateRightBank(); break;
                case 0xE000: rightFE = value & 0x1F; if (rightLatch == 1) UpdateRightBank(); break;
                case 0xF000: *Vertical = (value & 1) != 0 ? 0 : 1; break; // 0=vertical,1=horiz
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            int total8k = PRG_ROM_count * 2;
            if (address < 0xA000) return PRG_ROM[(address - 0x8000) + (prgBank << 13)];
            if (address < 0xC000) return PRG_ROM[(address - 0xA000) + ((total8k - 3) << 13)];
            if (address < 0xE000) return PRG_ROM[(address - 0xC000) + ((total8k - 2) << 13)];
            return               PRG_ROM[(address - 0xE000) + ((total8k - 1) << 13)];
        }

        void UpdateLeftBank()
        {
            byte* b = CHR_ROM + ((leftLatch == 0 ? leftFD : leftFE) << 12);
            NesCore.chrBankPtrs[0] = b;
            NesCore.chrBankPtrs[1] = b + 1024;
            NesCore.chrBankPtrs[2] = b + 2048;
            NesCore.chrBankPtrs[3] = b + 3072;
        }

        void UpdateRightBank()
        {
            byte* b = CHR_ROM + ((rightLatch == 0 ? rightFD : rightFE) << 12);
            NesCore.chrBankPtrs[4] = b;
            NesCore.chrBankPtrs[5] = b + 1024;
            NesCore.chrBankPtrs[6] = b + 2048;
            NesCore.chrBankPtrs[7] = b + 3072;
        }

        public void NotifyA12(int addr, int ppuAbsCycle)
        {
            // Apply deferred bank switch from previous latch change (1-notification delay)
            if (needChrUpdate) { UpdateLeftBank(); UpdateRightBank(); needChrUpdate = false; }

            if      (addr == 0x0FD8)                        { leftLatch  = 0; needChrUpdate = true; }
            else if (addr == 0x0FE8)                        { leftLatch  = 1; needChrUpdate = true; }
            else if (addr >= 0x1FD8 && addr <= 0x1FDF)     { rightLatch = 0; needChrUpdate = true; }
            else if (addr >= 0x1FE8 && addr <= 0x1FEF)     { rightLatch = 1; needChrUpdate = true; }
        }

        public byte MapperR_CHR(int address)
        {
            // $2007 reads: reflect current chrBankPtrs state (consistent with renderer)
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void UpdateCHRBanks() { UpdateLeftBank(); UpdateRightBank(); }

        public void MapperW_CHR(int addr, byte val) { }  // CHR-ROM only, no writes
        public void CpuCycle() { }
    }
}
