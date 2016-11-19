namespace AprNes
{
    unsafe public partial class NesCore
    {
        byte* NES_MEM;
        byte Mem_r(ushort address)
        {
            if (address < 0x2000) return NES_MEM[address & 0x7ff];
            else if (address < 0x4020) return IO_read(address);
            else if (address < 0x6000) return MapperRouterR_ExpansionROM(address);
            else if (address < 0x8000) return MapperRouterR_RAM(address);
            else return MapperRouterR_RPG(address);
        }
        void Mem_w(ushort address, byte value)
        {
            if (address < 0x2000) NES_MEM[address & 0x7ff] = value;
            else if (address < 0x4020) IO_write(address, value);
            else if (address < 0x6000) MapperRouterW_ExpansionROM(address, value);
            else if (address < 0x8000) MapperRouterW_RAM(address, value);
            else MapperRouterW_PRG(address, value);
        }
    }
}