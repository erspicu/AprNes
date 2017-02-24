
namespace AprNes
{
    unsafe public partial class NesCore
    {
        //CNROM
        static void mapper003write_ROM(ushort address, byte value)
        {
            CHR_Bankselect = value & 3;
        }

        static byte mapper003read_RPG(ushort address)
        {
            return PRG_ROM[address - 0x8000];
        }

        static byte mapper003read_CHR(int address)
        {
            return CHR_ROM[address + (CHR_Bankselect << 13)];
        }
    }
}
