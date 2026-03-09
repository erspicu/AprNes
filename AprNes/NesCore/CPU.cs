using System;
using System.Runtime.CompilerServices;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        static byte r_A = 0, r_X = 0, r_Y = 0, r_SP = 0xFD, flagN = 0, flagV = 0, flagD = 0, flagI = 1, flagZ = 0, flagC = 0;
        static ushort r_PC = 0;
        static byte opcode;

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
                switch (opcode)
                {
                    case 0x00: // BRK / NMI / IRQ / RESET
                        switch (operationCycle)
                        {
                            case 1:
                                if (!doBRK)
                                {
                                    CpuRead(addressBus); // dummy fetch without incrementing PC
                                }
                                else
                                {
                                    GetImmediate(); // dummy fetch and PC increment
                                }
                                break;
                            case 2:
                                if (!doReset)
                                    StackPush((byte)(r_PC >> 8));
                                else
                                    { CpuRead((ushort)(0x100 | r_SP)); r_SP--; } // reset: read instead of write
                                break;
                            case 3:
                                if (!doReset)
                                    StackPush((byte)r_PC);
                                else
                                    { CpuRead((ushort)(0x100 | r_SP)); r_SP--; }
                                break;
                            case 4:
                                if (!doReset)
                                {
                                    byte pushed = (byte)(GetFlag() | 0x20 | (doBRK ? 0x10 : 0x00));
                                    StackPush(pushed);
                                }
                                else
                                    { CpuRead((ushort)(0x100 | r_SP)); r_SP--; }
                                // NMI hijack check during interrupt vectoring
                                if (nmi_pending) { doNMI = true; nmi_pending = false; }
                                break;
                            case 5:
                                if (doNMI)
                                    r_PC = (ushort)((r_PC & 0xFF00) | CpuRead(0xFFFA));
                                else if (doReset)
                                    r_PC = (ushort)((r_PC & 0xFF00) | CpuRead(0xFFFC));
                                else
                                    r_PC = (ushort)((r_PC & 0xFF00) | CpuRead(0xFFFE));
                                break;
                            case 6:
                                if (doNMI)
                                    r_PC = (ushort)((r_PC & 0xFF) | (CpuRead(0xFFFB) << 8));
                                else if (doReset)
                                    r_PC = (ushort)((r_PC & 0xFF) | (CpuRead(0xFFFD) << 8));
                                else
                                    r_PC = (ushort)((r_PC & 0xFF) | (CpuRead(0xFFFF) << 8));

                                if (doNMI)
                                {
                                    dbgWrite("NMI_PUSH: PC=$" + r_PC.ToString("X4") + " SP=$" + r_SP.ToString("X2"));
                                }
                                if (doReset)
                                {
                                    Console.WriteLine("soft reset !");
                                    nmi_pending = false;
                                    nmi_delay_cycle = -1;
                                    nmi_output_prev = false;
                                    irq_pending = false;
                                    statusmapperint = false;
                                    apuSoftReset();
                                    strobeWritePending = 0;
                                    P1_LastWrite = 0;
                                }

                                CompleteOperation();
                                doReset = false;
                                doNMI = false;
                                doIRQ = false;
                                doBRK = false;
                                flagI = 1;
                                break;
                        }
                        break;

                    // ===== ORA =====
                    case 0x09: // ORA Imm
                        PollInterrupts(); GetImmediate(); Op_ORA(dl); CompleteOperation(); break;
                    case 0x05: // ORA ZP
                        if (operationCycle == 1) GetAddressZeroPage();
                        else { PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); }
                        break;
                    case 0x15: // ORA ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x0D: // ORA Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x1D: // ORA Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                            case 4: PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x19: // ORA Abs,Y
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                            case 4: PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x01: // ORA (Ind,X)
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                            case 5: PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x11: // ORA (Ind),Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                            case 5: PollInterrupts(); Op_ORA(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;

                    // ===== AND =====
                    case 0x29: // AND Imm
                        PollInterrupts(); GetImmediate(); Op_AND(dl); CompleteOperation(); break;
                    case 0x25: // AND ZP
                        if (operationCycle == 1) GetAddressZeroPage();
                        else { PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); }
                        break;
                    case 0x35: // AND ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x2D: // AND Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x3D: // AND Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                            case 4: PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x39: // AND Abs,Y
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                            case 4: PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x21: // AND (Ind,X)
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                            case 5: PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x31: // AND (Ind),Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                            case 5: PollInterrupts(); Op_AND(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;

                    // ===== EOR =====
                    case 0x49: // EOR Imm
                        PollInterrupts(); GetImmediate(); Op_EOR(dl); CompleteOperation(); break;
                    case 0x45: // EOR ZP
                        if (operationCycle == 1) GetAddressZeroPage();
                        else { PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); }
                        break;
                    case 0x55: // EOR ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x4D: // EOR Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x5D: // EOR Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                            case 4: PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x59: // EOR Abs,Y
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                            case 4: PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x41: // EOR (Ind,X)
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                            case 5: PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x51: // EOR (Ind),Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                            case 5: PollInterrupts(); Op_EOR(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;

                    // ===== ADC =====
                    case 0x69: // ADC Imm
                        PollInterrupts(); GetImmediate(); Op_ADC(dl); CompleteOperation(); break;
                    case 0x65: // ADC ZP
                        if (operationCycle == 1) GetAddressZeroPage();
                        else { PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); }
                        break;
                    case 0x75: // ADC ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x6D: // ADC Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x7D: // ADC Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                            case 4: PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x79: // ADC Abs,Y
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                            case 4: PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x61: // ADC (Ind,X)
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                            case 5: PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0x71: // ADC (Ind),Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                            case 5: PollInterrupts(); Op_ADC(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;

                    // ===== SBC =====
                    case 0xE9: // SBC Imm
                    case 0xEB: // SBC Imm *** (unofficial)
                        PollInterrupts(); GetImmediate(); Op_SBC(dl); CompleteOperation(); break;
                    case 0xE5: // SBC ZP
                        if (operationCycle == 1) GetAddressZeroPage();
                        else { PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); }
                        break;
                    case 0xF5: // SBC ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0xED: // SBC Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0xFD: // SBC Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                            case 4: PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0xF9: // SBC Abs,Y
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                            case 4: PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0xE1: // SBC (Ind,X)
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                            case 5: PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;
                    case 0xF1: // SBC (Ind),Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                            case 5: PollInterrupts(); Op_SBC(CpuRead(addressBus)); CompleteOperation(); break; }
                        break;

                    // ===== CMP =====
                    case 0xC9: // CMP Imm
                        PollInterrupts(); GetImmediate(); Op_CMP(dl, r_A); CompleteOperation(); break;
                    case 0xC5: // CMP ZP
                        if (operationCycle == 1) GetAddressZeroPage();
                        else { PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); }
                        break;
                    case 0xD5: // CMP ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); break; }
                        break;
                    case 0xCD: // CMP Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); break; }
                        break;
                    case 0xDD: // CMP Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                            case 4: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); break; }
                        break;
                    case 0xD9: // CMP Abs,Y
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                            case 4: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); break; }
                        break;
                    case 0xC1: // CMP (Ind,X)
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                            case 5: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); break; }
                        break;
                    case 0xD1: // CMP (Ind),Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                            case 5: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); break; }
                        break;

                    // ===== CPX =====
                    case 0xE0: // CPX Imm
                        PollInterrupts(); GetImmediate(); Op_CMP(dl, r_X); CompleteOperation(); break;
                    case 0xE4: // CPX ZP
                        if (operationCycle == 1) GetAddressZeroPage();
                        else { PollInterrupts(); Op_CMP(CpuRead(addressBus), r_X); CompleteOperation(); }
                        break;
                    case 0xEC: // CPX Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_X); CompleteOperation(); break; }
                        break;

                    // ===== CPY =====
                    case 0xC0: // CPY Imm
                        PollInterrupts(); GetImmediate(); Op_CMP(dl, r_Y); CompleteOperation(); break;
                    case 0xC4: // CPY ZP
                        if (operationCycle == 1) GetAddressZeroPage();
                        else { PollInterrupts(); Op_CMP(CpuRead(addressBus), r_Y); CompleteOperation(); }
                        break;
                    case 0xCC: // CPY Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: PollInterrupts(); Op_CMP(CpuRead(addressBus), r_Y); CompleteOperation(); break; }
                        break;

                    // ===== LDA =====
                    case 0xA9: // LDA Imm
                        PollInterrupts(); GetImmediate(); r_A = dl; SetNZ(r_A); CompleteOperation(); break;
                    case 0xA5: // LDA ZP
                        if (operationCycle == 1) GetAddressZeroPage();
                        else { PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); }
                        break;
                    case 0xB5: // LDA ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break; }
                        break;
                    case 0xAD: // LDA Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break; }
                        break;
                    case 0xBD: // LDA Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                            case 4: PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break; }
                        break;
                    case 0xB9: // LDA Abs,Y
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                            case 4: PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break; }
                        break;
                    case 0xA1: // LDA (Ind,X)
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                            case 5: PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break; }
                        break;
                    case 0xB1: // LDA (Ind),Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                            case 5: PollInterrupts(); r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break; }
                        break;

                    // ===== LDX =====
                    case 0xA2: // LDX Imm
                        PollInterrupts(); GetImmediate(); r_X = dl; SetNZ(r_X); CompleteOperation(); break;
                    case 0xA6: // LDX ZP
                        if (operationCycle == 1) GetAddressZeroPage();
                        else { PollInterrupts(); r_X = CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); }
                        break;
                    case 0xB6: // LDX ZP,Y
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffY(); break;
                            case 3: PollInterrupts(); r_X = CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); break; }
                        break;
                    case 0xAE: // LDX Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: PollInterrupts(); r_X = CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); break; }
                        break;
                    case 0xBE: // LDX Abs,Y
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                            case 4: PollInterrupts(); r_X = CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); break; }
                        break;

                    // ===== LDY =====
                    case 0xA0: // LDY Imm
                        PollInterrupts(); GetImmediate(); r_Y = dl; SetNZ(r_Y); CompleteOperation(); break;
                    case 0xA4: // LDY ZP
                        if (operationCycle == 1) GetAddressZeroPage();
                        else { PollInterrupts(); r_Y = CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); }
                        break;
                    case 0xB4: // LDY ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: PollInterrupts(); r_Y = CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); break; }
                        break;
                    case 0xAC: // LDY Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: PollInterrupts(); r_Y = CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); break; }
                        break;
                    case 0xBC: // LDY Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                            case 4: PollInterrupts(); r_Y = CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); break; }
                        break;

                    // ===== STA =====
                    case 0x85: // STA ZP
                        if (operationCycle == 1) { GetAddressZeroPage(); CPU_Read = false; }
                        else { PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); }
                        break;
                    case 0x95: // STA ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); if (operationCycle == 2) CPU_Read = false; break;
                            case 3: PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); break; }
                        break;
                    case 0x8D: // STA Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); if (operationCycle == 2) CPU_Read = false; break;
                            case 3: PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); break; }
                        break;
                    case 0x9D: // STA Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(false); if (operationCycle == 3) CPU_Read = false; break;
                            case 4: PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); break; }
                        break;
                    case 0x99: // STA Abs,Y
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(false); if (operationCycle == 3) CPU_Read = false; break;
                            case 4: PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); break; }
                        break;
                    case 0x81: // STA (Ind,X)
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); break; }
                        break;
                    case 0x91: // STA (Ind),Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: PollInterrupts(); CpuWrite(addressBus, r_A); CompleteOperation(); break; }
                        break;

                    // ===== STX =====
                    case 0x86: // STX ZP
                        if (operationCycle == 1) { GetAddressZeroPage(); CPU_Read = false; }
                        else { PollInterrupts(); CpuWrite(addressBus, r_X); CompleteOperation(); }
                        break;
                    case 0x96: // STX ZP,Y
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffY(); if (operationCycle == 2) CPU_Read = false; break;
                            case 3: PollInterrupts(); CpuWrite(addressBus, r_X); CompleteOperation(); break; }
                        break;
                    case 0x8E: // STX Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); if (operationCycle == 2) CPU_Read = false; break;
                            case 3: PollInterrupts(); CpuWrite(addressBus, r_X); CompleteOperation(); break; }
                        break;

                    // ===== STY =====
                    case 0x84: // STY ZP
                        if (operationCycle == 1) { GetAddressZeroPage(); CPU_Read = false; }
                        else { PollInterrupts(); CpuWrite(addressBus, r_Y); CompleteOperation(); }
                        break;
                    case 0x94: // STY ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); if (operationCycle == 2) CPU_Read = false; break;
                            case 3: PollInterrupts(); CpuWrite(addressBus, r_Y); CompleteOperation(); break; }
                        break;
                    case 0x8C: // STY Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); if (operationCycle == 2) CPU_Read = false; break;
                            case 3: PollInterrupts(); CpuWrite(addressBus, r_Y); CompleteOperation(); break; }
                        break;

                    // ===== BIT =====
                    case 0x24: // BIT ZP
                        switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                            case 2: PollInterrupts(); dl = CpuRead(addressBus);
                                flagZ = (byte)(((r_A & dl) == 0) ? 1 : 0);
                                flagN = (byte)((dl & 0x80) >> 7);
                                flagV = (byte)((dl & 0x40) >> 6);
                                CompleteOperation(); break; }
                        break;
                    case 0x2C: // BIT Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: PollInterrupts(); dl = CpuRead(addressBus);
                                flagZ = (byte)(((r_A & dl) == 0) ? 1 : 0);
                                flagN = (byte)((dl & 0x80) >> 7);
                                flagV = (byte)((dl & 0x40) >> 6);
                                CompleteOperation(); break; }
                        break;

                    // ===== ASL =====
                    case 0x0A: // ASL A
                        PollInterrupts(); CpuRead(addressBus); // dummy read
                        flagC = (byte)((r_A & 0x80) >> 7); r_A <<= 1; SetNZ(r_A);
                        CompleteOperation(); break;
                    case 0x06: // ASL ZP
                        switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                            case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 3: CpuWrite(addressBus, dl); break; // dummy write
                            case 4: PollInterrupts(); Op_ASL_mem(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x16: // ASL ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_ASL_mem(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x0E: // ASL Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_ASL_mem(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x1E: // ASL Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: CpuWrite(addressBus, dl); break;
                            case 6: PollInterrupts(); Op_ASL_mem(addressBus); CompleteOperation(); break; }
                        break;

                    // ===== LSR =====
                    case 0x4A: // LSR A
                        PollInterrupts(); CpuRead(addressBus);
                        flagC = (byte)(r_A & 1); r_A >>= 1; SetNZ(r_A);
                        CompleteOperation(); break;
                    case 0x46: // LSR ZP
                        switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                            case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 3: CpuWrite(addressBus, dl); break;
                            case 4: PollInterrupts(); Op_LSR_mem(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x56: // LSR ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_LSR_mem(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x4E: // LSR Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_LSR_mem(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x5E: // LSR Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: CpuWrite(addressBus, dl); break;
                            case 6: PollInterrupts(); Op_LSR_mem(addressBus); CompleteOperation(); break; }
                        break;

                    // ===== ROL =====
                    case 0x2A: // ROL A
                        PollInterrupts(); CpuRead(addressBus);
                        { byte oc = flagC; flagC = (byte)((r_A & 0x80) >> 7); r_A = (byte)((r_A << 1) | oc); SetNZ(r_A); }
                        CompleteOperation(); break;
                    case 0x26: // ROL ZP
                        switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                            case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 3: CpuWrite(addressBus, dl); break;
                            case 4: PollInterrupts(); Op_ROL_mem(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x36: // ROL ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_ROL_mem(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x2E: // ROL Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_ROL_mem(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x3E: // ROL Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: CpuWrite(addressBus, dl); break;
                            case 6: PollInterrupts(); Op_ROL_mem(addressBus); CompleteOperation(); break; }
                        break;

                    // ===== ROR =====
                    case 0x6A: // ROR A
                        PollInterrupts(); CpuRead(addressBus);
                        { byte oc = flagC; flagC = (byte)(r_A & 1); r_A = (byte)((r_A >> 1) | (oc << 7)); SetNZ(r_A); }
                        CompleteOperation(); break;
                    case 0x66: // ROR ZP
                        switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                            case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 3: CpuWrite(addressBus, dl); break;
                            case 4: PollInterrupts(); Op_ROR_mem(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x76: // ROR ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_ROR_mem(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x6E: // ROR Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_ROR_mem(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x7E: // ROR Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: CpuWrite(addressBus, dl); break;
                            case 6: PollInterrupts(); Op_ROR_mem(addressBus); CompleteOperation(); break; }
                        break;

                    // ===== INC =====
                    case 0xE6: // INC ZP
                        switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                            case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 3: CpuWrite(addressBus, dl); break;
                            case 4: PollInterrupts(); Op_INC_mem(addressBus); CompleteOperation(); break; }
                        break;
                    case 0xF6: // INC ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_INC_mem(addressBus); CompleteOperation(); break; }
                        break;
                    case 0xEE: // INC Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_INC_mem(addressBus); CompleteOperation(); break; }
                        break;
                    case 0xFE: // INC Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: CpuWrite(addressBus, dl); break;
                            case 6: PollInterrupts(); Op_INC_mem(addressBus); CompleteOperation(); break; }
                        break;

                    // ===== DEC =====
                    case 0xC6: // DEC ZP
                        switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                            case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 3: CpuWrite(addressBus, dl); break;
                            case 4: PollInterrupts(); Op_DEC_mem(addressBus); CompleteOperation(); break; }
                        break;
                    case 0xD6: // DEC ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_DEC_mem(addressBus); CompleteOperation(); break; }
                        break;
                    case 0xCE: // DEC Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_DEC_mem(addressBus); CompleteOperation(); break; }
                        break;
                    case 0xDE: // DEC Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: CpuWrite(addressBus, dl); break;
                            case 6: PollInterrupts(); Op_DEC_mem(addressBus); CompleteOperation(); break; }
                        break;

                    // ===== INX/INY/DEX/DEY =====
                    case 0xE8: // INX
                        PollInterrupts(); CpuRead(addressBus); r_X++; SetNZ(r_X); CompleteOperation(); break;
                    case 0xC8: // INY
                        PollInterrupts(); CpuRead(addressBus); r_Y++; SetNZ(r_Y); CompleteOperation(); break;
                    case 0xCA: // DEX
                        PollInterrupts(); CpuRead(addressBus); r_X--; SetNZ(r_X); CompleteOperation(); break;
                    case 0x88: // DEY
                        PollInterrupts(); CpuRead(addressBus); r_Y--; SetNZ(r_Y); CompleteOperation(); break;

                    // ===== Transfer =====
                    case 0xAA: // TAX
                        PollInterrupts(); r_X = r_A; CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); break;
                    case 0x8A: // TXA
                        PollInterrupts(); r_A = r_X; CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break;
                    case 0xA8: // TAY
                        PollInterrupts(); r_Y = r_A; CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); break;
                    case 0x98: // TYA
                        PollInterrupts(); r_A = r_Y; CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); break;
                    case 0xBA: // TSX
                        PollInterrupts(); r_X = r_SP; CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); break;
                    case 0x9A: // TXS
                        PollInterrupts(); r_SP = r_X; CpuRead(addressBus); CompleteOperation(); break;

                    // ===== Flag instructions =====
                    case 0x18: // CLC
                        PollInterrupts(); CpuRead(addressBus); flagC = 0; CompleteOperation(); break;
                    case 0x38: // SEC
                        PollInterrupts(); CpuRead(addressBus); flagC = 1; CompleteOperation(); break;
                    case 0x58: // CLI
                        PollInterrupts(); CpuRead(addressBus); flagI = 0; CompleteOperation(); break;
                    case 0x78: // SEI
                        PollInterrupts(); CpuRead(addressBus); flagI = 1; CompleteOperation(); break;
                    case 0xD8: // CLD
                        PollInterrupts(); CpuRead(addressBus); flagD = 0; CompleteOperation(); break;
                    case 0xF8: // SED
                        PollInterrupts(); CpuRead(addressBus); flagD = 1; CompleteOperation(); break;
                    case 0xB8: // CLV
                        PollInterrupts(); CpuRead(addressBus); flagV = 0; CompleteOperation(); break;

                    // ===== Stack instructions =====
                    case 0x48: // PHA
                        switch (operationCycle) { case 1: CpuRead(addressBus); break; // dummy fetch
                            case 2: PollInterrupts(); StackPush(r_A); CompleteOperation(); break; }
                        break;
                    case 0x08: // PHP
                        switch (operationCycle) { case 1: CpuRead(addressBus); break;
                            case 2: PollInterrupts(); StackPush((byte)(GetFlag() | 0x30)); CompleteOperation(); break; }
                        break;
                    case 0x68: // PLA
                        switch (operationCycle) { case 1: CpuRead(addressBus); break; // dummy fetch
                            case 2: CpuRead((ushort)(0x100 | r_SP)); r_SP++; break; // dummy stack read
                            case 3: PollInterrupts(); r_A = CpuRead((ushort)(0x100 | r_SP)); SetNZ(r_A); CompleteOperation(); break; }
                        break;
                    case 0x28: // PLP
                        switch (operationCycle) { case 1: CpuRead(addressBus); break;
                            case 2: CpuRead((ushort)(0x100 | r_SP)); r_SP++; break;
                            case 3: PollInterrupts(); SetFlag(CpuRead((ushort)(0x100 | r_SP))); CompleteOperation(); break; }
                        break;

                    // ===== Branches =====
                    case 0x10: DoBranch(flagN == 0); break; // BPL
                    case 0x30: DoBranch(flagN != 0); break; // BMI
                    case 0x50: DoBranch(flagV == 0); break; // BVC
                    case 0x70: DoBranch(flagV != 0); break; // BVS
                    case 0x90: DoBranch(flagC == 0); break; // BCC
                    case 0xB0: DoBranch(flagC != 0); break; // BCS
                    case 0xD0: DoBranch(flagZ == 0); break; // BNE
                    case 0xF0: DoBranch(flagZ != 0); break; // BEQ

                    // ===== JMP =====
                    case 0x4C: // JMP abs
                        if (operationCycle == 1) GetAddressAbsolute();
                        else { PollInterrupts(); GetAddressAbsolute(); r_PC = addressBus; CompleteOperation(); }
                        break;
                    case 0x6C: // JMP (ind)
                        switch (operationCycle) {
                            case 1: case 2: GetAddressAbsolute(); break;
                            case 3: specialBus = CpuRead(addressBus); break;
                            case 4: PollInterrupts();
                                dl = CpuRead((ushort)((addressBus & 0xFF00) | (byte)(addressBus + 1)));
                                r_PC = (ushort)((dl << 8) | specialBus);
                                CompleteOperation(); break;
                        }
                        break;

                    // ===== JSR =====
                    case 0x20: // JSR
                        switch (operationCycle) {
                            case 1:
                                addressBus = r_PC;
                                dl = CpuRead(addressBus);
                                r_PC++;
                                break;
                            case 2:
                                addressBus = (ushort)(0x100 | r_SP);
                                specialBus = dl;
                                CPU_Read = false;
                                CpuRead(addressBus); // dummy read (internal op)
                                break;
                            case 3:
                                CpuWrite(addressBus, (byte)(r_PC >> 8));
                                addressBus = (ushort)((byte)(addressBus - 1) | 0x100);
                                break;
                            case 4:
                                CpuWrite(addressBus, (byte)r_PC);
                                addressBus = (ushort)((byte)(addressBus - 1) | 0x100);
                                r_SP = (byte)addressBus;
                                CPU_Read = true;
                                break;
                            case 5:
                                PollInterrupts();
                                r_PC = (ushort)((CpuRead(r_PC) << 8) | specialBus);
                                CompleteOperation();
                                break;
                        }
                        break;

                    // ===== RTS =====
                    case 0x60: // RTS
                        switch (operationCycle) {
                            case 1: GetImmediate(); break;
                            case 2:
                                addressBus = (ushort)(0x100 | r_SP);
                                CpuRead(addressBus); // dummy read
                                addressBus = (ushort)((byte)(addressBus + 1) | 0x100);
                                break;
                            case 3:
                                dl = CpuRead(addressBus);
                                r_PC = (ushort)((r_PC & 0xFF00) | dl);
                                addressBus = (ushort)((byte)(addressBus + 1) | 0x100);
                                break;
                            case 4:
                                dl = CpuRead(addressBus);
                                r_PC = (ushort)((r_PC & 0xFF) | (dl << 8));
                                break;
                            case 5:
                                PollInterrupts();
                                r_SP = (byte)addressBus;
                                GetImmediate(); // PC++ (skip return address)
                                CompleteOperation();
                                break;
                        }
                        break;

                    // ===== RTI =====
                    case 0x40: // RTI
                        switch (operationCycle) {
                            case 1: GetImmediate(); break;
                            case 2:
                                addressBus = (ushort)(0x100 | r_SP);
                                CpuRead(addressBus); // dummy read
                                addressBus = (ushort)((byte)(addressBus + 1) | 0x100);
                                break;
                            case 3:
                                {
                                    byte status = CpuRead(addressBus);
                                    SetFlag(status);
                                    addressBus = (ushort)((byte)(addressBus + 1) | 0x100);
                                }
                                break;
                            case 4:
                                dl = CpuRead(addressBus);
                                r_PC = (ushort)((r_PC & 0xFF00) | dl);
                                addressBus = (ushort)((byte)(addressBus + 1) | 0x100);
                                break;
                            case 5:
                                PollInterrupts();
                                dl = CpuRead(addressBus);
                                r_PC = (ushort)((r_PC & 0xFF) | (dl << 8));
                                r_SP = (byte)addressBus;
                                CompleteOperation();
                                break;
                        }
                        break;

                    // ===== NOP =====
                    case 0xEA: // NOP
                    case 0x1A: case 0x3A: case 0x5A: case 0x7A: case 0xDA: case 0xFA: // NOP *** (implied)
                        PollInterrupts(); CpuRead(addressBus); CompleteOperation(); break;

                    // ===== DOP (Double NOP) *** - Immediate =====
                    case 0x80: case 0x82: case 0x89: case 0xC2: case 0xE2:
                        PollInterrupts(); GetImmediate(); CompleteOperation(); break;

                    // ===== DOP *** - ZeroPage =====
                    case 0x04: case 0x44: case 0x64:
                        if (operationCycle == 1) GetAddressZeroPage();
                        else { PollInterrupts(); CpuRead(addressBus); CompleteOperation(); }
                        break;

                    // ===== DOP *** - ZeroPage,X =====
                    case 0x14: case 0x34: case 0x54: case 0x74: case 0xD4: case 0xF4:
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: PollInterrupts(); CpuRead(addressBus); CompleteOperation(); break; }
                        break;

                    // ===== TOP (Triple NOP) *** - Absolute =====
                    case 0x0C:
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: PollInterrupts(); CpuRead(addressBus); CompleteOperation(); break; }
                        break;

                    // ===== TOP *** - Absolute,X =====
                    case 0x1C: case 0x3C: case 0x5C: case 0x7C: case 0xDC: case 0xFC:
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(true); break;
                            case 4: PollInterrupts(); CpuRead(addressBus); CompleteOperation(); break; }
                        break;

                    // ===== SLO *** =====
                    case 0x07: // SLO ZP
                        switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                            case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 3: CpuWrite(addressBus, dl); break;
                            case 4: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x17: // SLO ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x0F: // SLO Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x1F: // SLO Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: CpuWrite(addressBus, dl); break;
                            case 6: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x1B: // SLO Abs,Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffY(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: CpuWrite(addressBus, dl); break;
                            case 6: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x03: // SLO (Ind,X)
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                            case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 6: CpuWrite(addressBus, dl); break;
                            case 7: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x13: // SLO (Ind),Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                            case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 6: CpuWrite(addressBus, dl); break;
                            case 7: PollInterrupts(); Op_SLO(addressBus); CompleteOperation(); break; }
                        break;

                    // ===== RLA *** =====
                    case 0x27: // RLA ZP
                        switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                            case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 3: CpuWrite(addressBus, dl); break;
                            case 4: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x37: // RLA ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x2F: // RLA Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x3F: // RLA Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: CpuWrite(addressBus, dl); break;
                            case 6: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x3B: // RLA Abs,Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffY(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: CpuWrite(addressBus, dl); break;
                            case 6: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x23: // RLA (Ind,X)
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                            case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 6: CpuWrite(addressBus, dl); break;
                            case 7: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x33: // RLA (Ind),Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                            case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 6: CpuWrite(addressBus, dl); break;
                            case 7: PollInterrupts(); Op_RLA(addressBus); CompleteOperation(); break; }
                        break;

                    // ===== SRE *** =====
                    case 0x47: // SRE ZP
                        switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                            case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 3: CpuWrite(addressBus, dl); break;
                            case 4: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x57: // SRE ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x4F: // SRE Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x5F: // SRE Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: CpuWrite(addressBus, dl); break;
                            case 6: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x5B: // SRE Abs,Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffY(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: CpuWrite(addressBus, dl); break;
                            case 6: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x43: // SRE (Ind,X)
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                            case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 6: CpuWrite(addressBus, dl); break;
                            case 7: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x53: // SRE (Ind),Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                            case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 6: CpuWrite(addressBus, dl); break;
                            case 7: PollInterrupts(); Op_SRE(addressBus); CompleteOperation(); break; }
                        break;

                    // ===== RRA *** =====
                    case 0x67: // RRA ZP
                        switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                            case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 3: CpuWrite(addressBus, dl); break;
                            case 4: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x77: // RRA ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x6F: // RRA Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x7F: // RRA Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: CpuWrite(addressBus, dl); break;
                            case 6: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x7B: // RRA Abs,Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffY(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: CpuWrite(addressBus, dl); break;
                            case 6: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x63: // RRA (Ind,X)
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                            case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 6: CpuWrite(addressBus, dl); break;
                            case 7: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
                        break;
                    case 0x73: // RRA (Ind),Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                            case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 6: CpuWrite(addressBus, dl); break;
                            case 7: PollInterrupts(); Op_RRA(addressBus); CompleteOperation(); break; }
                        break;

                    // ===== SAX *** =====
                    case 0x87: // SAX ZP
                        if (operationCycle == 1) { GetAddressZeroPage(); CPU_Read = false; }
                        else { PollInterrupts(); CpuWrite(addressBus, (byte)(r_A & r_X)); CompleteOperation(); }
                        break;
                    case 0x97: // SAX ZP,Y
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffY(); if (operationCycle == 2) CPU_Read = false; break;
                            case 3: PollInterrupts(); CpuWrite(addressBus, (byte)(r_A & r_X)); CompleteOperation(); break; }
                        break;
                    case 0x8F: // SAX Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); if (operationCycle == 2) CPU_Read = false; break;
                            case 3: PollInterrupts(); CpuWrite(addressBus, (byte)(r_A & r_X)); CompleteOperation(); break; }
                        break;
                    case 0x83: // SAX (Ind,X)
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: PollInterrupts(); CpuWrite(addressBus, (byte)(r_A & r_X)); CompleteOperation(); break; }
                        break;

                    // ===== LAX *** =====
                    case 0xA7: // LAX ZP
                        if (operationCycle == 1) GetAddressZeroPage();
                        else { PollInterrupts(); r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); }
                        break;
                    case 0xB7: // LAX ZP,Y
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffY(); break;
                            case 3: PollInterrupts(); r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); break; }
                        break;
                    case 0xAF: // LAX Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: PollInterrupts(); r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); break; }
                        break;
                    case 0xBF: // LAX Abs,Y
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                            case 4: PollInterrupts(); r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); break; }
                        break;
                    case 0xA3: // LAX (Ind,X)
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                            case 5: PollInterrupts(); r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); break; }
                        break;
                    case 0xB3: // LAX (Ind),Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(true); break;
                            case 5: PollInterrupts(); r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); break; }
                        break;

                    // ===== DCP *** =====
                    case 0xC7: // DCP ZP
                        switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                            case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 3: CpuWrite(addressBus, dl); break;
                            case 4: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
                        break;
                    case 0xD7: // DCP ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
                        break;
                    case 0xCF: // DCP Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
                        break;
                    case 0xDF: // DCP Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: CpuWrite(addressBus, dl); break;
                            case 6: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
                        break;
                    case 0xDB: // DCP Abs,Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffY(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: CpuWrite(addressBus, dl); break;
                            case 6: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
                        break;
                    case 0xC3: // DCP (Ind,X)
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                            case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 6: CpuWrite(addressBus, dl); break;
                            case 7: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
                        break;
                    case 0xD3: // DCP (Ind),Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                            case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 6: CpuWrite(addressBus, dl); break;
                            case 7: PollInterrupts(); Op_DCP(addressBus); CompleteOperation(); break; }
                        break;

                    // ===== ISC (ISB) *** =====
                    case 0xE7: // ISC ZP
                        switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                            case 2: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 3: CpuWrite(addressBus, dl); break;
                            case 4: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
                        break;
                    case 0xF7: // ISC ZP,X
                        switch (operationCycle) { case 1: case 2: GetAddressZPOffX(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
                        break;
                    case 0xEF: // ISC Abs
                        switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                            case 3: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 4: CpuWrite(addressBus, dl); break;
                            case 5: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
                        break;
                    case 0xFF: // ISC Abs,X
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffX(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: CpuWrite(addressBus, dl); break;
                            case 6: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
                        break;
                    case 0xFB: // ISC Abs,Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressAbsOffY(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: CpuWrite(addressBus, dl); break;
                            case 6: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
                        break;
                    case 0xE3: // ISC (Ind,X)
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffX(); break;
                            case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 6: CpuWrite(addressBus, dl); break;
                            case 7: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
                        break;
                    case 0xF3: // ISC (Ind),Y
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                            case 5: dl = CpuRead(addressBus); CPU_Read = false; break;
                            case 6: CpuWrite(addressBus, dl); break;
                            case 7: PollInterrupts(); Op_ISC(addressBus); CompleteOperation(); break; }
                        break;

                    // ===== ANC *** =====
                    case 0x0B: case 0x2B:
                        PollInterrupts(); GetImmediate();
                        r_A = (byte)(r_A & dl);
                        flagC = (byte)((r_A & 0x80) >> 7);
                        SetNZ(r_A);
                        CompleteOperation(); break;

                    // ===== ALR (ASR) *** =====
                    case 0x4B:
                        PollInterrupts(); GetImmediate();
                        r_A = (byte)(r_A & dl);
                        flagC = (byte)(r_A & 1); r_A >>= 1; SetNZ(r_A);
                        CompleteOperation(); break;

                    // ===== ARR *** =====
                    case 0x6B:
                        PollInterrupts(); GetImmediate();
                        r_A = (byte)(r_A & dl);
                        { byte oc = flagC; flagC = (byte)(r_A & 1); r_A = (byte)((r_A >> 1) | (oc << 7)); }
                        SetNZ(r_A);
                        flagC = (byte)((r_A & 0x40) >> 6);
                        flagV = (byte)((((r_A >> 5) ^ (r_A >> 6)) & 1));
                        CompleteOperation(); break;

                    // ===== SBX (AXS) *** =====
                    case 0xCB:
                        PollInterrupts(); GetImmediate();
                        {
                            int tmp = (r_A & r_X) - dl;
                            flagC = (tmp >= 0) ? (byte)1 : (byte)0;
                            r_X = (byte)tmp;
                            SetNZ(r_X);
                        }
                        CompleteOperation(); break;

                    // ===== ANE (XAA) *** =====
                    case 0x8B:
                        PollInterrupts(); GetImmediate();
                        r_A = (byte)((r_A | 0xFF) & r_X & dl); // MAGIC = 0xFF
                        SetNZ(r_A);
                        CompleteOperation(); break;

                    // ===== LXA (LAX imm) *** =====
                    case 0xAB:
                        PollInterrupts(); GetImmediate();
                        r_A = (byte)((r_A | 0xFF) & dl); // MAGIC = 0xFF
                        r_X = r_A;
                        SetNZ(r_X);
                        CompleteOperation(); break;

                    // ===== LAE (LAS) *** Abs,Y =====
                    case 0xBB:
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                            case 4: PollInterrupts();
                                dl = CpuRead(addressBus);
                                r_A = (byte)(dl & r_SP);
                                r_X = r_A;
                                r_SP = r_A;
                                SetNZ(r_A);
                                CompleteOperation(); break; }
                        break;

                    // ===== SHA (Ind),Y *** =====
                    case 0x93: // SHA (Ind),Y — alternate behavior: A & (X|magic) & H
                        switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); if (operationCycle == 4) CPU_Read = false; break;
                            case 5: PollInterrupts();
                                if ((temporaryAddress & 0xFF00) != (addressBus & 0xFF00))
                                    addressBus = (ushort)((byte)addressBus | (((addressBus >> 8) & r_X) << 8));
                                if (ignoreH) H = 0xFF;
                                CpuWrite(addressBus, (byte)(r_A & (r_X | 0xF5) & H));
                                CompleteOperation(); break; }
                        break;

                    // ===== SHA Abs,Y *** =====
                    case 0x9F: // SHA Abs,Y — alternate behavior: A & (X|magic) & H
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(false); if (operationCycle == 3) CPU_Read = false; break;
                            case 4: PollInterrupts();
                                if ((temporaryAddress & 0xFF00) != (addressBus & 0xFF00))
                                    addressBus = (ushort)((byte)addressBus | (((addressBus >> 8) & r_X) << 8));
                                if (ignoreH) H = 0xFF;
                                CpuWrite(addressBus, (byte)(r_A & (r_X | 0xF5) & H));
                                CompleteOperation(); break; }
                        break;

                    // ===== SHY Abs,X *** =====
                    case 0x9C:
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(false); if (operationCycle == 3) CPU_Read = false; break;
                            case 4: PollInterrupts();
                                if ((temporaryAddress & 0xFF00) != (addressBus & 0xFF00))
                                    addressBus = (ushort)((byte)addressBus | (((addressBus >> 8) & r_Y) << 8));
                                if (ignoreH) H = 0xFF;
                                CpuWrite(addressBus, (byte)(r_Y & H));
                                CompleteOperation(); break; }
                        break;

                    // ===== SHX Abs,Y *** =====
                    case 0x9E:
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(false); if (operationCycle == 3) CPU_Read = false; break;
                            case 4: PollInterrupts();
                                if ((temporaryAddress & 0xFF00) != (addressBus & 0xFF00))
                                    addressBus = (ushort)((byte)addressBus | (((addressBus >> 8) & r_X) << 8));
                                if (ignoreH) H = 0xFF;
                                CpuWrite(addressBus, (byte)(r_X & H));
                                CompleteOperation(); break; }
                        break;

                    // ===== SHS (TAS) Abs,Y *** =====
                    case 0x9B: // SHS — alternate behavior: A & (X|magic) & H
                        switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(false); if (operationCycle == 3) CPU_Read = false; break;
                            case 4: PollInterrupts();
                                if ((temporaryAddress & 0xFF00) != (addressBus & 0xFF00))
                                    addressBus = (ushort)((byte)addressBus | (((addressBus >> 8) & r_X) << 8));
                                r_SP = (byte)(r_A & r_X);
                                if (ignoreH) H = 0xFF;
                                CpuWrite(addressBus, (byte)(r_A & (r_X | 0xF5) & H));
                                CompleteOperation(); break; }
                        break;

                    // ===== HLT (JAM) *** =====
                    case 0x02: case 0x12: case 0x22: case 0x32: case 0x42: case 0x52:
                    case 0x62: case 0x72: case 0x92: case 0xB2: case 0xD2: case 0xF2:
                        DoHLT(); break;

                    default: // should never happen
                        break;
                }
                operationCycle++;
            }
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
