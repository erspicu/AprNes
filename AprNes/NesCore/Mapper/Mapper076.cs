namespace AprNes
{
    // Namco 109 / Namco108_76 — Mapper 076
    // Namco 108 family: same register mechanism as Mapper088/206,
    // but CHR uses four 2KB banks instead of two 2KB + four 1KB.
    // PRG: two switchable 8KB at $8000/$A000, fixed last two 8KB at $C000/$E000
    // CHR: 4×2KB banks (registers R2, R3, R4, R5 select 2KB banks at $0000/$0800/$1000/$1800)
    // No IRQ. Hardwired mirroring (from header).
    unsafe public class Mapper076 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int cmdReg;
        int[] reg = new int[8]; // reg[2-5] used for CHR, reg[6-7] for PRG

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;
        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void CpuCycle() { }

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
        }

        public void Reset()
        {
            cmdReg = 0;
            for (int i = 0; i < 8; i++) reg[i] = 0;
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        public void MapperW_PRG(ushort address, byte value)
        {
            // Namco108: all writes redirected to $8000/$8001 (addr & 0x8001)
            // Disable CHR Mode and PRG Mode bits (force bits 6,7 to 0)
            int regAddr = address & 0x8001;
            if (regAddr == 0x8000)
            {
                // Command register: mask off CHR/PRG mode bits
                cmdReg = value & 0x07;
            }
            else // 0x8001
            {
                reg[cmdReg] = value & 0x3F;
                UpdateCHRBanks();
            }
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + i * 1024;
                return;
            }
            // 4×2KB CHR banks using reg[2..5]; each reg selects a 2KB page directly
            int n1k = CHR_ROM_count * 8;
            // reg values are 2KB page indices; convert to 1KB for pointer arithmetic
            int b0 = (reg[2] * 2) % n1k;
            int b1 = (reg[3] * 2) % n1k;
            int b2 = (reg[4] * 2) % n1k;
            int b3 = (reg[5] * 2) % n1k;
            NesCore.chrBankPtrs[0] = CHR_ROM + (b0 << 10);
            NesCore.chrBankPtrs[1] = CHR_ROM + ((b0 + 1) << 10);
            NesCore.chrBankPtrs[2] = CHR_ROM + (b1 << 10);
            NesCore.chrBankPtrs[3] = CHR_ROM + ((b1 + 1) << 10);
            NesCore.chrBankPtrs[4] = CHR_ROM + (b2 << 10);
            NesCore.chrBankPtrs[5] = CHR_ROM + ((b2 + 1) << 10);
            NesCore.chrBankPtrs[6] = CHR_ROM + (b3 << 10);
            NesCore.chrBankPtrs[7] = CHR_ROM + ((b3 + 1) << 10);
        }

        public byte MapperR_RPG(ushort address)
        {
            int n8k = PRG_ROM_count * 2;
            if (address < 0xA000) return PRG_ROM[(address - 0x8000) + ((reg[6] % n8k) << 13)];
            if (address < 0xC000) return PRG_ROM[(address - 0xA000) + ((reg[7] % n8k) << 13)];
            if (address < 0xE000) return PRG_ROM[(address - 0xC000) + ((n8k - 2) << 13)];
            return PRG_ROM[(address - 0xE000) + ((n8k - 1) << 13)];
        }

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
    }
}
