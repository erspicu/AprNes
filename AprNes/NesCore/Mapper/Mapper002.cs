namespace AprNes
{
    unsafe public class Mapper002 : IMapper
    {
        //UNROM
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int CHR_ROM_count;
        int PRG_ROM_count;
        int PRG_Bankselect;
        private int* Vertical;
        private int Rom_offset;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram, int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM;
            CHR_ROM = _CHR_ROM;
            ppu_ram = _ppu_ram;
            CHR_ROM_count = _CHR_ROM_count;
            PRG_ROM_count = _PRG_ROM_count;
            Vertical = _Vertical;

            Rom_offset = (PRG_ROM_count - 1) * 0x4000;
        }

        public byte MapperR_ExpansionROM(ushort address) { return 0; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return 0; }

        public void MapperW_PRG(ushort address, byte value)
        {
            PRG_Bankselect = value & 7;
        }

        public byte MapperR_RPG(ushort address)
        {
            if (address < 0xc000) return PRG_ROM[(address - 0x8000) + (PRG_Bankselect << 14)];//siwtch
            else return PRG_ROM[(address - 0xc000) + Rom_offset]; // fixed 
        }

        public byte MapperR_CHR(int address)
        {
            return ppu_ram[address];
        }
    }
}
