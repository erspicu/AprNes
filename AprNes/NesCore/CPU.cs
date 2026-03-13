using System;
using System.Runtime.CompilerServices;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        static byte r_A = 0, r_X = 0, r_Y = 0, r_SP = 0xFD, flagN = 0, flagV = 0, flagD = 0, flagI = 1, flagZ = 0, flagC = 0;
        static ushort r_PC = 0;
        static byte opcode;
        static Action[] opHandlers = new Action[256];

        static public bool exit = false;
        static bool nmi_pending = false;
        static bool irq_pending = false;
        static bool irqLinePrev = false;
        static bool irqLineCurrent = false;
        static public bool statusmapperint = false;
        static int nmi_trace_count = 0;

        // Per-cycle state machine state
        static byte operationCycle = 0;   // 0 = opcode fetch, 1..N = subsequent cycles
        static bool CPU_Read = true;      // true = read cycle, false = write cycle (for DMA halt logic)
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
            CPU_Read = true;
            cpuBusAddr = addr;
            cpuBusIsWrite = false;
            StartCpuCycle();
            if (dmaNeedHalt) ProcessPendingDma(addr);
            byte val = mem_read_fun[addr](addr);
            if (addr != 0x4015) cpubus = val;
            EndCpuCycle();
            return val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CpuWrite(ushort addr, byte val)
        {
            CPU_Read = false;
            cpuBusAddr = addr;
            cpuBusIsWrite = true;
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
            CPU_Read = true;
            cpuBusAddr = addr;
            cpuBusIsWrite = false;
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
            CPU_Read = false;
            cpuBusAddr = addr;
            cpuBusIsWrite = true;
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
            CPU_Read = true;
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
                opHandlers[opcode]();
                operationCycle++;
            }
        }

        static void InitOpHandlers()
        {
            // Default: treat unknown opcode as NOP-like
            Action defaultAct = () => { CpuRead(addressBus); CompleteOperation(); };
            for (int i = 0; i < 256; i++) opHandlers[i] = defaultAct;

            // === BRK / NMI / IRQ / RESET ===
            opHandlers[0x00] = () => {
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
            };

            // === ORA ===
            opHandlers[0x09] = () => { PollInterrupts(); GetImmediate(); Op_ORA(dl); CompleteOperation(); };
            opHandlers[0x05] = () => {
                if (operationCycle == 1) GetAddressZeroPage();
                else { PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); }
            };
            opHandlers[0x15] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x0D] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x1D] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                    case 4: PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x19] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                    case 4: PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x01] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                    case 5: PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x11] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                    case 5: PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); break; }
            };

            // === AND ===
            opHandlers[0x29] = () => { PollInterrupts(); GetImmediate(); Op_AND(dl); CompleteOperation(); };
            opHandlers[0x25] = () => {
                if (operationCycle == 1) GetAddressZeroPage();
                else { PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); }
            };
            opHandlers[0x35] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x2D] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x3D] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                    case 4: PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x39] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                    case 4: PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x21] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                    case 5: PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x31] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                    case 5: PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); break; }
            };

            // === EOR ===
            opHandlers[0x49] = () => { PollInterrupts(); GetImmediate(); Op_EOR(dl); CompleteOperation(); };
            opHandlers[0x45] = () => {
                if (operationCycle == 1) GetAddressZeroPage();
                else { PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); }
            };
            opHandlers[0x55] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x4D] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x5D] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                    case 4: PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x59] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                    case 4: PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x41] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                    case 5: PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x51] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                    case 5: PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); break; }
            };

            // === ADC ===
            opHandlers[0x69] = () => { PollInterrupts(); GetImmediate(); Op_ADC(dl); CompleteOperation(); };
            opHandlers[0x65] = () => {
                if (operationCycle == 1) GetAddressZeroPage();
                else { PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); }
            };
            opHandlers[0x75] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x6D] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x7D] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                    case 4: PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x79] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                    case 4: PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x61] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                    case 5: PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0x71] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                    case 5: PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); break; }
            };

            // === SBC ===
            Action sbcImmAct = () => { PollInterrupts(); GetImmediate(); Op_SBC(dl); CompleteOperation(); };
            opHandlers[0xE9] = opHandlers[0xEB] = sbcImmAct;
            opHandlers[0xE5] = () => {
                if (operationCycle == 1) GetAddressZeroPage();
                else { PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); }
            };
            opHandlers[0xF5] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0xED] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0xFD] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                    case 4: PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0xF9] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                    case 4: PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0xE1] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                    case 5: PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); break; }
            };
            opHandlers[0xF1] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                    case 5: PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); break; }
            };

            // === CMP ===
            opHandlers[0xC9] = () => { PollInterrupts(); GetImmediate(); Op_CMP(dl, r_A); CompleteOperation(); };
            opHandlers[0xC5] = () => {
                if (operationCycle == 1) GetAddressZeroPage();
                else { PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); }
            };
            opHandlers[0xD5] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); break; }
            };
            opHandlers[0xCD] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); break; }
            };
            opHandlers[0xDD] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                    case 4: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); break; }
            };
            opHandlers[0xD9] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                    case 4: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); break; }
            };
            opHandlers[0xC1] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                    case 5: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); break; }
            };
            opHandlers[0xD1] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                    case 5: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); break; }
            };

            // === CPX ===
            opHandlers[0xE0] = () => { PollInterrupts(); GetImmediate(); Op_CMP(dl, r_X); CompleteOperation(); };
            opHandlers[0xE4] = () => {
                if (operationCycle == 1) GetAddressZeroPage();
                else { PollInterrupts(); Op_CMP(CpuRead(addressBus), r_X); CompleteOperation(); }
            };
            opHandlers[0xEC] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_X); CompleteOperation(); break; }
            };

            // === CPY ===
            opHandlers[0xC0] = () => { PollInterrupts(); GetImmediate(); Op_CMP(dl, r_Y); CompleteOperation(); };
            opHandlers[0xC4] = () => {
                if (operationCycle == 1) GetAddressZeroPage();
                else { PollInterrupts(); Op_CMP(CpuRead(addressBus), r_Y); CompleteOperation(); }
            };
            opHandlers[0xCC] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_Y); CompleteOperation(); break; }
            };

            // === LDA ===
            opHandlers[0xA9] = () => { PollInterrupts(); GetImmediate(); r_A = dl; SetNZ(r_A); CompleteOperation(); };
            opHandlers[0xA5] = () => {
                if (operationCycle == 1) GetAddressZeroPage();
                else { PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); }
            };
            opHandlers[0xB5] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break; }
            };
            opHandlers[0xAD] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break; }
            };
            opHandlers[0xBD] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                    case 4: PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break; }
            };
            opHandlers[0xB9] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                    case 4: PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break; }
            };
            opHandlers[0xA1] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                    case 5: PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break; }
            };
            opHandlers[0xB1] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                    case 5: PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break; }
            };

            // === LDX ===
            opHandlers[0xA2] = () => { PollInterrupts(); GetImmediate(); r_X = dl; SetNZ(r_X); CompleteOperation(); };
            opHandlers[0xA6] = () => {
                if (operationCycle == 1) GetAddressZeroPage();
                else { PollInterrupts(); r_X = CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); }
            };
            opHandlers[0xB6] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffY(); break;
                    case 3: PollInterrupts(); r_X = CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); break; }
            };
            opHandlers[0xAE] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: PollInterrupts(); r_X = CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); break; }
            };
            opHandlers[0xBE] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                    case 4: PollInterrupts(); r_X = CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); break; }
            };

            // === LDY ===
            opHandlers[0xA0] = () => { PollInterrupts(); GetImmediate(); r_Y = dl; SetNZ(r_Y); CompleteOperation(); };
            opHandlers[0xA4] = () => {
                if (operationCycle == 1) GetAddressZeroPage();
                else { PollInterrupts(); r_Y = CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); }
            };
            opHandlers[0xB4] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: PollInterrupts(); r_Y = CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); break; }
            };
            opHandlers[0xAC] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: PollInterrupts(); r_Y = CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); break; }
            };
            opHandlers[0xBC] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                    case 4: PollInterrupts(); r_Y = CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); break; }
            };

            // === STA ===
            opHandlers[0x85] = () => {
                if (operationCycle == 1) { GetAddressZeroPage(); CPU_Read = false; }
                else { PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); }
            };
            opHandlers[0x95] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); if (operationCycle == 2) CPU_Read = false; break;
                    case 3: PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); break; }
            };
            opHandlers[0x8D] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); if (operationCycle == 2) CPU_Read = false; break;
                    case 3: PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); break; }
            };
            opHandlers[0x9D] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(false); if (operationCycle == 3) CPU_Read = false; break;
                    case 4: PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); break; }
            };
            opHandlers[0x99] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(false); if (operationCycle == 3) CPU_Read = false; break;
                    case 4: PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); break; }
            };
            opHandlers[0x81] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); break; }
            };
            opHandlers[0x91] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); break; }
            };

            // === STX ===
            opHandlers[0x86] = () => {
                if (operationCycle == 1) { GetAddressZeroPage(); CPU_Read = false; }
                else { PollInterrupts(); CpuWrite(addressBus, r_X); CompleteOperation(); }
            };
            opHandlers[0x96] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffY(); if (operationCycle == 2) CPU_Read = false; break;
                    case 3: PollInterrupts(); CpuWrite(addressBus, r_X); CompleteOperation(); break; }
            };
            opHandlers[0x8E] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); if (operationCycle == 2) CPU_Read = false; break;
                    case 3: PollInterrupts(); CpuWrite(addressBus, r_X); CompleteOperation(); break; }
            };

            // === STY ===
            opHandlers[0x84] = () => {
                if (operationCycle == 1) { GetAddressZeroPage(); CPU_Read = false; }
                else { PollInterrupts(); CpuWrite(addressBus, r_Y); CompleteOperation(); }
            };
            opHandlers[0x94] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); if (operationCycle == 2) CPU_Read = false; break;
                    case 3: PollInterrupts(); CpuWrite(addressBus, r_Y); CompleteOperation(); break; }
            };
            opHandlers[0x8C] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); if (operationCycle == 2) CPU_Read = false; break;
                    case 3: PollInterrupts(); CpuWrite(addressBus, r_Y); CompleteOperation(); break; }
            };

            // === BIT ===
            opHandlers[0x24] = () => {
                switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                    case 2: PollInterrupts(); dl = CpuRead(addressBus);
                        flagZ = (byte)(((r_A & dl) == 0) ? 1 : 0);
                        flagN = (byte)((dl & 0x80) >> 7);
                        flagV = (byte)((dl & 0x40) >> 6);
                        CompleteOperation(); break; }
            };
            opHandlers[0x2C] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: PollInterrupts(); dl = CpuRead(addressBus);
                        flagZ = (byte)(((r_A & dl) == 0) ? 1 : 0);
                        flagN = (byte)((dl & 0x80) >> 7);
                        flagV = (byte)((dl & 0x40) >> 6);
                        CompleteOperation(); break; }
            };

            // === ASL ===
            opHandlers[0x0A] = () => {
                PollInterrupts(); CpuRead(addressBus);
                flagC = (byte)((r_A & 0x80) >> 7); r_A <<= 1; SetNZ(r_A);
                CompleteOperation();
            };
            opHandlers[0x06] = () => {
                switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                    case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 3: CpuWrite(addressBus, dl); break;
                    case 4: PollInterrupts(); Op_ASL_mem(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x16] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_ASL_mem(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x0E] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_ASL_mem(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x1E] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: CpuWrite(addressBus, dl); break;
                    case 6: PollInterrupts(); Op_ASL_mem(addressBus); CompleteOperation(); break; }
            };

            // === LSR ===
            opHandlers[0x4A] = () => {
                PollInterrupts(); CpuRead(addressBus);
                flagC = (byte)(r_A & 1); r_A >>= 1; SetNZ(r_A);
                CompleteOperation();
            };
            opHandlers[0x46] = () => {
                switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                    case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 3: CpuWrite(addressBus, dl); break;
                    case 4: PollInterrupts(); Op_LSR_mem(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x56] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_LSR_mem(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x4E] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_LSR_mem(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x5E] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: CpuWrite(addressBus, dl); break;
                    case 6: PollInterrupts(); Op_LSR_mem(addressBus); CompleteOperation(); break; }
            };

            // === ROL ===
            opHandlers[0x2A] = () => {
                PollInterrupts(); CpuRead(addressBus);
                { byte oc = flagC; flagC = (byte)((r_A & 0x80) >> 7); r_A = (byte)((r_A << 1) | oc); SetNZ(r_A); }
                CompleteOperation();
            };
            opHandlers[0x26] = () => {
                switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                    case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 3: CpuWrite(addressBus, dl); break;
                    case 4: PollInterrupts(); Op_ROL_mem(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x36] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_ROL_mem(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x2E] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_ROL_mem(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x3E] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: CpuWrite(addressBus, dl); break;
                    case 6: PollInterrupts(); Op_ROL_mem(addressBus); CompleteOperation(); break; }
            };

            // === ROR ===
            opHandlers[0x6A] = () => {
                PollInterrupts(); CpuRead(addressBus);
                { byte oc = flagC; flagC = (byte)(r_A & 1); r_A = (byte)((r_A >> 1) | (oc << 7)); SetNZ(r_A); }
                CompleteOperation();
            };
            opHandlers[0x66] = () => {
                switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                    case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 3: CpuWrite(addressBus, dl); break;
                    case 4: PollInterrupts(); Op_ROR_mem(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x76] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_ROR_mem(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x6E] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_ROR_mem(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x7E] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: CpuWrite(addressBus, dl); break;
                    case 6: PollInterrupts(); Op_ROR_mem(addressBus); CompleteOperation(); break; }
            };

            // === INC ===
            opHandlers[0xE6] = () => {
                switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                    case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 3: CpuWrite(addressBus, dl); break;
                    case 4: PollInterrupts(); Op_INC_mem(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0xF6] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_INC_mem(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0xEE] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_INC_mem(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0xFE] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: CpuWrite(addressBus, dl); break;
                    case 6: PollInterrupts(); Op_INC_mem(addressBus); CompleteOperation(); break; }
            };

            // === DEC ===
            opHandlers[0xC6] = () => {
                switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                    case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 3: CpuWrite(addressBus, dl); break;
                    case 4: PollInterrupts(); Op_DEC_mem(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0xD6] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_DEC_mem(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0xCE] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_DEC_mem(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0xDE] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: CpuWrite(addressBus, dl); break;
                    case 6: PollInterrupts(); Op_DEC_mem(addressBus); CompleteOperation(); break; }
            };

            // === INX / INY / DEX / DEY ===
            opHandlers[0xE8] = () => { PollInterrupts(); CpuRead(addressBus); r_X++; SetNZ(r_X); CompleteOperation(); };
            opHandlers[0xC8] = () => { PollInterrupts(); CpuRead(addressBus); r_Y++; SetNZ(r_Y); CompleteOperation(); };
            opHandlers[0xCA] = () => { PollInterrupts(); CpuRead(addressBus); r_X--; SetNZ(r_X); CompleteOperation(); };
            opHandlers[0x88] = () => { PollInterrupts(); CpuRead(addressBus); r_Y--; SetNZ(r_Y); CompleteOperation(); };

            // === Transfer ===
            opHandlers[0xAA] = () => { PollInterrupts(); r_X = r_A; CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); };
            opHandlers[0x8A] = () => { PollInterrupts(); r_A = r_X; CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); };
            opHandlers[0xA8] = () => { PollInterrupts(); r_Y = r_A; CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); };
            opHandlers[0x98] = () => { PollInterrupts(); r_A = r_Y; CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); };
            opHandlers[0xBA] = () => { PollInterrupts(); r_X = r_SP; CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); };
            opHandlers[0x9A] = () => { PollInterrupts(); r_SP = r_X; CpuRead(addressBus); CompleteOperation(); };

            // === Flag instructions ===
            opHandlers[0x18] = () => { PollInterrupts(); CpuRead(addressBus); flagC = 0; CompleteOperation(); };
            opHandlers[0x38] = () => { PollInterrupts(); CpuRead(addressBus); flagC = 1; CompleteOperation(); };
            opHandlers[0x58] = () => { PollInterrupts(); CpuRead(addressBus); flagI = 0; CompleteOperation(); };
            opHandlers[0x78] = () => { PollInterrupts(); CpuRead(addressBus); flagI = 1; CompleteOperation(); };
            opHandlers[0xD8] = () => { PollInterrupts(); CpuRead(addressBus); flagD = 0; CompleteOperation(); };
            opHandlers[0xF8] = () => { PollInterrupts(); CpuRead(addressBus); flagD = 1; CompleteOperation(); };
            opHandlers[0xB8] = () => { PollInterrupts(); CpuRead(addressBus); flagV = 0; CompleteOperation(); };

            // === Stack instructions ===
            opHandlers[0x48] = () => {
                switch (operationCycle) { case 1: CpuRead(addressBus); break;
                    case 2: PollInterrupts(); StackPush(r_A); CompleteOperation(); break; }
            };
            opHandlers[0x08] = () => {
                switch (operationCycle) { case 1: CpuRead(addressBus); break;
                    case 2: PollInterrupts(); StackPush((byte)(GetFlag() | 0x30)); CompleteOperation(); break; }
            };
            opHandlers[0x68] = () => {
                switch (operationCycle) { case 1: CpuRead(addressBus); break;
                    case 2: CpuRead((ushort)(0x100 | r_SP)); r_SP++; break;
                    case 3: PollInterrupts(); r_A = CpuRead((ushort)(0x100 | r_SP)); SetNZ(r_A); CompleteOperation(); break; }
            };
            opHandlers[0x28] = () => {
                switch (operationCycle) { case 1: CpuRead(addressBus); break;
                    case 2: CpuRead((ushort)(0x100 | r_SP)); r_SP++; break;
                    case 3: PollInterrupts(); SetFlag(CpuRead((ushort)(0x100 | r_SP))); CompleteOperation(); break; }
            };

            // === Branches ===
            opHandlers[0x10] = () => { DoBranch(flagN == 0); };
            opHandlers[0x30] = () => { DoBranch(flagN != 0); };
            opHandlers[0x50] = () => { DoBranch(flagV == 0); };
            opHandlers[0x70] = () => { DoBranch(flagV != 0); };
            opHandlers[0x90] = () => { DoBranch(flagC == 0); };
            opHandlers[0xB0] = () => { DoBranch(flagC != 0); };
            opHandlers[0xD0] = () => { DoBranch(flagZ == 0); };
            opHandlers[0xF0] = () => { DoBranch(flagZ != 0); };

            // === JMP ===
            opHandlers[0x4C] = () => {
                if (operationCycle == 1) GetAddressAbsolute();
                else { PollInterrupts(); GetAddressAbsolute(); r_PC = addressBus; CompleteOperation(); }
            };
            opHandlers[0x6C] = () => {
                switch (operationCycle) {
                    case 1: case 2: GetAddressAbsolute(); break;
                    case 3: specialBus = CpuRead(addressBus); break;
                    case 4: PollInterrupts();
                        dl = CpuRead((ushort)((addressBus & 0xFF00) | (byte)(addressBus + 1)));
                        r_PC = (ushort)((dl << 8) | specialBus);
                        CompleteOperation(); break;
                }
            };

            // === JSR ===
            opHandlers[0x20] = () => {
                switch (operationCycle) {
                    case 1:
                        addressBus = r_PC; dl = CpuRead(addressBus); r_PC++; break;
                    case 2:
                        addressBus = (ushort)(0x100 | r_SP); specialBus = dl; CPU_Read = false;
                        CpuRead(addressBus); break;
                    case 3:
                        CpuWrite(addressBus, (byte)(r_PC >> 8));
                        addressBus = (ushort)((byte)(addressBus - 1) | 0x100); break;
                    case 4:
                        CpuWrite(addressBus, (byte)r_PC);
                        addressBus = (ushort)((byte)(addressBus - 1) | 0x100);
                        r_SP = (byte)addressBus; CPU_Read = true; break;
                    case 5:
                        PollInterrupts();
                        r_PC = (ushort)((CpuRead(r_PC) << 8) | specialBus);
                        CompleteOperation(); break;
                }
            };

            // === RTS ===
            opHandlers[0x60] = () => {
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
            };

            // === RTI ===
            opHandlers[0x40] = () => {
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
            };

            // === NOP (implied) ===
            Action nopAct = () => { PollInterrupts(); CpuRead(addressBus); CompleteOperation(); };
            opHandlers[0xEA] = opHandlers[0x1A] = opHandlers[0x3A] = opHandlers[0x5A] =
                opHandlers[0x7A] = opHandlers[0xDA] = opHandlers[0xFA] = nopAct;

            // === DOP Immediate ===
            Action dopImmAct = () => { PollInterrupts(); GetImmediate(); CompleteOperation(); };
            opHandlers[0x80] = opHandlers[0x82] = opHandlers[0x89] = opHandlers[0xC2] = opHandlers[0xE2] = dopImmAct;

            // === DOP ZeroPage ===
            Action dopZpAct = () => {
                if (operationCycle == 1) GetAddressZeroPage();
                else { PollInterrupts(); CpuRead(addressBus); CompleteOperation(); }
            };
            opHandlers[0x04] = opHandlers[0x44] = opHandlers[0x64] = dopZpAct;

            // === DOP ZeroPage,X ===
            Action dopZpXAct = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: PollInterrupts(); CpuRead(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x14] = opHandlers[0x34] = opHandlers[0x54] = opHandlers[0x74] =
                opHandlers[0xD4] = opHandlers[0xF4] = dopZpXAct;

            // === TOP Absolute ===
            opHandlers[0x0C] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: PollInterrupts(); CpuRead(addressBus); CompleteOperation(); break; }
            };

            // === TOP Absolute,X ===
            Action topAbsXAct = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                    case 4: PollInterrupts(); CpuRead(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x1C] = opHandlers[0x3C] = opHandlers[0x5C] = opHandlers[0x7C] =
                opHandlers[0xDC] = opHandlers[0xFC] = topAbsXAct;

            // === SLO ===
            opHandlers[0x07] = () => {
                switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                    case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 3: CpuWrite(addressBus, dl); break;
                    case 4: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x17] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x0F] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x1F] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: CpuWrite(addressBus, dl); break;
                    case 6: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x1B] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffY(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: CpuWrite(addressBus, dl); break;
                    case 6: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x03] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                    case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 6: CpuWrite(addressBus, dl); break;
                    case 7: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x13] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                    case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 6: CpuWrite(addressBus, dl); break;
                    case 7: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
            };

            // === RLA ===
            opHandlers[0x27] = () => {
                switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                    case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 3: CpuWrite(addressBus, dl); break;
                    case 4: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x37] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x2F] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x3F] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: CpuWrite(addressBus, dl); break;
                    case 6: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x3B] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffY(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: CpuWrite(addressBus, dl); break;
                    case 6: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x23] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                    case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 6: CpuWrite(addressBus, dl); break;
                    case 7: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x33] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                    case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 6: CpuWrite(addressBus, dl); break;
                    case 7: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
            };

            // === SRE ===
            opHandlers[0x47] = () => {
                switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                    case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 3: CpuWrite(addressBus, dl); break;
                    case 4: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x57] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x4F] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x5F] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: CpuWrite(addressBus, dl); break;
                    case 6: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x5B] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffY(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: CpuWrite(addressBus, dl); break;
                    case 6: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x43] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                    case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 6: CpuWrite(addressBus, dl); break;
                    case 7: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x53] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                    case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 6: CpuWrite(addressBus, dl); break;
                    case 7: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
            };

            // === RRA ===
            opHandlers[0x67] = () => {
                switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                    case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 3: CpuWrite(addressBus, dl); break;
                    case 4: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x77] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x6F] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x7F] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: CpuWrite(addressBus, dl); break;
                    case 6: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x7B] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffY(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: CpuWrite(addressBus, dl); break;
                    case 6: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x63] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                    case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 6: CpuWrite(addressBus, dl); break;
                    case 7: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0x73] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                    case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 6: CpuWrite(addressBus, dl); break;
                    case 7: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
            };

            // === SAX ===
            opHandlers[0x87] = () => {
                if (operationCycle == 1) { GetAddressZeroPage(); CPU_Read = false; }
                else { PollInterrupts(); CpuWrite(addressBus, (byte)(r_A & r_X)); CompleteOperation(); }
            };
            opHandlers[0x97] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffY(); if (operationCycle == 2) CPU_Read = false; break;
                    case 3: PollInterrupts(); CpuWrite(addressBus, (byte)(r_A & r_X)); CompleteOperation(); break; }
            };
            opHandlers[0x8F] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); if (operationCycle == 2) CPU_Read = false; break;
                    case 3: PollInterrupts(); CpuWrite(addressBus, (byte)(r_A & r_X)); CompleteOperation(); break; }
            };
            opHandlers[0x83] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: PollInterrupts(); CpuWrite(addressBus, (byte)(r_A & r_X)); CompleteOperation(); break; }
            };

            // === LAX ===
            opHandlers[0xA7] = () => {
                if (operationCycle == 1) GetAddressZeroPage();
                else { PollInterrupts(); r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); }
            };
            opHandlers[0xB7] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffY(); break;
                    case 3: PollInterrupts(); r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); break; }
            };
            opHandlers[0xAF] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: PollInterrupts(); r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); break; }
            };
            opHandlers[0xBF] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                    case 4: PollInterrupts(); r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); break; }
            };
            opHandlers[0xA3] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                    case 5: PollInterrupts(); r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); break; }
            };
            opHandlers[0xB3] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                    case 5: PollInterrupts(); r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); break; }
            };

            // === DCP ===
            opHandlers[0xC7] = () => {
                switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                    case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 3: CpuWrite(addressBus, dl); break;
                    case 4: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0xD7] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0xCF] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0xDF] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: CpuWrite(addressBus, dl); break;
                    case 6: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0xDB] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffY(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: CpuWrite(addressBus, dl); break;
                    case 6: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0xC3] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                    case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 6: CpuWrite(addressBus, dl); break;
                    case 7: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0xD3] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                    case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 6: CpuWrite(addressBus, dl); break;
                    case 7: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
            };

            // === ISC ===
            opHandlers[0xE7] = () => {
                switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                    case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 3: CpuWrite(addressBus, dl); break;
                    case 4: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0xF7] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0xEF] = () => {
                switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                    case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 4: CpuWrite(addressBus, dl); break;
                    case 5: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0xFF] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: CpuWrite(addressBus, dl); break;
                    case 6: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0xFB] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffY(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: CpuWrite(addressBus, dl); break;
                    case 6: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0xE3] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                    case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 6: CpuWrite(addressBus, dl); break;
                    case 7: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
            };
            opHandlers[0xF3] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                    case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                    case 6: CpuWrite(addressBus, dl); break;
                    case 7: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
            };

            // === ANC ===
            Action ancAct = () => {
                PollInterrupts(); GetImmediate();
                r_A = (byte)(r_A & dl); flagC = (byte)((r_A & 0x80) >> 7); SetNZ(r_A);
                CompleteOperation();
            };
            opHandlers[0x0B] = opHandlers[0x2B] = ancAct;

            // === ALR ===
            opHandlers[0x4B] = () => {
                PollInterrupts(); GetImmediate();
                r_A = (byte)(r_A & dl); flagC = (byte)(r_A & 1); r_A >>= 1; SetNZ(r_A);
                CompleteOperation();
            };

            // === ARR ===
            opHandlers[0x6B] = () => {
                PollInterrupts(); GetImmediate();
                r_A = (byte)(r_A & dl);
                { byte oc = flagC; flagC = (byte)(r_A & 1); r_A = (byte)((r_A >> 1) | (oc << 7)); }
                SetNZ(r_A);
                flagC = (byte)((r_A & 0x40) >> 6);
                flagV = (byte)((((r_A >> 5) ^ (r_A >> 6)) & 1));
                CompleteOperation();
            };

            // === SBX ===
            opHandlers[0xCB] = () => {
                PollInterrupts(); GetImmediate();
                { int tmp = (r_A & r_X) - dl; flagC = (tmp >= 0) ? (byte)1 : (byte)0; r_X = (byte)tmp; SetNZ(r_X); }
                CompleteOperation();
            };

            // === ANE ===
            opHandlers[0x8B] = () => {
                PollInterrupts(); GetImmediate();
                r_A = (byte)((r_A | 0xFF) & r_X & dl);
                SetNZ(r_A); CompleteOperation();
            };

            // === LXA ===
            opHandlers[0xAB] = () => {
                PollInterrupts(); GetImmediate();
                r_A = (byte)((r_A | 0xFF) & dl); r_X = r_A; SetNZ(r_X);
                CompleteOperation();
            };

            // === LAE ===
            opHandlers[0xBB] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                    case 4: PollInterrupts();
                        dl = CpuRead(addressBus); r_A = (byte)(dl & r_SP); r_X = r_A; r_SP = r_A; SetNZ(r_A);
                        CompleteOperation(); break; }
            };

            // === SHA (Ind),Y ===
            opHandlers[0x93] = () => {
                switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); if (operationCycle == 4) CPU_Read = false; break;
                    case 5: PollInterrupts();
                        if ((temporaryAddress & 0xFF00) != (addressBus & 0xFF00))
                            addressBus = (ushort)((byte)addressBus | (((addressBus >> 8) & r_X) << 8));
                        if (ignoreH) H = 0xFF;
                        CpuWrite(addressBus, (byte)(r_A & (r_X | 0xF5) & H));
                        CompleteOperation(); break; }
            };

            // === SHA Abs,Y ===
            opHandlers[0x9F] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(false); if (operationCycle == 3) CPU_Read = false; break;
                    case 4: PollInterrupts();
                        if ((temporaryAddress & 0xFF00) != (addressBus & 0xFF00))
                            addressBus = (ushort)((byte)addressBus | (((addressBus >> 8) & r_X) << 8));
                        if (ignoreH) H = 0xFF;
                        CpuWrite(addressBus, (byte)(r_A & (r_X | 0xF5) & H));
                        CompleteOperation(); break; }
            };

            // === SHY Abs,X ===
            opHandlers[0x9C] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(false); if (operationCycle == 3) CPU_Read = false; break;
                    case 4: PollInterrupts();
                        if ((temporaryAddress & 0xFF00) != (addressBus & 0xFF00))
                            addressBus = (ushort)((byte)addressBus | (((addressBus >> 8) & r_Y) << 8));
                        if (ignoreH) H = 0xFF;
                        CpuWrite(addressBus, (byte)(r_Y & H));
                        CompleteOperation(); break; }
            };

            // === SHX Abs,Y ===
            opHandlers[0x9E] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(false); if (operationCycle == 3) CPU_Read = false; break;
                    case 4: PollInterrupts();
                        if ((temporaryAddress & 0xFF00) != (addressBus & 0xFF00))
                            addressBus = (ushort)((byte)addressBus | (((addressBus >> 8) & r_X) << 8));
                        if (ignoreH) H = 0xFF;
                        CpuWrite(addressBus, (byte)(r_X & H));
                        CompleteOperation(); break; }
            };

            // === SHS Abs,Y ===
            opHandlers[0x9B] = () => {
                switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(false); if (operationCycle == 3) CPU_Read = false; break;
                    case 4: PollInterrupts();
                        if ((temporaryAddress & 0xFF00) != (addressBus & 0xFF00))
                            addressBus = (ushort)((byte)addressBus | (((addressBus >> 8) & r_X) << 8));
                        r_SP = (byte)(r_A & r_X);
                        if (ignoreH) H = 0xFF;
                        CpuWrite(addressBus, (byte)(r_A & (r_X | 0xF5) & H));
                        CompleteOperation(); break; }
            };

            // === HLT (JAM) ===
            Action hltAct = () => { DoHLT(); };
            opHandlers[0x02] = opHandlers[0x12] = opHandlers[0x22] = opHandlers[0x32] =
                opHandlers[0x42] = opHandlers[0x52] = opHandlers[0x62] = opHandlers[0x72] =
                opHandlers[0x92] = opHandlers[0xB2] = opHandlers[0xD2] = opHandlers[0xF2] = hltAct;
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
