namespace AprNes
{
    // MMC1 (SxROM) mapper for Speed Core
    unsafe public class Mapper001_S : IMapper_S
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int CHR_ROM_count, PRG_ROM_count;
        int* Vertical;

        int PRG_Bankmode    = 3;
        int CHR_Bankmode    = 0;
        int CHR0_Bankselect = 0;
        int CHR1_Bankselect = 0;
        int PRG_Bankselect  = 0;

        int MapperShiftCount = 0;
        int MapperRegBuffer  = 0;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
                               int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            CHR_ROM_count = _CHR_ROM_count; PRG_ROM_count = _PRG_ROM_count;
            Vertical = _Vertical;
            PRG_Bankselect = _PRG_ROM_count - 2;
            UpdateChrPtrs();
            UpdatePrgPtrs();
        }

        void UpdatePrgPtrs()
        {
            int last = PRG_ROM_count - 1;
            if (PRG_Bankmode == 0 || PRG_Bankmode == 1)
            {
                // 32KB mode: map $8000-$FFFF to (PRG_Bankselect & ~1) * 16KB
                byte* b = PRG_ROM + ((PRG_Bankselect & ~1) << 14);
                NesCoreSpeed.prgBankPtrs_S[4] = b;
                NesCoreSpeed.prgBankPtrs_S[5] = b + 8192;
                NesCoreSpeed.prgBankPtrs_S[6] = b + 16384;
                NesCoreSpeed.prgBankPtrs_S[7] = b + 24576;
            }
            else if (PRG_Bankmode == 2)
            {
                // Fix first 16KB at $8000, swap last 16KB at $C000
                NesCoreSpeed.prgBankPtrs_S[4] = PRG_ROM;
                NesCoreSpeed.prgBankPtrs_S[5] = PRG_ROM + 8192;
                NesCoreSpeed.prgBankPtrs_S[6] = PRG_ROM + (PRG_Bankselect << 14);
                NesCoreSpeed.prgBankPtrs_S[7] = PRG_ROM + (PRG_Bankselect << 14) + 8192;
            }
            else
            {
                // Swap first 16KB at $8000, fix last 16KB at $C000
                NesCoreSpeed.prgBankPtrs_S[4] = PRG_ROM + (PRG_Bankselect << 14);
                NesCoreSpeed.prgBankPtrs_S[5] = PRG_ROM + (PRG_Bankselect << 14) + 8192;
                NesCoreSpeed.prgBankPtrs_S[6] = PRG_ROM + (last << 14);
                NesCoreSpeed.prgBankPtrs_S[7] = PRG_ROM + (last << 14) + 8192;
            }
        }

        void UpdateChrPtrs()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCoreSpeed.chrBankPtrs_S[i] = ppu_ram + i * 1024;
                return;
            }
            int banks4k = CHR_ROM_count * 2;
            if (CHR_Bankmode == 0)
            {
                // 8KB mode: CHR0_Bankselect selects 8KB bank (low bit ignored)
                byte* b = CHR_ROM + ((CHR0_Bankselect & ~1) % CHR_ROM_count) * 8192;
                for (int i = 0; i < 8; i++) NesCoreSpeed.chrBankPtrs_S[i] = b + i * 1024;
            }
            else
            {
                // 4KB mode
                byte* b0 = CHR_ROM + ((CHR0_Bankselect % banks4k) << 12);
                byte* b1 = CHR_ROM + ((CHR1_Bankselect % banks4k) << 12);
                for (int i = 0; i < 4; i++) NesCoreSpeed.chrBankPtrs_S[i]   = b0 + i * 1024;
                for (int i = 0; i < 4; i++) NesCoreSpeed.chrBankPtrs_S[4+i] = b1 + i * 1024;
            }
        }

        public byte MapperR_PRG(ushort address)
        {
            if (PRG_Bankmode == 0 || PRG_Bankmode == 1)
                return PRG_ROM[(address - 0x8000) + ((PRG_Bankselect & ~1) << 14)];
            else if (PRG_Bankmode == 2)
            {
                if (address < 0xC000) return PRG_ROM[address - 0x8000];
                else                  return PRG_ROM[(address - 0xC000) + (PRG_Bankselect << 14)];
            }
            else
            {
                if (address < 0xC000) return PRG_ROM[(address - 0x8000) + (PRG_Bankselect << 14)];
                else                  return PRG_ROM[(address - 0xC000) + ((PRG_ROM_count - 1) << 14)];
            }
        }

        public void MapperW_PRG(ushort address, byte value)
        {
            if ((value & 0x80) != 0) { MapperShiftCount = MapperRegBuffer = 0; PRG_Bankmode = 3; return; }
            MapperRegBuffer |= (value & 1) << MapperShiftCount;
            if (++MapperShiftCount < 5) return;
            int reg = MapperRegBuffer;
            MapperShiftCount = MapperRegBuffer = 0;
            if (address < 0xA000)
            {
                int mirrorType = reg & 3;
                if      (mirrorType == 0) *Vertical = 2;
                else if (mirrorType == 1) *Vertical = 3;
                else if (mirrorType == 2) *Vertical = 1;
                else                      *Vertical = 0;
                PRG_Bankmode = (reg & 0x0C) >> 2;
                CHR_Bankmode = (reg & 0x10) >> 4;
                UpdateChrPtrs();
                UpdatePrgPtrs();
            }
            else if (address < 0xC000) { CHR0_Bankselect = reg; UpdateChrPtrs(); }
            else if (address < 0xE000) { CHR1_Bankselect = reg; UpdateChrPtrs(); }
            else                       { PRG_Bankselect = reg & 0x0F; UpdatePrgPtrs(); }
        }

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address & 0x1FFF];
            if (CHR_Bankmode == 0)
            {
                int bank8k = (CHR0_Bankselect >> 1) % CHR_ROM_count;
                return CHR_ROM[address + (bank8k << 13)];
            }
            else
            {
                int banks4k = CHR_ROM_count * 2;
                if (address < 0x1000) return CHR_ROM[address + ((CHR0_Bankselect % banks4k) << 12)];
                else                  return CHR_ROM[(address - 0x1000) + ((CHR1_Bankselect % banks4k) << 12)];
            }
        }

        public byte MapperR_RAM(ushort address)              { return NesCoreSpeed.NES_MEM_S[address]; }
        public void MapperW_RAM(ushort address, byte value)  { NesCoreSpeed.NES_MEM_S[address] = value; }
        public byte MapperR_EXP(ushort address)              { return 0; }
        public void MapperW_EXP(ushort address, byte value)  { }
    }
}
