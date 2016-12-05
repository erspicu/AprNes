
namespace AprNes
{
    unsafe public partial class NesCore
    {
        //MMC1 http://wiki.nesdev.com/w/index.php/MMC1
        int PRG_Bankmode, CHR_Bankmode, Mirroring_type;
        void mapper001write_ROM(ushort address, byte value)
        {
            if ((value & 0x80) != 0)
            {
                MapperShiftCount = MapperRegBuffer = 0;
                PRG_Bankmode = 3;
                return;
            }
            MapperRegBuffer |= (value & 1) << MapperShiftCount;

            if (++MapperShiftCount < 5) return;

            if (address < 0xa000)
            {
                // $8000-$9FFF
                Mirroring_type = MapperRegBuffer & 3; //(0: one-screen, lower bank; 1: one-screen, upper bank; 2: vertical; 3: horizontal)

                if (Mirroring_type == 2) Vertical = true;
                else if (Mirroring_type == 3) Vertical = false;

                PRG_Bankmode = (MapperRegBuffer & 0xc) >> 2;
                //0, 1: switch 32 KB at $8000, ignoring low bit of bank number;
                //2: fix first bank at $8000 and switch 16 KB bank at $C000;
                //3: fix last bank at $C000 and switch 16 KB bank at $8000

                CHR_Bankmode = (MapperRegBuffer & 0x10) >> 4;//(0: switch 8 KB at a time; 1: switch two separate 4 KB banks)
            }
            else if (address < 0xc000) CHR0_Bankselect = MapperRegBuffer;// $A000-$BFFF (low bit ignored in 8 KB mode)
            else if (address < 0xe000) CHR1_Bankselect = MapperRegBuffer; // $C000-$DFFF (ignored in 8 KB mode)   
            else PRG_Bankselect = MapperRegBuffer & 0xf;//$E000-$FFFF

            MapperShiftCount = MapperRegBuffer = 0;
        }

        byte mapper001read_RPG(ushort address) // need fix
        {
            if (PRG_Bankmode == 0 || PRG_Bankmode == 1) return PRG_ROM[(address - 0x8000) + (PRG_Bankselect << 14)];//32k
            else if (PRG_Bankmode == 2)
            {
                if (address < 0xc000) return PRG_ROM[address - 0x8000];//fixed
                else return PRG_ROM[(address - 0xc000) + (PRG_Bankselect << 14)]; // switch
            }
            else
            {
                if (address < 0xc000) return PRG_ROM[(address - 0x8000) + (PRG_Bankselect << 14)];//switch
                else return PRG_ROM[(address - 0xc000) + ((PRG_ROM_count - 1) << 14)]; // fixed
            }
        }

        byte mapper001read_CHR(int address) //checking
        {
            if (CHR_Bankmode > 0) //4K
            {
                if (address < 0x1000) return CHR_ROM[address + (CHR0_Bankselect << 12)];
                else return CHR_ROM[(address - 0x1000) + (CHR1_Bankselect << 12)];
            }
            else return CHR_ROM[address + 0x2000 * (CHR0_Bankselect >> 1)];
        }

    }
}
