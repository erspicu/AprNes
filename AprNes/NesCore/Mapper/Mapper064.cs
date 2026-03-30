namespace AprNes
{
    // Tengen RAMBO-1 — Shinobi (Tengen), Klax (Tengen), Skull & Crossbones (Tengen)
    // Similar to MMC3 but with key differences:
    //   PRG: 3×8K switchable (regs 6,7,15) + fixed last 8K at $E000; $8000 writes select cmd via bits 3-0
    //   CHR: regs 0,1 address 2K pairs (fine mode: regs 8,9 each split one 1K of the pair independently)
    //        regs 2-5 select 1K each; bit7 inverts A12 (swaps $0xxx/$1xxx CHR pattern tables)
    //   IRQ: A12 scanline mode OR CPU-cycle mode ($C001 bit0); 8-bit down-counter with reload
    //   Mirror: $A000 bit0 (0=Vertical, 1=Horizontal) — same encoding as MMC3
    //
    // IRQ counter logic (NESdev Wiki / MAME):
    //   On A12 rise (PPU mode) or every 4 CPU cycles (CPU mode): ClockIrqCounter
    //   ClockIrqCounter (IF/ELSE, no always-decrement):
    //     if needReload → counter = latch | (latch ? 1 : 0)  (OR-with-1 bias)
    //     else if counter==0 → counter = latch
    //     else counter--
    //     if counter==0 && enabled → set needIrqDelay
    //   needIrqDelay counts down per CPU cycle; fires IRQ when it reaches 0
    //   CPU mode delay = 1 cycle, PPU mode delay = 2 cycles
    //
    // Special: forceClock — when leaving CPU mode, forces one more 4-cycle clock group (Skull & Crossbones fix)

    unsafe public class Mapper064 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram, NES_MEM;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        byte currentReg;
        byte[] regs = new byte[16];

        // IRQ state
        bool irqEnabled;
        bool irqCycleMode;
        bool needReload;
        byte irqCounter;
        byte irqReloadValue;
        byte cpuClockCounter;
        int  needIrqDelay;   // counts down CPU cycles before asserting IRQ
        bool forceClock;

        const int PpuIrqDelay = 2;
        const int CpuIrqDelay = 1;

        // A12 watcher (Mesen2-style accumulating cyclesDown)
        int a12LastCycle  = 0;
        int a12CyclesDown = 0;
        const int A12_MIN_DELAY = 30;

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
            currentReg = 0;
            for (int i = 0; i < 16; i++) regs[i] = 0;
            irqEnabled = false;
            irqCycleMode = false;
            needReload = false;
            irqCounter = irqReloadValue = 0;
            cpuClockCounter = 0;
            needIrqDelay = 0;
            forceClock = false;
            a12LastCycle = 0; a12CyclesDown = 0;
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NES_MEM[address] = value; }

        public void MapperW_PRG(ushort address, byte value)
        {
            switch (address & 0xE001)
            {
                case 0x8000: {
                    byte changed = (byte)(currentReg ^ value);
                    currentReg = value;
                    // MAME: update CHR only if A12-inversion (bit7) or fine-mode (bit5) changed
                    if ((changed & 0xA0) != 0) UpdateCHRBanks();
                    break;
                }

                case 0x8001:
                    regs[currentReg & 0x0F] = value;
                    UpdateCHRBanks();
                    break;

                case 0xA000:
                    *Vertical = (value & 0x01) != 0 ? 0 : 1;  // bit0: 0=V, 1=H (same encoding as MMC3)
                    break;

                case 0xC000:
                    irqReloadValue = value;
                    break;

                case 0xC001:
                    // Switching FROM cycle mode TO A12 mode: one extra forced clock (Skull & Crossbones fix)
                    if (irqCycleMode && (value & 0x01) == 0)
                        forceClock = true;
                    irqCycleMode = (value & 0x01) == 1;
                    if (irqCycleMode) cpuClockCounter = 0;
                    needReload = true;
                    break;

                case 0xE000:
                    irqEnabled = false;
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                    break;

                case 0xE001:
                    irqEnabled = true;
                    break;
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            int total8k = PRG_ROM_count * 2;
            int bank;
            if ((currentReg & 0x40) == 0)
            {
                // PRG mode 0: $8000=reg[6], $A000=reg[7], $C000=reg[15], $E000=last(fixed)
                if      (address < 0xA000) bank = regs[6]  % total8k;
                else if (address < 0xC000) bank = regs[7]  % total8k;
                else if (address < 0xE000) bank = regs[15] % total8k;
                else                       bank = total8k - 1;
            }
            else
            {
                // PRG mode 1: $8000=reg[15], $A000=reg[6], $C000=reg[7], $E000=last(fixed)
                if      (address < 0xA000) bank = regs[15] % total8k;
                else if (address < 0xC000) bank = regs[6]  % total8k;
                else if (address < 0xE000) bank = regs[7]  % total8k;
                else                       bank = total8k - 1;
            }
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
            int inv = (currentReg & 0x80) != 0 ? 4 : 0;  // A12 inversion: XOR CHR slots with 4

            // Slots 0,1 ^ inv: reg[0] pair (2K); fine mode splits slot 1^inv to reg[8]
            // Slots 2,3 ^ inv: reg[1] pair (2K); fine mode splits slot 3^inv to reg[9]
            // Slots 4,5,6,7 ^ inv: reg[2],reg[3],reg[4],reg[5] (1K each)
            NesCore.chrBankPtrs[0 ^ inv] = CHR_ROM + ((regs[0] % total1k) << 10);
            NesCore.chrBankPtrs[2 ^ inv] = CHR_ROM + ((regs[1] % total1k) << 10);
            NesCore.chrBankPtrs[4 ^ inv] = CHR_ROM + ((regs[2] % total1k) << 10);
            NesCore.chrBankPtrs[5 ^ inv] = CHR_ROM + ((regs[3] % total1k) << 10);
            NesCore.chrBankPtrs[6 ^ inv] = CHR_ROM + ((regs[4] % total1k) << 10);
            NesCore.chrBankPtrs[7 ^ inv] = CHR_ROM + ((regs[5] % total1k) << 10);

            if ((currentReg & 0x20) != 0)
            {
                // Fine mode: reg[8] and reg[9] independently address the second 1K of each pair
                NesCore.chrBankPtrs[1 ^ inv] = CHR_ROM + ((regs[8] % total1k) << 10);
                NesCore.chrBankPtrs[3 ^ inv] = CHR_ROM + ((regs[9] % total1k) << 10);
            }
            else
            {
                // Normal mode: second 1K of each pair is reg+1
                NesCore.chrBankPtrs[1 ^ inv] = CHR_ROM + (((regs[0] + 1) % total1k) << 10);
                NesCore.chrBankPtrs[3 ^ inv] = CHR_ROM + (((regs[1] + 1) % total1k) << 10);
            }
        }

        public byte MapperR_CHR(int address) { return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF]; }
        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }

        void ClockIrqCounter(int delay)
        {
            // NESdev Wiki / MAME style: IF/ELSE (no always-decrement)
            if (needReload)
            {
                // OR-with-1: reload value ORed with 1 if non-zero (wiki-documented behaviour)
                irqCounter = (byte)(irqReloadValue | (irqReloadValue != 0 ? 1 : 0));
                needReload = false;
            }
            else if (irqCounter == 0)
            {
                irqCounter = irqReloadValue;
            }
            else
            {
                irqCounter--;
            }

            if (irqCounter == 0 && irqEnabled)
                needIrqDelay = delay;
        }

        public void CpuCycle()
        {
            // Delayed IRQ assertion
            if (needIrqDelay > 0)
            {
                needIrqDelay--;
                if (needIrqDelay == 0)
                {
                    NesCore.statusmapperint = true;
                    NesCore.UpdateIRQLine();
                }
            }

            // CPU cycle counting mode: clock IRQ counter every 4 CPU cycles
            if (irqCycleMode || forceClock)
            {
                cpuClockCounter = (byte)((cpuClockCounter + 1) & 0x03);
                if (cpuClockCounter == 0)
                {
                    ClockIrqCounter(CpuIrqDelay);
                    forceClock = false;
                }
            }
        }

        public void NotifyA12(int addr, int ppuAbsCycle)
        {
            if (irqCycleMode) return;  // CPU mode: A12 ignored

            // Mesen2-style A12Watcher: accumulate cycles while A12 is low
            if (a12CyclesDown > 0)
            {
                if (a12LastCycle > ppuAbsCycle)
                    a12CyclesDown += (89342 - a12LastCycle) + ppuAbsCycle; // frame wrapped
                else
                    a12CyclesDown += (ppuAbsCycle - a12LastCycle);
            }

            if ((addr & 0x1000) == 0)
            {
                // A12 low: start counting if not already
                if (a12CyclesDown == 0)
                    a12CyclesDown = 1;
            }
            else
            {
                // A12 high: clock IRQ if was low long enough
                if (a12CyclesDown > A12_MIN_DELAY)
                    ClockIrqCounter(PpuIrqDelay);
                a12CyclesDown = 0;
            }
            a12LastCycle = ppuAbsCycle;
        }
            public void Cleanup() { }
}
}
