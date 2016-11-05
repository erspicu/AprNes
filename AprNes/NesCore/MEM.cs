using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AprNes
{
    public partial class NesCore
    {
        byte[] NES_MEM = new byte[65536];

        byte Mem_r(ushort address)
        {
            if (address < 0x2000)
                return NES_MEM[address % 0x800];
            else if (address < 0x4020)
            {
                if (address >= 0x4000)
                    return IO_read(address);
                else
                    return IO_read((ushort)(0x2000 + (address % 8)));
            }
            else if (address < 0x6000) { } //expansion rom , no support now
            else if (address < 0x8000) { }//sram , no support now
            else return MapperRouterR(address);

            return 0;//impossible there
        }

        private void Mem_w(ushort address, byte value)
        {
            if (address < 0x2000)
                NES_MEM[address % 0x800] = value;
            else if (address < 0x4020)
            {
                if (address >= 0x4000)
                    IO_write(address, value);
                else
                    IO_write((ushort)(0x2000 + (address % 8)), value);
            }
            else if (address < 0x6000) { } //expansion rom , no support now
            else if (address < 0x8000) { } //sram , no support now
            else MapperRouterW(address, value);
        }
    }
}
