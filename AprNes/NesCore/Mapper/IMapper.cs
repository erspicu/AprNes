namespace AprNes
{
    unsafe interface IMapper
    {
        void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM , byte* _ppu_ram ,int _PRG_ROM_count, int _CHR_ROM_count ,int * _Vertical);
        byte MapperR_ExpansionROM(ushort address);
        void MapperW_ExpansionROM(ushort address, byte value);
        void MapperW_RAM(ushort address, byte value);
        byte MapperR_RAM(ushort address);
        void MapperW_PRG(ushort address, byte value);
        byte MapperR_RPG(ushort address);
        byte MapperR_CHR(int address);
    }
}
