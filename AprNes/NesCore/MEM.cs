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
        // Per-master-clock dividers (TriCNES: CPUClock/PPUClock countdown timers)
        // Count DOWN to 0, component executes when counter reaches 0, then resets.
        static int mcCpuClock = 12;   // TriCNES: CPU fires after 12 MC (not immediately)
        static int mcPpuClock = 4;    // TriCNES: PPU fires after 4 MC (not immediately)
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
            if (opcode >= 0x93)
            {
                if (operationCycle == 3 && (opcode == 0x9B || opcode == 0x9C || opcode == 0x9E || opcode == 0x9F))
                    ignoreH = true;
                else if (operationCycle == 4 && opcode == 0x93)
                    ignoreH = true;
            }

            // FirstCycleOfOAMDMA: set halt if on GET cycle
            if (dmaFirstCycleOam && spriteDmaTransfer)
            {
                dmaFirstCycleOam = false;
                if (!mcApuPutCycle)
                    dmaOamHalt = true;
            }

            // ── PUT cycle — OAM has priority ──
            if (mcApuPutCycle)
            {
                if (spriteDmaTransfer && !dmaOamHalt)        OamDmaPut();
                else if (dmcDmaRunning && !dmcDmaHalt)       DmcDmaPut();
                // WARNING: OamDmaHalted() and DmcDmaHalted() are currently identical (DmaFetch(addressBus)).
                // If either is changed in the future, this merge MUST be split back to separate calls.
                else if (spriteDmaTransfer | dmcDmaRunning)  DmcDmaHalted();
            }
            // ── GET cycle — DMC has priority ──
            else
            {
                if (dmcDmaRunning && !dmcDmaHalt)            DmcDmaGet();
                else if (spriteDmaTransfer && !dmaOamHalt)   OamDmaGet();
                // WARNING: same merge as PUT — split if OamDmaHalted/DmcDmaHalted diverge.
                else if (dmcDmaRunning | spriteDmaTransfer)  DmcDmaHalted();

                dmcDmaHalt = false;
                dmaOamHalt = false;
            }

            // TriCNES: implicit abort — after each DMA cycle, if implicit abort active,
            // clear flag and cancel DMC if no samples left (1-cycle phantom DMA)
            if (dmcImplicitAbortActive)
            {
                dmcImplicitAbortActive = false;
                if (dmcDmaRunning & (dmcsamplesleft == 0))
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
                val = mem_read_page[addr >> 13](addr);
                dataPinsNotFloating = true;
            }
            else if (addr < 0x2000)
            {
                val = NES_MEM[addr & 0x7FF];
                dataPinsNotFloating = true;
            }
            else if (addr < 0x4000)
            {
                val = mem_read_page[addr >> 13](addr); // PPU $2000-$3FFF
                dataPinsNotFloating = true;
            }
            else if (addr >= 0x4020)
            {
                val = mem_read_page[addr >> 13](addr); // Mapper $4020+
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
                    // DMA read of $4015: bit5 open bus comes from the EXTERNAL bus (the DMA's own
                    // data bus value), NOT internalBus. A CPU LDA $4015 (apu_r_4015) uses internalBus;
                    // a DMA fetch of $4015 uses external. (AC P14 "APU Register Activation" code 7.)
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
                byte mask = (byte)(0xFFE3FFFF >> ((spr_ram_add & 3) << 3));
                spr_ram[spr_ram_add++] = (byte)(dmaOamInternalBus & mask);
                cpubus = dmaOamInternalBus;
                if (++dmaOamAddr == 0)
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

        // ── 8-page memory dispatch table ──
        // NES's 64 KB bus is indexed by (addr >> 13), yielding 8 pages of 8 KB each.
        // Page 0: $0000-$1FFF  RAM (mirrored every 2 KB)
        // Page 1: $2000-$3FFF  PPU registers (mirrored every 8 bytes)
        // Page 2: $4000-$5FFF  APU / joypad / open-bus / mapper expansion (internal dispatch)
        // Page 3: $6000-$7FFF  Mapper WRAM
        // Page 4-7: $8000-$FFFF  Mapper PRG
        //
        // Replaces the previous 65536-entry table (only 6 unique handlers were being
        // replicated across 65536 slots). The 8-slot table fits in one cache line.
#if NET10_0_OR_GREATER
        // .NET 10: unmanaged native function pointer table. `calli`, no bounds check.
        static delegate*<ushort, byte>*       mem_read_page  = null;
        static delegate*<ushort, byte, void>* mem_write_page = null;
#else
        // .NET Framework 4.8.1: managed delegate arrays (bounds check present but cheap).
        static Action<ushort, byte>[] mem_write_page = null;
        static Func<ushort, byte>[]   mem_read_page  = null;
#endif

        // ── Static helpers (replace previously-lambda bodies) ──
        // Needed for .NET 10 fp binding (&Name syntax requires static target).
        static byte Read_NesRam(ushort addr) { return NES_MEM[addr & 0x7ff]; }
        static void Write_NesRam(ushort addr, byte val) { NES_MEM[addr & 0x7ff] = val; }
        static byte Read_OpenBus(ushort addr) { return cpubus; }
        static void Write_NoOp(ushort addr, byte val) { }

        // ── Static wrappers for mapper instance methods ──
        // IMapper methods live on MapperObj (static field). fp requires static target,
        // so we forward through trivial static wrappers. JIT inlines these for free.
        static byte Wrap_MapperR_ExpansionROM(ushort addr) => MapperObj.MapperR_ExpansionROM(addr);
        static byte Wrap_MapperR_RAM(ushort addr) => MapperObj.MapperR_RAM(addr);
        static byte Wrap_MapperR_RPG(ushort addr) => MapperObj.MapperR_RPG(addr);
        static void Wrap_MapperW_ExpansionROM(ushort addr, byte val) => MapperObj.MapperW_ExpansionROM(addr, val);
        static void Wrap_MapperW_RAM(ushort addr, byte val) => MapperObj.MapperW_RAM(addr, val);
        static void Wrap_MapperW_PRG(ushort addr, byte val) => MapperObj.MapperW_PRG(addr, val);

        // ── Page 2 ($4000-$5FFF): mixed region needing internal sub-dispatch ──
        // $4000-$401F → APU / joypad / frame counter (IO_read/write)
        // $4020-$40FF → open bus (read) / no-op (write)
        // $4100-$5FFF → mapper expansion ROM
        // First branch is predictable (~99% of traffic goes to $4000-$4017).
        static byte Read_Page2(ushort addr)
        {
            if (addr < 0x4020) return IO_read(addr);
            if (addr < 0x4100) return Read_OpenBus(addr);
            return Wrap_MapperR_ExpansionROM(addr);
        }
        static void Write_Page2(ushort addr, byte val)
        {
            if (addr < 0x4020) { IO_write(addr, val); return; }
            if (addr < 0x4100) return; // $4020-$40FF open bus: write ignored
            Wrap_MapperW_ExpansionROM(addr, val);
        }

        static void init_function()
        {
#if NET10_0_OR_GREATER
            // Allocate once. 8 slots × 8 bytes = 64 bytes, one cache line.
            if (mem_write_page == null)
                mem_write_page = (delegate*<ushort, byte, void>*)AllocUnmanaged(8 * sizeof(delegate*<ushort, byte, void>));
            if (mem_read_page == null)
                mem_read_page  = (delegate*<ushort, byte>*)AllocUnmanaged(8 * sizeof(delegate*<ushort, byte>));

            mem_read_page[0] = &Read_NesRam;
            mem_read_page[1] = &IO_read;
            mem_read_page[2] = &Read_Page2;
            mem_read_page[3] = &Wrap_MapperR_RAM;
            mem_read_page[4] = &Wrap_MapperR_RPG;
            mem_read_page[5] = &Wrap_MapperR_RPG;
            mem_read_page[6] = &Wrap_MapperR_RPG;
            mem_read_page[7] = &Wrap_MapperR_RPG;

            mem_write_page[0] = &Write_NesRam;
            mem_write_page[1] = &IO_write;
            mem_write_page[2] = &Write_Page2;
            mem_write_page[3] = &Wrap_MapperW_RAM;
            mem_write_page[4] = &Wrap_MapperW_PRG;
            mem_write_page[5] = &Wrap_MapperW_PRG;
            mem_write_page[6] = &Wrap_MapperW_PRG;
            mem_write_page[7] = &Wrap_MapperW_PRG;
#else
            mem_write_page = new Action<ushort, byte>[8];
            mem_read_page  = new Func<ushort, byte>[8];

            mem_read_page[0] = Read_NesRam;
            mem_read_page[1] = IO_read;
            mem_read_page[2] = Read_Page2;
            mem_read_page[3] = Wrap_MapperR_RAM;
            mem_read_page[4] = Wrap_MapperR_RPG;
            mem_read_page[5] = Wrap_MapperR_RPG;
            mem_read_page[6] = Wrap_MapperR_RPG;
            mem_read_page[7] = Wrap_MapperR_RPG;

            mem_write_page[0] = Write_NesRam;
            mem_write_page[1] = IO_write;
            mem_write_page[2] = Write_Page2;
            mem_write_page[3] = Wrap_MapperW_RAM;
            mem_write_page[4] = Wrap_MapperW_PRG;
            mem_write_page[5] = Wrap_MapperW_PRG;
            mem_write_page[6] = Wrap_MapperW_PRG;
            mem_write_page[7] = Wrap_MapperW_PRG;
#endif
        }
    }
}
