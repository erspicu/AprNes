
namespace AprNes
{
    unsafe public partial class NesCore
    {
        //UNROM
        static void mapper002write_ROM(ushort address, byte value)
        {
            PRG_Bankselect = value & 7;
        }

        static byte mapper002read_RPG(ushort address)
        {
            if (address < 0xc000) return PRG_ROM[(address - 0x8000) + (PRG_Bankselect << 14)];//siwtch
            else return PRG_ROM[(address - 0xc000) + Rom_offset]; // fixed 
        }

        static byte mapper002read_CHR(int address)
        {
            return ppu_ram[address];
        }

    }
}
