namespace AprNes
{
    // Irem TAM-S1 — Mapper 097
    // PRG: $8000-$BFFF fixed to last 16KB; $C000-$FFFF switchable 16KB
    //   (note: opposite of standard UNROM — fixed bank is FIRST, switchable is SECOND)
    // CHR: 8KB CHR-RAM (no CHR-ROM)
    // Mirror: bits[7:6] of written value: 0=SingleA, 1=H, 2=V, 3=SingleB
    unsafe public class Mapper097 : IMapper
    {
        byte* PRG_ROM, ppu_ram;
        int PRG_ROM_count;
        int* Vertical;

        int prgBank; // 16KB bank at $C000-$FFFF

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;
        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void CpuCycle() { }

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count;
            Vertical = _Vertical;
        }

        public void Reset()
        {
            prgBank = 0;
            UpdateCHRBanks();
        }

        public void UpdateCHRBanks()
        {
            // 8KB CHR-RAM in ppu_ram[0..8191]
            for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + i * 1024;
        }

        public byte MapperR_ExpansionROM(ushort address) { return 0; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        public void MapperW_PRG(ushort address, byte value)
        {
            prgBank = value & 0x0F;
            switch (value >> 6)
            {
                case 0: *Vertical = 2; break; // Single-screen A
                case 1: *Vertical = 1; break; // Horizontal
                case 2: *Vertical = 0; break; // Vertical
                case 3: *Vertical = 3; break; // Single-screen B
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            int n16k = PRG_ROM_count; // PRG_ROM_count is already in 16KB units
            if (address < 0xC000)
                return PRG_ROM[(address - 0x8000) + ((n16k - 1) << 14)]; // fixed last
            return PRG_ROM[(address - 0xC000) + ((prgBank % n16k) << 14)]; // switchable
        }

        // CHR-RAM (8KB in ppu_ram)
        public byte MapperR_CHR(int address) { return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF]; }
        public void MapperW_CHR(int addr, byte val) { NesCore.chrBankPtrs[(addr >> 10) & 7][addr & 0x3FF] = val; }
    }
}
