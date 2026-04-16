namespace AprNes
{
    // Ntdec / Asder — Mapper 112
    // Used by Asder 三國志 (San Guo Zhi), 封神榜 (Fengshenbang), Shen Hua Jian etc.
    // Register select via $8000-$FFFF (addr & 0xE001):
    //   $8000: currentReg = value & 7
    //   $A000: registers[currentReg] = value
    //   $C000: outerChrBank = value (extends high bits of chr regs 4..7)
    //   $E000: mirroring = value & 1 (0=Vertical, 1=Horizontal)
    //
    // PRG: 2×8K switchable at $8000/$A000 (regs 0/1), last 2×8K fixed.
    // CHR: 2×2K at $0000/$0800 (regs 2/3 × 2), 4×1K at $1000-$1C00 (regs 4..7)
    //      Outer CHR extends bit 8+ for regs 4..7: bits 4,5,6,7 of outerChrBank → << for regs 4..7
    // Ref: Mesen2/Ntdec/Mapper112.h
    unsafe public class Mapper112 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        byte currentReg;
        byte outerChrBank;
        byte[] registers = new byte[8];

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
            UpdateCHRBanks();
        }

        public void Reset()
        {
            currentReg = 0;
            outerChrBank = 0;
            for (int i = 0; i < 8; i++) registers[i] = 0;
            *Vertical = 1; // Vertical default
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }

        public void MapperW_PRG(ushort address, byte value)
        {
            switch (address & 0xE001)
            {
                case 0x8000: currentReg = (byte)(value & 7); break;
                case 0xA000: registers[currentReg] = value; UpdateCHRBanks(); break;
                case 0xC000: outerChrBank = value; UpdateCHRBanks(); break;
                case 0xE000: *Vertical = (value & 1) != 0 ? 0 : 1; break; // 0=V, 1=H (NES convention)
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            // PRG_ROM_count is 16KB units. 8KB banks = count * 2.
            int total8k = PRG_ROM_count * 2;
            if (total8k < 1) total8k = 1;
            int bank;
            if      (address < 0xA000) bank = registers[0] % total8k;
            else if (address < 0xC000) bank = registers[1] % total8k;
            else if (address < 0xE000) bank = (total8k - 2 + total8k) % total8k;
            else                       bank = (total8k - 1 + total8k) % total8k;
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
            // 2KB banks at $0000/$0800: registers[2]/[3] × 2 (2K step = 2×1K)
            int b01 = (registers[2] & 0xFE) % total1k;
            int b23 = ((registers[2] & 0xFE) + 1) % total1k;
            int b45 = (registers[3] & 0xFE) % total1k;
            int b67 = ((registers[3] & 0xFE) + 1) % total1k;
            NesCore.chrBankPtrs[0] = CHR_ROM + (b01 << 10);
            NesCore.chrBankPtrs[1] = CHR_ROM + (b23 << 10);
            NesCore.chrBankPtrs[2] = CHR_ROM + (b45 << 10);
            NesCore.chrBankPtrs[3] = CHR_ROM + (b67 << 10);
            // 1KB banks at $1000-$1C00: registers[4..7] with outerChrBank extending high bits
            // Mesen2: SelectChrPage(4, registers[4] | ((outer & 0x10) << 4))
            //         SelectChrPage(5, registers[5] | ((outer & 0x20) << 3))
            //         SelectChrPage(6, registers[6] | ((outer & 0x40) << 2))
            //         SelectChrPage(7, registers[7] | ((outer & 0x80) << 1))
            int r4 = (registers[4] | ((outerChrBank & 0x10) << 4)) % total1k;
            int r5 = (registers[5] | ((outerChrBank & 0x20) << 3)) % total1k;
            int r6 = (registers[6] | ((outerChrBank & 0x40) << 2)) % total1k;
            int r7 = (registers[7] | ((outerChrBank & 0x80) << 1)) % total1k;
            NesCore.chrBankPtrs[4] = CHR_ROM + (r4 << 10);
            NesCore.chrBankPtrs[5] = CHR_ROM + (r5 << 10);
            NesCore.chrBankPtrs[6] = CHR_ROM + (r6 << 10);
            NesCore.chrBankPtrs[7] = CHR_ROM + (r7 << 10);
        }

        public byte MapperR_CHR(int address)
        {
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
        public void CpuCycle() { }
        public void CpuClockRise() { }
        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void PpuClock() { }
        public void Cleanup() { }
    }
}
