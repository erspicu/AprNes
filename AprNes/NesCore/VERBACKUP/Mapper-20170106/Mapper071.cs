
namespace AprNes
{
    unsafe public partial class NesCore
    {
        //Camerica http://wiki.nesdev.com/w/index.php?title=INES_Mapper_071 need check!
        static void mapper071write_ROM(ushort address, byte value)
        {
            //Select 16 KiB PRG ROM bank for CPU $8000-$BFFF
            if (address >= 0xc000 && address <= 0xffff) PRG_Bankselect = (value & 0xf);
        }

        static byte mapper071read_RPG(ushort address)
        {
            if (address < 0xc000) return PRG_ROM[(address - 0x8000) + (PRG_Bankselect << 14)]; // swap
            else return PRG_ROM[(address - 0xc000) + (PRG_ROM_count << 14)];
        }

        static byte mapper071read_CHR(int address)
        {
            return CHR_ROM[address];
        }
    }
}
