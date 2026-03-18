using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AprNes
{

    #region video & audio output event arg
    unsafe class VideoOut : EventArgs
    {
        //nothing need to pass
    }
    #endregion


    unsafe public partial class NesCore
    {


        public static event EventHandler VideoOutput;


        static VideoOut VideoOut_arg = new VideoOut();

        static int mapper;
        static byte PRG_ROM_count, CHR_ROM_count, ROM_Control_1, ROM_Control_2, RAM_banks_count;
        static byte* PRG_ROM, CHR_ROM;
        static bool NesHeaderV2 = false;
        static public bool HasBattery = false;
        static public string rom_file_name = "";

        static IMapper MapperObj;
        static public byte*[] chrBankPtrs = new byte*[8]; // P34: 8×1KB CHR bank pointers, updated by mapper


        // ROM info accessors (read-only, set during init)
        static public int  RomMapper   => mapper;
        static public int  RomPrgCount => PRG_ROM_count;
        static public int  RomChrCount => CHR_ROM_count;
        static public bool RomHorizMirror => (ROM_Control_1 & 1) == 0;

        // FPS limiting flag (set by UI, checked in VideoOutput handler)
        static public bool LimitFPS = false;

        // Accuracy option: per-dot secondary OAM evaluation FSM (dots 1-64 clear, 65-256 evaluate)
        // true = full hardware accuracy; false = skip FSM for ~13% performance gain (no test failures)
        static public bool AccuracyOptA = false;

        static int* Vertical; //  Vertical = false,

        static public ManualResetEvent _event = new ManualResetEvent(true);

        static public Action<string> OnError;

        static public void ShowError(string msg)
        {
            OnError?.Invoke(msg);
        }

        static public bool init(byte[] rom_bytes) //for Hard Reset effect
        {
            try
            {
                //http://nesdev.com/iNES.txt
                //https://github.com/dsedivec/inestool/blob/master/inestool.py
                if (!(rom_bytes[0] == 'N' && rom_bytes[1] == 'E' && rom_bytes[2] == 'S' && rom_bytes[3] == 0x1a))
                {
                    ShowError("Bad Magic Number !");
                    return false;
                }
                Console.WriteLine("iNes header");


                Vertical = (int*)Marshal.AllocHGlobal(sizeof(int));

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

                ROM_Control_1 = rom_bytes[6];
                ROM_Control_2 = rom_bytes[7];

                if ((ROM_Control_1 & 1) != 0)
                {
                    *Vertical = 1;// true;
                    Console.WriteLine("vertical mirroring");
                }
                else
                {
                    *Vertical = 0;// false;
                    Console.WriteLine("horizontal mirroring");
                }

                if ((ROM_Control_1 & 2) != 0)
                {
                    HasBattery = true;
                    Console.WriteLine("battery-backed RAM : yes");
                }
                else
                {
                    HasBattery = false;
                    Console.WriteLine("battery-backed RAM : no");
                }

                if ((ROM_Control_1 & 4) != 0) Console.WriteLine("trainer : yes");
                else Console.WriteLine("trainer : no");

                if ((ROM_Control_1 & 8) != 0)
                {
                    Console.WriteLine("fourscreen mirroring : yes");
                }
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
                if (!MapperRegistry.IsSupported(mapper))
                {
                    ShowError("not support mapper ! " + mapper);
                    return false;
                }
                if (NesHeaderV2)
                {
                    RAM_banks_count = rom_bytes[8];
                    Console.WriteLine("RAM banks count : " + RAM_banks_count);
                }

                //init allocate
                ScreenBuf1x      = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 61440);
                Buffer_BG_array  = (int* )Marshal.AllocHGlobal(sizeof(int)  * 61440);
                NesColors        = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 64);
                palCacheR        = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 4);
                palCacheN        = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 4);
                spr_ram          = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 256);
                secondaryOAM     = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 32);
                corruptOamRow    = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 32);
                ppu_ram          = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 0x4000);
                P1_joypad_status = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 8);
                NES_MEM          = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 65536);

                uint romCrc = 0;
                if (mapper == 4)
                {
                    romCrc = 0xFFFFFFFF;
                    for (int i = 0; i < rom_bytes.Length; i++)
                    {
                        romCrc ^= rom_bytes[i];
                        for (int j = 0; j < 8; j++)
                            romCrc = (romCrc >> 1) ^ (((romCrc & 1) != 0) ? 0xEDB88320u : 0);
                    }
                    romCrc ^= 0xFFFFFFFF;
                    Console.WriteLine("ROM CRC32: " + romCrc.ToString("X8"));
                }
                MapperObj = MapperRegistry.Create(mapper, romCrc);
                MapperObj.MapperInit(PRG_ROM, CHR_ROM, ppu_ram, PRG_ROM_count, CHR_ROM_count, Vertical);
                MapperObj.Reset();
                MapperObj.UpdateCHRBanks();

                for (int i = 0; i < 61440; i++) ScreenBuf1x[i] = 0;
                for (int i = 0; i < 16384; i++) ppu_ram[i] = 0;
                for (int i = 0; i < 256; i++) spr_ram[i] = 0;
                for (int i = 0; i < 32; i++) { secondaryOAM[i] = 0; corruptOamRow[i] = 0; }
                for (int i = 0; i < 8; i++) P1_joypad_status[i] = 0x40;
                for (int i = 0; i < 65536; i++) NES_MEM[i] = 0;

                initPalette();

                //init function array
                init_function();
                InitOpHandlers();

                //init APU & audio output (must be before reset vector read so tick() can run)
                initAPU();

                //init cpu pc (read reset vector — no tick needed, boot cycles already counted)
                r_PC = (ushort)(mem_read_fun[0xfffc](0xfffc) | (mem_read_fun[0xfffd](0xfffd) << 8));


            }
            catch (Exception e)
            {
                ShowError(e.Message);
                return false;
            }
            return true;
        }

        static public void LoadSRam(byte[] data)
        {
            for (int i = 0; i < 0x2000; i++) NES_MEM[i + 0x6000] = data[i];
        }

        static public byte[] DumpSRam()
        {
            byte[] buf = new byte[0x2000];
            for (int i = 0; i < 0x2000; i++) buf[i] = NES_MEM[i + 0x6000];
            return buf;
        }

        static public void run()
        {
            bool nmi_just_deferred = false;
            while (!exit)
            {
                // === Interrupt service point (before instruction fetch) ===
                if (nmi_pending && !nmi_just_deferred)
                {
                    nmi_pending = false;
                    doNMI = true;
                    irq_pending = false; // NMI handler sets I=1; IRQ re-polled after RTI
                }
                else if (nmi_just_deferred)
                {
                    // Deferred NMI: skip this iteration, let 1 instruction run first
                    nmi_just_deferred = false;
                }
                else if (irq_pending)
                {
                    irq_pending = false;
                    doIRQ = true;
                }

                byte prevFlagI_run = flagI; // capture I flag before instruction for IRQ delay
                cpu_step();

                // BRK/NMI/IRQ: NMI arising during sequence → defer 1 instruction
                if (opcode == 0x00 && nmi_pending)
                    nmi_just_deferred = true;

                // === IRQ polling (end of instruction, for next instruction) ===
                if (opcode != 0x00)
                {
                    byte irqPollI = (opcode == 0x40) ? flagI : prevFlagI_run;
                    irq_pending = (irqPollI == 0 && irqLinePrev);
                }
            }
            Console.WriteLine("exit..");
        }
    }

}
