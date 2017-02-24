
namespace AprNes
{
    unsafe public class Mapper066 : IMapper
    {
        //GxROM http://wiki.nesdev.com/w/index.php/GxROM ok
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int CHR_ROM_count;
        int PRG_ROM_count;
        int* Vertical;
        int CHR_Bankselect;
        private int PRG_Bankselect;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram, int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM;
            CHR_ROM = _CHR_ROM;
            ppu_ram = _ppu_ram;
            CHR_ROM_count = _CHR_ROM_count;
            PRG_ROM_count = _PRG_ROM_count;
            Vertical = _Vertical;
        }

        public byte MapperR_ExpansionROM(ushort address) { return 0; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return 0; }

        public void MapperW_PRG(ushort address, byte value)
        {
            CHR_Bankselect = value & 3; // Select 8 KB CHR ROM bank for PPU $0000-$1FFF
            PRG_Bankselect = (value & 0x30) >> 4; //select 32 KB PRG ROM bank for CPU $8000-$FFFF
        }

        public byte MapperR_RPG(ushort address)
        {
            return PRG_ROM[(address - 0x8000) + (PRG_Bankselect << 15)];
        }

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];
            return CHR_ROM[address + (CHR_Bankselect << 13)];
        }
    }
}