namespace AprNes
{
    // CNROM + CHR copy-protection — Mapper 185
    // Write to $8000-$FFFF selects CHR 8KB bank AND controls protection.
    // Protection (submapper 0 heuristic): CHR enabled if (value & 0x0F) != 0 && value != 0x13
    // When CHR is disabled, PPU reads return 0xFF (D0 pull-up: OR with 0x01 = 0xFF).
    // PRG: up to 32KB fixed (mirror if 16KB).
    //
    // Note: Most games write the unlock value once at startup. The correct bank
    // is always bank 0 (CHR page 0 when enabled). Bits[1:0] select the bank but
    // in practice games only use bank 0.

    unsafe public class Mapper185 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        bool chrEnabled;

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
        }

        public void Reset()
        {
            chrEnabled = true;
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }

        public void MapperW_PRG(ushort address, byte value)
        {
            // Submapper 0 heuristic: CHR enabled if (value & 0x0F) != 0 and value != 0x13
            chrEnabled = (value & 0x0F) != 0 && value != 0x13;
            UpdateCHRBanks();
        }

        public byte MapperR_RPG(ushort address)
        {
            int offset = address - 0x8000;
            int size = PRG_ROM_count << 15;
            return PRG_ROM[offset % size];
        }

        public void UpdateCHRBanks()
        {
            if (!chrEnabled)
            {
                // CHR disabled: point to null/garbage — use ppu_ram zeroed area as placeholder
                // Reads will go through MapperR_CHR which returns 0xFF when disabled
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
                return;
            }
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
                return;
            }
            // Always use CHR bank 0 (protection games only have 1 CHR bank)
            byte* b = CHR_ROM;
            for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = b + (i << 10);
        }

        public byte MapperR_CHR(int address)
        {
            if (!chrEnabled)
                return 0xFF;  // pull-up on D0 gives 0xFF when protection active
            if (CHR_ROM_count == 0) return ppu_ram[address];
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }

        public void CpuCycle() { }
        public void NotifyA12(int addr, int ppuAbsCycle) { }
    }
}
