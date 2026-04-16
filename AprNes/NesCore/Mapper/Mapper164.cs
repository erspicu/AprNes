namespace AprNes
{
    // Waixing 164 — Mapper 164
    // PRG: 32KB switch at $8000-$FFFF
    // CHR: 8KB fixed (CHR-RAM typically)
    // Registers at $5000-$5FFF (masked 0x7300):
    //   $5000: prgBank low nibble (value & 0x0F)
    //   $5100: prgBank high nibble ((value & 0x0F) << 4)
    // Init: prgBank = 0x0F
    // Games: Final Fantasy V hack, 幻想水滸傳 etc.
    // Ref: Mesen2/Waixing/Waixing164.h
    unsafe public class Mapper164 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int prgBank;

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
            UpdateCHRBanks();
        }

        public void Reset() { prgBank = 0x0F; }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }

        public void MapperW_ExpansionROM(ushort address, byte value)
        {
            // $5000-$5FFF: register writes
            if (address < 0x5000 || address > 0x5FFF) return;
            switch (address & 0x7300)
            {
                case 0x5000: prgBank = (prgBank & 0xF0) | (value & 0x0F); break;
                case 0x5100: prgBank = (prgBank & 0x0F) | ((value & 0x0F) << 4); break;
            }
        }

        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public void MapperW_PRG(ushort address, byte value) { }

        public byte MapperR_RPG(ushort address)
        {
            int total32k = PRG_ROM_count / 2;
            if (total32k < 1) total32k = 1;
            int bank = prgBank % total32k;
            return PRG_ROM[(address - 0x8000) + (bank << 15)];
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
                return;
            }
            for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = CHR_ROM + (i << 10);
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
