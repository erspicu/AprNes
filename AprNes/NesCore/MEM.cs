namespace AprNes
{
    public partial class NesCore
    {
        byte[] NES_MEM = new byte[65536];
        byte Mem_r(ushort address)
        {
            if (address < 0x2000) return NES_MEM[address % 0x800];
            else if (address < 0x4020)
            {
                if (address >= 0x4000) return IO_read(address); else return IO_read((ushort)(0x2000 | (address & 7)));
            }
            else if (address < 0x6000) { } //expansion rom , no support now
            else if (address < 0x8000) return NES_MEM[address]; //直接假設SRAM存在
            else return MapperRouterR_RPG(address);
            return 0;//impossible there
        }
        void Mem_w(ushort address, byte value)
        {
            if (address < 0x2000) NES_MEM[address % 0x800] = value;
            else if (address < 0x4020)
            {
                if (address >= 0x4000) IO_write(address, value); else IO_write((ushort)(0x2000 | (address & 7)), value);
            }
            else if (address < 0x6000) { } //expansion rom , no support now
            else if (address < 0x8000) NES_MEM[address] = value; //直接假設SRAM存在
            else MapperRouterW(address, value);
        }
    }
}