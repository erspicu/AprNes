using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AprNes
{
    internal interface IMapper
    {
        byte Read_CHR(int address);
        byte Read_ExpansionROM(ushort address); //for register configure
        byte Read_PRG(ushort address);
        void Write_ExpansionROM(ushort address, byte value); //for register configure
        void Write_Rom(ushort address, byte value);
    }

    unsafe public abstract class CMapper : IMapper
    {
        protected byte* PRG_ROM, CHR_ROM;

        public CMapper(byte* PRG_ROM, byte* CHR_ROM)
        {
            this.PRG_ROM = PRG_ROM;
            this.CHR_ROM = CHR_ROM;
        }

        public abstract byte Read_CHR(int address);

        public virtual byte Read_ExpansionROM(ushort address) { return 0x00; }

        public abstract byte Read_PRG(ushort address);

        public abstract void Write_Rom(ushort address, byte value);

        public virtual void Write_ExpansionROM(ushort address, byte value) { return; }
    }
}
