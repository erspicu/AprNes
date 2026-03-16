namespace AprNes
{
    unsafe public interface IMapper_S
    {
        void MapperInit(byte* PRG_ROM, byte* CHR_ROM, byte* ppu_ram,
                        int PRG_ROM_count, int CHR_ROM_count, int* Vertical);

        byte  MapperR_PRG(ushort address);
        void  MapperW_PRG(ushort address, byte value);
        byte  MapperR_CHR(int address);
        byte  MapperR_RAM(ushort address);
        void  MapperW_RAM(ushort address, byte value);
        byte  MapperR_EXP(ushort address);
        void  MapperW_EXP(ushort address, byte value);
    }
}
