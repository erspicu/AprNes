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

        // (ProcessDmaRead removed — DMA now uses simple Fetch like TriCNES)

        // Master Clock timing (TriCNES model: per-master-clock execution)
        // NTSC: 21,477,272.73 Hz — CPU = master ÷ 12, PPU = master ÷ 4 (3:1)
        // PAL:  26,601,714 Hz   — CPU = master ÷ 16, PPU = master ÷ 5 (3.2:1)
        static long cpuCycleCount = 0; // TriCNES: defaults to 0

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
        // TriCNES _6502() DMA dispatch — exact port
        // Gate condition checked in MasterClockTick before calling this.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void DmaOneCycle()
        {
            // SH* opcodes: DMA during critical cycle makes H invisible
            if ((opcode == 0x93 && operationCycle == 4) ||
                (opcode == 0x9B && operationCycle == 3) ||
                (opcode == 0x9C && operationCycle == 3) ||
                (opcode == 0x9E && operationCycle == 3) ||
                (opcode == 0x9F && operationCycle == 3))
                ignoreH = true;

            // FirstCycleOfOAMDMA: set halt if on GET cycle
            if (spriteDmaTransfer && dmaFirstCycleOam)
            {
                dmaFirstCycleOam = false;
                if (!mcApuPutCycle)
                    dmaOamHalt = true;
            }

            // ── PUT cycle (APU_PutCycle == true) — OAM has priority ──
            if (mcApuPutCycle)
            {
                if (dmcDmaRunning && spriteDmaTransfer)
                {
                    if (dmcDmaHalt && dmaOamHalt)       OamDmaHalted();
                    else if (!dmaOamHalt && dmcDmaHalt)  OamDmaPut();
                    else if (dmaOamHalt && !dmcDmaHalt)  DmcDmaPut();
                    else                                 OamDmaPut();
                }
                else if (dmcDmaRunning)
                {
                    if (dmcDmaHalt) DmcDmaHalted();
                    else            DmcDmaPut();
                }
                else
                {
                    if (dmaOamHalt) OamDmaHalted();
                    else            OamDmaPut();
                }
            }
            // ── GET cycle (APU_PutCycle == false) — DMC has priority ──
            else
            {
                if (dmcDmaRunning && spriteDmaTransfer)
                {
                    if (dmcDmaHalt && dmaOamHalt)       DmcDmaHalted();
                    else if (!dmaOamHalt && dmcDmaHalt)  OamDmaGet();
                    else if (dmaOamHalt && !dmcDmaHalt)  DmcDmaGet();
                    else                                 DmcDmaGet();
                }
                else if (dmcDmaRunning)
                {
                    if (dmcDmaHalt) DmcDmaHalted();
                    else            DmcDmaGet();
                }
                else
                {
                    if (dmaOamHalt) OamDmaHalted();
                    else            OamDmaGet();
                }

                // Clear halt flags after GET cycle
                dmcDmaHalt = false;
                dmaOamHalt = false;
            }

            // TriCNES: implicit abort — after each DMA cycle, if implicit abort active,
            // clear flag and cancel DMC if no samples left (1-cycle phantom DMA)
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

        // ── DMA helper functions (TriCNES exact port) ──

        // TriCNES: dataPinsAreNotFloating — tracks whether the data bus is actively driven.
        // Set true when reading from RAM (<$2000) or ROM (>=$8000), false otherwise.
        // Used for $4016/$4017 masking during OAM DMA: only mask when bus is driven.
        static bool dataPinsNotFloating = false;

        // DMA bus read — TriCNES Fetch() exact port
        // Main path: ROM/RAM/PPU through handlers; $4000-$401F → open bus (MapperFetch)
        // Bus conflict: addressBus gates APU chip; addr & 0x1F selects register
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte DmaFetch(ushort addr)
        {
            dataPinsNotFloating = false;
            byte val;

            // ── Main read path (TriCNES Fetch) ──
            if (addr >= 0x8000)
            {
                val = mem_read_fun[addr](addr);
                dataPinsNotFloating = true;
            }
            else if (addr < 0x2000)
            {
                val = NES_MEM[addr & 0x7FF];
                dataPinsNotFloating = true;
            }
            else if (addr < 0x4000)
            {
                val = mem_read_fun[addr](addr); // PPU $2000-$3FFF
                dataPinsNotFloating = true;
            }
            else if (addr >= 0x4020)
            {
                val = mem_read_fun[addr](addr); // Mapper $4020+
            }
            else
            {
                // $4000-$401F: open bus (TriCNES MapperFetch → no APU side effects)
                val = cpubus;
            }

            // ── Bus conflict (TriCNES Fetch line 9058) ──
            if (addressBus >= 0x4000 && addressBus <= 0x401F)
            {
                byte reg = (byte)(addr & 0x1F);
                if (reg == 0x15)
                {
                    byte status = (byte)(val & 0x20);
                    if (statusdmcint)   status |= 0x80;
                    if (statusframeint) status |= 0x40;
                    if (dmcsamplesleft > 0 && dmcDelayedEnable) status |= 0x10;
                    if (lengthctr[3] > 0) status |= 0x08;
                    if (lengthctr[2] > 0) status |= 0x04;
                    if (lengthctr[1] > 0) status |= 0x02;
                    if (lengthctr[0] > 0) status |= 0x01;
                    clearingFrameInterrupt = true;
                    cpubus = val;
                    return status;
                }
                else if (reg == 0x16 || reg == 0x17)
                {
                    byte ctrlData;
                    if (reg == 0x16)
                    {
                        ctrlData = (byte)(((P1_ShiftRegister & 0x80) != 0 ? 1 : 0) | (val & 0xE0));
                        P1_ShiftCounter = 2;
                    }
                    else
                    {
                        ctrlData = (byte)(((P2_ShiftRegister & 0x80) != 0 ? 1 : 0) | (val & 0xE0));
                        P2_ShiftCounter = 2;
                    }
                    controllerStrobed = false;
                    if (spriteDmaTransfer && dataPinsNotFloating)
                        { cpubus = val; return val; }
                    val = ctrlData;
                }
            }

            cpubus = val;
            return val;
        }

        // TriCNES: Fetch(addressBus) — read from CPU address bus (PC, not last access target)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void OamDmaHalted()  { DmaFetch(addressBus); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void DmcDmaHalted()  { DmaFetch(addressBus); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void DmcDmaPut()     { DmaFetch(addressBus); }

        // TriCNES: OAMDMA_Get — read source byte into latch
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void OamDmaGet()
        {
            ushort srcAddr = (ushort)(spriteDmaOffset * 0x100 + dmaOamAddr);
            dmaOamAligned = true;
            dmaOamInternalBus = DmaFetch(srcAddr);
        }

        // TriCNES: OAMDMA_Put — write latched byte to OAM via $2004
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void OamDmaPut()
        {
            if (dmaOamAligned)
            {
                // TriCNES: Store(OAM_InternalBus, 0x2004) — goes through $2004 write path
                // Attribute byte (offset 2) is masked with 0xE3 (bits 2-4 don't exist)
                byte dmaVal = dmaOamInternalBus;
                if ((spr_ram_add & 3) == 2) dmaVal &= 0xE3;
                spr_ram[spr_ram_add++] = dmaVal;
                dmaOamAddr++;
                if (dmaOamAddr == 0)
                {
                    spriteDmaTransfer = false;
                    dmaOamAligned = false;
                }
            }
            else
            {
                DmaFetch(addressBus); // alignment cycle: Fetch(addressBus)
            }
        }

        // TriCNES: DMCDMA_Get — read one sample byte, complete DMC DMA
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void DmcDmaGet()
        {
            ushort dmcReadAddr = (ushort)dmcaddr;
            byte val = DmaFetch(dmcReadAddr);
            dmcDmaRunning = false;
            dmaOamAligned = false;
            dmcDmaCooldown = 2;
            dmcSetReadBuffer(val);
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
