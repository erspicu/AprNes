using System;
using System.Runtime.CompilerServices;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        public static byte* NES_MEM;

        static ushort cpuBusAddr = 0;    // CPU current bus address (for DMC phantom reads)


        // ── DMA state — TriCNES per-cycle dispatch model ──
        // Each DmaOneCycle() call executes exactly ONE DMA cycle.
        // PPU advancement happens naturally via MasterClockTick's PPU gate.

        // OAM DMA ($4014)
        static bool spriteDmaTransfer = false;  // OAM DMA in progress (TriCNES: DoOAMDMA)
        static byte spriteDmaOffset = 0;        // OAM source page ($4014 value)
        static bool dmaOamHalt = false;         // OAM halt flag — dummy read (TriCNES: OAMDMA_Halt)
        static bool dmaOamAligned = false;      // OAM data phase — has prefetched (TriCNES: OAMDMA_Aligned)
        static bool dmaFirstCycleOam = false;   // First cycle of OAM DMA (TriCNES: FirstCycleOfOAMDMA)
        static byte dmaOamInternalBus = 0;      // OAM read data latch (TriCNES: OAM_InternalBus)
        static byte dmaOamAddr = 0;             // OAM source low byte (TriCNES: DMAAddress)

        // DMC DMA
        static bool dmcDmaRunning = false;      // DMC DMA fetch pending (TriCNES: DoDMCDMA)
        static bool dmcDmaHalt = false;         // DMC halt flag (TriCNES: DMCDMA_Halt)

        // Shared DMA state
        static ushort dmaPrevReadAddress = 0;   // Last DMA address (for $4016/$4017 tracking)
        static bool dmaReadSkipBusUpdate;       // $4015 bus conflict: don't update cpubus
        static bool dmaEnableInternalRegReads = false; // Captured at DMA start: CPU was in $4000-$401F

        // Master Clock timing (TriCNES model: per-master-clock execution)
        // NTSC: 21,477,272.73 Hz — CPU = master ÷ 12, PPU = master ÷ 4 (3:1)
        // PAL:  26,601,714 Hz   — CPU = master ÷ 16, PPU = master ÷ 5 (3.2:1)
        static long cpuCycleCount = 7;

        // Per-master-clock dividers (TriCNES: CPUClock/PPUClock countdown timers)
        // Count DOWN to 0, component executes when counter reaches 0, then resets.
        static int mcCpuClock = 0;    // TriCNES: defaults to 0 (CPU fires on first tick)
        static int mcPpuClock = 0;    // PPU: 4→0 (full step at 0, half step at 2)
        static bool mcApuPutCycle = false; // M2 phase (toggles every APU/CPU step)

        // Called at every site that changes statusframeint, apuintflag, statusdmcint, or statusmapperint
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateIRQLine()
        {
            irqLineCurrent = (statusframeint && !apuintflag) || statusdmcint || statusmapperint;
        }

        // ── Per-cycle DMA dispatch (TriCNES _6502() DMA gate model) ──
        // Called from MasterClockTick CPU gate — executes exactly ONE DMA cycle and returns.
        // PPU advances naturally via MasterClockTick (no StartCpuCycle needed).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void DmaOneCycle()
        {
            // TriCNES gate: cancel DMC if status disabled (line 3974 gate equivalent)
            if (dmcDmaRunning && !dmcStatusEnabled && !dmcDelayedEnable && !dmcImplicitAbortActive)
            {
                dmcDmaRunning = false;
                dmcDmaHalt = false;
                if (!spriteDmaTransfer) return;
            }

            // Absorb deferred abort (from dmcStopTransfer)
            if (dmcAbortDma)
            {
                dmcDmaRunning = false;
                dmcAbortDma = false;
                dmcDmaHalt = false;
                if (!spriteDmaTransfer) return;
            }

            // SH* opcodes: DMA during critical cycle makes H invisible (TriCNES: IgnoreH)
            if ((opcode == 0x93 && operationCycle == 4) ||
                (opcode == 0x9B && operationCycle == 3) ||
                (opcode == 0x9C && operationCycle == 3) ||
                (opcode == 0x9E && operationCycle == 3) ||
                (opcode == 0x9F && operationCycle == 3))
                ignoreH = true;

            // First cycle of OAM DMA: set halt if on get cycle (TriCNES: FirstCycleOfOAMDMA)
            if (spriteDmaTransfer && dmaFirstCycleOam)
            {
                dmaFirstCycleOam = false;
                if (!mcApuPutCycle)
                    dmaOamHalt = true;
            }

            if (mcApuPutCycle)  // ── Put cycle — OAM has priority ──
            {
                if (dmcDmaRunning && spriteDmaTransfer)
                {
                    if (dmcDmaHalt && dmaOamHalt)     DmaDummyRead();    // Both halted: OAM priority (dummy)
                    else if (!dmaOamHalt && dmcDmaHalt) OamDmaPut();      // OAM active, DMC halted
                    else if (dmaOamHalt && !dmcDmaHalt) DmaDummyRead();   // OAM halted, DMC put = dummy
                    else                                OamDmaPut();      // Both active: OAM priority
                }
                else if (dmcDmaRunning) DmaDummyRead(); // DMC put is always dummy read
                else if (dmaOamHalt)    DmaDummyRead(); // OAM halted
                else                    OamDmaPut();    // OAM active
            }
            else  // ── Get cycle — DMC has priority ──
            {
                if (dmcDmaRunning && spriteDmaTransfer)
                {
                    if (dmcDmaHalt && dmaOamHalt)       DmaDummyRead();                      // Both halted: DMC priority (dummy)
                    else if (!dmaOamHalt && dmcDmaHalt)  OamDmaGet(dmaEnableInternalRegReads); // OAM active, DMC halted
                    else if (dmaOamHalt && !dmcDmaHalt)  DmcDmaGet(dmaEnableInternalRegReads); // DMC active, OAM halted
                    else                                 DmcDmaGet(dmaEnableInternalRegReads); // Both active: DMC priority
                }
                else if (dmcDmaRunning)
                {
                    if (dmcDmaHalt) DmaDummyRead();
                    else            DmcDmaGet(dmaEnableInternalRegReads);
                }
                else
                {
                    if (dmaOamHalt) DmaDummyRead();
                    else            OamDmaGet(dmaEnableInternalRegReads);
                }

                // Clear halt flags after get cycle (TriCNES model)
                dmcDmaHalt = false;
                dmaOamHalt = false;
            }

            // TriCNES: clear implicit abort after each DMA cycle (line 8758-8761)
            // Implicit abort gives a 1-cycle phantom DMA (halt only) when no samples left.
            if (dmcImplicitAbortActive)
            {
                dmcImplicitAbortActive = false;
                if (dmcDmaRunning && dmcsamplesleft == 0)
                {
                    dmcDmaRunning = false;
                    dmcDmaHalt = false;
                }
            }
        }

        // ── DMA helper functions ──

        // Dummy read from current CPU bus address (halt/alignment cycles)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void DmaDummyRead()
        {
            if (cpuBusAddr != 0x4016 && cpuBusAddr != 0x4017)
            {
                ppu2007SM = 9;
                mem_read_fun[cpuBusAddr](cpuBusAddr);
            }
        }

        // OAM DMA Get — read source byte into latch (TriCNES: OAMDMA_Get)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void OamDmaGet(bool enableInternalRegReads)
        {
            ushort srcAddr = (ushort)(spriteDmaOffset * 0x100 + dmaOamAddr);
            cpuBusAddr = srcAddr;
            dmaOamInternalBus = ProcessDmaRead(srcAddr, enableInternalRegReads);
            cpubus = dmaOamInternalBus;
            dmaOamAligned = true;
        }

        // OAM DMA Put — write latched byte to OAM $2004 (TriCNES: OAMDMA_Put)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void OamDmaPut()
        {
            if (dmaOamAligned)
            {
                cpuBusAddr = 0x2004;
                spr_ram[spr_ram_add++] = dmaOamInternalBus;
                dmaOamAddr++;
                if (dmaOamAddr == 0) // Overflow: 256 bytes transferred → DMA complete
                {
                    spriteDmaTransfer = false;
                    dmaOamAligned = false;
                }
            }
            else
            {
                DmaDummyRead(); // Alignment cycle (not yet in data phase)
            }
        }

        // DMC DMA Get — read sample byte, completes in one cycle (TriCNES: DMCDMA_Get)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void DmcDmaGet(bool enableInternalRegReads)
        {
            ushort dmcReadAddr = (ushort)dmcaddr;
            byte val = ProcessDmaRead(dmcReadAddr, enableInternalRegReads);
            if (!dmaReadSkipBusUpdate) cpubus = val;
            dmcDmaRunning = false;
            dmcDmaHalt = false;
            dmcAbortDma = false;
            dmcSetReadBuffer(val);
            dmcDmaCooldown = 2; // TriCNES: CannotRunDMCDMARightNow
            dmaOamAligned = false; // TriCNES: reset OAM alignment after DMC interleave
        }

        // ── DMA read with bus conflict handling ──

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
