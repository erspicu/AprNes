namespace AprNes
{
    unsafe public partial class NesCore
    {
        //NROM ok!
        byte mapper000read_RPG(ushort address)
        {
            return PRG_ROM[address - 0x8000];
        }

        byte mapper000read_CHR(int address)
        {
            if (address >= 0x2000) return 0;
            return CHR_ROM[address];
        }
    }
}
