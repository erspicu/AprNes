using System;
using System.Runtime.CompilerServices;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        static byte r_A = 0, r_X = 0, r_Y = 0, r_SP = 0xFD, flagN = 0, flagV = 0, flagD = 0, flagI = 1, flagZ = 0, flagC = 0;
        static ushort r_PC = 0;
        static byte opcode;
        static unsafe delegate*<void>[] opFnPtrs = new delegate*<void>[256];

        static public bool exit = false;
        static bool nmi_pending = false;
        static bool irq_pending = false;
        static bool irqLinePrev = false;
        static bool irqLineCurrent = false;
        static public bool statusmapperint = false;
        static int nmi_trace_count = 0;

        // Per-cycle state machine state
        static byte operationCycle = 0;   // 0 = opcode fetch, 1..N = subsequent cycles
        static ushort addressBus = 0;     // current address on bus
        static byte dl = 0;              // data latch (intermediate value between cycles)
        static ushort temporaryAddress;   // used for branch/page-cross calculations
        static byte specialBus;           // used by JSR/JMP ind
        static byte H;                    // high byte for SH*/SHA illegals
        static bool ignoreH;             // DMA during SH* critical cycle → H=0xFF
        static bool fixHighByte;          // for abs,X/Y page cross tracking

        // Interrupt flags for per-cycle model
        static bool doNMI = false;
        static bool doIRQ = false;
        static bool doReset = false;
        static bool doBRK = false;

        // Headless mode (console test runner)
        static public bool HeadlessMode = false;

        // Debug logging
        static public System.IO.StreamWriter dbgLog;
        static public int dbgCount = 0;
        static public int dbgMaxConfig = 15000;
        static public string DebugLogPath = @"c:\ai_project\AprNes\emu_debug.log";
        static public void dbgInit()
        {
            if (!HeadlessMode) { dbgLog = null; return; }
            string pidLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "aprnes_debug_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".log");
            if (DebugLogPath != null && DebugLogPath.Length > 0)
                pidLog = DebugLogPath;
            try {
                if (System.IO.File.Exists(pidLog))
                    System.IO.File.Delete(pidLog);
                dbgLog = System.IO.File.AppendText(pidLog);
            } catch {
                dbgLog = null;
                return;
            }
            dbgCount = 0;
        }
        static public void dbgWrite(string s)
        {
            if (dbgLog != null && dbgCount < dbgMaxConfig)
            {
                dbgLog.WriteLine(s);
                dbgCount++;
                if (dbgCount >= dbgMaxConfig) { dbgLog.Flush(); dbgLog.Close(); dbgLog = null; }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte GetFlag()
        {
            return (byte)((flagN << 7) | (flagV << 6) | (flagD << 3) | (flagI << 2) | (flagZ << 1) | flagC);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SetFlag(byte flag)
        {
            flagN = (byte)((flag & 0x80) >> 7);
            flagV = (byte)((flag & 0x40) >> 6);
            flagD = (byte)((flag & 0x8) >> 3);
            flagI = (byte)((flag & 0x4) >> 2);
            flagZ = (byte)((flag & 0x2) >> 1);
            flagC = (byte)(flag & 0x1);
        }

        static bool softreset = false;
        public static void SoftReset()
        {
            softreset = true;
            doReset = true;
        }

        // --- Per-cycle bus access functions ---
        // Each call advances the clock (StartCpuCycle/EndCpuCycle), matching Mem_r/Mem_w behavior.
        // CpuRead also triggers DMA via ProcessPendingDma when dmaNeedHalt is set.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte CpuRead(ushort addr)
        {
            cpuBusAddr = addr;
            StartCpuCycle();
            if (dmaNeedHalt) ProcessPendingDma(addr);
            byte val;
            if (addr < 0x2000)
            {
                val = NES_MEM[addr & 0x7FF];
                cpubus = val;
            }
            else
            {
                val = mem_read_fun[addr](addr);
                if (addr != 0x4015) cpubus = val;
            }
            EndCpuCycle();
            return val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CpuWrite(ushort addr, byte val)
        {
            cpuBusAddr = addr;
            StartCpuCycle();
            // TriCNES line 8758: implicit abort DMA cancelled if delayed by write cycle
            // "The 1-cycle DMA should not get delayed by a write cycle, rather it just shouldn't occur"
            if (dmcImplicitAbortActive && dmaNeedHalt)
            {
                dmcImplicitAbortActive = false;
                dmcDmaRunning = false;
                dmcNeedDummyRead = false;
                dmaNeedHalt = false;
            }
            cpubus = val;
            mem_write_fun[addr](addr, val);
            EndCpuCycle();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte CpuReadZP(byte addr)
        {
            cpuBusAddr = addr;
            StartCpuCycle();
            if (dmaNeedHalt) ProcessPendingDma(addr);
            byte val = NES_MEM[addr];
            cpubus = val;
            EndCpuCycle();
            return val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CpuWriteZP(byte addr, byte val)
        {
            cpuBusAddr = addr;
            StartCpuCycle();
            NES_MEM[addr] = val;
            cpubus = val;
            EndCpuCycle();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void StackPush(byte val)
        {
            CpuWrite((ushort)(0x100 | r_SP), val);
            r_SP--;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte StackPull()
        {
            r_SP++;
            return CpuRead((ushort)(0x100 | r_SP));
        }

        // PollInterrupts: no-op — interrupt detection moved to run() loop in Main.cs
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PollInterrupts() { }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PollInterrupts_CantDisableIRQ() { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CompleteOperation()
        {
            operationCycle = 0xFF; // will be incremented to 0 at end of cpu_step_one_cycle
            addressBus = r_PC;
        }

        // --- Operation helpers ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SetNZ(byte val)
        {
            flagN = (byte)((val & 0x80) >> 7);
            flagZ = (val == 0) ? (byte)1 : (byte)0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Op_ADC(byte val)
        {
            int result = r_A + val + flagC;
            flagV = (byte)((((r_A ^ val) & 0x80) == 0 && ((r_A ^ result) & 0x80) != 0) ? 1 : 0);
            flagC = (result > 0xFF) ? (byte)1 : (byte)0;
            r_A = (byte)result;
            SetNZ(r_A);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Op_SBC(byte val)
        {
            int result = r_A - val - (1 - flagC);
            flagV = (byte)((((r_A ^ val) & 0x80) != 0 && ((r_A ^ result) & 0x80) != 0) ? 1 : 0);
            flagC = (result >= 0) ? (byte)1 : (byte)0;
            r_A = (byte)result;
            SetNZ(r_A);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Op_AND(byte val)
        {
            r_A &= val;
            SetNZ(r_A);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Op_ORA(byte val)
        {
            r_A |= val;
            SetNZ(r_A);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Op_EOR(byte val)
        {
            r_A ^= val;
            SetNZ(r_A);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Op_CMP(byte val, byte reg)
        {
            flagC = (reg >= val) ? (byte)1 : (byte)0;
            flagZ = (reg == val) ? (byte)1 : (byte)0;
            flagN = (byte)((((byte)(reg - val)) & 0x80) >> 7);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Op_ASL_mem(ushort addr)
        {
            flagC = (byte)((dl & 0x80) >> 7);
            dl <<= 1;
            SetNZ(dl);
            CpuWrite(addr, dl);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Op_LSR_mem(ushort addr)
        {
            flagC = (byte)(dl & 1);
            dl >>= 1;
            SetNZ(dl);
            CpuWrite(addr, dl);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Op_ROL_mem(ushort addr)
        {
            byte oldC = flagC;
            flagC = (byte)((dl & 0x80) >> 7);
            dl = (byte)((dl << 1) | oldC);
            SetNZ(dl);
            CpuWrite(addr, dl);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Op_ROR_mem(ushort addr)
        {
            byte oldC = flagC;
            flagC = (byte)(dl & 1);
            dl = (byte)((dl >> 1) | (oldC << 7));
            SetNZ(dl);
            CpuWrite(addr, dl);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Op_INC_mem(ushort addr)
        {
            dl++;
            SetNZ(dl);
            CpuWrite(addr, dl);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Op_DEC_mem(ushort addr)
        {
            dl--;
            SetNZ(dl);
            CpuWrite(addr, dl);
        }

        // Illegal op helpers
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Op_SLO(ushort addr)
        {
            Op_ASL_mem(addr);
            Op_ORA(dl);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Op_RLA(ushort addr)
        {
            Op_ROL_mem(addr);
            Op_AND(dl);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Op_SRE(ushort addr)
        {
            Op_LSR_mem(addr);
            Op_EOR(dl);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Op_RRA(ushort addr)
        {
            Op_ROR_mem(addr);
            Op_ADC(dl);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Op_ISC(ushort addr)
        {
            Op_INC_mem(addr);
            Op_SBC(dl);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Op_DCP(ushort addr)
        {
            Op_DEC_mem(addr);
            Op_CMP(dl, r_A);
        }

        // --- Addressing mode helpers ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void GetImmediate()
        {
            dl = CpuRead(r_PC);
            r_PC++;
            addressBus = r_PC;
        }

        static void GetAddressAbsolute()
        {
            if (operationCycle == 1)
            {
                dl = CpuRead(r_PC);
            }
            else
            {
                addressBus = (ushort)(dl | (CpuRead(r_PC) << 8));
            }
            r_PC++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void GetAddressZeroPage()
        {
            addressBus = CpuRead(r_PC);
            r_PC++;
        }

        static void GetAddressIndOffX()
        {
            switch (operationCycle)
            {
                case 1:
                    addressBus = CpuRead(r_PC);
                    r_PC++;
                    break;
                case 2:
                    CpuReadZP((byte)addressBus); // dummy read
                    addressBus = (byte)(addressBus + r_X);
                    break;
                case 3:
                    dl = CpuReadZP((byte)addressBus);
                    break;
                case 4:
                    addressBus = (ushort)(dl | (CpuReadZP((byte)(addressBus + 1)) << 8));
                    break;
            }
        }

        static void GetAddressIndOffY(bool optionalExtraCycle)
        {
            if (optionalExtraCycle)
            {
                switch (operationCycle)
                {
                    case 1:
                        addressBus = CpuRead(r_PC);
                        r_PC++;
                        break;
                    case 2:
                        dl = CpuReadZP((byte)addressBus);
                        break;
                    case 3:
                        addressBus = (ushort)(dl | (CpuReadZP((byte)(addressBus + 1)) << 8));
                        temporaryAddress = addressBus;
                        H = (byte)(addressBus >> 8);
                        if (((temporaryAddress + r_Y) & 0xFF00) == (temporaryAddress & 0xFF00))
                        {
                            operationCycle++; // skip next cycle
                        }
                        addressBus = (ushort)((addressBus & 0xFF00) | ((addressBus + r_Y) & 0xFF));
                        break;
                    case 4:
                        dl = CpuRead(addressBus); // dummy read
                        H = (byte)(addressBus >> 8);
                        H++;
                        addressBus += 0x100;
                        break;
                }
            }
            else
            {
                switch (operationCycle)
                {
                    case 1:
                        addressBus = CpuRead(r_PC);
                        r_PC++;
                        break;
                    case 2:
                        dl = CpuReadZP((byte)addressBus);
                        break;
                    case 3:
                        addressBus = (ushort)(dl | (CpuReadZP((byte)(addressBus + 1)) << 8));
                        temporaryAddress = addressBus;
                        addressBus = (ushort)((addressBus & 0xFF00) | ((addressBus + r_Y) & 0xFF));
                        break;
                    case 4:
                        dl = CpuRead(addressBus); // dummy read
                        H = (byte)(addressBus >> 8);
                        H++;
                        if (((temporaryAddress + r_Y) & 0xFF00) != (temporaryAddress & 0xFF00))
                        {
                            addressBus += 0x100;
                        }
                        break;
                }
            }
        }

        static void GetAddressZPOffX()
        {
            if (operationCycle == 1)
            {
                addressBus = CpuRead(r_PC);
                r_PC++;
            }
            else
            {
                dl = CpuReadZP((byte)addressBus); // dummy read
                addressBus = (byte)(addressBus + r_X);
            }
        }

        static void GetAddressZPOffY()
        {
            if (operationCycle == 1)
            {
                addressBus = CpuRead(r_PC);
                r_PC++;
            }
            else
            {
                dl = CpuReadZP((byte)addressBus); // dummy read
                addressBus = (byte)(addressBus + r_Y);
            }
        }

        static void GetAddressAbsOffX(bool optionalExtraCycle)
        {
            if (optionalExtraCycle)
            {
                switch (operationCycle)
                {
                    case 1:
                        dl = CpuRead(r_PC);
                        r_PC++;
                        break;
                    case 2:
                        addressBus = (ushort)(dl | (CpuRead(r_PC) << 8));
                        temporaryAddress = addressBus;
                        H = (byte)(addressBus >> 8);
                        if (((temporaryAddress + r_X) & 0xFF00) == (temporaryAddress & 0xFF00))
                        {
                            operationCycle++; // skip next cycle
                            fixHighByte = false;
                        }
                        else
                        {
                            fixHighByte = true;
                        }
                        addressBus = (ushort)((addressBus & 0xFF00) | ((addressBus + r_X) & 0xFF));
                        r_PC++;
                        break;
                    case 3:
                        dl = CpuRead(addressBus); // dummy read with wrong high byte
                        H = (byte)(addressBus >> 8);
                        H++;
                        if (fixHighByte)
                        {
                            addressBus += 0x100;
                        }
                        break;
                    case 4:
                        dl = CpuRead(addressBus); // read from final address
                        break;
                }
            }
            else
            {
                switch (operationCycle)
                {
                    case 1:
                        dl = CpuRead(r_PC);
                        r_PC++;
                        break;
                    case 2:
                        addressBus = (ushort)(dl | (CpuRead(r_PC) << 8));
                        temporaryAddress = addressBus;
                        addressBus = (ushort)((addressBus & 0xFF00) | ((addressBus + r_X) & 0xFF));
                        r_PC++;
                        break;
                    case 3:
                        dl = CpuRead(addressBus); // dummy read with possibly wrong high byte
                        H = (byte)(addressBus >> 8);
                        H++;
                        if (((temporaryAddress + r_X) & 0xFF00) != (temporaryAddress & 0xFF00))
                        {
                            addressBus += 0x100;
                        }
                        break;
                    case 4:
                        dl = CpuRead(addressBus); // read from final address
                        break;
                }
            }
        }

        static void GetAddressAbsOffY(bool optionalExtraCycle)
        {
            if (optionalExtraCycle)
            {
                switch (operationCycle)
                {
                    case 1:
                        dl = CpuRead(r_PC);
                        r_PC++;
                        break;
                    case 2:
                        addressBus = (ushort)(dl | (CpuRead(r_PC) << 8));
                        temporaryAddress = addressBus;
                        H = (byte)(addressBus >> 8);
                        if (((temporaryAddress + r_Y) & 0xFF00) == (temporaryAddress & 0xFF00))
                        {
                            operationCycle++; // skip next cycle
                            fixHighByte = false;
                        }
                        else
                        {
                            fixHighByte = true;
                        }
                        addressBus = (ushort)((addressBus & 0xFF00) | ((addressBus + r_Y) & 0xFF));
                        r_PC++;
                        break;
                    case 3:
                        dl = CpuRead(addressBus); // dummy read with wrong high byte
                        H = (byte)(addressBus >> 8);
                        H++;
                        if (fixHighByte)
                        {
                            addressBus += 0x100;
                        }
                        break;
                    case 4:
                        dl = CpuRead(addressBus); // read from final address
                        break;
                }
            }
            else
            {
                switch (operationCycle)
                {
                    case 1:
                        dl = CpuRead(r_PC);
                        r_PC++;
                        break;
                    case 2:
                        addressBus = (ushort)(dl | (CpuRead(r_PC) << 8));
                        temporaryAddress = addressBus;
                        addressBus = (ushort)((addressBus & 0xFF00) | ((addressBus + r_Y) & 0xFF));
                        r_PC++;
                        break;
                    case 3:
                        dl = CpuRead(addressBus); // dummy read with possibly wrong high byte
                        H = (byte)(addressBus >> 8);
                        H++;
                        if (((temporaryAddress + r_Y) & 0xFF00) != (temporaryAddress & 0xFF00))
                        {
                            addressBus += 0x100;
                        }
                        break;
                    case 4:
                        dl = CpuRead(addressBus); // read from final address
                        break;
                }
            }
        }

        // --- Branch helper ---
        static bool branchIrqSaved; // saved irqLinePrev for branch-taken-no-cross

        static void DoBranch(bool condition)
        {
            switch (operationCycle)
            {
                case 1:
                    GetImmediate();
                    if (!condition)
                    {
                        CompleteOperation();
                    }
                    else
                    {
                        branchIrqSaved = irqLinePrev; // save before taken-dummy tick
                    }
                    break;
                case 2:
                    CpuRead(addressBus); // dummy read
                    temporaryAddress = (ushort)(r_PC + ((dl >= 0x80) ? -(256 - dl) : dl));
                    r_PC = (ushort)((r_PC & 0xFF00) | (byte)((r_PC & 0xFF) + dl));
                    addressBus = r_PC;
                    if ((temporaryAddress & 0xFF00) == (r_PC & 0xFF00))
                    {
                        irqLinePrev = branchIrqSaved; // restore: IRQ penultimate = pre-branch state
                        CompleteOperation();
                    }
                    break;
                case 3:
                    CpuRead(addressBus); // dummy read (page fix)
                    r_PC = (ushort)((r_PC & 0xFF) | (temporaryAddress & 0xFF00));
                    CompleteOperation();
                    break;
            }
        }

        // --- HLT (JAM) helper ---
        static void DoHLT()
        {
            switch (operationCycle)
            {
                case 1:
                    dl = CpuRead(addressBus);
                    break;
                case 2:
                    addressBus = 0xFFFF;
                    CpuRead(addressBus);
                    break;
                case 3:
                case 4:
                    addressBus = 0xFFFE;
                    CpuRead(addressBus);
                    break;
                case 5:
                    addressBus = 0xFFFF;
                    CpuRead(addressBus);
                    break;
                case 6:
                    addressBus = 0xFFFF;
                    CpuRead(addressBus);
                    operationCycle = 5; // loop infinitely
                    break;
            }
        }

        // ============================================================
        // Main per-cycle CPU step function
        // Called once per CPU cycle from the master clock loop.
        // ============================================================
        static void cpu_step_one_cycle()
        {
            if (operationCycle == 0)
            {
                // --- Cycle 0: Opcode Fetch ---
                ignoreH = false;
                addressBus = r_PC;
                opcode = CpuRead(addressBus);

                if (softreset || doReset)
                {
                    opcode = 0x00;
                    doReset = true;
                    softreset = false;
                }
                else if (doNMI)
                {
                    opcode = 0x00; // BRK with NMI behavior
                }
                else if (doIRQ)
                {
                    opcode = 0x00; // BRK with IRQ behavior
                }
                else if (opcode == 0x00)
                {
                    doBRK = true;
                }

                // Pre-instruction trace
                if (nmi_trace_count > 0)
                    dbgWrite("TRACE: PC=$" + r_PC.ToString("X4") + " op=$" + opcode.ToString("X2")
                        + " A=$" + r_A.ToString("X2") + " X=$" + r_X.ToString("X2")
                        + " Y=$" + r_Y.ToString("X2") + " SP=$" + r_SP.ToString("X2")
                        + " P=$" + (GetFlag() | 0x20).ToString("X2"));

                if (!doNMI && !doIRQ && !doReset)
                {
                    r_PC++;
                    addressBus = r_PC;
                }

                operationCycle++;
            }
            else
            {
                // --- Cycles 1..N: Execute based on opcode ---
                opFnPtrs[opcode]();
                operationCycle++;
            }
        }

        // === Named static op methods (for delegate* function pointer table) ===

        static void Op_Default() { CpuRead(addressBus); CompleteOperation(); }

        // === BRK / NMI / IRQ / RESET ===
        static void Op_00() {
            switch (operationCycle)
            {
                case 1:
                    if (!doBRK) { CpuRead(addressBus); }
                    else { GetImmediate(); }
                    break;
                case 2:
                    if (!doReset) StackPush((byte)(r_PC >> 8));
                    else { CpuRead((ushort)(0x100 | r_SP)); r_SP--; }
                    break;
                case 3:
                    if (!doReset) StackPush((byte)r_PC);
                    else { CpuRead((ushort)(0x100 | r_SP)); r_SP--; }
                    break;
                case 4:
                    if (!doReset)
                    {
                        byte pushed = (byte)(GetFlag() | 0x20 | (doBRK ? 0x10 : 0x00));
                        StackPush(pushed);
                    }
                    else { CpuRead((ushort)(0x100 | r_SP)); r_SP--; }
                    if (nmi_pending) { doNMI = true; nmi_pending = false; }
                    break;
                case 5:
                    if (doNMI) r_PC = (ushort)((r_PC & 0xFF00) | CpuRead(0xFFFA));
                    else if (doReset) r_PC = (ushort)((r_PC & 0xFF00) | CpuRead(0xFFFC));
                    else r_PC = (ushort)((r_PC & 0xFF00) | CpuRead(0xFFFE));
                    break;
                case 6:
                    if (doNMI) r_PC = (ushort)((r_PC & 0xFF) | (CpuRead(0xFFFB) << 8));
                    else if (doReset) r_PC = (ushort)((r_PC & 0xFF) | (CpuRead(0xFFFD) << 8));
                    else r_PC = (ushort)((r_PC & 0xFF) | (CpuRead(0xFFFF) << 8));
                    if (doNMI) { dbgWrite("NMI_PUSH: PC=$" + r_PC.ToString("X4") + " SP=$" + r_SP.ToString("X2")); }
                    if (doReset)
                    {
                        Console.WriteLine("soft reset !");
                        nmi_pending = false; nmi_delay_cycle = -1; nmi_output_prev = false;
                        irq_pending = false; statusmapperint = false;
                        apuSoftReset(); strobeWritePending = 0; P1_LastWrite = 0;
                    }
                    CompleteOperation();
                    doReset = false; doNMI = false; doIRQ = false; doBRK = false; flagI = 1;
                    break;
            }
        }

        // === ORA ===
        static void Op_09() { PollInterrupts(); GetImmediate(); Op_ORA(dl); CompleteOperation(); }
        static void Op_05() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_15() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_0D() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_1D() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                case 4: PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_19() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                case 4: PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_01() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                case 5: PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_11() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                case 5: PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); break; }
        }

        // === AND ===
        static void Op_29() { PollInterrupts(); GetImmediate(); Op_AND(dl); CompleteOperation(); }
        static void Op_25() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_35() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_2D() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_3D() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                case 4: PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_39() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                case 4: PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_21() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                case 5: PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_31() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                case 5: PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); break; }
        }

        // === EOR ===
        static void Op_49() { PollInterrupts(); GetImmediate(); Op_EOR(dl); CompleteOperation(); }
        static void Op_45() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_55() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_4D() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_5D() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                case 4: PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_59() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                case 4: PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_41() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                case 5: PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_51() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                case 5: PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); break; }
        }

        // === ADC ===
        static void Op_69() { PollInterrupts(); GetImmediate(); Op_ADC(dl); CompleteOperation(); }
        static void Op_65() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_75() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_6D() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_7D() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                case 4: PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_79() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                case 4: PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_61() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                case 5: PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_71() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                case 5: PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); break; }
        }

        // === SBC ===
        static void Op_E9_SBC_Imm() { PollInterrupts(); GetImmediate(); Op_SBC(dl); CompleteOperation(); }
        static void Op_E5() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_F5() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_ED() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_FD() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                case 4: PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_F9() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                case 4: PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_E1() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                case 5: PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); break; }
        }
        static void Op_F1() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                case 5: PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); break; }
        }

        // === CMP ===
        static void Op_C9() { PollInterrupts(); GetImmediate(); Op_CMP(dl, r_A); CompleteOperation(); }
        static void Op_C5() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); }
        }
        static void Op_D5() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); break; }
        }
        static void Op_CD() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); break; }
        }
        static void Op_DD() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                case 4: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); break; }
        }
        static void Op_D9() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                case 4: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); break; }
        }
        static void Op_C1() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                case 5: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); break; }
        }
        static void Op_D1() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                case 5: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); break; }
        }

        // === CPX ===
        static void Op_E0() { PollInterrupts(); GetImmediate(); Op_CMP(dl, r_X); CompleteOperation(); }
        static void Op_E4() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { PollInterrupts(); Op_CMP(CpuRead(addressBus), r_X); CompleteOperation(); }
        }
        static void Op_EC() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_X); CompleteOperation(); break; }
        }

        // === CPY ===
        static void Op_C0() { PollInterrupts(); GetImmediate(); Op_CMP(dl, r_Y); CompleteOperation(); }
        static void Op_C4() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { PollInterrupts(); Op_CMP(CpuRead(addressBus), r_Y); CompleteOperation(); }
        }
        static void Op_CC() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_Y); CompleteOperation(); break; }
        }

        // === LDA ===
        static void Op_A9() { PollInterrupts(); GetImmediate(); r_A = dl; SetNZ(r_A); CompleteOperation(); }
        static void Op_A5() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); }
        }
        static void Op_B5() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break; }
        }
        static void Op_AD() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break; }
        }
        static void Op_BD() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                case 4: PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break; }
        }
        static void Op_B9() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                case 4: PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break; }
        }
        static void Op_A1() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                case 5: PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break; }
        }
        static void Op_B1() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                case 5: PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break; }
        }

        // === LDX ===
        static void Op_A2() { PollInterrupts(); GetImmediate(); r_X = dl; SetNZ(r_X); CompleteOperation(); }
        static void Op_A6() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { PollInterrupts(); r_X = CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); }
        }
        static void Op_B6() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffY(); break;
                case 3: PollInterrupts(); r_X = CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); break; }
        }
        static void Op_AE() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: PollInterrupts(); r_X = CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); break; }
        }
        static void Op_BE() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                case 4: PollInterrupts(); r_X = CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); break; }
        }

        // === LDY ===
        static void Op_A0() { PollInterrupts(); GetImmediate(); r_Y = dl; SetNZ(r_Y); CompleteOperation(); }
        static void Op_A4() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { PollInterrupts(); r_Y = CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); }
        }
        static void Op_B4() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: PollInterrupts(); r_Y = CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); break; }
        }
        static void Op_AC() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: PollInterrupts(); r_Y = CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); break; }
        }
        static void Op_BC() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                case 4: PollInterrupts(); r_Y = CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); break; }
        }

        // === STA ===
        static void Op_85() {
            if (operationCycle == 1) { GetAddressZeroPage(); }
            else { PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); }
        }
        static void Op_95() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); break; }
        }
        static void Op_8D() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); break; }
        }
        static void Op_9D() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(false); break;
                case 4: PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); break; }
        }
        static void Op_99() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(false); break;
                case 4: PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); break; }
        }
        static void Op_81() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                case 5: PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); break; }
        }
        static void Op_91() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                case 5: PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); break; }
        }

        // === STX ===
        static void Op_86() {
            if (operationCycle == 1) { GetAddressZeroPage(); }
            else { PollInterrupts(); CpuWrite(addressBus, r_X); CompleteOperation(); }
        }
        static void Op_96() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffY(); break;
                case 3: PollInterrupts(); CpuWrite(addressBus, r_X); CompleteOperation(); break; }
        }
        static void Op_8E() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: PollInterrupts(); CpuWrite(addressBus, r_X); CompleteOperation(); break; }
        }

        // === STY ===
        static void Op_84() {
            if (operationCycle == 1) { GetAddressZeroPage(); }
            else { PollInterrupts(); CpuWrite(addressBus, r_Y); CompleteOperation(); }
        }
        static void Op_94() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: PollInterrupts(); CpuWrite(addressBus, r_Y); CompleteOperation(); break; }
        }
        static void Op_8C() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: PollInterrupts(); CpuWrite(addressBus, r_Y); CompleteOperation(); break; }
        }

        // === BIT ===
        static void Op_24() {
            switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                case 2: PollInterrupts(); dl = CpuRead(addressBus);
                    flagZ = (byte)(((r_A & dl) == 0) ? 1 : 0);
                    flagN = (byte)((dl & 0x80) >> 7);
                    flagV = (byte)((dl & 0x40) >> 6);
                    CompleteOperation(); break; }
        }
        static void Op_2C() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: PollInterrupts(); dl = CpuRead(addressBus);
                    flagZ = (byte)(((r_A & dl) == 0) ? 1 : 0);
                    flagN = (byte)((dl & 0x80) >> 7);
                    flagV = (byte)((dl & 0x40) >> 6);
                    CompleteOperation(); break; }
        }

        // === ASL ===
        static void Op_0A() {
            PollInterrupts(); CpuRead(addressBus);
            flagC = (byte)((r_A & 0x80) >> 7); r_A <<= 1; SetNZ(r_A);
            CompleteOperation();
        }
        static void Op_06() {
            switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                case 2: dl = CpuRead(addressBus); break;
                case 3: CpuWrite(addressBus, dl); break;
                case 4: PollInterrupts(); Op_ASL_mem(addressBus); CompleteOperation(); break; }
        }
        static void Op_16() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_ASL_mem(addressBus); CompleteOperation(); break; }
        }
        static void Op_0E() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_ASL_mem(addressBus); CompleteOperation(); break; }
        }
        static void Op_1E() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); break;
                case 5: CpuWrite(addressBus, dl); break;
                case 6: PollInterrupts(); Op_ASL_mem(addressBus); CompleteOperation(); break; }
        }

        // === LSR ===
        static void Op_4A() {
            PollInterrupts(); CpuRead(addressBus);
            flagC = (byte)(r_A & 1); r_A >>= 1; SetNZ(r_A);
            CompleteOperation();
        }
        static void Op_46() {
            switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                case 2: dl = CpuRead(addressBus); break;
                case 3: CpuWrite(addressBus, dl); break;
                case 4: PollInterrupts(); Op_LSR_mem(addressBus); CompleteOperation(); break; }
        }
        static void Op_56() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_LSR_mem(addressBus); CompleteOperation(); break; }
        }
        static void Op_4E() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_LSR_mem(addressBus); CompleteOperation(); break; }
        }
        static void Op_5E() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); break;
                case 5: CpuWrite(addressBus, dl); break;
                case 6: PollInterrupts(); Op_LSR_mem(addressBus); CompleteOperation(); break; }
        }

        // === ROL ===
        static void Op_2A() {
            PollInterrupts(); CpuRead(addressBus);
            { byte oc = flagC; flagC = (byte)((r_A & 0x80) >> 7); r_A = (byte)((r_A << 1) | oc); SetNZ(r_A); }
            CompleteOperation();
        }
        static void Op_26() {
            switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                case 2: dl = CpuRead(addressBus); break;
                case 3: CpuWrite(addressBus, dl); break;
                case 4: PollInterrupts(); Op_ROL_mem(addressBus); CompleteOperation(); break; }
        }
        static void Op_36() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_ROL_mem(addressBus); CompleteOperation(); break; }
        }
        static void Op_2E() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_ROL_mem(addressBus); CompleteOperation(); break; }
        }
        static void Op_3E() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); break;
                case 5: CpuWrite(addressBus, dl); break;
                case 6: PollInterrupts(); Op_ROL_mem(addressBus); CompleteOperation(); break; }
        }

        // === ROR ===
        static void Op_6A() {
            PollInterrupts(); CpuRead(addressBus);
            { byte oc = flagC; flagC = (byte)(r_A & 1); r_A = (byte)((r_A >> 1) | (oc << 7)); SetNZ(r_A); }
            CompleteOperation();
        }
        static void Op_66() {
            switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                case 2: dl = CpuRead(addressBus); break;
                case 3: CpuWrite(addressBus, dl); break;
                case 4: PollInterrupts(); Op_ROR_mem(addressBus); CompleteOperation(); break; }
        }
        static void Op_76() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_ROR_mem(addressBus); CompleteOperation(); break; }
        }
        static void Op_6E() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_ROR_mem(addressBus); CompleteOperation(); break; }
        }
        static void Op_7E() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); break;
                case 5: CpuWrite(addressBus, dl); break;
                case 6: PollInterrupts(); Op_ROR_mem(addressBus); CompleteOperation(); break; }
        }

        // === INC ===
        static void Op_E6() {
            switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                case 2: dl = CpuRead(addressBus); break;
                case 3: CpuWrite(addressBus, dl); break;
                case 4: PollInterrupts(); Op_INC_mem(addressBus); CompleteOperation(); break; }
        }
        static void Op_F6() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_INC_mem(addressBus); CompleteOperation(); break; }
        }
        static void Op_EE() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_INC_mem(addressBus); CompleteOperation(); break; }
        }
        static void Op_FE() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); break;
                case 5: CpuWrite(addressBus, dl); break;
                case 6: PollInterrupts(); Op_INC_mem(addressBus); CompleteOperation(); break; }
        }

        // === DEC ===
        static void Op_C6() {
            switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                case 2: dl = CpuRead(addressBus); break;
                case 3: CpuWrite(addressBus, dl); break;
                case 4: PollInterrupts(); Op_DEC_mem(addressBus); CompleteOperation(); break; }
        }
        static void Op_D6() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_DEC_mem(addressBus); CompleteOperation(); break; }
        }
        static void Op_CE() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_DEC_mem(addressBus); CompleteOperation(); break; }
        }
        static void Op_DE() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); break;
                case 5: CpuWrite(addressBus, dl); break;
                case 6: PollInterrupts(); Op_DEC_mem(addressBus); CompleteOperation(); break; }
        }

        // === INX / INY / DEX / DEY ===
        static void Op_E8() { PollInterrupts(); CpuRead(addressBus); r_X++; SetNZ(r_X); CompleteOperation(); }
        static void Op_C8() { PollInterrupts(); CpuRead(addressBus); r_Y++; SetNZ(r_Y); CompleteOperation(); }
        static void Op_CA() { PollInterrupts(); CpuRead(addressBus); r_X--; SetNZ(r_X); CompleteOperation(); }
        static void Op_88() { PollInterrupts(); CpuRead(addressBus); r_Y--; SetNZ(r_Y); CompleteOperation(); }

        // === Transfer ===
        static void Op_AA() { PollInterrupts(); r_X = r_A; CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); }
        static void Op_8A() { PollInterrupts(); r_A = r_X; CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); }
        static void Op_A8() { PollInterrupts(); r_Y = r_A; CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); }
        static void Op_98() { PollInterrupts(); r_A = r_Y; CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); }
        static void Op_BA() { PollInterrupts(); r_X = r_SP; CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); }
        static void Op_9A() { PollInterrupts(); r_SP = r_X; CpuRead(addressBus); CompleteOperation(); }

        // === Flag instructions ===
        static void Op_18() { PollInterrupts(); CpuRead(addressBus); flagC = 0; CompleteOperation(); }
        static void Op_38() { PollInterrupts(); CpuRead(addressBus); flagC = 1; CompleteOperation(); }
        static void Op_58() { PollInterrupts(); CpuRead(addressBus); flagI = 0; CompleteOperation(); }
        static void Op_78() { PollInterrupts(); CpuRead(addressBus); flagI = 1; CompleteOperation(); }
        static void Op_D8() { PollInterrupts(); CpuRead(addressBus); flagD = 0; CompleteOperation(); }
        static void Op_F8() { PollInterrupts(); CpuRead(addressBus); flagD = 1; CompleteOperation(); }
        static void Op_B8() { PollInterrupts(); CpuRead(addressBus); flagV = 0; CompleteOperation(); }

        // === Stack instructions ===
        static void Op_48() {
            switch (operationCycle) { case 1: CpuRead(addressBus); break;
                case 2: PollInterrupts(); StackPush(r_A); CompleteOperation(); break; }
        }
        static void Op_08() {
            switch (operationCycle) { case 1: CpuRead(addressBus); break;
                case 2: PollInterrupts(); StackPush((byte)(GetFlag() | 0x30)); CompleteOperation(); break; }
        }
        static void Op_68() {
            switch (operationCycle) { case 1: CpuRead(addressBus); break;
                case 2: CpuRead((ushort)(0x100 | r_SP)); r_SP++; break;
                case 3: PollInterrupts(); r_A = CpuRead((ushort)(0x100 | r_SP)); SetNZ(r_A); CompleteOperation(); break; }
        }
        static void Op_28() {
            switch (operationCycle) { case 1: CpuRead(addressBus); break;
                case 2: CpuRead((ushort)(0x100 | r_SP)); r_SP++; break;
                case 3: PollInterrupts(); SetFlag(CpuRead((ushort)(0x100 | r_SP))); CompleteOperation(); break; }
        }

        // === Branches ===
        static void Op_10() { DoBranch(flagN == 0); }
        static void Op_30() { DoBranch(flagN != 0); }
        static void Op_50() { DoBranch(flagV == 0); }
        static void Op_70() { DoBranch(flagV != 0); }
        static void Op_90() { DoBranch(flagC == 0); }
        static void Op_B0() { DoBranch(flagC != 0); }
        static void Op_D0() { DoBranch(flagZ == 0); }
        static void Op_F0() { DoBranch(flagZ != 0); }

        // === JMP ===
        static void Op_4C() {
            if (operationCycle == 1) GetAddressAbsolute();
            else { PollInterrupts(); GetAddressAbsolute(); r_PC = addressBus; CompleteOperation(); }
        }
        static void Op_6C() {
            switch (operationCycle) {
                case 1: case 2: GetAddressAbsolute(); break;
                case 3: specialBus = CpuRead(addressBus); break;
                case 4: PollInterrupts();
                    dl = CpuRead((ushort)((addressBus & 0xFF00) | (byte)(addressBus + 1)));
                    r_PC = (ushort)((dl << 8) | specialBus);
                    CompleteOperation(); break;
            }
        }

        // === JSR ===
        static void Op_20() {
            switch (operationCycle) {
                case 1:
                    addressBus = r_PC; dl = CpuRead(addressBus); r_PC++; break;
                case 2:
                    addressBus = (ushort)(0x100 | r_SP); specialBus = dl;
                    CpuRead(addressBus); break;
                case 3:
                    CpuWrite(addressBus, (byte)(r_PC >> 8));
                    addressBus = (ushort)((byte)(addressBus - 1) | 0x100); break;
                case 4:
                    CpuWrite(addressBus, (byte)r_PC);
                    addressBus = (ushort)((byte)(addressBus - 1) | 0x100);
                    r_SP = (byte)addressBus; break;
                case 5:
                    PollInterrupts();
                    r_PC = (ushort)((CpuRead(r_PC) << 8) | specialBus);
                    CompleteOperation(); break;
            }
        }

        // === RTS ===
        static void Op_60() {
            switch (operationCycle) {
                case 1: GetImmediate(); break;
                case 2:
                    addressBus = (ushort)(0x100 | r_SP); CpuRead(addressBus);
                    addressBus = (ushort)((byte)(addressBus + 1) | 0x100); break;
                case 3:
                    dl = CpuRead(addressBus); r_PC = (ushort)((r_PC & 0xFF00) | dl);
                    addressBus = (ushort)((byte)(addressBus + 1) | 0x100); break;
                case 4:
                    dl = CpuRead(addressBus); r_PC = (ushort)((r_PC & 0xFF) | (dl << 8)); break;
                case 5:
                    PollInterrupts(); r_SP = (byte)addressBus; GetImmediate(); CompleteOperation(); break;
            }
        }

        // === RTI ===
        static void Op_40() {
            switch (operationCycle) {
                case 1: GetImmediate(); break;
                case 2:
                    addressBus = (ushort)(0x100 | r_SP); CpuRead(addressBus);
                    addressBus = (ushort)((byte)(addressBus + 1) | 0x100); break;
                case 3:
                    { byte status = CpuRead(addressBus); SetFlag(status); addressBus = (ushort)((byte)(addressBus + 1) | 0x100); } break;
                case 4:
                    dl = CpuRead(addressBus); r_PC = (ushort)((r_PC & 0xFF00) | dl);
                    addressBus = (ushort)((byte)(addressBus + 1) | 0x100); break;
                case 5:
                    PollInterrupts(); dl = CpuRead(addressBus);
                    r_PC = (ushort)((r_PC & 0xFF) | (dl << 8)); r_SP = (byte)addressBus;
                    CompleteOperation(); break;
            }
        }

        // === NOP (implied) ===
        static void Op_NOP() { PollInterrupts(); CpuRead(addressBus); CompleteOperation(); }

        // === DOP Immediate ===
        static void Op_DOP_Imm() { PollInterrupts(); GetImmediate(); CompleteOperation(); }

        // === DOP ZeroPage ===
        static void Op_DOP_ZP() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { PollInterrupts(); CpuRead(addressBus); CompleteOperation(); }
        }

        // === DOP ZeroPage,X ===
        static void Op_DOP_ZPX() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: PollInterrupts(); CpuRead(addressBus); CompleteOperation(); break; }
        }

        // === TOP Absolute ===
        static void Op_0C() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: PollInterrupts(); CpuRead(addressBus); CompleteOperation(); break; }
        }

        // === TOP Absolute,X ===
        static void Op_TOP_AbsX() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                case 4: PollInterrupts(); CpuRead(addressBus); CompleteOperation(); break; }
        }

        // === SLO ===
        static void Op_07() {
            switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                case 2: dl = CpuRead(addressBus); break;
                case 3: CpuWrite(addressBus, dl); break;
                case 4: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
        }
        static void Op_17() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
        }
        static void Op_0F() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
        }
        static void Op_1F() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); break;
                case 5: CpuWrite(addressBus, dl); break;
                case 6: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
        }
        static void Op_1B() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffY(false); break;
                case 5: CpuWrite(addressBus, dl); break;
                case 6: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
        }
        static void Op_03() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                case 5: dl = CpuRead(addressBus); break;
                case 6: CpuWrite(addressBus, dl); break;
                case 7: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
        }
        static void Op_13() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                case 5: dl = CpuRead(addressBus); break;
                case 6: CpuWrite(addressBus, dl); break;
                case 7: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
        }

        // === RLA ===
        static void Op_27() {
            switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                case 2: dl = CpuRead(addressBus); break;
                case 3: CpuWrite(addressBus, dl); break;
                case 4: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
        }
        static void Op_37() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
        }
        static void Op_2F() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
        }
        static void Op_3F() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); break;
                case 5: CpuWrite(addressBus, dl); break;
                case 6: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
        }
        static void Op_3B() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffY(false); break;
                case 5: CpuWrite(addressBus, dl); break;
                case 6: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
        }
        static void Op_23() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                case 5: dl = CpuRead(addressBus); break;
                case 6: CpuWrite(addressBus, dl); break;
                case 7: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
        }
        static void Op_33() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                case 5: dl = CpuRead(addressBus); break;
                case 6: CpuWrite(addressBus, dl); break;
                case 7: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
        }

        // === SRE ===
        static void Op_47() {
            switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                case 2: dl = CpuRead(addressBus); break;
                case 3: CpuWrite(addressBus, dl); break;
                case 4: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
        }
        static void Op_57() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
        }
        static void Op_4F() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
        }
        static void Op_5F() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); break;
                case 5: CpuWrite(addressBus, dl); break;
                case 6: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
        }
        static void Op_5B() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffY(false); break;
                case 5: CpuWrite(addressBus, dl); break;
                case 6: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
        }
        static void Op_43() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                case 5: dl = CpuRead(addressBus); break;
                case 6: CpuWrite(addressBus, dl); break;
                case 7: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
        }
        static void Op_53() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                case 5: dl = CpuRead(addressBus); break;
                case 6: CpuWrite(addressBus, dl); break;
                case 7: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
        }

        // === RRA ===
        static void Op_67() {
            switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                case 2: dl = CpuRead(addressBus); break;
                case 3: CpuWrite(addressBus, dl); break;
                case 4: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
        }
        static void Op_77() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
        }
        static void Op_6F() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
        }
        static void Op_7F() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); break;
                case 5: CpuWrite(addressBus, dl); break;
                case 6: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
        }
        static void Op_7B() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffY(false); break;
                case 5: CpuWrite(addressBus, dl); break;
                case 6: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
        }
        static void Op_63() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                case 5: dl = CpuRead(addressBus); break;
                case 6: CpuWrite(addressBus, dl); break;
                case 7: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
        }
        static void Op_73() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                case 5: dl = CpuRead(addressBus); break;
                case 6: CpuWrite(addressBus, dl); break;
                case 7: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
        }

        // === SAX ===
        static void Op_87() {
            if (operationCycle == 1) { GetAddressZeroPage(); }
            else { PollInterrupts(); CpuWrite(addressBus, (byte)(r_A & r_X)); CompleteOperation(); }
        }
        static void Op_97() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffY(); break;
                case 3: PollInterrupts(); CpuWrite(addressBus, (byte)(r_A & r_X)); CompleteOperation(); break; }
        }
        static void Op_8F() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: PollInterrupts(); CpuWrite(addressBus, (byte)(r_A & r_X)); CompleteOperation(); break; }
        }
        static void Op_83() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                case 5: PollInterrupts(); CpuWrite(addressBus, (byte)(r_A & r_X)); CompleteOperation(); break; }
        }

        // === LAX ===
        static void Op_A7() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { PollInterrupts(); r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); }
        }
        static void Op_B7() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffY(); break;
                case 3: PollInterrupts(); r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); break; }
        }
        static void Op_AF() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: PollInterrupts(); r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); break; }
        }
        static void Op_BF() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                case 4: PollInterrupts(); r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); break; }
        }
        static void Op_A3() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                case 5: PollInterrupts(); r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); break; }
        }
        static void Op_B3() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                case 5: PollInterrupts(); r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); break; }
        }

        // === DCP ===
        static void Op_C7() {
            switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                case 2: dl = CpuRead(addressBus); break;
                case 3: CpuWrite(addressBus, dl); break;
                case 4: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
        }
        static void Op_D7() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
        }
        static void Op_CF() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
        }
        static void Op_DF() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); break;
                case 5: CpuWrite(addressBus, dl); break;
                case 6: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
        }
        static void Op_DB() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffY(false); break;
                case 5: CpuWrite(addressBus, dl); break;
                case 6: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
        }
        static void Op_C3() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                case 5: dl = CpuRead(addressBus); break;
                case 6: CpuWrite(addressBus, dl); break;
                case 7: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
        }
        static void Op_D3() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                case 5: dl = CpuRead(addressBus); break;
                case 6: CpuWrite(addressBus, dl); break;
                case 7: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
        }

        // === ISC ===
        static void Op_E7() {
            switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                case 2: dl = CpuRead(addressBus); break;
                case 3: CpuWrite(addressBus, dl); break;
                case 4: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
        }
        static void Op_F7() {
            switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
        }
        static void Op_EF() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: dl = CpuRead(addressBus); break;
                case 4: CpuWrite(addressBus, dl); break;
                case 5: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
        }
        static void Op_FF() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); break;
                case 5: CpuWrite(addressBus, dl); break;
                case 6: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
        }
        static void Op_FB() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffY(false); break;
                case 5: CpuWrite(addressBus, dl); break;
                case 6: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
        }
        static void Op_E3() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                case 5: dl = CpuRead(addressBus); break;
                case 6: CpuWrite(addressBus, dl); break;
                case 7: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
        }
        static void Op_F3() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                case 5: dl = CpuRead(addressBus); break;
                case 6: CpuWrite(addressBus, dl); break;
                case 7: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
        }

        // === ANC ===
        static void Op_ANC() {
            PollInterrupts(); GetImmediate();
            r_A = (byte)(r_A & dl); flagC = (byte)((r_A & 0x80) >> 7); SetNZ(r_A);
            CompleteOperation();
        }

        // === ALR ===
        static void Op_4B() {
            PollInterrupts(); GetImmediate();
            r_A = (byte)(r_A & dl); flagC = (byte)(r_A & 1); r_A >>= 1; SetNZ(r_A);
            CompleteOperation();
        }

        // === ARR ===
        static void Op_6B() {
            PollInterrupts(); GetImmediate();
            r_A = (byte)(r_A & dl);
            { byte oc = flagC; flagC = (byte)(r_A & 1); r_A = (byte)((r_A >> 1) | (oc << 7)); }
            SetNZ(r_A);
            flagC = (byte)((r_A & 0x40) >> 6);
            flagV = (byte)((((r_A >> 5) ^ (r_A >> 6)) & 1));
            CompleteOperation();
        }

        // === SBX ===
        static void Op_CB() {
            PollInterrupts(); GetImmediate();
            { int tmp = (r_A & r_X) - dl; flagC = (tmp >= 0) ? (byte)1 : (byte)0; r_X = (byte)tmp; SetNZ(r_X); }
            CompleteOperation();
        }

        // === ANE ===
        static void Op_8B() {
            PollInterrupts(); GetImmediate();
            r_A = (byte)((r_A | 0xFF) & r_X & dl);
            SetNZ(r_A); CompleteOperation();
        }

        // === LXA ===
        static void Op_AB() {
            PollInterrupts(); GetImmediate();
            r_A = (byte)((r_A | 0xFF) & dl); r_X = r_A; SetNZ(r_X);
            CompleteOperation();
        }

        // === LAE ===
        static void Op_BB() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                case 4: PollInterrupts();
                    dl = CpuRead(addressBus); r_A = (byte)(dl & r_SP); r_X = r_A; r_SP = r_A; SetNZ(r_A);
                    CompleteOperation(); break; }
        }

        // === SHA (Ind),Y ===
        static void Op_93() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                case 5: PollInterrupts();
                    if ((temporaryAddress & 0xFF00) != (addressBus & 0xFF00))
                        addressBus = (ushort)((byte)addressBus | (((addressBus >> 8) & r_X) << 8));
                    if (ignoreH) H = 0xFF;
                    CpuWrite(addressBus, (byte)(r_A & (r_X | 0xF5) & H));
                    CompleteOperation(); break; }
        }

        // === SHA Abs,Y ===
        static void Op_9F() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(false); break;
                case 4: PollInterrupts();
                    if ((temporaryAddress & 0xFF00) != (addressBus & 0xFF00))
                        addressBus = (ushort)((byte)addressBus | (((addressBus >> 8) & r_X) << 8));
                    if (ignoreH) H = 0xFF;
                    CpuWrite(addressBus, (byte)(r_A & (r_X | 0xF5) & H));
                    CompleteOperation(); break; }
        }

        // === SHY Abs,X ===
        static void Op_9C() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(false); break;
                case 4: PollInterrupts();
                    if ((temporaryAddress & 0xFF00) != (addressBus & 0xFF00))
                        addressBus = (ushort)((byte)addressBus | (((addressBus >> 8) & r_Y) << 8));
                    if (ignoreH) H = 0xFF;
                    CpuWrite(addressBus, (byte)(r_Y & H));
                    CompleteOperation(); break; }
        }

        // === SHX Abs,Y ===
        static void Op_9E() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(false); break;
                case 4: PollInterrupts();
                    if ((temporaryAddress & 0xFF00) != (addressBus & 0xFF00))
                        addressBus = (ushort)((byte)addressBus | (((addressBus >> 8) & r_X) << 8));
                    if (ignoreH) H = 0xFF;
                    CpuWrite(addressBus, (byte)(r_X & H));
                    CompleteOperation(); break; }
        }

        // === SHS Abs,Y ===
        static void Op_9B() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(false); break;
                case 4: PollInterrupts();
                    if ((temporaryAddress & 0xFF00) != (addressBus & 0xFF00))
                        addressBus = (ushort)((byte)addressBus | (((addressBus >> 8) & r_X) << 8));
                    r_SP = (byte)(r_A & r_X);
                    if (ignoreH) H = 0xFF;
                    CpuWrite(addressBus, (byte)(r_A & (r_X | 0xF5) & H));
                    CompleteOperation(); break; }
        }

        // === HLT (JAM) ===
        static void Op_HLT() { DoHLT(); }

        static unsafe void InitOpHandlers()
        {
            // Default: treat unknown opcode as NOP-like
            for (int i = 0; i < 256; i++) opFnPtrs[i] = &Op_Default;

            // === BRK / NMI / IRQ / RESET ===
            opFnPtrs[0x00] = &Op_00;

            // === ORA ===
            opFnPtrs[0x09] = &Op_09;
            opFnPtrs[0x05] = &Op_05;
            opFnPtrs[0x15] = &Op_15;
            opFnPtrs[0x0D] = &Op_0D;
            opFnPtrs[0x1D] = &Op_1D;
            opFnPtrs[0x19] = &Op_19;
            opFnPtrs[0x01] = &Op_01;
            opFnPtrs[0x11] = &Op_11;

            // === AND ===
            opFnPtrs[0x29] = &Op_29;
            opFnPtrs[0x25] = &Op_25;
            opFnPtrs[0x35] = &Op_35;
            opFnPtrs[0x2D] = &Op_2D;
            opFnPtrs[0x3D] = &Op_3D;
            opFnPtrs[0x39] = &Op_39;
            opFnPtrs[0x21] = &Op_21;
            opFnPtrs[0x31] = &Op_31;

            // === EOR ===
            opFnPtrs[0x49] = &Op_49;
            opFnPtrs[0x45] = &Op_45;
            opFnPtrs[0x55] = &Op_55;
            opFnPtrs[0x4D] = &Op_4D;
            opFnPtrs[0x5D] = &Op_5D;
            opFnPtrs[0x59] = &Op_59;
            opFnPtrs[0x41] = &Op_41;
            opFnPtrs[0x51] = &Op_51;

            // === ADC ===
            opFnPtrs[0x69] = &Op_69;
            opFnPtrs[0x65] = &Op_65;
            opFnPtrs[0x75] = &Op_75;
            opFnPtrs[0x6D] = &Op_6D;
            opFnPtrs[0x7D] = &Op_7D;
            opFnPtrs[0x79] = &Op_79;
            opFnPtrs[0x61] = &Op_61;
            opFnPtrs[0x71] = &Op_71;

            // === SBC ===
            opFnPtrs[0xE9] = opFnPtrs[0xEB] = &Op_E9_SBC_Imm;
            opFnPtrs[0xE5] = &Op_E5;
            opFnPtrs[0xF5] = &Op_F5;
            opFnPtrs[0xED] = &Op_ED;
            opFnPtrs[0xFD] = &Op_FD;
            opFnPtrs[0xF9] = &Op_F9;
            opFnPtrs[0xE1] = &Op_E1;
            opFnPtrs[0xF1] = &Op_F1;

            // === CMP ===
            opFnPtrs[0xC9] = &Op_C9;
            opFnPtrs[0xC5] = &Op_C5;
            opFnPtrs[0xD5] = &Op_D5;
            opFnPtrs[0xCD] = &Op_CD;
            opFnPtrs[0xDD] = &Op_DD;
            opFnPtrs[0xD9] = &Op_D9;
            opFnPtrs[0xC1] = &Op_C1;
            opFnPtrs[0xD1] = &Op_D1;

            // === CPX ===
            opFnPtrs[0xE0] = &Op_E0;
            opFnPtrs[0xE4] = &Op_E4;
            opFnPtrs[0xEC] = &Op_EC;

            // === CPY ===
            opFnPtrs[0xC0] = &Op_C0;
            opFnPtrs[0xC4] = &Op_C4;
            opFnPtrs[0xCC] = &Op_CC;

            // === LDA ===
            opFnPtrs[0xA9] = &Op_A9;
            opFnPtrs[0xA5] = &Op_A5;
            opFnPtrs[0xB5] = &Op_B5;
            opFnPtrs[0xAD] = &Op_AD;
            opFnPtrs[0xBD] = &Op_BD;
            opFnPtrs[0xB9] = &Op_B9;
            opFnPtrs[0xA1] = &Op_A1;
            opFnPtrs[0xB1] = &Op_B1;

            // === LDX ===
            opFnPtrs[0xA2] = &Op_A2;
            opFnPtrs[0xA6] = &Op_A6;
            opFnPtrs[0xB6] = &Op_B6;
            opFnPtrs[0xAE] = &Op_AE;
            opFnPtrs[0xBE] = &Op_BE;

            // === LDY ===
            opFnPtrs[0xA0] = &Op_A0;
            opFnPtrs[0xA4] = &Op_A4;
            opFnPtrs[0xB4] = &Op_B4;
            opFnPtrs[0xAC] = &Op_AC;
            opFnPtrs[0xBC] = &Op_BC;

            // === STA ===
            opFnPtrs[0x85] = &Op_85;
            opFnPtrs[0x95] = &Op_95;
            opFnPtrs[0x8D] = &Op_8D;
            opFnPtrs[0x9D] = &Op_9D;
            opFnPtrs[0x99] = &Op_99;
            opFnPtrs[0x81] = &Op_81;
            opFnPtrs[0x91] = &Op_91;

            // === STX ===
            opFnPtrs[0x86] = &Op_86;
            opFnPtrs[0x96] = &Op_96;
            opFnPtrs[0x8E] = &Op_8E;

            // === STY ===
            opFnPtrs[0x84] = &Op_84;
            opFnPtrs[0x94] = &Op_94;
            opFnPtrs[0x8C] = &Op_8C;

            // === BIT ===
            opFnPtrs[0x24] = &Op_24;
            opFnPtrs[0x2C] = &Op_2C;

            // === ASL ===
            opFnPtrs[0x0A] = &Op_0A;
            opFnPtrs[0x06] = &Op_06;
            opFnPtrs[0x16] = &Op_16;
            opFnPtrs[0x0E] = &Op_0E;
            opFnPtrs[0x1E] = &Op_1E;

            // === LSR ===
            opFnPtrs[0x4A] = &Op_4A;
            opFnPtrs[0x46] = &Op_46;
            opFnPtrs[0x56] = &Op_56;
            opFnPtrs[0x4E] = &Op_4E;
            opFnPtrs[0x5E] = &Op_5E;

            // === ROL ===
            opFnPtrs[0x2A] = &Op_2A;
            opFnPtrs[0x26] = &Op_26;
            opFnPtrs[0x36] = &Op_36;
            opFnPtrs[0x2E] = &Op_2E;
            opFnPtrs[0x3E] = &Op_3E;

            // === ROR ===
            opFnPtrs[0x6A] = &Op_6A;
            opFnPtrs[0x66] = &Op_66;
            opFnPtrs[0x76] = &Op_76;
            opFnPtrs[0x6E] = &Op_6E;
            opFnPtrs[0x7E] = &Op_7E;

            // === INC ===
            opFnPtrs[0xE6] = &Op_E6;
            opFnPtrs[0xF6] = &Op_F6;
            opFnPtrs[0xEE] = &Op_EE;
            opFnPtrs[0xFE] = &Op_FE;

            // === DEC ===
            opFnPtrs[0xC6] = &Op_C6;
            opFnPtrs[0xD6] = &Op_D6;
            opFnPtrs[0xCE] = &Op_CE;
            opFnPtrs[0xDE] = &Op_DE;

            // === INX / INY / DEX / DEY ===
            opFnPtrs[0xE8] = &Op_E8;
            opFnPtrs[0xC8] = &Op_C8;
            opFnPtrs[0xCA] = &Op_CA;
            opFnPtrs[0x88] = &Op_88;

            // === Transfer ===
            opFnPtrs[0xAA] = &Op_AA;
            opFnPtrs[0x8A] = &Op_8A;
            opFnPtrs[0xA8] = &Op_A8;
            opFnPtrs[0x98] = &Op_98;
            opFnPtrs[0xBA] = &Op_BA;
            opFnPtrs[0x9A] = &Op_9A;

            // === Flag instructions ===
            opFnPtrs[0x18] = &Op_18;
            opFnPtrs[0x38] = &Op_38;
            opFnPtrs[0x58] = &Op_58;
            opFnPtrs[0x78] = &Op_78;
            opFnPtrs[0xD8] = &Op_D8;
            opFnPtrs[0xF8] = &Op_F8;
            opFnPtrs[0xB8] = &Op_B8;

            // === Stack instructions ===
            opFnPtrs[0x48] = &Op_48;
            opFnPtrs[0x08] = &Op_08;
            opFnPtrs[0x68] = &Op_68;
            opFnPtrs[0x28] = &Op_28;

            // === Branches ===
            opFnPtrs[0x10] = &Op_10;
            opFnPtrs[0x30] = &Op_30;
            opFnPtrs[0x50] = &Op_50;
            opFnPtrs[0x70] = &Op_70;
            opFnPtrs[0x90] = &Op_90;
            opFnPtrs[0xB0] = &Op_B0;
            opFnPtrs[0xD0] = &Op_D0;
            opFnPtrs[0xF0] = &Op_F0;

            // === JMP ===
            opFnPtrs[0x4C] = &Op_4C;
            opFnPtrs[0x6C] = &Op_6C;

            // === JSR ===
            opFnPtrs[0x20] = &Op_20;

            // === RTS ===
            opFnPtrs[0x60] = &Op_60;

            // === RTI ===
            opFnPtrs[0x40] = &Op_40;

            // === NOP (implied) ===
            opFnPtrs[0xEA] = opFnPtrs[0x1A] = opFnPtrs[0x3A] = opFnPtrs[0x5A] =
                opFnPtrs[0x7A] = opFnPtrs[0xDA] = opFnPtrs[0xFA] = &Op_NOP;

            // === DOP Immediate ===
            opFnPtrs[0x80] = opFnPtrs[0x82] = opFnPtrs[0x89] = opFnPtrs[0xC2] = opFnPtrs[0xE2] = &Op_DOP_Imm;

            // === DOP ZeroPage ===
            opFnPtrs[0x04] = opFnPtrs[0x44] = opFnPtrs[0x64] = &Op_DOP_ZP;

            // === DOP ZeroPage,X ===
            opFnPtrs[0x14] = opFnPtrs[0x34] = opFnPtrs[0x54] = opFnPtrs[0x74] =
                opFnPtrs[0xD4] = opFnPtrs[0xF4] = &Op_DOP_ZPX;

            // === TOP Absolute ===
            opFnPtrs[0x0C] = &Op_0C;

            // === TOP Absolute,X ===
            opFnPtrs[0x1C] = opFnPtrs[0x3C] = opFnPtrs[0x5C] = opFnPtrs[0x7C] =
                opFnPtrs[0xDC] = opFnPtrs[0xFC] = &Op_TOP_AbsX;

            // === SLO ===
            opFnPtrs[0x07] = &Op_07;
            opFnPtrs[0x17] = &Op_17;
            opFnPtrs[0x0F] = &Op_0F;
            opFnPtrs[0x1F] = &Op_1F;
            opFnPtrs[0x1B] = &Op_1B;
            opFnPtrs[0x03] = &Op_03;
            opFnPtrs[0x13] = &Op_13;

            // === RLA ===
            opFnPtrs[0x27] = &Op_27;
            opFnPtrs[0x37] = &Op_37;
            opFnPtrs[0x2F] = &Op_2F;
            opFnPtrs[0x3F] = &Op_3F;
            opFnPtrs[0x3B] = &Op_3B;
            opFnPtrs[0x23] = &Op_23;
            opFnPtrs[0x33] = &Op_33;

            // === SRE ===
            opFnPtrs[0x47] = &Op_47;
            opFnPtrs[0x57] = &Op_57;
            opFnPtrs[0x4F] = &Op_4F;
            opFnPtrs[0x5F] = &Op_5F;
            opFnPtrs[0x5B] = &Op_5B;
            opFnPtrs[0x43] = &Op_43;
            opFnPtrs[0x53] = &Op_53;

            // === RRA ===
            opFnPtrs[0x67] = &Op_67;
            opFnPtrs[0x77] = &Op_77;
            opFnPtrs[0x6F] = &Op_6F;
            opFnPtrs[0x7F] = &Op_7F;
            opFnPtrs[0x7B] = &Op_7B;
            opFnPtrs[0x63] = &Op_63;
            opFnPtrs[0x73] = &Op_73;

            // === SAX ===
            opFnPtrs[0x87] = &Op_87;
            opFnPtrs[0x97] = &Op_97;
            opFnPtrs[0x8F] = &Op_8F;
            opFnPtrs[0x83] = &Op_83;

            // === LAX ===
            opFnPtrs[0xA7] = &Op_A7;
            opFnPtrs[0xB7] = &Op_B7;
            opFnPtrs[0xAF] = &Op_AF;
            opFnPtrs[0xBF] = &Op_BF;
            opFnPtrs[0xA3] = &Op_A3;
            opFnPtrs[0xB3] = &Op_B3;

            // === DCP ===
            opFnPtrs[0xC7] = &Op_C7;
            opFnPtrs[0xD7] = &Op_D7;
            opFnPtrs[0xCF] = &Op_CF;
            opFnPtrs[0xDF] = &Op_DF;
            opFnPtrs[0xDB] = &Op_DB;
            opFnPtrs[0xC3] = &Op_C3;
            opFnPtrs[0xD3] = &Op_D3;

            // === ISC ===
            opFnPtrs[0xE7] = &Op_E7;
            opFnPtrs[0xF7] = &Op_F7;
            opFnPtrs[0xEF] = &Op_EF;
            opFnPtrs[0xFF] = &Op_FF;
            opFnPtrs[0xFB] = &Op_FB;
            opFnPtrs[0xE3] = &Op_E3;
            opFnPtrs[0xF3] = &Op_F3;

            // === ANC ===
            opFnPtrs[0x0B] = opFnPtrs[0x2B] = &Op_ANC;

            // === ALR ===
            opFnPtrs[0x4B] = &Op_4B;

            // === ARR ===
            opFnPtrs[0x6B] = &Op_6B;

            // === SBX ===
            opFnPtrs[0xCB] = &Op_CB;

            // === ANE ===
            opFnPtrs[0x8B] = &Op_8B;

            // === LXA ===
            opFnPtrs[0xAB] = &Op_AB;

            // === LAE ===
            opFnPtrs[0xBB] = &Op_BB;

            // === SHA (Ind),Y ===
            opFnPtrs[0x93] = &Op_93;

            // === SHA Abs,Y ===
            opFnPtrs[0x9F] = &Op_9F;

            // === SHY Abs,X ===
            opFnPtrs[0x9C] = &Op_9C;

            // === SHX Abs,Y ===
            opFnPtrs[0x9E] = &Op_9E;

            // === SHS Abs,Y ===
            opFnPtrs[0x9B] = &Op_9B;

            // === HLT (JAM) ===
            opFnPtrs[0x02] = opFnPtrs[0x12] = opFnPtrs[0x22] = opFnPtrs[0x32] =
                opFnPtrs[0x42] = opFnPtrs[0x52] = opFnPtrs[0x62] = opFnPtrs[0x72] =
                opFnPtrs[0x92] = opFnPtrs[0xB2] = opFnPtrs[0xD2] = opFnPtrs[0xF2] = &Op_HLT;
        }

        // Legacy cpu_step() wrapper - runs one full instruction using the per-cycle model
        static void cpu_step()
        {
            // Execute one full instruction — each CpuRead/CpuWrite advances the clock
            do
            {
                cpu_step_one_cycle();
            } while (operationCycle != 0);
        }
    }
}
