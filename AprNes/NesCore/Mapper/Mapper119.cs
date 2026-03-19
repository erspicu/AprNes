using System.Runtime.InteropServices;

namespace AprNes
{
    // TQROM — Mapper 119
    // MMC3 variant where CHR bank values 0x40-0x7F address 8KB of CHR-RAM.
    // Values outside 0x40-0x7F address CHR-ROM as normal.
    // All PRG banking and IRQ behaviour is identical to standard MMC3 (Mapper004).
    unsafe public class Mapper119 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram, NES_MEM;
        int CHR_ROM_count, PRG_ROM_count;
        int* Vertical;

        // 8KB CHR-RAM (bank values 0x40-0x7F map here; 8 pages of 1KB)
        byte* chrRam;

        // MMC3 state
        bool IRQ_enable, IRQReset;
        int IRQlatchVal, IRQCounter;
        int BankReg;
        int lastA12, a12LowSince, lastNotifyTime;
        const int A12_FILTER = 16;

        int chr2k_0, chr2k_1;              // 2KB CHR bank registers (R0, R1)
        int chr1k_0, chr1k_1, chr1k_2, chr1k_3;  // 1KB CHR bank registers (R2-R5)
        int prg0, prg1;                    // 8KB PRG bank registers (R6, R7)
        int PRG_Bankmode, CHR_Bankmode;

        public MapperA12Mode A12NotifyMode => MapperA12Mode.MMC3;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
            NES_MEM = NesCore.NES_MEM;
            if (chrRam == null)
                chrRam = (byte*)Marshal.AllocHGlobal(8 * 1024);
        }

        public void Reset()
        {
            IRQ_enable = false; IRQReset = false;
            IRQlatchVal = 0; IRQCounter = 0;
            BankReg = 0;
            lastA12 = 0; a12LowSince = -100; lastNotifyTime = -100;
            chr2k_0 = 0; chr2k_1 = 0;
            chr1k_0 = 0; chr1k_1 = 0; chr1k_2 = 0; chr1k_3 = 0;
            prg0 = 0; prg1 = 0;
            PRG_Bankmode = 0; CHR_Bankmode = 0;
            for (int i = 0; i < 8 * 1024; i++) chrRam[i] = 0;
            UpdateCHRBanks();
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
            else IRQCounter--;

            if (IRQCounter == 0 && IRQ_enable)
            {
                NesCore.statusmapperint = true;
                NesCore.UpdateIRQLine();
            }
        }

        public void MapperW_PRG(ushort address, byte value)
        {
            if ((address & 1) == 0)
            {
                if (address < 0xA000)
                {
                    BankReg = value & 7;
                    PRG_Bankmode = (value & 0x40) >> 6;
                    int newCHRMode = (value & 0x80) >> 7;
                    if (newCHRMode != CHR_Bankmode) { CHR_Bankmode = newCHRMode; UpdateCHRBanks(); }
                    else CHR_Bankmode = newCHRMode;
                }
                else if (address < 0xC000) *Vertical = ((value & 1) > 0) ? 0 : 1;
                else if (address < 0xE000) IRQlatchVal = value;
                else { IRQ_enable = false; NesCore.statusmapperint = false; NesCore.UpdateIRQLine(); }
            }
            else
            {
                if (address < 0xA000)
                {
                    bool changed = false;
                    if (BankReg == 0) { chr2k_0 = value; changed = true; }
                    else if (BankReg == 1) { chr2k_1 = value; changed = true; }
                    else if (BankReg == 2) { chr1k_0 = value; changed = true; }
                    else if (BankReg == 3) { chr1k_1 = value; changed = true; }
                    else if (BankReg == 4) { chr1k_2 = value; changed = true; }
                    else if (BankReg == 5) { chr1k_3 = value; changed = true; }
                    else if (BankReg == 6) prg0 = value;
                    else prg1 = value;
                    if (changed) UpdateCHRBanks();
                }
                else if (address < 0xC000) { /* PRG RAM protect */ }
                else if (address < 0xE000) IRQReset = true;
                else IRQ_enable = true;
            }
        }

        bool IsRamBank(int v) { return v >= 0x40 && v <= 0x7F; }

        public void UpdateCHRBanks()
        {
            int total1k = CHR_ROM_count > 0 ? CHR_ROM_count * 8 : 8;

            if (CHR_Bankmode == 0)
            {
                Set2K(0, chr2k_0, total1k);
                Set2K(2, chr2k_1, total1k);
                Set1K(4, chr1k_0, total1k);
                Set1K(5, chr1k_1, total1k);
                Set1K(6, chr1k_2, total1k);
                Set1K(7, chr1k_3, total1k);
            }
            else
            {
                Set1K(0, chr1k_0, total1k);
                Set1K(1, chr1k_1, total1k);
                Set1K(2, chr1k_2, total1k);
                Set1K(3, chr1k_3, total1k);
                Set2K(4, chr2k_0, total1k);
                Set2K(6, chr2k_1, total1k);
            }
        }

        void Set1K(int slot, int bankVal, int total1k)
        {
            if (IsRamBank(bankVal))
                NesCore.chrBankPtrs[slot] = chrRam + ((bankVal - 0x40) & 7) * 1024;
            else if (CHR_ROM_count > 0)
                NesCore.chrBankPtrs[slot] = CHR_ROM + ((bankVal % total1k) << 10);
            else
                NesCore.chrBankPtrs[slot] = ppu_ram + slot * 1024;
        }

        void Set2K(int slot, int bankVal, int total1k)
        {
            int even = bankVal & 0xFE;
            if (IsRamBank(bankVal))
            {
                int ramSlot = (even - 0x40) & 6;
                NesCore.chrBankPtrs[slot]     = chrRam + ramSlot * 1024;
                NesCore.chrBankPtrs[slot + 1] = chrRam + (ramSlot + 1) * 1024;
            }
            else if (CHR_ROM_count > 0)
            {
                NesCore.chrBankPtrs[slot]     = CHR_ROM + ((even % total1k) << 10);
                NesCore.chrBankPtrs[slot + 1] = CHR_ROM + (((even + 1) % total1k) << 10);
            }
            else
            {
                NesCore.chrBankPtrs[slot]     = ppu_ram + slot * 1024;
                NesCore.chrBankPtrs[slot + 1] = ppu_ram + (slot + 1) * 1024;
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            int n8k = PRG_ROM_count * 2;
            if (PRG_Bankmode == 0)
            {
                if (address < 0xA000) return PRG_ROM[(address - 0x8000) + (prg0 << 13)];
                if (address < 0xC000) return PRG_ROM[(address - 0xA000) + (prg1 << 13)];
                if (address < 0xE000) return PRG_ROM[(address - 0xC000) + ((n8k - 2) << 13)];
                return PRG_ROM[(address - 0xE000) + ((n8k - 1) << 13)];
            }
            else
            {
                if (address < 0xA000) return PRG_ROM[(address - 0x8000) + ((n8k - 2) << 13)];
                if (address < 0xC000) return PRG_ROM[(address - 0xA000) + (prg1 << 13)];
                if (address < 0xE000) return PRG_ROM[(address - 0xC000) + (prg0 << 13)];
                return PRG_ROM[(address - 0xE000) + ((n8k - 1) << 13)];
            }
        }

        public byte MapperR_CHR(int address)
        {
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val)
        {
            // Write only if the slot points into CHR-RAM
            byte* ptr = NesCore.chrBankPtrs[(addr >> 10) & 7];
            if (ptr >= chrRam && ptr < chrRam + 8 * 1024)
                ptr[addr & 0x3FF] = val;
        }
    }
}
