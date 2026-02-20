//#define debug
using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using WINAPIGDI;
using XBRz_speed;
using System.Runtime.InteropServices;
using System.Threading;
using System.Reflection;

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
        static bool NesHeaderV2 = false, battery = false;
        static public string rom_file_name = "";
        static string rom_sav = "";

        static IMapper MapperObj;

        static public int[] Mapper_Allow = new int[] { 0, 1, 2, 3, 4, 7, 11, 66 };

        static int* Vertical; //  Vertical = false,

        static public ManualResetEvent _event = new ManualResetEvent(true);

        static public void ShowError(string msg)
        {
            if (HeadlessMode)
                Console.Error.WriteLine("ERROR: " + msg);
            else
                MessageBox.Show(msg);
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
                    battery = true;
                    Console.WriteLine("battery-backed RAM : yes");
                }
                else Console.WriteLine("battery-backed RAM : no");

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
                bool mapper_pass = false;
                foreach (int i in Mapper_Allow) if (i == mapper) mapper_pass = true;

                if (!mapper_pass)
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
                ScreenBuf1x = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 61440);

                Buffer_BG_array = (int*)Marshal.AllocHGlobal(sizeof(int) * 61440);
                NesColors = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 64);
                cycle_table = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 256);
                spr_ram = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 256);

                ppu_ram = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 0x4000);
                P1_joypad_status = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 8);
                NES_MEM = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 65536);

                MapperObj = (IMapper)Activator.CreateInstance(Type.GetType("AprNes.Mapper" + mapper.ToString("d3")));
                MapperObj.MapperInit(PRG_ROM, CHR_ROM, ppu_ram, PRG_ROM_count, CHR_ROM_count, Vertical);

                for (int i = 0; i < 61440; i++) ScreenBuf1x[i] = 0;
                for (int i = 0; i < 16384; i++) ppu_ram[i] = 0;
                for (int i = 0; i < 256; i++) spr_ram[i] = 0;
                for (int i = 0; i < 256; i++) cycle_table[i] = cycle_tableData[i];
                for (int i = 0; i < 8; i++) P1_joypad_status[i] = 0x40;
                for (int i = 0; i < 64; i++) NesColors[i] = NesColorsData[i];
                for (int i = 0; i < 65536; i++) NES_MEM[i] = 0;

                //int default pal
                for (int i = 0; i < 32; i++) ppu_ram[0x3f00 + i] = defaultPal[i];

                //init function array
                init_function();

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

                //init APU & audio output
                initAPU();

                //init debug log
                dbgInit();
            }
            catch (Exception e)
            {
                ShowError(e.Message);
                return false;
            }
            return true;
        }

        static void* GetField(string name)
        {
            return Pointer.Unbox((Pointer)typeof(NesCore).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null));
        }

        static public void SaveRam()
        {
            if (!battery) return;
            byte[] sav_bytes = new byte[0x2000];
            for (int i = 0; i < 0x2000; i++) sav_bytes[i] = NES_MEM[i + 0x6000]; //copy from ram
            File.WriteAllBytes(rom_sav, sav_bytes);
        }
        static public void run()
        {
            timeBeginPeriod(1); // 設定 1ms 計時器精度，確保 Thread.Sleep(1) 準確
            StopWatch.Restart();
            while (!exit)
            {
                bool irq_just_fired = false;
                // === Interrupt service point (before instruction fetch) ===
                // NMI is edge-triggered and takes priority over IRQ.
                if (nmi_pending)
                {
                    nmi_pending = false;
                    dbgWrite("NMI_FIRE: sl=" + scanline + " cx=" + ppu_cycles_x + " PC=$" + r_PC.ToString("X4") + " flags=$" + GetFlag().ToString("X2") + " I=" + flagI);
                    NMIInterrupt();
                    nmi_trace_count = 25;
                }
                else if (irq_pending && !nmi_delayed)
                {
                    // IRQ is level-triggered. Skip if NMI is delayed (will fire next iteration).
                    irq_pending = false;
                    dbgWrite("IRQ_FIRE: sl=" + scanline + " cx=" + ppu_cycles_x + " PC=$" + r_PC.ToString("X4") + " flags=$" + GetFlag().ToString("X2") + " I=" + flagI);
                    IRQInterrupt();
                    irq_just_fired = true;
                }
                // Transfer delayed NMI: VBL on last cycle of prev instruction
                // becomes pending NOW (after NMI check), so it fires after the
                // NEXT instruction — matching real NES 1-instruction delay.
                if (nmi_delayed)
                {
                    nmi_delayed = false;
                    nmi_pending = true;
                }
                byte prevFlagI_run = flagI; // capture I flag before instruction for IRQ delay
                cpu_step();
                int initial_cpu_cycles = cpu_cycles;
                bool nmi_was_pending = false;
                bool nmi_late_in_cycle = false; // true if VBL on sub-step 1 or 2 (not 0)
                int cycles_remaining = cpu_cycles;
                do
                {
                    ppu_step_new();
                    // Check if VBL fired on sub-step 0 (first PPU dot of CPU cycle)
                    bool vbl_on_step0 = (nmi_pending && !nmi_was_pending);
                    ppu_step_new(); ppu_step_new();
                    // Track if nmi_pending was set during this CPU cycle
                    if (nmi_pending && !nmi_was_pending)
                    {
                        nmi_was_pending = true;
                        cycles_remaining = cpu_cycles; // snapshot: how many cycles left (including this one)
                        nmi_late_in_cycle = !vbl_on_step0; // VBL on sub-step 1 or 2
                    }
                    apu_step();//1x cpu cycles
                } while (--cpu_cycles > 0);
                // NMI edge detection: on real 6502, NMI is sampled at the rising edge
                // of φ2 (early in each CPU cycle). If VBL fires at PPU sub-step 0 (the
                // very start of the CPU cycle), the edge is detected in this cycle and
                // NMI fires after the current instruction. If VBL fires at sub-step 1 or 2
                // (mid/late cycle), the edge misses the sampling point and is detected at
                // the start of the NEXT CPU cycle — causing a 1-instruction delay when
                // this was the last cycle of the instruction.
                if (nmi_was_pending && cycles_remaining == 1 && nmi_late_in_cycle
                    && opcode != 0x00 && !irq_just_fired)
                {
                    nmi_pending = false;
                    nmi_delayed = true;
                }
                // IRQ-NMI hijacking: if NMI asserts during IRQ's 7-cycle vectoring
                // overhead, redirect to NMI vector. IRQ already pushed the correct
                // return address and flags to the stack, so NMI handler's RTI will
                // return to the right place with original flags restored.
                // On real NES, vector fetch is on cycles 6-7. NMI detected on cycles
                // 1-5 hijacks the vector; cycles 6-7 are too late (vector already fetched).
                if (irq_just_fired && nmi_pending && nmi_was_pending
                    && cycles_remaining > (initial_cpu_cycles - 5))
                {
                    dbgWrite("IRQ_NMI_HIJACK: sl=" + scanline + " cx=" + ppu_cycles_x
                        + " PC=$" + r_PC.ToString("X4") + " cycles_remaining=" + cycles_remaining
                        + " initial=" + initial_cpu_cycles);
                    nmi_pending = false;
                    r_PC = (ushort)(Mem_r(0xfffa) | (Mem_r(0xfffb) << 8));
                    nmi_trace_count = 25;
                }
                // BRK NMI hijacking: if BRK just executed and NMI fired during its cycles,
                // redirect to NMI vector (BRK already pushed flags with B bit and return addr).
                // On real NES, vector fetch is on BRK's last 2 cycles (6-7). If NMI is detected
                // only on those cycles, it's too late to hijack — BRK uses IRQ vector, NMI fires
                // after the next instruction.
                if (nmi_pending && opcode == 0x00)
                {
                    if (nmi_was_pending && cycles_remaining <= 3)
                    {
                        // VBL on BRK's last 3 cycles (5-7): too late to hijack vector
                        // NMI detection has 1-cycle latency, so cycle 5 NMI misses
                        // the vector fetch at cycle 6
                        dbgWrite("BRK_LATE_NMI: sl=" + scanline + " cx=" + ppu_cycles_x + " PC=$" + r_PC.ToString("X4") + " cycles_remaining=" + cycles_remaining);
                        nmi_pending = false;
                        nmi_delayed = true;
                    }
                    else
                    {
                        dbgWrite("BRK_HIJACK: sl=" + scanline + " cx=" + ppu_cycles_x + " PC=$" + r_PC.ToString("X4") + " SP=$" + r_SP.ToString("X2")
                            + " stk=[" + NES_MEM[0x100 + ((r_SP + 1) & 0xFF)].ToString("X2") + " " + NES_MEM[0x100 + ((r_SP + 2) & 0xFF)].ToString("X2") + " " + NES_MEM[0x100 + ((r_SP + 3) & 0xFF)].ToString("X2") + "]");
                        nmi_pending = false;
                        r_PC = (ushort)(Mem_r(0xfffa) | (Mem_r(0xfffb) << 8));
                        nmi_trace_count = 25;
                    }
                }
                // === IRQ polling (end of instruction, for next instruction) ===
                // IRQ is level-triggered: fires as long as source is asserted and I=0.
                // CLI/SEI/PLP: I flag change delayed 1 instruction → use prevFlagI_run
                // RTI: immediate effect → use flagI
                // BRK: already handles interrupt internally → skip
                if (opcode != 0x00)
                {
                    byte irqPollI = (opcode == 0x40) ? flagI : prevFlagI_run;
                    irq_pending = (irqPollI == 0 && (statusframeint || statusdmcint));
                }
            }
            timeEndPeriod(1);
            Console.WriteLine("exit..");
        }


#if debug
        static StreamWriter StepsLog;
        static int scount = 0;
        static public void debug()
        {

            if (scount == 0)
            {

                if (File.Exists(@"c:\log\log.txt")) File.Delete(@"c:\log\log.txt");
                if (!Directory.Exists(@"c:\log")) Directory.CreateDirectory((@"c:\log"));
                StepsLog = File.AppendText(@"c:\log\log.txt");
            }

           StepsLog.WriteLine(opcode.ToString("x2") + " " + r_PC.ToString("x4") + " " + r_A.ToString("x2") + " " + r_X.ToString("x2") + " " + r_Y.ToString("x2") + " " + r_SP.ToString("x2"));// + " " + scanline + " " + ppu_cycles.ToString("x2"));

            //if (scount > 1000000 * 50)
            //StepsLog.WriteLine(vram_addr.ToString("x4") + " " +  vram_addr_internal.ToString ("x4"));
            //StepsLog.WriteLine(scanline + " " + ppu_cycles.ToString("x2"));

            // 1000000

            if (scount == 250000*4)// 1000000 * 2)
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
