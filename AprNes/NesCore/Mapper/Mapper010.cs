namespace AprNes
{
    // MMC4 — Fire Emblem, Famicom Wars
    // PRG: 16K swappable at $8000; last 16K fixed at $C000
    // CHR: Two 4K banks, each with $FD/$FE sub-page selected by PPU latch
    // Latch trigger (via NotifyA12 from PPU, deferred 1-notification like MMC2):
    //   $0FD8-$0FDF → leftLatch  = $FD (index 0)
    //   $0FE8-$0FEF → leftLatch  = $FE (index 1)
    //   $1FD8-$1FDF → rightLatch = $FD (index 0)
    //   $1FE8-$1FEF → rightLatch = $FE (index 1)
    unsafe public class Mapper010 : IMapper
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
        bool needChrUpdate;    // deferred: apply chrBankPtrs on next NotifyA12 call (same as MMC2)

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

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        public void MapperW_PRG(ushort address, byte value)
        {
            int page = address & 0xF000;
            if      (page == 0xA000) { prgBank = value & 0x0F; }
            else if (page == 0xB000) { leftFD  = value & 0x1F; if (leftLatch  == 0) UpdateLeftBank();  }
            else if (page == 0xC000) { leftFE  = value & 0x1F; if (leftLatch  == 1) UpdateLeftBank();  }
            else if (page == 0xD000) { rightFD = value & 0x1F; if (rightLatch == 0) UpdateRightBank(); }
            else if (page == 0xE000) { rightFE = value & 0x1F; if (rightLatch == 1) UpdateRightBank(); }
            else if (page == 0xF000) { *Vertical = (value & 1) != 0 ? 0 : 1; } // 0=vertical,1=horiz
        }

        public byte MapperR_RPG(ushort address)
        {
            // $8000-$BFFF: selected 16K bank; $C000-$FFFF: last 16K fixed
            if (address < 0xC000) return PRG_ROM[(address - 0x8000) + ((prgBank % PRG_ROM_count) << 14)];
            return PRG_ROM[(address - 0xC000) + ((PRG_ROM_count - 1) << 14)];
        }

        void UpdateLeftBank()
        {
            int total4k = CHR_ROM_count * 2;
            int bank = (leftLatch == 0 ? leftFD : leftFE) % total4k;
            byte* b = CHR_ROM + (bank << 12);
            NesCore.chrBankPtrs[0] = b;
            NesCore.chrBankPtrs[1] = b + 1024;
            NesCore.chrBankPtrs[2] = b + 2048;
            NesCore.chrBankPtrs[3] = b + 3072;
        }

        void UpdateRightBank()
        {
            int total4k = CHR_ROM_count * 2;
            int bank = (rightLatch == 0 ? rightFD : rightFE) % total4k;
            byte* b = CHR_ROM + (bank << 12);
            NesCore.chrBankPtrs[4] = b;
            NesCore.chrBankPtrs[5] = b + 1024;
            NesCore.chrBankPtrs[6] = b + 2048;
            NesCore.chrBankPtrs[7] = b + 3072;
        }

        public void NotifyA12(int addr, int ppuAbsCycle)
        {
            // Apply deferred bank switch from previous latch change (1-notification delay, same as MMC2)
            if (needChrUpdate) { UpdateLeftBank(); UpdateRightBank(); needChrUpdate = false; }

            if      (addr >= 0x0FD8 && addr <= 0x0FDF) { leftLatch  = 0; needChrUpdate = true; }
            else if (addr >= 0x0FE8 && addr <= 0x0FEF) { leftLatch  = 1; needChrUpdate = true; }
            else if (addr >= 0x1FD8 && addr <= 0x1FDF) { rightLatch = 0; needChrUpdate = true; }
            else if (addr >= 0x1FE8 && addr <= 0x1FEF) { rightLatch = 1; needChrUpdate = true; }
        }

        public byte MapperR_CHR(int address)
        {
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void UpdateCHRBanks() { UpdateLeftBank(); UpdateRightBank(); }

        public void MapperW_CHR(int addr, byte val) { }  // CHR-ROM only, no writes
        public void CpuCycle() { }
        public MapperA12Mode A12NotifyMode => MapperA12Mode.MMC2_4;
    }
}
