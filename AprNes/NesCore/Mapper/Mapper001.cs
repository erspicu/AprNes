
namespace AprNes
{
    unsafe public class Mapper001 : IMapper
    {

        byte* PRG_ROM, CHR_ROM, ppu_ram, NES_MEM;
        int CHR_ROM_count;

        //MMC1 http://wiki.nesdev.com/w/index.php/MMC1
        int PRG_Bankmode = 3 , CHR_Bankmode = 0 , Mirroring_type = 0 ; // PRG mode 3 on power-up (fix last bank at $C000)
        int CHR0_Bankselect = 0;
        int CHR1_Bankselect = 0;
        int PRG_Bankselect = 0;
        int PRG_ROM_count = 0;
        int MapperShiftCount = 0;
        int MapperRegBuffer = 0;
        int* Vertical;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram, int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM;
            CHR_ROM = _CHR_ROM;
            ppu_ram = _ppu_ram;
            CHR_ROM_count = _CHR_ROM_count;
            PRG_ROM_count = _PRG_ROM_count;
            Vertical = _Vertical;
            PRG_Bankselect = _PRG_ROM_count - 2;
            NES_MEM = NesCore.NES_MEM;
        }

        public byte MapperR_ExpansionROM(ushort address)
        {
            return 0;
        }

        public void MapperW_ExpansionROM(ushort address, byte value)
        {

        }

        public void MapperW_RAM(ushort address, byte value)
        {
            NES_MEM[address] = value;
        }

        public byte MapperR_RAM(ushort address)
        {
            return NES_MEM[address];
        }

        public void MapperW_PRG(ushort address, byte value)
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

                if (Mirroring_type == 0) *Vertical = 2;      // one-screen, lower bank
                else if (Mirroring_type == 1) *Vertical = 3;  // one-screen, upper bank
                else if (Mirroring_type == 2) *Vertical = 1;  // vertical
                else *Vertical = 0;                            // horizontal

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

        public byte MapperR_RPG(ushort address)
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

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];

            if (CHR_Bankmode > 0) //4K
            {
                int banks4k = CHR_ROM_count * 2;
                if (address < 0x1000) return CHR_ROM[address + ((CHR0_Bankselect % banks4k) << 12)];
                else return CHR_ROM[(address - 0x1000) + ((CHR1_Bankselect % banks4k) << 12)];
            }
            else return CHR_ROM[address + 0x2000 * ((CHR0_Bankselect >> 1) % CHR_ROM_count)];
        }




    }
}
