using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using NativeWIN32API;
using XBRz_speed;

namespace AprNes
{
    public partial class NesCore
    {

        Graphics device;

        byte PRG_ROM_count;
        byte CHR_ROM_count;
        byte ROM_Control_1;
        byte ROM_Control_2;
        byte RAM_banks_count;

        byte mapper;

        byte[] PRG_ROM;
        byte[] CHR_ROM;

        bool Vertical = false;

        StreamWriter StepsLog;

        public bool init(Graphics _device, byte[] rom_bytes)
        {
            try
            {
                if (!(rom_bytes[0] == 'N' && rom_bytes[1] == 'E' && rom_bytes[2] == 'S' && rom_bytes[3] == 0x1a))
                {
                    MessageBox.Show("Bad Magic Number !");
                    return false;
                }

                if (rom_bytes[7] == 8 && rom_bytes[12] == 8)
                {
                    Console.WriteLine("Nes 2.0 header");
                    MessageBox.Show("only for iNes header now !");
                    return false;
                }

                if (rom_bytes[7] == 0 && rom_bytes[12] == 0 && rom_bytes[13] == 0 && rom_bytes[14] == 0 && rom_bytes[15] == 0)
                {
                    Console.WriteLine("iNes header");
                }

                PRG_ROM_count = rom_bytes[4];
                Console.WriteLine("PRG-ROM count : " + PRG_ROM_count);

                int PRG_ROM_count_needs = PRG_ROM_count;
                if (PRG_ROM_count == 1) PRG_ROM_count_needs = 2;//min PRG ROM is 2

                PRG_ROM = new byte[PRG_ROM_count_needs * 16384];

                for (int i = 0; i < PRG_ROM_count * 16384; i++) PRG_ROM[i] = rom_bytes[16 + i];

                if (PRG_ROM_count == 1) // if only 1 RPG_ROM ,copy to another space
                    for (int i = 0; i < PRG_ROM_count * 16384; i++) PRG_ROM[i + 16384] = rom_bytes[16 + i];

                CHR_ROM_count = rom_bytes[5];
                Console.WriteLine("CHR-ROM count : " + CHR_ROM_count);

                CHR_ROM = new byte[CHR_ROM_count * 8192];

                for (int i = 0; i < CHR_ROM_count * 8192; i++)                
                    CHR_ROM[i] = rom_bytes[PRG_ROM_count * 16384 + 16 + i];
                
                ROM_Control_1 = rom_bytes[6];

                if ((ROM_Control_1 & 1) != 0)
                {
                    Vertical = true;
                    Console.WriteLine("vertical mirroring");
                }
                else
                {
                    Vertical = false;
                    Console.WriteLine("horizontal mirroring");
                }

                if ((ROM_Control_1 & 2) != 0)
                {
                    Console.WriteLine("battery-backed RAM : yes");

                    MessageBox.Show("battery-backed RAM not support");
                    return false;
                }
                else
                {
                    Console.WriteLine("battery-backed RAM : no");
                }

                if ((ROM_Control_1 & 4) != 0)
                {
                    Console.WriteLine("trainer : yes");
                }
                else
                {
                    Console.WriteLine("trainer : no");
                }

                if ((ROM_Control_1 & 8) != 0)
                {
                    Console.WriteLine("fourscreen mirroring : yes");
                }
                else
                {
                    Console.WriteLine("fourscreen mirroring : no");
                }

                ROM_Control_2 = rom_bytes[7];

                mapper = (byte)(((ROM_Control_1 & 0xf0) >> 4) | (ROM_Control_2 & 0xf0));
                Console.WriteLine("Mapper number : " + mapper);

                if (mapper != 0)
                {
                    MessageBox.Show("Only Mapper 0 support !");
                    return false;
                }

                RAM_banks_count = rom_bytes[8];
                Console.WriteLine("RAM banks count : " + RAM_banks_count);
                if (RAM_banks_count > 0)
                {
                    MessageBox.Show("RAM Bank not support !");
                    return false;
                }

                for (int i = 0; i < 65535; i++) NES_MEM[i] = 0;
                for (int i = 0; i < 16384; i++) ppu_ram[i] = 0;

                for (int i = 0; i < 256; i++)
                {
                    Buffer_Screen_array[i] = new uint[240];
                    Buffer_BG_array[i] = new int[240];
                }

                //init cpu pc
                r_PC = (ushort)(Mem_r(0xfffc) | Mem_r(0xfffd) << 8);

                //pre decode tiles
                DecodeTiles();

                //bind graphic device
                NativeGDI.initHighSpeed(_device, 512, 480, ScreenBuffer2x , 0, 0);

                //for debug
                /*if (File.Exists(@"c:\log\log.txt"))
                    File.Delete(@"c:\log\log.txt");
                if (!Directory.Exists(@"c:\log"))
                    Directory.CreateDirectory((@"c:\log"));
                StepsLog = File.AppendText(@"c:\log\log.txt");*/

                for (int i = 0; i < 0x400; i++)
                {
                    AttributeShift[i] = ((i >> 4) & 0x04) | (i & 0x02);
                    AttributeLocation[i] = ((i >> 2) & 0x07) | (((i >> 4) & 0x38) | 0x3C0);
                }

                HS_XBRz.initTable();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                return false;
            }
            return true;
        }

        public void run()
        {
            StopWatch.Restart();
            while (true)
            {
                cpu_step();
                for (int i = 0; i < cpu_cycles * 3; i++) ppu_step();
                if (exit) break;
            }
        }
    }
}
