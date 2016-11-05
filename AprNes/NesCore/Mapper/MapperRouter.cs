using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AprNes
{
    public partial class NesCore
    {
        int RPG_Bankselect = 0;
        public void MapperRouterW(ushort address, byte value)
        {
            switch (mapper)
            {
                case 0: break;//NROM , nothing 
                case 2: mapper02write(address, value); break; //UNROM
                default: break;
            }
        }

        public byte MapperRouterR(ushort address)
        {
            switch (mapper)
            {
                case 0: return mapper00read(address);
                case 2: return mapper02read(address);
                default: return 0;
            }
        }
    }
}
