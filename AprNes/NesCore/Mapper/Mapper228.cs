namespace AprNes
{
    // Action 52 / ActionEnterprises — Mapper 228
    // All logic derived from write address + data bits.
    // PRG: 16KB or 32KB switchable at $8000
    //   addr bits[13:7] = outer PRG select (7 bits)
    //   chipSelect = addr bits[12:11] (chip 0,1,2; 3 treated as 2)
    //   addr bit[5] = 0→32KB mode, 1→16KB mode
    // CHR: 8KB switchable (one bank)
    //   (addr bits[3:0] << 2) | (data bits[1:0]) = CHR bank
    // Mirror: addr bit[13] = 0→Vertical, 1→Horizontal
    unsafe public class Mapper228 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int prgBank0, prgBank1; // two 16KB PRG slots
        int chrBank;


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
            // Initialize to bank 0
            WriteRegister(0x8000, 0);
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        public void MapperW_PRG(ushort address, byte value)
        {
            WriteRegister(address, value);
        }

        void WriteRegister(int addr, byte value)
        {
            // Chip select from addr bits[12:11]
            int chipSelect = (addr >> 11) & 0x03;
            if (chipSelect == 3) chipSelect = 2;

            // PRG page: addr bits[10:6] combined with chip select bits
            int prgPage = ((addr >> 6) & 0x1F) | (chipSelect << 5);

            // Mirroring: addr bit[13]
            *Vertical = (addr & 0x2000) != 0 ? 1 : 0; // 1=H, 0=V

            // CHR bank: (addr bits[3:0] << 2) | data bits[1:0]
            chrBank = ((addr & 0x0F) << 2) | (value & 0x03);

            // PRG mode: addr bit[5]
            if ((addr & 0x20) != 0)
            {
                // 16KB mode: both slots point to same bank
                prgBank0 = prgPage;
                prgBank1 = prgPage;
            }
            else
            {
                // 32KB mode: prgPage points to even pair
                prgBank0 = prgPage & 0xFE;
                prgBank1 = (prgPage & 0xFE) + 1;
            }
            UpdateCHRBanks();
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + i * 1024;
                return;
            }
            int n8k = CHR_ROM_count;
            int bank = chrBank % n8k;
            for (int i = 0; i < 8; i++)
                NesCore.chrBankPtrs[i] = CHR_ROM + (bank << 13) + i * 1024;
        }

        public byte MapperR_RPG(ushort address)
        {
            int n16k = PRG_ROM_count; // PRG_ROM_count is already in 16KB units
            if (address < 0xC000)
                return PRG_ROM[(address - 0x8000) + ((prgBank0 % n16k) << 14)];
            return PRG_ROM[(address - 0xC000) + ((prgBank1 % n16k) << 14)];
        }

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
    }
}
