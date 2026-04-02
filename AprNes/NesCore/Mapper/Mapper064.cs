using System.Runtime.CompilerServices;

namespace AprNes
{
    // Tengen RAMBO-1 — Shinobi (Tengen), Klax (Tengen), Skull & Crossbones (Tengen)
    // 包含極限效能快取、精準 PRG 平移映射與 A12 邊緣觸發 IRQ。
    unsafe public class Mapper064 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram, NES_MEM;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        byte currentReg;
        byte[] regs = new byte[16];

        // 超高速 PRG 記憶體指標快取
        byte*[] prgBankPtrs = new byte*[4];

        // IRQ 狀態機
        bool irqEnabled;
        bool irqCycleMode;
        bool needReload;
        byte irqCounter;
        byte irqReloadValue;
        byte cpuClockCounter;
        int  needIrqDelay;
        bool forceClock;

        const int PpuIrqDelay = 2;
        const int CpuIrqDelay = 1;

        // A12 watcher (Mesen2-style: accumulating cyclesDown, minDelay=30)
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

            // 【開機防呆】Klax 預期開機時 $C000 必須在倒數第二個 Bank
            if (PRG_ROM_count > 0)
                regs[15] = (byte)((PRG_ROM_count * 2) - 2);

            irqEnabled = false;
            irqCycleMode = false;
            needReload = false;
            irqCounter = irqReloadValue = 0;
            cpuClockCounter = 0;
            needIrqDelay = 0;
            forceClock = false;

            a12LastCycle = 0;
            a12CyclesDown = 0;

