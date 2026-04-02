using System;
using System.Runtime.CompilerServices;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        public static byte* NES_MEM;

        static ushort cpuBusAddr = 0;    // CPU current bus address (for DMC phantom reads)

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

        // Master Clock timing (TriCNES model: per-master-clock execution)
        // NTSC: 21,477,272.73 Hz — CPU = master ÷ 12, PPU = master ÷ 4 (3:1)
        // PAL:  26,601,714 Hz   — CPU = master ÷ 16, PPU = master ÷ 5 (3.2:1)
        static long cpuCycleCount = 7;

        // Per-master-clock dividers (TriCNES: CPUClock/PPUClock countdown timers)
        // Count DOWN to 0, component executes when counter reaches 0, then resets.
        static int mcCpuClock = 12;   // CPU: 12→0 (execute at 0, reset to 12) [NTSC]
        static int mcPpuClock = 0;    // PPU: 4→0 (full step at 0, half step at 2)
        static bool mcApuPutCycle = false; // M2 phase (toggles every APU/CPU step)

        // StartCpuCycle: ONLY used by DMA stolen cycles (ProcessPendingDma)
        // Normal CPU cycles driven by MasterClockTick in Main.cs
        // Advances PPU for one CPU cycle but does NOT touch mcCpuClock
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void StartCpuCycle()
        {
            irqLinePrev = irqLineCurrent;
            cpuCycleCount++;
            m2PhaseIsWrite = (cpuCycleCount & 1) != 0;

            // Advance PPU for one CPU cycle (DMA stolen cycle)
            // Uses mcPpuClock but NOT mcCpuClock (mcCpuClock is MasterClockTick's domain)
            for (int t = 0; t < masterPerCpu; t++)
            {
                if (mcPpuClock == 0)
                {
                    mcPpuClock = masterPerPpu;
                    if (regionMode == 0)      ppu_step_ntsc();
                    else if (regionMode == 1) ppu_step_pal();
                    else                      ppu_step_dendy();
                    bool o = isVblank && NMIable;
                    if (o && !nmi_output_prev) nmi_delay_cycle = cpuCycleCount;
                    nmi_output_prev = o;
                }
                if (mcPpuClock == (masterPerPpu >> 1))
                    ppu_half_step();
                mcPpuClock--;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void EndCpuCycle()
        {
        }

        // Called at every site that changes statusframeint, apuintflag, statusdmcint, or statusmapperint
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateIRQLine()
        {
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
        // Bridge: called from MasterClockTick CPU gate when DMA is pending
        // Runs ALL DMA cycles blocking (legacy model — PPU pauses during DMA)
        // TODO: convert to per-cycle DMA dispatch for true interleaving
        static void DmaOneCycle()
        {
            ProcessPendingDma(cpuBusAddr);
        }

        static void ProcessPendingDma(ushort readAddress)
        {
            if (!dmaNeedHalt && !dmcDmaRunning && !dmcNeedDummyRead && !spriteDmaTransfer && !dmcImplicitAbortPending) return;

            // SH* opcodes: DMA during critical cycle makes H invisible (TriCNES IgnoreH)
            if ((opcode == 0x93 && operationCycle == 4) ||
                (opcode == 0x9B && operationCycle == 3) ||
                (opcode == 0x9C && operationCycle == 3) ||
                (opcode == 0x9E && operationCycle == 3) ||
                (opcode == 0x9F && operationCycle == 3))
            {
                ignoreH = true;
            }

            bool skipDummyReads = (readAddress == 0x4016 || readAddress == 0x4017);
            bool enableInternalRegReads = (readAddress & 0xFFE0) == 0x4000;
            dmaPrevReadAddress = readAddress;

            // --- Halt cycle ---
            dmaNeedHalt = false;
            cpuBusAddr = readAddress;
            StartCpuCycle();
            if (!(dmcAbortDma && skipDummyReads))
            {
                ppu2007SM = 9;
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

            // TriCNES: clear implicit abort after halt cycle (line 8758-8761)
            // In TriCNES, after each CPU cycle, if DoDMCDMA && APU_ImplicitAbortDMC4015,
            // the flag is cleared. Next cycle's gate check fails because neither
            // APU_Status_DMC nor APU_ImplicitAbortDMC4015 is true.
            // This gives a 1-cycle phantom DMA (halt only).
            // Only cancel when DMA was SOLELY for implicit abort (no samples left to play).
            // If dmcsamplesleft > 0, this is a normal refill DMA that also had the flag set.
            if (dmcImplicitAbortActive)
            {
                dmcImplicitAbortActive = false;
                if (dmcDmaRunning && dmcsamplesleft == 0)
                {
                    dmcDmaRunning = false;
                    dmcNeedDummyRead = false;
                    if (!spriteDmaTransfer) return;
                }
            }

            // --- Main DMA loop ---
            int spriteDmaCounter = 0;
            byte spriteReadAddr = 0;
            byte readValue = 0;

            while (dmcDmaRunning || spriteDmaTransfer)
            {
                // TriCNES per-cycle gate (line 3974): DoDMCDMA && (APU_Status_DMC || ImplicitAbort)
                // Only abort when deferred $4015 disable has been APPLIED (dmcStatusEnabled=false)
                // AND the pending value is also disable (dmcDelayedEnable=false).
                // This avoids blocking DMA before the initial enable status has been applied.
                if (dmcDmaRunning && !dmcStatusEnabled && !dmcDelayedEnable && !dmcImplicitAbortActive)
                {
                    dmcDmaRunning = false;
                    dmcNeedDummyRead = false;
                    if (!spriteDmaTransfer) break;
                }

                bool getCycle = (cpuCycleCount & 1) == 0;

                if (getCycle)
                {
                    if (dmcDmaRunning && !dmaNeedHalt && !dmcNeedDummyRead)
                    {
                        absorbDmaFlags();
                        StartCpuCycle();
                        ushort dmcReadAddr = (ushort)dmcaddr;
                        byte val = ProcessDmaRead(dmcReadAddr, enableInternalRegReads);
                        if (!dmaReadSkipBusUpdate) cpubus = val;
                        EndCpuCycle();
                        dmcDmaRunning = false;
                        dmcAbortDma = false;
                        dmcSetReadBuffer(val);
                        dmcDmaCooldown = 2; // TriCNES: CannotRunDMCDMARightNow
                    }
                    else if (spriteDmaTransfer)
                    {
                        absorbDmaFlags();
                        StartCpuCycle();
                        ushort srcAddr = (ushort)(spriteDmaOffset * 0x100 + spriteReadAddr);
                        cpuBusAddr = srcAddr;
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
                        if (!skipDummyReads)
                        {
                            ppu2007SM = 9;
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
                        if (!skipDummyReads)
                        {
                            ppu2007SM = 9;
                            mem_read_fun[readAddress](readAddress);
                        }
                        EndCpuCycle();
                    }
                }

                // TriCNES: clear implicit abort after each DMA cycle (line 8758-8761)
                if (dmcImplicitAbortActive) dmcImplicitAbortActive = false;
            }
        }

        static ushort dmaPrevReadAddress = 0;

        static bool dmaReadSkipBusUpdate; // $4015 bus conflict: don't update cpubus with return value

        static byte ProcessDmaRead(ushort addr, bool enableInternalRegReads)
        {
            dmaReadSkipBusUpdate = false;
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
                    if (internalAddr != addr)
                    {
                        // TriCNES bus conflict: read ROM first, then construct $4015 value
                        byte romVal = mem_read_fun[addr](addr);
                        cpubus = romVal;  // set bus to ROM value
                        val = IO_read(0x4015);  // apu_r_4015 uses cpubus & 0x20 for bit 5
                        // TriCNES: "reading from $4015 can not affect the databus"
                        // dataBus stays as ROM value; return status for DMA buffer only
                        dmaReadSkipBusUpdate = true;
                    }
                    else
                    {
                        val = IO_read(0x4015);
                    }
                    break;
                case 0x4016:
                case 0x4017:
                    if (internalAddr != addr)
                    {
                        // Bus conflict: read ROM first to set data bus,
                        // then controller read uses cpubus for open bus bits
                        cpubus = mem_read_fun[addr](addr);
                    }
                    if (dmaPrevReadAddress == internalAddr) val = cpubus;
                    else val = IO_read(internalAddr);
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
            StartCpuCycle();
            byte val;
            if (address < 0x2000)
            {
                val = NES_MEM[address & 0x7FF];
                cpubus = val;
            }
            else
            {
                val = mem_read_fun[address](address);
                if (address != 0x4015) cpubus = val;
            }
            EndCpuCycle();
            return val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Mem_w(ushort address, byte value)
        {
            cpuBusAddr = address;
            StartCpuCycle();
            cpubus = value;
            mem_write_fun[address](address, value);
            EndCpuCycle();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte ZP_r(byte addr)
        {
            cpuBusAddr = addr;
            StartCpuCycle();
            byte val = NES_MEM[addr]; cpubus = val;
            EndCpuCycle();
            return val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ZP_w(byte addr, byte value)
        {
            cpuBusAddr = addr;
            StartCpuCycle();
            NES_MEM[addr] = value; cpubus = value;
            EndCpuCycle();
        }

        static Action<ushort, byte>[] mem_write_fun = null;
        static Func<ushort, byte>[] mem_read_fun = null;

        // ppu_read_fun/ppu_write_fun removed — replaced by PpuBusRead/PpuBusWrite in PPU.cs

        static void init_function()
        {
            mem_write_fun = new Action<ushort, byte>[0x10000];
            mem_read_fun = new Func<ushort, byte>[0x10000];

            // ppu_write_fun/ppu_read_fun arrays removed (replaced by PpuBusRead/PpuBusWrite)

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
                else if (address < 0x4100) mem_read_fun[address] = new Func<ushort, byte>((addr) => { return cpubus; }); // $4020-$40FF: CPU open bus
                else if (address < 0x6000) mem_read_fun[address] = new Func<ushort, byte>(MapperObj.MapperR_ExpansionROM); // $4100-$5FFF: mapper expansion ROM
                else if (address < 0x8000) mem_read_fun[address] = new Func<ushort, byte>(MapperObj.MapperR_RAM);
                else mem_read_fun[address] = new Func<ushort, byte>(MapperObj.MapperR_RPG);
            }


            // PPU bus read/write lambdas removed — replaced by PpuBusRead/PpuBusWrite in PPU.cs
            // $2007 register behavior (buffer, increment) handled in ppu_r_2007/ppu_w_2007
        }
    }
}