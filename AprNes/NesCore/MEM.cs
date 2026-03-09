using System;
using System.Runtime.CompilerServices;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        public static byte* NES_MEM;

        static ushort cpuBusAddr = 0;    // CPU current bus address (for DMC phantom reads)
        static bool cpuBusIsWrite = false; // true = write cycle, false = read cycle

        // M2 phase tracking: true = PUT (M2 low, write phase), false = GET (M2 high, read phase)
        // Derived from cpuCycleCount parity — represents the absolute M2 clock phase,
        // independent of whether the current bus access is a read or write.
        // DMA halt/alignment decisions depend on this phase.
        static bool m2PhaseIsWrite = false;

        // DMA state (Mesen2-style ProcessPendingDma model)
        static bool dmaNeedHalt = false;        // DMA needs halt cycle (shared OAM/DMC)
        static bool dmcNeedDummyRead = false;   // DMC needs dummy read before data read
        static bool dmcDmaRunning = false;      // DMC DMA fetch pending
        static bool spriteDmaTransfer = false;  // OAM DMA in progress
        static byte spriteDmaOffset = 0;        // OAM source page ($4014 value)

        // Master Clock timing (NTSC: 21,477,272.73 Hz)
        // CPU = master ÷ 12, PPU = master ÷ 4, APU = CPU rate
        // 1 CPU cycle = 12 master clocks = 3 PPU dots
        const int MASTER_PER_CPU = 12;
        const int MASTER_PER_PPU = 4;
        static long masterClock = 7 * MASTER_PER_CPU; // calibrated: 7 boot CPU cycles worth
        static long cpuCycleCount = 7;   // derived: masterClock / MASTER_PER_CPU
        static long ppuClock = 7 * MASTER_PER_CPU;    // PPU catch-up position (master clock units)
        static long apuClock = 7 * MASTER_PER_CPU - 4; // APU catch-up position (4 MCU behind → fires in tick_pre)

        // Catch up PPU to current master clock position
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void catchUpPPU()
        {
            while (ppuClock + MASTER_PER_PPU <= masterClock)
            {
                ppuClock += MASTER_PER_PPU;
                ppu_step_new();
                bool nmi_output = isVblank && NMIable;
                if (nmi_output && !nmi_output_prev)
                    nmi_delay_cycle = cpuCycleCount;
                nmi_output_prev = nmi_output;
            }
        }

        // Catch up APU to current master clock position
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void catchUpAPU()
        {
            while (apuClock + MASTER_PER_CPU <= masterClock)
            {
                apuClock += MASTER_PER_CPU;
                apu_step();
            }
        }

        // --- M2 Phase Split (Mesen2 model) ---
        // StartCpuCycle: full cycle advance (CC++, NMI promote, PPU, APU, IRQ)
        // Same content as old tick_pre, kept as single unit to preserve timing.
        // The key change is ProcessPendingDma moving BEFORE StartCpuCycle in Mem_r/ZP_r.
        // EndCpuCycle: placeholder for future sub-cycle split.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void StartCpuCycle()
        {
            masterClock += MASTER_PER_CPU;
            cpuCycleCount++;
            m2PhaseIsWrite = (cpuCycleCount & 1) != 0;

            if (nmi_delay_cycle >= 0 && cpuCycleCount > nmi_delay_cycle)
            { nmi_pending = true; nmi_delay_cycle = -1; }
            catchUpPPU();
            catchUpAPU();
            if (strobeWritePending > 0) processStrobeWrite();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void EndCpuCycle()
        {
            irqLinePrev = irqLineCurrent;
            irqLineCurrent = (statusframeint && !apuintflag) || statusdmcint || statusmapperint;
        }

        // Full tick: StartCpuCycle + EndCpuCycle (used by DMA stolen cycles)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void tick()
        {
            StartCpuCycle();
            EndCpuCycle();
        }

        // Mesen2-style DMA engine — called from CpuRead when dmaNeedHalt is set
        // Runs ALL DMA cycles in one blocking call (each with Start/EndCpuCycle)
        static void ProcessPendingDma(ushort readAddress)
        {
            if (!dmaNeedHalt) return;

            bool skipDummyReads = (readAddress == 0x4016 || readAddress == 0x4017);
            bool enableInternalRegReads = (readAddress & 0xFFE0) == 0x4000;
            dmaPrevReadAddress = readAddress;

            // --- Halt cycle ---
            dmaNeedHalt = false;
            cpuBusAddr = readAddress;
            cpuBusIsWrite = true;
            StartCpuCycle();
            cpuBusIsWrite = false;
            if (!(dmcAbortDma && skipDummyReads))
            {
                ppu2007ReadCooldown = 0;
                mem_read_fun[readAddress](readAddress);
            }
            EndCpuCycle();

            // Check DMC abort after halt
            if (dmcAbortDma)
            {
                dmcDmaRunning = false;
                dmcAbortDma = false;
                if (!spriteDmaTransfer)
                {
                    dmcNeedDummyRead = false;
                    return;
                }
            }

            // --- Main DMA loop ---
            int spriteDmaCounter = 0;
            byte spriteReadAddr = 0;
            byte readValue = 0;

            while (dmcDmaRunning || spriteDmaTransfer)
            {
                bool getCycle = (cpuCycleCount & 1) == 0;

                if (getCycle)
                {
                    if (dmcDmaRunning && !dmaNeedHalt && !dmcNeedDummyRead)
                    {
                        absorbDmaFlags();
                        StartCpuCycle();
                        ushort dmcReadAddr = (ushort)dmcaddr;
                        byte val = ProcessDmaRead(dmcReadAddr, enableInternalRegReads);
                        cpubus = val;
                        EndCpuCycle();
                        dmcDmaRunning = false;
                        dmcAbortDma = false;
                        dmcSetReadBuffer(val);
                    }
                    else if (spriteDmaTransfer)
                    {
                        absorbDmaFlags();
                        StartCpuCycle();
                        ushort srcAddr = (ushort)(spriteDmaOffset * 0x100 + spriteReadAddr);
                        cpuBusAddr = srcAddr;
                        cpuBusIsWrite = false;
                        readValue = ProcessDmaRead(srcAddr, enableInternalRegReads);
                        cpubus = readValue;
                        EndCpuCycle();
                        spriteReadAddr++;
                        spriteDmaCounter++;
                    }
                    else
                    {
                        absorbDmaFlags();
                        StartCpuCycle();
                        cpuBusAddr = readAddress;
                        cpuBusIsWrite = false;
                        if (!skipDummyReads)
                        {
                            ppu2007ReadCooldown = 0;
                            mem_read_fun[readAddress](readAddress);
                        }
                        EndCpuCycle();
                    }
                }
                else
                {
                    if (spriteDmaTransfer && (spriteDmaCounter & 1) != 0)
                    {
                        absorbDmaFlags();
                        StartCpuCycle();
                        cpuBusAddr = 0x2004;
                        cpuBusIsWrite = true;
                        spr_ram[spr_ram_add++] = readValue;
                        EndCpuCycle();
                        spriteDmaCounter++;
                        if (spriteDmaCounter == 0x200)
                            spriteDmaTransfer = false;
                    }
                    else
                    {
                        absorbDmaFlags();
                        StartCpuCycle();
                        cpuBusAddr = readAddress;
                        cpuBusIsWrite = false;
                        if (!skipDummyReads)
                        {
                            ppu2007ReadCooldown = 0;
                            mem_read_fun[readAddress](readAddress);
                        }
                        EndCpuCycle();
                    }
                }
            }
        }

        static ushort dmaPrevReadAddress = 0;

        static byte ProcessDmaRead(ushort addr, bool enableInternalRegReads)
        {
            if (!enableInternalRegReads)
            {
                if (addr >= 0x4000 && addr <= 0x401F)
                    return cpubus;
                return mem_read_fun[addr](addr);
            }
            ushort internalAddr = (ushort)(0x4000 | (addr & 0x1F));
            byte val;
            switch (internalAddr)
            {
                case 0x4015:
                    val = IO_read(0x4015);
                    if (internalAddr != addr) { cpubus = val; mem_read_fun[addr](addr); }
                    break;
                case 0x4016:
                case 0x4017:
                    if (dmaPrevReadAddress == internalAddr) val = cpubus;
                    else val = IO_read(internalAddr);
                    if (internalAddr != addr)
                    {
                        cpubus = val;
                        byte externalVal = mem_read_fun[addr](addr);
                        byte obMask = 0xE0;
                        val = (byte)((externalVal & obMask) | ((val & ~obMask) & (externalVal & ~obMask)));
                    }
                    break;
                default:
                    val = mem_read_fun[addr](addr);
                    break;
            }
            dmaPrevReadAddress = internalAddr;
            return val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void absorbDmaFlags()
        {
            if (dmcAbortDma) { dmcDmaRunning = false; dmcAbortDma = false; dmcNeedDummyRead = false; dmaNeedHalt = false; }
            else if (dmaNeedHalt) { dmaNeedHalt = false; }
            else if (dmcNeedDummyRead) { dmcNeedDummyRead = false; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte Mem_r(ushort address)
        {
            cpuBusAddr = address;
            cpuBusIsWrite = false;
            StartCpuCycle();
            byte val = mem_read_fun[address](address);
            if (address != 0x4015) cpubus = val;
            EndCpuCycle();
            return val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Mem_w(ushort address, byte value)
        {
            cpuBusAddr = address;
            cpuBusIsWrite = true;
            StartCpuCycle();
            cpubus = value;
            mem_write_fun[address](address, value);
            EndCpuCycle();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte ZP_r(byte addr)
        {
            cpuBusAddr = addr; cpuBusIsWrite = false;
            StartCpuCycle();
            byte val = NES_MEM[addr]; cpubus = val;
            EndCpuCycle();
            return val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ZP_w(byte addr, byte value)
        {
            cpuBusAddr = addr; cpuBusIsWrite = true;
            StartCpuCycle();
            NES_MEM[addr] = value; cpubus = value;
            EndCpuCycle();
        }

        static Action<ushort, byte>[] mem_write_fun = null;
        static Func<ushort, byte>[] mem_read_fun = null;

        static Action<byte>[] ppu_write_fun = null;
        static Func<int, byte>[] ppu_read_fun = null;

        static void init_function()
        {
            mem_write_fun = new Action<ushort, byte>[0x10000];
            mem_read_fun = new Func<ushort, byte>[0x10000];

            ppu_write_fun = new Action<byte>[0x10000];
            ppu_read_fun = new Func<int, byte>[0x10000];

            for (int address = 0; address < 0x10000; address++)
            {
                if (address < 0x2000) mem_write_fun[address] = new Action<ushort, byte>((addr, val) => { NES_MEM[addr & 0x7ff] = val; });
                else if (address < 0x4020) mem_write_fun[address] = new Action<ushort, byte>(IO_write);
                else if (address < 0x4100) mem_write_fun[address] = new Action<ushort, byte>((addr, val) => { }); // $4020-$40FF: open bus (no effect on write)
                else if (address < 0x6000) mem_write_fun[address] = new Action<ushort, byte>(MapperObj.MapperW_ExpansionROM);
                else if (address < 0x8000) mem_write_fun[address] = new Action<ushort, byte>(MapperObj.MapperW_RAM);
                else mem_write_fun[address] = new Action<ushort, byte>(MapperObj.MapperW_PRG);
            }
            for (int address = 0; address < 0x10000; address++)
            {
                if (address < 0x2000) mem_read_fun[address] = new Func<ushort, byte>((addr) => { return NES_MEM[addr & 0x7ff]; });
                else if (address < 0x4020) mem_read_fun[address] = new Func<ushort, byte>(IO_read);
                else if (address < 0x6000) mem_read_fun[address] = new Func<ushort, byte>((addr) => { return cpubus; }); // $4020-$5FFF: CPU open bus
                else if (address < 0x8000) mem_read_fun[address] = new Func<ushort, byte>(MapperObj.MapperR_RAM);
                else mem_read_fun[address] = new Func<ushort, byte>(MapperObj.MapperR_RPG);
            }


            for (int address = 0; address < 0x10000; address++)
            {

                int vram_addr_wrap = 0;
                if ((address & 0x3F00) == 0x3F00)
                {


                    vram_addr_wrap = address & 0x2FFF;

                    if (vram_addr_wrap < 0x2000)
                    {
                        ppu_read_fun[address] = new Func<int, byte>((val) =>
                        {


                            ppu_2007_temp = ppu_ram[val & ((val & 0x03) == 0 ? 0x0C : 0x1F) + 0x3f00];
                            ppu_2007_buffer = MapperObj.MapperR_CHR(val & 0x2FFF);

                            ppu_2007_temp = (byte)((openbus & 0xC0) | (ppu_2007_temp & 0x3F));//add openbus fix

                            Increment2007();
                            openbus = ppu_2007_temp;
                            open_bus_decay_timer = 77777;//fixed add

                            return openbus;
                        });
                    }
                    else
                    {

                        ppu_read_fun[address] = new Func<int, byte>((val) =>
                        {

                            ppu_2007_temp = ppu_ram[val & ((val & 0x03) == 0 ? 0x0C : 0x1F) + 0x3f00];
                            ppu_2007_buffer = ppu_ram[val & 0x2FFF];

                            ppu_2007_temp = (byte)((openbus & 0xC0) | (ppu_2007_temp & 0x3F));//add openbus fix

                            Increment2007();
                            openbus = ppu_2007_temp;
                            open_bus_decay_timer = 77777;//fixed add
                            return openbus;
                        });
                    }
                }
                else
                {

                    vram_addr_wrap = address & 0x3FFF;

                    if (vram_addr_wrap < 0x2000)
                    {

                        ppu_read_fun[address] = new Func<int, byte>((val) =>
                        {
                            ppu_2007_temp = ppu_2007_buffer; //need read from buffer
                            ppu_2007_buffer = MapperObj.MapperR_CHR(val & 0x3FFF);//Pattern Table
                            Increment2007();
                            openbus = ppu_2007_temp;
                            open_bus_decay_timer = 77777;//fixed add
                            return openbus;
                        });
                    }
                    else if (vram_addr_wrap < 0x3F00)
                    {
                        ppu_read_fun[address] = new Func<int, byte>((val) =>
                        {


                            ppu_2007_temp = ppu_2007_buffer; //need read from buffer
                            ppu_2007_buffer = ppu_ram[val & 0x2FFF]; //Name Table & Attribute Table ($3000-$3EFF mirrors $2000-$2EFF)
                            Increment2007();
                            openbus = ppu_2007_temp;
                            open_bus_decay_timer = 77777;//fixed add
                            return openbus;
                        });
                    }
                    else
                    {

                        ppu_read_fun[address] = new Func<int, byte>((val) =>
                        {
                            ppu_2007_temp = ppu_2007_buffer; //need read from buffer
                            int _vram_addr_wrap = val & 0x2FFF;
                            ppu_2007_buffer = ppu_ram[_vram_addr_wrap & ((_vram_addr_wrap & 0x03) == 0 ? 0x0C : 0x1F) + 0x3f00]; // //Sprite Palette & Image Palette
                            Increment2007();
                            openbus = ppu_2007_temp;
                            open_bus_decay_timer = 77777;//fixed add
                            return openbus;
                        });


                    }
                }

            }


            for (int address = 0; address < 0x10000; address++)
            {

                int vram_addr_wrap = 0;

                vram_addr_wrap = address & 0x3FFF;
                if (vram_addr_wrap < 0x2000)
                {
                    ppu_write_fun[address] = new Action<byte>((val) =>
                    {
                        int _vram_addr_wrap = vram_addr & 0x3FFF;
                        openbus = val;
                        if (CHR_ROM_count == 0) ppu_ram[_vram_addr_wrap] = val;
                        Increment2007();
                    });
                }
                else if (vram_addr_wrap < 0x3f00) //Name Table & Attribute Table
                {
                    ppu_write_fun[address] = new Action<byte>((val) =>
                   {
                       int _vram_addr_wrap = vram_addr & 0x2FFF; // $3000-$3EFF mirrors $2000-$2EFF
                       int _addr_range = _vram_addr_wrap & 0xc00;
                       openbus = val;
                       int mirror = *Vertical;
                       if (mirror >= 2)
                       {
                           // One-screen mirroring: all 4 nametables map to same 1KB
                           int rel = _vram_addr_wrap & 0x3FF;
                           ppu_ram[0x2000 + rel] = ppu_ram[0x2400 + rel] = ppu_ram[0x2800 + rel] = ppu_ram[0x2C00 + rel] = val;
                       }
                       else if (mirror == 1)
                       {
                           if (_addr_range < 0x800) ppu_ram[_vram_addr_wrap] = ppu_ram[_vram_addr_wrap | 0x800] = val;
                           else ppu_ram[_vram_addr_wrap] = ppu_ram[_vram_addr_wrap & 0x37ff] = val;
                       }
                       else
                       {
                           if (_addr_range < 0x400) ppu_ram[_vram_addr_wrap] = ppu_ram[_vram_addr_wrap | 0x400] = val;
                           else if (_addr_range < 0x800) ppu_ram[_vram_addr_wrap] = ppu_ram[_vram_addr_wrap & 0x3bff] = val;
                           else if (_addr_range < 0xc00) ppu_ram[_vram_addr_wrap] = ppu_ram[_vram_addr_wrap | 0x400] = val;
                           else ppu_ram[_vram_addr_wrap] = ppu_ram[_vram_addr_wrap & 0x3bff] = val;
                       }
                       Increment2007();
                   });
                }
                else
                {
                    ppu_write_fun[address] = new Action<byte>((val) =>
                   {
                       int _vram_addr_wrap = vram_addr & 0x3FFF;
                       openbus = val;
                       ppu_ram[(_vram_addr_wrap & ((_vram_addr_wrap & 0x03) == 0 ? 0x0C : 0x1F)) + 0x3f00] = val; //Sprite Palette & Image Palette
                       Increment2007();
                   });
                }



            }
        }
    }
}