
namespace AprNes
{
    unsafe public class Mapper004MMC6 : Mapper004
    {
        byte prgRamProtect = 0; // $A001 register

        // IRQ behaviour identical to Rev A
        public override void Mapper04step_IRQ()
        {
            int oldCounter = IRQCounter;
            bool wasReset = IRQReset;
            bool reload = (IRQCounter == 0 || IRQReset);
            if (reload)
                IRQCounter = IRQlatchVal;
            else
                IRQCounter--;
            IRQReset = false;

            if (IRQCounter == 0 && IRQ_enable && (oldCounter != 0 || wasReset))
                NesCore.statusmapperint = true;
        }

        // $A001 odd -> PRG-RAM protect register
        public override void MapperW_PRG(ushort address, byte value)
        {
            if ((address & 1) != 0 && address >= 0xA000 && address < 0xC000)
            {
                prgRamProtect = value;
                return;
            }
            base.MapperW_PRG(address, value);
        }

        // PRG-RAM read: bit5=lower 1K read enable, bit7=upper 1K read enable
        public override byte MapperR_RAM(ushort address)
        {
            if (address < 0x7000)
            { if ((prgRamProtect & 0x20) == 0) return 0; }
            else
            { if ((prgRamProtect & 0x80) == 0) return 0; }
            return NesCore.NES_MEM[address];
        }

        // PRG-RAM write: bit4=lower 1K write enable, bit6=upper 1K write enable
        public override void MapperW_RAM(ushort address, byte value)
        {
            if (address < 0x7000)
            { if ((prgRamProtect & 0x10) == 0) return; }
            else
            { if ((prgRamProtect & 0x40) == 0) return; }
            NesCore.NES_MEM[address] = value;
        }
    }
}
