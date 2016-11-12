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
        byte PRG_ROM_count;
        byte CHR_ROM_count;
        byte ROM_Control_1;
        byte ROM_Control_2;
        byte RAM_banks_count;

        int mapper;

        byte[] PRG_ROM;
        byte[] CHR_ROM;

        bool Vertical = false;
        bool NesHeaderV2 = false;

        public bool init(Graphics _device, byte[] rom_bytes)
        {
            try
            {
                //http://nesdev.com/iNES.txt
                //https://github.com/dsedivec/inestool/blob/master/inestool.py
                if (!(rom_bytes[0] == 'N' && rom_bytes[1] == 'E' && rom_bytes[2] == 'S' && rom_bytes[3] == 0x1a))
                {
                    MessageBox.Show("Bad Magic Number !");
                    return false;
                }

                Console.WriteLine("iNes header");

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

                if (CHR_ROM_count != 0)
                {
                    CHR_ROM = new byte[CHR_ROM_count * 8192];
                    for (int i = 0; i < CHR_ROM_count * 8192; i++)
                        CHR_ROM[i] = rom_bytes[PRG_ROM_count * 16384 + 16 + i];
                }

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

                // https://wiki.nesdev.com/w/index.php/NES_2.0

                ROM_Control_2 = rom_bytes[7];

                if ((ROM_Control_2 & 0xf) != 0)
                {
                    mapper = (ROM_Control_1 & 0xf0) >> 4;

                    if ((ROM_Control_2 & 0xc) == 8)
                    {
                        NesHeaderV2 = true;
                        mapper = (byte)(((ROM_Control_1 & 0xf0) >> 4) | (ROM_Control_2 & 0xf0));
                        Console.WriteLine("Nes header 2.0 version !");
                    }
                    else
                    {
                        mapper = (ROM_Control_1 & 0xf0) >> 4;
                        Console.WriteLine("Old style Mapper info !");
                    }
                }
                else
                    mapper = (byte)(((ROM_Control_1 & 0xf0) >> 4) | (ROM_Control_2 & 0xf0));

                Console.WriteLine("Mapper number : " + mapper);


                bool mapper_pass = false;

                foreach (int i in Mapper_Allow)
                    if (i == mapper) mapper_pass = true;

                if (!mapper_pass)
                {
                    MessageBox.Show("not support mapper !");
                    return false;
                }

                if (NesHeaderV2)
                {
                    RAM_banks_count = rom_bytes[8];
                    Console.WriteLine("RAM banks count : " + RAM_banks_count);
                }

                for (int i = 0; i < 65535; i++) NES_MEM[i] = 0;
                for (int i = 0; i < 16384; i++) ppu_ram[i] = 0;

                for (int i = 0; i < 256; i++)
                {
                    Buffer_Screen_array[i] = new uint[240];
                    Buffer_BG_array[i] = new int[240];
                }

                //for mapper value init region 
                if (mapper == 1)
                    PRG_Bankselect = PRG_ROM_count - 2;

                //init cpu pc
                r_PC = (ushort)(Mem_r(0xfffc) | Mem_r(0xfffd) << 8);

                //bind graphic device
                switch (ScreenSize)
                {
                    case 1: NativeGDI.initHighSpeed(_device, 256, 240, ScreenBuffer1x, 0, 0); break;
                    case 2: NativeGDI.initHighSpeed(_device, 512, 480, ScreenBuffer2x, 0, 0); break;
                    case 3: NativeGDI.initHighSpeed(_device, 768, 720, ScreenBuffer3x, 0, 0); break;
                    case 4: NativeGDI.initHighSpeed(_device, 1024, 960, ScreenBuffer4x, 0, 0); break;
                    case 5: NativeGDI.initHighSpeed(_device, 1280, 1200, ScreenBuffer5x, 0, 0); break;
                }

                //for debug
                /*if (File.Exists(@"c:\log\log.txt"))
                    File.Delete(@"c:\log\log.txt");
                if (!Directory.Exists(@"c:\log"))
                    Directory.CreateDirectory((@"c:\log"));
                StepsLog = File.AppendText(@"c:\log\log.txt");*/

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
                //debug();
                if (exit) break;
            }
        }

        /*
        StreamWriter StepsLog;
        int scount = 0;
        public void debug()
        {
            StepsLog.WriteLine(cpu_cycles.ToString("x2") + " " + opcode.ToString("x2") + " " + r_PC.ToString("x4") + " " + r_A.ToString("x2") + " " + r_X.ToString("x2") + " " + r_Y.ToString("x2") + " " + r_SP.ToString("x2") + " " + scanline + " " + ppu_cycles.ToString("x2"));
            if (scount == 300000)
            {
                StepsLog.Flush();
                StepsLog.Close();
                Console.WriteLine("ok!!!");
                Console.ReadLine();
            }
            scount++;
        }*/

    }
}
