namespace AprNes
{
    // Irem 74HC161/32 — Holy Diver (J), Uchuusen Cosmo Carrier (J)
    // Single register $8000-$FFFF: CCCCPPPP
    //   Bits 0-2: Select 16K PRG bank at $8000
    //   Bit  3:   Mirroring (Holy Diver variant: 0=single-A, 1=single-B)
    //   Bits 4-7: Select 8K CHR bank at PPU $0000
    // Last 16K PRG fixed at $C000.
    // No IRQ. CHR-ROM only.
    unsafe public class Mapper078 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int CHR_ROM_count, PRG_ROM_count;
        int* Vertical;

        int prgBank;
        int chrBank;
        public bool isHolyDiver = false;  // submapper 3: V/H mirroring; else submapper 1: fixed mirroring

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM;
            CHR_ROM = _CHR_ROM;
            ppu_ram = _ppu_ram;
            CHR_ROM_count = _CHR_ROM_count;
            PRG_ROM_count = _PRG_ROM_count;
            Vertical = _Vertical;
            UpdateCHRBanks();
        }

        public void Reset()
        {
            prgBank = 0;
            chrBank = 0;
            // Uchuusen (submapper 1) uses single-screen mirroring.
            // ROM header has four-screen flag set as a side-effect of mapper encoding,
            // so we force the correct initial mirroring here (same as Mesen2 DB override).
            if (!isHolyDiver)
                *Vertical = 2;  // single-A by default
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return 0; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        public void MapperW_PRG(ushort address, byte value)
        {
            prgBank = value & 0x07;                      // bits 0-2: 16K PRG bank
            if (isHolyDiver)
                *Vertical = (value & 0x08) != 0 ? 1 : 0;    // Holy Diver: 1=Vertical, 0=Horizontal
            else
                *Vertical = (value & 0x08) != 0 ? 3 : 2;    // Uchuusen: 3=single-B, 2=single-A
            chrBank   = (value >> 4) & 0x0F;             // bits 4-7: 8K CHR bank
            UpdateCHRBanks();
        }

        public byte MapperR_RPG(ushort address)
        {
            if (address < 0xC000)
                return PRG_ROM[(address - 0x8000) + ((prgBank % PRG_ROM_count) << 14)];
            return PRG_ROM[(address - 0xC000) + ((PRG_ROM_count - 1) << 14)];
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0) return;
            byte* b = CHR_ROM + ((chrBank % CHR_ROM_count) << 13);
            for (int i = 0; i < 8; i++)
                NesCore.chrBankPtrs[i] = b + (i << 10);
        }

        public byte MapperR_CHR(int address)
        {
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
        public void CpuCycle() { }
        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;
        public void NotifyA12(int addr, int ppuAbsCycle) { }
    }
}
