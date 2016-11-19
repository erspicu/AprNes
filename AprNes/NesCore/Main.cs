//#define debug
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using NativeWIN32API;
using XBRz_speed;
using System.Runtime.InteropServices;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        int mapper;
        byte PRG_ROM_count, CHR_ROM_count, ROM_Control_1, ROM_Control_2, RAM_banks_count;
        byte* PRG_ROM, CHR_ROM;
        bool Vertical = false, NesHeaderV2 = false, battery = false;

        public string rom_file_name = "";
        string rom_sav = "";
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
                PRG_ROM = (byte*)Marshal.AllocHGlobal(sizeof(byte) * PRG_ROM_count_needs * 16384);
                for (int i = 0; i < PRG_ROM_count * 16384; i++) PRG_ROM[i] = rom_bytes[16 + i];
                if (PRG_ROM_count == 1) for (int i = 0; i < PRG_ROM_count * 16384; i++) PRG_ROM[i + 16384] = rom_bytes[16 + i]; // if only 1 RPG_ROM ,copy to another space

                CHR_ROM_count = rom_bytes[5];
                Console.WriteLine("CHR-ROM count : " + CHR_ROM_count);

                if (CHR_ROM_count != 0)
                {
                    CHR_ROM = (byte*)Marshal.AllocHGlobal(sizeof(byte) * CHR_ROM_count * 8192);
                    for (int i = 0; i < CHR_ROM_count * 8192; i++)
                        CHR_ROM[i] = rom_bytes[PRG_ROM_count * 16384 + 16 + i];
                }
                else
                    CHR_ROM = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 8192); //new byte[8192];

                ROM_Control_1 = rom_bytes[6];
                ROM_Control_2 = rom_bytes[7];

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
                    battery = true;
                    Console.WriteLine("battery-backed RAM : yes");
                }
                else Console.WriteLine("battery-backed RAM : no");

                if ((ROM_Control_1 & 4) != 0) Console.WriteLine("trainer : yes");
                else Console.WriteLine("trainer : no");

                if ((ROM_Control_1 & 8) != 0) Console.WriteLine("fourscreen mirroring : yes");
                else Console.WriteLine("fourscreen mirroring : no");

                // https://wiki.nesdev.com/w/index.php/NES_2.0
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
                else mapper = (byte)(((ROM_Control_1 & 0xf0) >> 4) | (ROM_Control_2 & 0xf0));
                Console.WriteLine("Mapper number : " + mapper);
                bool mapper_pass = false;
                foreach (int i in Mapper_Allow) if (i == mapper) mapper_pass = true;

                if (!mapper_pass)
                {
                    MessageBox.Show("not support mapper ! " + mapper);
                    return false;
                }
                if (NesHeaderV2)
                {
                    RAM_banks_count = rom_bytes[8];
                    Console.WriteLine("RAM banks count : " + RAM_banks_count);
                }

                //for mapper value init region 
                if (mapper == 1) PRG_Bankselect = PRG_ROM_count - 2;
                if (mapper == 2) Rom_offset = (PRG_ROM_count - 1) * 0x4000;

                //init allocate
                ScreenBuf1x = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 61440);
                ScreenBuf2x = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 245760);
                ScreenBuf3x = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 552960);
                ScreenBuf4x = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 983040);
                ScreenBuf5x = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 1536000);
                ScreenBuf6x = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 2211840);
                Buffer_BG_array = (int*)Marshal.AllocHGlobal(sizeof(int) * 61440);
                NesColors = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 64);
                cycle_table = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 256);
                spr_ram = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 256);
                ppu_ram = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 0x4000);
                P1_joypad_status = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 8);
                NES_MEM = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 65536);

                for (int i = 0; i < 16384; i++) ppu_ram[i] = 0;
                for (int i = 0; i < 256; i++) spr_ram[i] = 0;
                for (int i = 0; i < 256; i++) cycle_table[i] = cycle_tableData[i];
                for (int i = 0; i < 8; i++) P1_joypad_status[i] = 0x40;
                for (int i = 0; i < 64; i++) NesColors[i] = NesColorsData[i];
                for (int i = 0; i < 65535; i++) NES_MEM[i] = 0;

                //init sram
                if (battery == true)
                {
                    rom_sav = rom_file_name + ".sav";
                    if (File.Exists(rom_sav))
                    {
                        byte[] sav_bytes = File.ReadAllBytes(rom_sav);
                        for (int i = 0; i < 0x2000; i++) NES_MEM[i + 0x6000] = sav_bytes[i]; //copy to ram
                    }
                    else File.WriteAllBytes(rom_sav, new byte[0x2000]);
                }

                //init cpu pc
                r_PC = (ushort)(Mem_r(0xfffc) | Mem_r(0xfffd) << 8);

                //bind graphic device
                switch (ScreenSize)
                {
                    case 1: NativeGDI.initHighSpeed(_device, 256, 240, ScreenBuf1x, 0, 0); break;
                    case 2: NativeGDI.initHighSpeed(_device, 512, 480, ScreenBuf2x, 0, 0); break;
                    case 3: NativeGDI.initHighSpeed(_device, 768, 720, ScreenBuf3x, 0, 0); break;
                    case 4: NativeGDI.initHighSpeed(_device, 1024, 960, ScreenBuf4x, 0, 0); break;
                    case 5: NativeGDI.initHighSpeed(_device, 1280, 1200, ScreenBuf5x, 0, 0); break;
                    case 6: NativeGDI.initHighSpeed(_device, 1536, 1440, ScreenBuf6x, 0, 0); break;
                }
#if debug
                if (File.Exists(@"c:\log\log.txt")) File.Delete(@"c:\log\log.txt");
                if (!Directory.Exists(@"c:\log")) Directory.CreateDirectory((@"c:\log"));
                StepsLog = File.AppendText(@"c:\log\log.txt");
#endif

                HS_XBRz.initTable(256);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                return false;
            }
            return true;
        }
        public void SaveRam()
        {
            if (!battery) return;
            byte[] sav_bytes = new byte[0x2000];
            for (int i = 0; i < 0x2000; i++) sav_bytes[i] = NES_MEM[i + 0x6000]; //copy from ram
            File.WriteAllBytes(rom_sav, sav_bytes);
        }
        public void run()
        {
            StopWatch.Restart();
            while (true)
            {
                cpu_step();
                for (int i = 0; i < cpu_cycles * 3; i++) ppu_step();
#if debug
                debug();
#endif
                if (exit) break;
            }
        }
#if debug
        StreamWriter StepsLog;
        int scount = 0;
        public void debug()
        {
            StepsLog.WriteLine(cpu_cycles.ToString("x2") + " " + opcode.ToString("x2") + " " + r_PC.ToString("x4") + " " + r_A.ToString("x2") + " " + r_X.ToString("x2") + " " + r_Y.ToString("x2") + " " + r_SP.ToString("x2") + " " + scanline + " " + ppu_cycles.ToString("x2"));
            if (scount == 100000)
            {
                StepsLog.Flush();
                StepsLog.Close();
                Console.WriteLine("ok!!!");
                Console.ReadLine();
            }
            scount++;
        }
#endif
    }
}
