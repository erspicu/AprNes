using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AprNes
{
    unsafe public partial class NesCoreSpeed
    {
        public static event EventHandler VideoOutput_S;
        static VideoOut VideoOut_arg_S = new VideoOut();

        // ROM info
        static int    mapper_S;
        static byte   PRG_ROM_count_S, CHR_ROM_count_S, ROM_Control_1_S, ROM_Control_2_S;
        static byte*  PRG_ROM_S, CHR_ROM_S;

        static IMapper_S MapperObj_S;
        static int*      Vertical_S;

        static public int[]  Mapper_Allow_S = new int[] { 0, 1, 2, 3, 4, 7, 11, 66 };
        static public string rom_file_name_S = "";
        static public bool   HasBattery_S = false;

        static public int  RomMapper_S   => mapper_S;
        static public int  RomPrgCount_S => PRG_ROM_count_S;
        static public int  RomChrCount_S => CHR_ROM_count_S;

        static public ManualResetEvent _event_S = new ManualResetEvent(true);
        static public Action<string> OnError_S;
        static public void ShowError_S(string msg) { OnError_S?.Invoke(msg); }

        // ----------------------------------------------------------------
        static public bool init_S(byte[] rom_bytes)
        {
            try
            {
                if (!(rom_bytes[0] == 'N' && rom_bytes[1] == 'E' &&
                      rom_bytes[2] == 'S' && rom_bytes[3] == 0x1a))
                { ShowError_S("Bad Magic Number!"); return false; }

                Vertical_S = (int*)Marshal.AllocHGlobal(sizeof(int));

                PRG_ROM_count_S = rom_bytes[4];
                CHR_ROM_count_S = rom_bytes[5];
                ROM_Control_1_S = rom_bytes[6];
                ROM_Control_2_S = rom_bytes[7];
                mapper_S = ((ROM_Control_2_S >> 4) << 4) | (ROM_Control_1_S >> 4);

                *Vertical_S = (ROM_Control_1_S & 1);

                // Allocate PRG ROM
                int prg_size = PRG_ROM_count_S * 16384;
                PRG_ROM_S = (byte*)Marshal.AllocHGlobal(prg_size);
                for (int i = 0; i < prg_size; i++)
                    PRG_ROM_S[i] = rom_bytes[16 + i];

                // Allocate CHR ROM / RAM
                int chr_size = CHR_ROM_count_S > 0 ? CHR_ROM_count_S * 8192 : 8192;
                CHR_ROM_S = (byte*)Marshal.AllocHGlobal(chr_size);
                if (CHR_ROM_count_S > 0)
                    for (int i = 0; i < chr_size; i++)
                        CHR_ROM_S[i] = rom_bytes[16 + prg_size + i];
                else
                    for (int i = 0; i < chr_size; i++)
                        CHR_ROM_S[i] = 0;

                // CPU / PPU memory
                NES_MEM_S = (byte*)Marshal.AllocHGlobal(65536);
                ppu_ram_S = (byte*)Marshal.AllocHGlobal(16384);
                for (int i = 0; i < 65536; i++) NES_MEM_S[i] = 0;
                for (int i = 0; i < 16384; i++) ppu_ram_S[i] = 0;

                // Mapper
                MapperObj_S = CreateMapper_S(mapper_S);
                if (MapperObj_S == null)
                { ShowError_S($"Mapper {mapper_S} not supported (Speed Core)"); return false; }

                MapperObj_S.MapperInit(PRG_ROM_S, CHR_ROM_S, ppu_ram_S,
                                       PRG_ROM_count_S, CHR_ROM_count_S, Vertical_S);

                // Joypad
                P1_joypad_status_S = (byte*)Marshal.AllocHGlobal(8);
                for (int i = 0; i < 8; i++) P1_joypad_status_S[i] = 0x40;

                init_mem_S();
                init_ppu_S();
                init_apu_S();
                init_cpu_S();

                Console.WriteLine($"[SpeedCore] Mapper {mapper_S}, PRG={PRG_ROM_count_S}x16K, CHR={CHR_ROM_count_S}x8K");
                return true;
            }
            catch (Exception ex)
            {
                ShowError_S("init_S error: " + ex.Message);
                return false;
            }
        }

        static IMapper_S CreateMapper_S(int id)
        {
            switch (id)
            {
                case 0:  return new Mapper000_S();
                case 1:  return new Mapper001_S();
                case 2:  return new Mapper002_S();
                case 3:  return new Mapper003_S();
                case 4:  return new Mapper004_S();
                case 7:  return new Mapper007_S();
                case 11: return new Mapper011_S();
                case 66: return new Mapper066_S();
                default: return null;
            }
        }

        // ----------------------------------------------------------------
        static public bool exit_S = false;

        static public void run_S()
        {
            exit_S = false;
            while (!exit_S)
            {
                _event_S.WaitOne();
                cpu_step_S();
            }
        }

        static public void cleanup_S()
        {
            closeAudio_S();
            cleanup_ppu_S();
            if (P1_joypad_status_S != null) { Marshal.FreeHGlobal((IntPtr)P1_joypad_status_S); P1_joypad_status_S = null; }
            if (PRG_ROM_S  != null) { Marshal.FreeHGlobal((IntPtr)PRG_ROM_S);  PRG_ROM_S  = null; }
            if (CHR_ROM_S  != null) { Marshal.FreeHGlobal((IntPtr)CHR_ROM_S);  CHR_ROM_S  = null; }
            if (NES_MEM_S  != null) { Marshal.FreeHGlobal((IntPtr)NES_MEM_S);  NES_MEM_S  = null; }
            if (ppu_ram_S  != null) { Marshal.FreeHGlobal((IntPtr)ppu_ram_S);  ppu_ram_S  = null; }
            if (Vertical_S != null) { Marshal.FreeHGlobal((IntPtr)Vertical_S); Vertical_S = null; }
        }

        static void RenderScreen_S()
        {
            VideoOutput_S?.Invoke(null, VideoOut_arg_S);
            _event_S.WaitOne();
        }
    }
}
