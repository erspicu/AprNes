namespace AprNes
{
    // MMC1 (SxROM) mapper for Speed Core
    // Ref: https://wiki.nesdev.com/w/index.php/MMC1
    unsafe public class Mapper001_S : IMapper_S
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int CHR_ROM_count;
        int PRG_ROM_count;
        int* Vertical;

        // PRG mode 3 on power-up: fix last bank at $C000, switch $8000
        int PRG_Bankmode    = 3;
        int CHR_Bankmode    = 0; // 0=8KB mode, 1=4KB mode
        int CHR0_Bankselect = 0;
        int CHR1_Bankselect = 0;
        int PRG_Bankselect  = 0;

        // Serial shift register
        int MapperShiftCount  = 0;
        int MapperRegBuffer   = 0;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
                               int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM       = _PRG_ROM;
            CHR_ROM       = _CHR_ROM;
            ppu_ram       = _ppu_ram;
            CHR_ROM_count = _CHR_ROM_count;
            PRG_ROM_count = _PRG_ROM_count;
            Vertical      = _Vertical;
            // Power-up: PRG mode 3, last bank fixed at $C000
            PRG_Bankselect = _PRG_ROM_count - 2; // points to second-to-last 16KB bank (ignored for fixed)
        }

        public byte MapperR_PRG(ushort address)
        {
            if (PRG_Bankmode == 0 || PRG_Bankmode == 1)
            {
                // 32KB switch: ignore low bit of PRG_Bankselect
                return PRG_ROM[(address - 0x8000) + ((PRG_Bankselect & ~1) << 14)];
            }
            else if (PRG_Bankmode == 2)
            {
                // Fix first bank at $8000, switch $C000
                if (address < 0xC000)
                    return PRG_ROM[address - 0x8000];
                else
                    return PRG_ROM[(address - 0xC000) + (PRG_Bankselect << 14)];
            }
            else // mode 3 (power-up default)
            {
                // Switch $8000, fix last bank at $C000
                if (address < 0xC000)
                    return PRG_ROM[(address - 0x8000) + (PRG_Bankselect << 14)];
                else
                    return PRG_ROM[(address - 0xC000) + ((PRG_ROM_count - 1) << 14)];
            }
        }

        public void MapperW_PRG(ushort address, byte value)
        {
            // Bit 7 set: reset shift register, set PRG mode 3
            if ((value & 0x80) != 0)
            {
                MapperShiftCount = MapperRegBuffer = 0;
                PRG_Bankmode = 3;
                return;
            }

            // Accumulate 5 bits LSB-first
            MapperRegBuffer |= (value & 1) << MapperShiftCount;
            if (++MapperShiftCount < 5) return;

            // Fifth write: commit to the register determined by address bits 13-14
            int reg = MapperRegBuffer;
            MapperShiftCount = MapperRegBuffer = 0;

            if (address < 0xA000)
            {
                // $8000-$9FFF: Control register
                // bits 0-1: mirroring (0=one-screen lower, 1=one-screen upper, 2=vertical, 3=horizontal)
                int mirrorType = reg & 3;
                if      (mirrorType == 0) *Vertical = 2; // one-screen lower
                else if (mirrorType == 1) *Vertical = 3; // one-screen upper
                else if (mirrorType == 2) *Vertical = 1; // vertical
                else                      *Vertical = 0; // horizontal

                PRG_Bankmode = (reg & 0x0C) >> 2;
                CHR_Bankmode = (reg & 0x10) >> 4;
            }
            else if (address < 0xC000)
            {
                // $A000-$BFFF: CHR bank 0
                CHR0_Bankselect = reg;
            }
            else if (address < 0xE000)
            {
                // $C000-$DFFF: CHR bank 1 (ignored in 8KB mode)
                CHR1_Bankselect = reg;
            }
            else
            {
                // $E000-$FFFF: PRG bank select (bits 0-3)
                PRG_Bankselect = reg & 0x0F;
            }
        }

        public byte MapperR_CHR(int address)
        {
            // CHR RAM fallback
            if (CHR_ROM_count == 0) return ppu_ram[address & 0x1FFF];

            if (CHR_Bankmode == 0)
            {
                // 8KB mode: CHR0_Bankselect selects 8KB bank (low bit ignored)
                int bank8k = (CHR0_Bankselect >> 1) % CHR_ROM_count;
                return CHR_ROM[address + (bank8k << 13)];
            }
            else
            {
                // 4KB mode: two independent 4KB banks
                int banks4k = CHR_ROM_count * 2;
                if (address < 0x1000)
                    return CHR_ROM[address + ((CHR0_Bankselect % banks4k) << 12)];
                else
                    return CHR_ROM[(address - 0x1000) + ((CHR1_Bankselect % banks4k) << 12)];
            }
        }

        public byte MapperR_RAM(ushort address)              { return NesCoreSpeed.NES_MEM_S[address]; }
        public void MapperW_RAM(ushort address, byte value)  { NesCoreSpeed.NES_MEM_S[address] = value; }
        public byte MapperR_EXP(ushort address)              { return 0; }
        public void MapperW_EXP(ushort address, byte value)  { }
    }
}