            UpdatePRGBanks();
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
                case 0x8000:
                {
                    byte changed = (byte)(currentReg ^ value);
                    currentReg = value;

                    if ((changed & 0xA0) != 0) UpdateCHRBanks();
                    if ((changed & 0x40) != 0) UpdatePRGBanks();
                    break;
                }
                case 0x8001:
                {
                    int regIdx = currentReg & 0x0F;
                    regs[regIdx] = value;

                    if (regIdx == 6 || regIdx == 7 || regIdx == 15)
                        UpdatePRGBanks();
                    else
                        UpdateCHRBanks();
                    break;
                }
                case 0xA000:
                    *Vertical = (value & 0x01) != 0 ? 0 : 1;
                    break;
                case 0xC000:
                    irqReloadValue = value;
                    break;
                case 0xC001:
                {
                    bool newCycleMode = (value & 0x01) == 1;
                    // Mesen2: CPU→PPU mode switch sets forceClock (deferred to next CpuCycle)
                    if (irqCycleMode && !newCycleMode)
                        forceClock = true;
                    irqCycleMode = newCycleMode;
                    if (irqCycleMode) cpuClockCounter = 0;
                    needReload = true;
                    break;
                }
                case 0xE000:
                    irqEnabled = false;
                    // Mesen2: clear IRQ source but do NOT clear needIrqDelay
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                    break;
                case 0xE001:
                    irqEnabled = true;
                    break;
            }
        }

        void UpdatePRGBanks()
        {
            if (PRG_ROM_count == 0) return;
            int total8k = PRG_ROM_count * 2;

            if ((currentReg & 0x40) == 0)
            {
                // PRG Mode 0 (正常順序)
                prgBankPtrs[0] = PRG_ROM + ((regs[6] % total8k) << 13);  // $8000
                prgBankPtrs[1] = PRG_ROM + ((regs[7] % total8k) << 13);  // $A000
                prgBankPtrs[2] = PRG_ROM + ((regs[15] % total8k) << 13); // $C000
            }
            else
            {
                // PRG Mode 1: 向下平移 (Mesen2/FCEUX 標準做法)
                prgBankPtrs[0] = PRG_ROM + ((regs[15] % total8k) << 13); // $8000
                prgBankPtrs[1] = PRG_ROM + ((regs[6] % total8k) << 13);  // $A000
                prgBankPtrs[2] = PRG_ROM + ((regs[7] % total8k) << 13);  // $C000
            }

            // $E000 永遠固定在最後一個 8KB Bank
            prgBankPtrs[3] = PRG_ROM + ((total8k - 1) << 13);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte MapperR_RPG(ushort address)
        {
            // O(1) 極速讀取
            return prgBankPtrs[(address - 0x8000) >> 13][address & 0x1FFF];
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
                return;
            }

            int total1k = CHR_ROM_count * 8;
            int inv = (currentReg & 0x80) != 0 ? 4 : 0;

            NesCore.chrBankPtrs[0 ^ inv] = CHR_ROM + ((regs[0] % total1k) << 10);
            NesCore.chrBankPtrs[2 ^ inv] = CHR_ROM + ((regs[1] % total1k) << 10);
            NesCore.chrBankPtrs[4 ^ inv] = CHR_ROM + ((regs[2] % total1k) << 10);
            NesCore.chrBankPtrs[5 ^ inv] = CHR_ROM + ((regs[3] % total1k) << 10);
            NesCore.chrBankPtrs[6 ^ inv] = CHR_ROM + ((regs[4] % total1k) << 10);
            NesCore.chrBankPtrs[7 ^ inv] = CHR_ROM + ((regs[5] % total1k) << 10);

            if ((currentReg & 0x20) != 0)
            {
                // Fine mode: 獨立控制 1K 對的下半部
                NesCore.chrBankPtrs[1 ^ inv] = CHR_ROM + ((regs[8] % total1k) << 10);
                NesCore.chrBankPtrs[3 ^ inv] = CHR_ROM + ((regs[9] % total1k) << 10);
            }
            else
            {
                // Normal mode: 連續的 2K
                NesCore.chrBankPtrs[1 ^ inv] = CHR_ROM + (((regs[0] + 1) % total1k) << 10);
                NesCore.chrBankPtrs[3 ^ inv] = CHR_ROM + (((regs[1] + 1) % total1k) << 10);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }

        void ClockIrqCounter(int delay)
        {
            // Mesen2 RAMBO-1 logic: reload with +1/+2 bias, then always decrement
            if (needReload)
            {
                irqCounter = (byte)(irqReloadValue <= 1
                    ? irqReloadValue + 1
                    : irqReloadValue + 2);
                needReload = false;
            }
            else if (irqCounter == 0)
            {
                irqCounter = (byte)(irqReloadValue + 1);
            }

            irqCounter--;
            if (irqCounter == 0 && irqEnabled)
                needIrqDelay = delay;
        }

        public void CpuCycle()
        {
            // Mesen2 order: CPU clock counter first, then IRQ delay assertion
            if (irqCycleMode || forceClock)
            {
                cpuClockCounter = (byte)((cpuClockCounter + 1) & 0x03);
                if (cpuClockCounter == 0)
                {
                    ClockIrqCounter(CpuIrqDelay);
                    forceClock = false;
                }
            }

            if (needIrqDelay > 0)
            {
                needIrqDelay--;
                if (needIrqDelay == 0)
                {
                    NesCore.statusmapperint = true;
                    NesCore.UpdateIRQLine();
                }
            }
        }

        public void NotifyA12(int addr, int ppuAbsCycle)
        {
            if (irqCycleMode) return;

            // Mesen2-style A12Watcher: accumulate cycles while A12 is low
            if (a12CyclesDown > 0)
            {
                if (a12LastCycle > ppuAbsCycle)
                    a12CyclesDown += (89342 - a12LastCycle) + ppuAbsCycle;
                else
                    a12CyclesDown += (ppuAbsCycle - a12LastCycle);
            }

            if ((addr & 0x1000) == 0)
            {
                if (a12CyclesDown == 0)
                    a12CyclesDown = 1;
            }
            else
            {
                if (a12CyclesDown > A12_MIN_DELAY)
                    ClockIrqCounter(PpuIrqDelay);
                a12CyclesDown = 0;
            }
            a12LastCycle = ppuAbsCycle;
        }

        public void CpuClockRise() { }
        public void Cleanup() { }
    }
}
