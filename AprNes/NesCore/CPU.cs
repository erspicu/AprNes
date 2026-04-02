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

        // --- Pure bus access functions ---
        // No clock advancement — MasterClockTick handles timing.
        // DMA gate is in MasterClockTick CPU gate (TriCNES: DMA check in _6502).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte CpuRead(ushort addr)
        {
            cpuBusAddr = addr;
            byte val;
            if (addr < 0x2000) { val = NES_MEM[addr & 0x7FF]; cpubus = val; }
            else { val = mem_read_fun[addr](addr); if (addr != 0x4015) cpubus = val; }
            return val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CpuWrite(ushort addr, byte val)
        {
            cpuBusAddr = addr;
            // Implicit abort: DMA cancelled on write cycle (still in halt phase)
            if (dmcImplicitAbortActive && dmcDmaHalt)
            { dmcImplicitAbortActive = false; dmcDmaRunning = false; dmcDmaHalt = false; }
            cpubus = val;
            mem_write_fun[addr](addr, val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte CpuReadZP(byte addr)
        {
            cpuBusAddr = addr;
            byte val = NES_MEM[addr]; cpubus = val;
            return val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CpuWriteZP(byte addr, byte val)
        {
            cpuBusAddr = addr;
            NES_MEM[addr] = val; cpubus = val;
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
            if (operationCycle == 1) { addressBus = CpuRead(r_PC); r_PC++; }
            else if (operationCycle == 2) { CpuReadZP((byte)addressBus); addressBus = (byte)(addressBus + r_X); } // dummy read
            else if (operationCycle == 3) { dl = CpuReadZP((byte)addressBus); }
            else { addressBus = (ushort)(dl | (CpuReadZP((byte)(addressBus + 1)) << 8)); }
        }

        static void GetAddressIndOffY(bool optionalExtraCycle)
        {
            if (optionalExtraCycle)
            {
                if (operationCycle == 1) { addressBus = CpuRead(r_PC); r_PC++; }
                else if (operationCycle == 2) { dl = CpuReadZP((byte)addressBus); }
                else if (operationCycle == 3)
                {
                    addressBus = (ushort)(dl | (CpuReadZP((byte)(addressBus + 1)) << 8));
                    temporaryAddress = addressBus;
                    H = (byte)(addressBus >> 8);
                    if (((temporaryAddress + r_Y) & 0xFF00) == (temporaryAddress & 0xFF00))
                        operationCycle++; // skip next cycle
                    addressBus = (ushort)((addressBus & 0xFF00) | ((addressBus + r_Y) & 0xFF));
                }
                else
                {
                    dl = CpuRead(addressBus); // dummy read
                    H = (byte)(addressBus >> 8);
                    H++;
                    addressBus += 0x100;
                }
            }
            else
            {
                if (operationCycle == 1) { addressBus = CpuRead(r_PC); r_PC++; }
                else if (operationCycle == 2) { dl = CpuReadZP((byte)addressBus); }
                else if (operationCycle == 3)
                {
                    addressBus = (ushort)(dl | (CpuReadZP((byte)(addressBus + 1)) << 8));
                    temporaryAddress = addressBus;
                    addressBus = (ushort)((addressBus & 0xFF00) | ((addressBus + r_Y) & 0xFF));
                }
                else
                {
                    dl = CpuRead(addressBus); // dummy read
                    H = (byte)(addressBus >> 8);
                    H++;
                    if (((temporaryAddress + r_Y) & 0xFF00) != (temporaryAddress & 0xFF00))
                        addressBus += 0x100;
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
                if (operationCycle == 1) { dl = CpuRead(r_PC); r_PC++; }
                else if (operationCycle == 2)
                {
                    addressBus = (ushort)(dl | (CpuRead(r_PC) << 8));
                    temporaryAddress = addressBus;
                    H = (byte)(addressBus >> 8);
                    if (((temporaryAddress + r_X) & 0xFF00) == (temporaryAddress & 0xFF00))
                    { operationCycle++; fixHighByte = false; } // skip next cycle
                    else { fixHighByte = true; }
                    addressBus = (ushort)((addressBus & 0xFF00) | ((addressBus + r_X) & 0xFF));
                    r_PC++;
                }
                else if (operationCycle == 3)
                {
                    dl = CpuRead(addressBus); // dummy read with wrong high byte
                    H = (byte)(addressBus >> 8); H++;
                    if (fixHighByte) addressBus += 0x100;
                }
                else { dl = CpuRead(addressBus); } // read from final address
            }
            else
            {
                if (operationCycle == 1) { dl = CpuRead(r_PC); r_PC++; }
                else if (operationCycle == 2)
                {
                    addressBus = (ushort)(dl | (CpuRead(r_PC) << 8));
                    temporaryAddress = addressBus;
                    addressBus = (ushort)((addressBus & 0xFF00) | ((addressBus + r_X) & 0xFF));
                    r_PC++;
                }
                else if (operationCycle == 3)
                {
                    dl = CpuRead(addressBus); // dummy read with possibly wrong high byte
                    H = (byte)(addressBus >> 8); H++;
                    if (((temporaryAddress + r_X) & 0xFF00) != (temporaryAddress & 0xFF00))
                        addressBus += 0x100;
                }
                else { dl = CpuRead(addressBus); } // read from final address
            }
        }

        static void GetAddressAbsOffY(bool optionalExtraCycle)
        {
            if (optionalExtraCycle)
            {
                if (operationCycle == 1) { dl = CpuRead(r_PC); r_PC++; }
                else if (operationCycle == 2)
                {
                    addressBus = (ushort)(dl | (CpuRead(r_PC) << 8));
                    temporaryAddress = addressBus;
                    H = (byte)(addressBus >> 8);
                    if (((temporaryAddress + r_Y) & 0xFF00) == (temporaryAddress & 0xFF00))
                    { operationCycle++; fixHighByte = false; } // skip next cycle
                    else { fixHighByte = true; }
                    addressBus = (ushort)((addressBus & 0xFF00) | ((addressBus + r_Y) & 0xFF));
                    r_PC++;
                }
                else if (operationCycle == 3)
                {
                    dl = CpuRead(addressBus); // dummy read with wrong high byte
                    H = (byte)(addressBus >> 8); H++;
                    if (fixHighByte) addressBus += 0x100;
                }
                else { dl = CpuRead(addressBus); } // read from final address
            }
            else
            {
                if (operationCycle == 1) { dl = CpuRead(r_PC); r_PC++; }
                else if (operationCycle == 2)
                {
                    addressBus = (ushort)(dl | (CpuRead(r_PC) << 8));
                    temporaryAddress = addressBus;
                    addressBus = (ushort)((addressBus & 0xFF00) | ((addressBus + r_Y) & 0xFF));
                    r_PC++;
                }
                else if (operationCycle == 3)
                {
                    dl = CpuRead(addressBus); // dummy read with possibly wrong high byte
                    H = (byte)(addressBus >> 8); H++;
                    if (((temporaryAddress + r_Y) & 0xFF00) != (temporaryAddress & 0xFF00))
                        addressBus += 0x100;
                }
                else { dl = CpuRead(addressBus); } // read from final address
            }
        }

        // --- Branch helper ---
        static bool branchIrqSaved; // saved irqLinePrev for branch-taken-no-cross

        static void DoBranch(bool condition)
        {
            if (operationCycle == 1)
            {
                GetImmediate();
                if (!condition) CompleteOperation();
                else branchIrqSaved = irqLinePrev; // save before taken-dummy tick
            }
            else if (operationCycle == 2)
            {
                CpuRead(addressBus); // dummy read
                temporaryAddress = (ushort)(r_PC + ((dl >= 0x80) ? -(256 - dl) : dl));
                r_PC = (ushort)((r_PC & 0xFF00) | (byte)((r_PC & 0xFF) + dl));
                addressBus = r_PC;
                if ((temporaryAddress & 0xFF00) == (r_PC & 0xFF00))
                {
                    irqLinePrev = branchIrqSaved; // restore: IRQ penultimate = pre-branch state
                    CompleteOperation();
                }
            }
            else
            {
                CpuRead(addressBus); // dummy read (page fix)
                r_PC = (ushort)((r_PC & 0xFF) | (temporaryAddress & 0xFF00));
                CompleteOperation();
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
        static void Op_09() { GetImmediate(); Op_ORA(dl); CompleteOperation(); }
        static void Op_05() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { Op_ORA(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_15() {
            if (operationCycle < 3) GetAddressZPOffX();
            else { Op_ORA(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_0D() {
            if (operationCycle < 3) GetAddressAbsolute();
            else { Op_ORA(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_1D() {
            if (operationCycle < 4) GetAddressAbsOffX(true);
            else { Op_ORA(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_19() {
            if (operationCycle < 4) GetAddressAbsOffY(true);
            else { Op_ORA(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_01() {
            if (operationCycle < 5) GetAddressIndOffX();
            else { Op_ORA(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_11() {
            if (operationCycle < 5) GetAddressIndOffY(true);
            else { Op_ORA(CpuRead(addressBus)); CompleteOperation(); }
        }

        // === AND ===
        static void Op_29() { GetImmediate(); Op_AND(dl); CompleteOperation(); }
        static void Op_25() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { Op_AND(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_35() {
            if (operationCycle < 3) GetAddressZPOffX();
            else { Op_AND(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_2D() {
            if (operationCycle < 3) GetAddressAbsolute();
            else { Op_AND(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_3D() {
            if (operationCycle < 4) GetAddressAbsOffX(true);
            else { Op_AND(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_39() {
            if (operationCycle < 4) GetAddressAbsOffY(true);
            else { Op_AND(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_21() {
            if (operationCycle < 5) GetAddressIndOffX();
            else { Op_AND(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_31() {
            if (operationCycle < 5) GetAddressIndOffY(true);
            else { Op_AND(CpuRead(addressBus)); CompleteOperation(); }
        }

        // === EOR ===
        static void Op_49() { GetImmediate(); Op_EOR(dl); CompleteOperation(); }
        static void Op_45() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { Op_EOR(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_55() {
            if (operationCycle < 3) GetAddressZPOffX();
            else { Op_EOR(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_4D() {
            if (operationCycle < 3) GetAddressAbsolute();
            else { Op_EOR(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_5D() {
            if (operationCycle < 4) GetAddressAbsOffX(true);
            else { Op_EOR(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_59() {
            if (operationCycle < 4) GetAddressAbsOffY(true);
            else { Op_EOR(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_41() {
            if (operationCycle < 5) GetAddressIndOffX();
            else { Op_EOR(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_51() {
            if (operationCycle < 5) GetAddressIndOffY(true);
            else { Op_EOR(CpuRead(addressBus)); CompleteOperation(); }
        }

        // === ADC ===
        static void Op_69() { GetImmediate(); Op_ADC(dl); CompleteOperation(); }
        static void Op_65() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { Op_ADC(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_75() {
            if (operationCycle < 3) GetAddressZPOffX();
            else { Op_ADC(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_6D() {
            if (operationCycle < 3) GetAddressAbsolute();
            else { Op_ADC(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_7D() {
            if (operationCycle < 4) GetAddressAbsOffX(true);
            else { Op_ADC(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_79() {
            if (operationCycle < 4) GetAddressAbsOffY(true);
            else { Op_ADC(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_61() {
            if (operationCycle < 5) GetAddressIndOffX();
            else { Op_ADC(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_71() {
            if (operationCycle < 5) GetAddressIndOffY(true);
            else { Op_ADC(CpuRead(addressBus)); CompleteOperation(); }
        }

        // === SBC ===
        static void Op_E9_SBC_Imm() { GetImmediate(); Op_SBC(dl); CompleteOperation(); }
        static void Op_E5() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { Op_SBC(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_F5() {
            if (operationCycle < 3) GetAddressZPOffX();
            else { Op_SBC(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_ED() {
            if (operationCycle < 3) GetAddressAbsolute();
            else { Op_SBC(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_FD() {
            if (operationCycle < 4) GetAddressAbsOffX(true);
            else { Op_SBC(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_F9() {
            if (operationCycle < 4) GetAddressAbsOffY(true);
            else { Op_SBC(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_E1() {
            if (operationCycle < 5) GetAddressIndOffX();
            else { Op_SBC(CpuRead(addressBus)); CompleteOperation(); }
        }
        static void Op_F1() {
            if (operationCycle < 5) GetAddressIndOffY(true);
            else { Op_SBC(CpuRead(addressBus)); CompleteOperation(); }
        }

        // === CMP ===
        static void Op_C9() { GetImmediate(); Op_CMP(dl, r_A); CompleteOperation(); }
        static void Op_C5() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); }
        }
        static void Op_D5() {
            if (operationCycle < 3) GetAddressZPOffX();
            else { Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); }
        }
        static void Op_CD() {
            if (operationCycle < 3) GetAddressAbsolute();
            else { Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); }
        }
        static void Op_DD() {
            if (operationCycle < 4) GetAddressAbsOffX(true);
            else { Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); }
        }
        static void Op_D9() {
            if (operationCycle < 4) GetAddressAbsOffY(true);
            else { Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); }
        }
        static void Op_C1() {
            if (operationCycle < 5) GetAddressIndOffX();
            else { Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); }
        }
        static void Op_D1() {
            if (operationCycle < 5) GetAddressIndOffY(true);
            else { Op_CMP(CpuRead(addressBus), r_A); CompleteOperation(); }
        }

        // === CPX ===
        static void Op_E0() { GetImmediate(); Op_CMP(dl, r_X); CompleteOperation(); }
        static void Op_E4() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { Op_CMP(CpuRead(addressBus), r_X); CompleteOperation(); }
        }
        static void Op_EC() {
            if (operationCycle < 3) GetAddressAbsolute();
            else { Op_CMP(CpuRead(addressBus), r_X); CompleteOperation(); }
        }

        // === CPY ===
        static void Op_C0() { GetImmediate(); Op_CMP(dl, r_Y); CompleteOperation(); }
        static void Op_C4() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { Op_CMP(CpuRead(addressBus), r_Y); CompleteOperation(); }
        }
        static void Op_CC() {
            if (operationCycle < 3) GetAddressAbsolute();
            else { Op_CMP(CpuRead(addressBus), r_Y); CompleteOperation(); }
        }

        // === LDA ===
        static void Op_A9() { GetImmediate(); r_A = dl; SetNZ(r_A); CompleteOperation(); }
        static void Op_A5() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); }
        }
        static void Op_B5() {
            if (operationCycle < 3) GetAddressZPOffX();
            else { r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); }
        }
        static void Op_AD() {
            if (operationCycle < 3) GetAddressAbsolute();
            else { r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); }
        }
        static void Op_BD() {
            if (operationCycle < 4) GetAddressAbsOffX(true);
            else { r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); }
        }
        static void Op_B9() {
            if (operationCycle < 4) GetAddressAbsOffY(true);
            else { r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); }
        }
        static void Op_A1() {
            if (operationCycle < 5) GetAddressIndOffX();
            else { r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); }
        }
        static void Op_B1() {
            if (operationCycle < 5) GetAddressIndOffY(true);
            else { r_A = CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); }
        }

        // === LDX ===
        static void Op_A2() { GetImmediate(); r_X = dl; SetNZ(r_X); CompleteOperation(); }
        static void Op_A6() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { r_X = CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); }
        }
        static void Op_B6() {
            if (operationCycle < 3) GetAddressZPOffY();
            else { r_X = CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); }
        }
        static void Op_AE() {
            if (operationCycle < 3) GetAddressAbsolute();
            else { r_X = CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); }
        }
        static void Op_BE() {
            if (operationCycle < 4) GetAddressAbsOffY(true);
            else { r_X = CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); }
        }

        // === LDY ===
        static void Op_A0() { GetImmediate(); r_Y = dl; SetNZ(r_Y); CompleteOperation(); }
        static void Op_A4() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { r_Y = CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); }
        }
        static void Op_B4() {
            if (operationCycle < 3) GetAddressZPOffX();
            else { r_Y = CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); }
        }
        static void Op_AC() {
            if (operationCycle < 3) GetAddressAbsolute();
            else { r_Y = CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); }
        }
        static void Op_BC() {
            if (operationCycle < 4) GetAddressAbsOffX(true);
            else { r_Y = CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); }
        }

        // === STA ===
        static void Op_85() {
            if (operationCycle == 1) { GetAddressZeroPage(); }
            else { CpuWrite(addressBus, r_A); CompleteOperation(); }
        }
        static void Op_95() {
            if (operationCycle < 3) GetAddressZPOffX();
            else { CpuWrite(addressBus, r_A); CompleteOperation(); }
        }
        static void Op_8D() {
            if (operationCycle < 3) GetAddressAbsolute();
            else { CpuWrite(addressBus, r_A); CompleteOperation(); }
        }
        static void Op_9D() {
            if (operationCycle < 4) GetAddressAbsOffX(false);
            else { CpuWrite(addressBus, r_A); CompleteOperation(); }
        }
        static void Op_99() {
            if (operationCycle < 4) GetAddressAbsOffY(false);
            else { CpuWrite(addressBus, r_A); CompleteOperation(); }
        }
        static void Op_81() {
            if (operationCycle < 5) GetAddressIndOffX();
            else { CpuWrite(addressBus, r_A); CompleteOperation(); }
        }
        static void Op_91() {
            if (operationCycle < 5) GetAddressIndOffY(false);
            else { CpuWrite(addressBus, r_A); CompleteOperation(); }
        }

        // === STX ===
        static void Op_86() {
            if (operationCycle == 1) { GetAddressZeroPage(); }
            else { CpuWrite(addressBus, r_X); CompleteOperation(); }
        }
        static void Op_96() {
            if (operationCycle < 3) GetAddressZPOffY();
            else { CpuWrite(addressBus, r_X); CompleteOperation(); }
        }
        static void Op_8E() {
            if (operationCycle < 3) GetAddressAbsolute();
            else { CpuWrite(addressBus, r_X); CompleteOperation(); }
        }

        // === STY ===
        static void Op_84() {
            if (operationCycle == 1) { GetAddressZeroPage(); }
            else { CpuWrite(addressBus, r_Y); CompleteOperation(); }
        }
        static void Op_94() {
            if (operationCycle < 3) GetAddressZPOffX();
            else { CpuWrite(addressBus, r_Y); CompleteOperation(); }
        }
        static void Op_8C() {
            if (operationCycle < 3) GetAddressAbsolute();
            else { CpuWrite(addressBus, r_Y); CompleteOperation(); }
        }

        // === BIT ===
        static void Op_24() {
            switch (operationCycle) { case 1: GetAddressZeroPage(); break;
                case 2: dl = CpuRead(addressBus);
                    flagZ = (byte)(((r_A & dl) == 0) ? 1 : 0);
                    flagN = (byte)((dl & 0x80) >> 7);
                    flagV = (byte)((dl & 0x40) >> 6);
                    CompleteOperation(); break; }
        }
        static void Op_2C() {
            switch (operationCycle) { case 1: case 2: GetAddressAbsolute(); break;
                case 3: dl = CpuRead(addressBus);
                    flagZ = (byte)(((r_A & dl) == 0) ? 1 : 0);
                    flagN = (byte)((dl & 0x80) >> 7);
                    flagV = (byte)((dl & 0x40) >> 6);
                    CompleteOperation(); break; }
        }

        // === ASL ===
        static void Op_0A() {
            CpuRead(addressBus);
            flagC = (byte)((r_A & 0x80) >> 7); r_A <<= 1; SetNZ(r_A);
            CompleteOperation();
        }
        static void Op_06() {
            if (operationCycle < 2) GetAddressZeroPage();
            else if (operationCycle == 2) { dl = CpuRead(addressBus); }
            else if (operationCycle == 3) { CpuWrite(addressBus, dl); }
            else { Op_ASL_mem(addressBus); CompleteOperation(); }
        }
        static void Op_16() {
            if (operationCycle < 3) GetAddressZPOffX();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_ASL_mem(addressBus); CompleteOperation(); }
        }
        static void Op_0E() {
            if (operationCycle < 3) GetAddressAbsolute();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_ASL_mem(addressBus); CompleteOperation(); }
        }
        static void Op_1E() {
            if (operationCycle < 5) GetAddressAbsOffX(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { Op_ASL_mem(addressBus); CompleteOperation(); }
        }

        // === LSR ===
        static void Op_4A() {
            CpuRead(addressBus);
            flagC = (byte)(r_A & 1); r_A >>= 1; SetNZ(r_A);
            CompleteOperation();
        }
        static void Op_46() {
            if (operationCycle < 2) GetAddressZeroPage();
            else if (operationCycle == 2) { dl = CpuRead(addressBus); }
            else if (operationCycle == 3) { CpuWrite(addressBus, dl); }
            else { Op_LSR_mem(addressBus); CompleteOperation(); }
        }
        static void Op_56() {
            if (operationCycle < 3) GetAddressZPOffX();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_LSR_mem(addressBus); CompleteOperation(); }
        }
        static void Op_4E() {
            if (operationCycle < 3) GetAddressAbsolute();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_LSR_mem(addressBus); CompleteOperation(); }
        }
        static void Op_5E() {
            if (operationCycle < 5) GetAddressAbsOffX(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { Op_LSR_mem(addressBus); CompleteOperation(); }
        }

        // === ROL ===
        static void Op_2A() {
            CpuRead(addressBus);
            { byte oc = flagC; flagC = (byte)((r_A & 0x80) >> 7); r_A = (byte)((r_A << 1) | oc); SetNZ(r_A); }
            CompleteOperation();
        }
        static void Op_26() {
            if (operationCycle < 2) GetAddressZeroPage();
            else if (operationCycle == 2) { dl = CpuRead(addressBus); }
            else if (operationCycle == 3) { CpuWrite(addressBus, dl); }
            else { Op_ROL_mem(addressBus); CompleteOperation(); }
        }
        static void Op_36() {
            if (operationCycle < 3) GetAddressZPOffX();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_ROL_mem(addressBus); CompleteOperation(); }
        }
        static void Op_2E() {
            if (operationCycle < 3) GetAddressAbsolute();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_ROL_mem(addressBus); CompleteOperation(); }
        }
        static void Op_3E() {
            if (operationCycle < 5) GetAddressAbsOffX(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { Op_ROL_mem(addressBus); CompleteOperation(); }
        }

        // === ROR ===
        static void Op_6A() {
            CpuRead(addressBus);
            { byte oc = flagC; flagC = (byte)(r_A & 1); r_A = (byte)((r_A >> 1) | (oc << 7)); SetNZ(r_A); }
            CompleteOperation();
        }
        static void Op_66() {
            if (operationCycle < 2) GetAddressZeroPage();
            else if (operationCycle == 2) { dl = CpuRead(addressBus); }
            else if (operationCycle == 3) { CpuWrite(addressBus, dl); }
            else { Op_ROR_mem(addressBus); CompleteOperation(); }
        }
        static void Op_76() {
            if (operationCycle < 3) GetAddressZPOffX();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_ROR_mem(addressBus); CompleteOperation(); }
        }
        static void Op_6E() {
            if (operationCycle < 3) GetAddressAbsolute();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_ROR_mem(addressBus); CompleteOperation(); }
        }
        static void Op_7E() {
            if (operationCycle < 5) GetAddressAbsOffX(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { Op_ROR_mem(addressBus); CompleteOperation(); }
        }

        // === INC ===
        static void Op_E6() {
            if (operationCycle < 2) GetAddressZeroPage();
            else if (operationCycle == 2) { dl = CpuRead(addressBus); }
            else if (operationCycle == 3) { CpuWrite(addressBus, dl); }
            else { Op_INC_mem(addressBus); CompleteOperation(); }
        }
        static void Op_F6() {
            if (operationCycle < 3) GetAddressZPOffX();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_INC_mem(addressBus); CompleteOperation(); }
        }
        static void Op_EE() {
            if (operationCycle < 3) GetAddressAbsolute();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_INC_mem(addressBus); CompleteOperation(); }
        }
        static void Op_FE() {
            if (operationCycle < 5) GetAddressAbsOffX(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { Op_INC_mem(addressBus); CompleteOperation(); }
        }

        // === DEC ===
        static void Op_C6() {
            if (operationCycle < 2) GetAddressZeroPage();
            else if (operationCycle == 2) { dl = CpuRead(addressBus); }
            else if (operationCycle == 3) { CpuWrite(addressBus, dl); }
            else { Op_DEC_mem(addressBus); CompleteOperation(); }
        }
        static void Op_D6() {
            if (operationCycle < 3) GetAddressZPOffX();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_DEC_mem(addressBus); CompleteOperation(); }
        }
        static void Op_CE() {
            if (operationCycle < 3) GetAddressAbsolute();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_DEC_mem(addressBus); CompleteOperation(); }
        }
        static void Op_DE() {
            if (operationCycle < 5) GetAddressAbsOffX(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { Op_DEC_mem(addressBus); CompleteOperation(); }
        }

        // === INX / INY / DEX / DEY ===
        static void Op_E8() { CpuRead(addressBus); r_X++; SetNZ(r_X); CompleteOperation(); }
        static void Op_C8() { CpuRead(addressBus); r_Y++; SetNZ(r_Y); CompleteOperation(); }
        static void Op_CA() { CpuRead(addressBus); r_X--; SetNZ(r_X); CompleteOperation(); }
        static void Op_88() { CpuRead(addressBus); r_Y--; SetNZ(r_Y); CompleteOperation(); }

        // === Transfer ===
        static void Op_AA() { r_X = r_A; CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); }
        static void Op_8A() { r_A = r_X; CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); }
        static void Op_A8() { r_Y = r_A; CpuRead(addressBus); SetNZ(r_Y); CompleteOperation(); }
        static void Op_98() { r_A = r_Y; CpuRead(addressBus); SetNZ(r_A); CompleteOperation(); }
        static void Op_BA() { r_X = r_SP; CpuRead(addressBus); SetNZ(r_X); CompleteOperation(); }
        static void Op_9A() { r_SP = r_X; CpuRead(addressBus); CompleteOperation(); }

        // === Flag instructions ===
        static void Op_18() { CpuRead(addressBus); flagC = 0; CompleteOperation(); }
        static void Op_38() { CpuRead(addressBus); flagC = 1; CompleteOperation(); }
        static void Op_58() { CpuRead(addressBus); flagI = 0; CompleteOperation(); }
        static void Op_78() { CpuRead(addressBus); flagI = 1; CompleteOperation(); }
        static void Op_D8() { CpuRead(addressBus); flagD = 0; CompleteOperation(); }
        static void Op_F8() { CpuRead(addressBus); flagD = 1; CompleteOperation(); }
        static void Op_B8() { CpuRead(addressBus); flagV = 0; CompleteOperation(); }

        // === Stack instructions ===
        static void Op_48() {
            if (operationCycle < 2) CpuRead(addressBus);
            else { StackPush(r_A); CompleteOperation(); }
        }
        static void Op_08() {
            if (operationCycle < 2) CpuRead(addressBus);
            else { StackPush((byte)(GetFlag() | 0x30)); CompleteOperation(); }
        }
        static void Op_68() {
            if (operationCycle < 2) CpuRead(addressBus);
            else if (operationCycle == 2) { CpuRead((ushort)(0x100 | r_SP)); r_SP++; }
            else { r_A = CpuRead((ushort)(0x100 | r_SP)); SetNZ(r_A); CompleteOperation(); }
        }
        static void Op_28() {
            if (operationCycle < 2) CpuRead(addressBus);
            else if (operationCycle == 2) { CpuRead((ushort)(0x100 | r_SP)); r_SP++; }
            else { SetFlag(CpuRead((ushort)(0x100 | r_SP))); CompleteOperation(); }
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
            else { GetAddressAbsolute(); r_PC = addressBus; CompleteOperation(); }
        }
        static void Op_6C() {
            switch (operationCycle) {
                case 1: case 2: GetAddressAbsolute(); break;
                case 3: specialBus = CpuRead(addressBus); break;
                case 4:
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
                    r_SP = (byte)addressBus; GetImmediate(); CompleteOperation(); break;
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
                    dl = CpuRead(addressBus);
                    r_PC = (ushort)((r_PC & 0xFF) | (dl << 8)); r_SP = (byte)addressBus;
                    CompleteOperation(); break;
            }
        }

        // === NOP (implied) ===
        static void Op_NOP() { CpuRead(addressBus); CompleteOperation(); }

        // === DOP Immediate ===
        static void Op_DOP_Imm() { GetImmediate(); CompleteOperation(); }

        // === DOP ZeroPage ===
        static void Op_DOP_ZP() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { CpuRead(addressBus); CompleteOperation(); }
        }

        // === DOP ZeroPage,X ===
        static void Op_DOP_ZPX() {
            if (operationCycle < 3) GetAddressZPOffX();
            else { CpuRead(addressBus); CompleteOperation(); }
        }

        // === TOP Absolute ===
        static void Op_0C() {
            if (operationCycle < 3) GetAddressAbsolute();
            else { CpuRead(addressBus); CompleteOperation(); }
        }

        // === TOP Absolute,X ===
        static void Op_TOP_AbsX() {
            if (operationCycle < 4) GetAddressAbsOffX(true);
            else { CpuRead(addressBus); CompleteOperation(); }
        }

        // === SLO ===
        static void Op_07() {
            if (operationCycle < 2) GetAddressZeroPage();
            else if (operationCycle == 2) { dl = CpuRead(addressBus); }
            else if (operationCycle == 3) { CpuWrite(addressBus, dl); }
            else { Op_SLO(addressBus); CompleteOperation(); }
        }
        static void Op_17() {
            if (operationCycle < 3) GetAddressZPOffX();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_SLO(addressBus); CompleteOperation(); }
        }
        static void Op_0F() {
            if (operationCycle < 3) GetAddressAbsolute();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_SLO(addressBus); CompleteOperation(); }
        }
        static void Op_1F() {
            if (operationCycle < 5) GetAddressAbsOffX(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { Op_SLO(addressBus); CompleteOperation(); }
        }
        static void Op_1B() {
            if (operationCycle < 5) GetAddressAbsOffY(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { Op_SLO(addressBus); CompleteOperation(); }
        }
        static void Op_03() {
            if (operationCycle < 5) GetAddressIndOffX();
            else if (operationCycle == 5) { dl = CpuRead(addressBus); }
            else if (operationCycle == 6) { CpuWrite(addressBus, dl); }
            else { Op_SLO(addressBus); CompleteOperation(); }
        }
        static void Op_13() {
            if (operationCycle < 5) GetAddressIndOffY(false);
            else if (operationCycle == 5) { dl = CpuRead(addressBus); }
            else if (operationCycle == 6) { CpuWrite(addressBus, dl); }
            else { Op_SLO(addressBus); CompleteOperation(); }
        }

        // === RLA ===
        static void Op_27() {
            if (operationCycle < 2) GetAddressZeroPage();
            else if (operationCycle == 2) { dl = CpuRead(addressBus); }
            else if (operationCycle == 3) { CpuWrite(addressBus, dl); }
            else { Op_RLA(addressBus); CompleteOperation(); }
        }
        static void Op_37() {
            if (operationCycle < 3) GetAddressZPOffX();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_RLA(addressBus); CompleteOperation(); }
        }
        static void Op_2F() {
            if (operationCycle < 3) GetAddressAbsolute();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_RLA(addressBus); CompleteOperation(); }
        }
        static void Op_3F() {
            if (operationCycle < 5) GetAddressAbsOffX(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { Op_RLA(addressBus); CompleteOperation(); }
        }
        static void Op_3B() {
            if (operationCycle < 5) GetAddressAbsOffY(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { Op_RLA(addressBus); CompleteOperation(); }
        }
        static void Op_23() {
            if (operationCycle < 5) GetAddressIndOffX();
            else if (operationCycle == 5) { dl = CpuRead(addressBus); }
            else if (operationCycle == 6) { CpuWrite(addressBus, dl); }
            else { Op_RLA(addressBus); CompleteOperation(); }
        }
        static void Op_33() {
            if (operationCycle < 5) GetAddressIndOffY(false);
            else if (operationCycle == 5) { dl = CpuRead(addressBus); }
            else if (operationCycle == 6) { CpuWrite(addressBus, dl); }
            else { Op_RLA(addressBus); CompleteOperation(); }
        }

        // === SRE ===
        static void Op_47() {
            if (operationCycle < 2) GetAddressZeroPage();
            else if (operationCycle == 2) { dl = CpuRead(addressBus); }
            else if (operationCycle == 3) { CpuWrite(addressBus, dl); }
            else { Op_SRE(addressBus); CompleteOperation(); }
        }
        static void Op_57() {
            if (operationCycle < 3) GetAddressZPOffX();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_SRE(addressBus); CompleteOperation(); }
        }
        static void Op_4F() {
            if (operationCycle < 3) GetAddressAbsolute();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_SRE(addressBus); CompleteOperation(); }
        }
        static void Op_5F() {
            if (operationCycle < 5) GetAddressAbsOffX(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { Op_SRE(addressBus); CompleteOperation(); }
        }
        static void Op_5B() {
            if (operationCycle < 5) GetAddressAbsOffY(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { Op_SRE(addressBus); CompleteOperation(); }
        }
        static void Op_43() {
            if (operationCycle < 5) GetAddressIndOffX();
            else if (operationCycle == 5) { dl = CpuRead(addressBus); }
            else if (operationCycle == 6) { CpuWrite(addressBus, dl); }
            else { Op_SRE(addressBus); CompleteOperation(); }
        }
        static void Op_53() {
            if (operationCycle < 5) GetAddressIndOffY(false);
            else if (operationCycle == 5) { dl = CpuRead(addressBus); }
            else if (operationCycle == 6) { CpuWrite(addressBus, dl); }
            else { Op_SRE(addressBus); CompleteOperation(); }
        }

        // === RRA ===
        static void Op_67() {
            if (operationCycle < 2) GetAddressZeroPage();
            else if (operationCycle == 2) { dl = CpuRead(addressBus); }
            else if (operationCycle == 3) { CpuWrite(addressBus, dl); }
            else { Op_RRA(addressBus); CompleteOperation(); }
        }
        static void Op_77() {
            if (operationCycle < 3) GetAddressZPOffX();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_RRA(addressBus); CompleteOperation(); }
        }
        static void Op_6F() {
            if (operationCycle < 3) GetAddressAbsolute();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_RRA(addressBus); CompleteOperation(); }
        }
        static void Op_7F() {
            if (operationCycle < 5) GetAddressAbsOffX(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { Op_RRA(addressBus); CompleteOperation(); }
        }
        static void Op_7B() {
            if (operationCycle < 5) GetAddressAbsOffY(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { Op_RRA(addressBus); CompleteOperation(); }
        }
        static void Op_63() {
            if (operationCycle < 5) GetAddressIndOffX();
            else if (operationCycle == 5) { dl = CpuRead(addressBus); }
            else if (operationCycle == 6) { CpuWrite(addressBus, dl); }
            else { Op_RRA(addressBus); CompleteOperation(); }
        }
        static void Op_73() {
            if (operationCycle < 5) GetAddressIndOffY(false);
            else if (operationCycle == 5) { dl = CpuRead(addressBus); }
            else if (operationCycle == 6) { CpuWrite(addressBus, dl); }
            else { Op_RRA(addressBus); CompleteOperation(); }
        }

        // === SAX ===
        static void Op_87() {
            if (operationCycle == 1) { GetAddressZeroPage(); }
            else { CpuWrite(addressBus, (byte)(r_A & r_X)); CompleteOperation(); }
        }
        static void Op_97() {
            if (operationCycle < 3) GetAddressZPOffY();
            else { CpuWrite(addressBus, (byte)(r_A & r_X)); CompleteOperation(); }
        }
        static void Op_8F() {
            if (operationCycle < 3) GetAddressAbsolute();
            else { CpuWrite(addressBus, (byte)(r_A & r_X)); CompleteOperation(); }
        }
        static void Op_83() {
            if (operationCycle < 5) GetAddressIndOffX();
            else { CpuWrite(addressBus, (byte)(r_A & r_X)); CompleteOperation(); }
        }

        // === LAX ===
        static void Op_A7() {
            if (operationCycle == 1) GetAddressZeroPage();
            else { r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); }
        }
        static void Op_B7() {
            if (operationCycle < 3) GetAddressZPOffY();
            else { r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); }
        }
        static void Op_AF() {
            if (operationCycle < 3) GetAddressAbsolute();
            else { r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); }
        }
        static void Op_BF() {
            if (operationCycle < 4) GetAddressAbsOffY(true);
            else { r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); }
        }
        static void Op_A3() {
            if (operationCycle < 5) GetAddressIndOffX();
            else { r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); }
        }
        static void Op_B3() {
            if (operationCycle < 5) GetAddressIndOffY(true);
            else { r_A = CpuRead(addressBus); r_X = r_A; SetNZ(r_X); CompleteOperation(); }
        }

        // === DCP ===
        static void Op_C7() {
            if (operationCycle < 2) GetAddressZeroPage();
            else if (operationCycle == 2) { dl = CpuRead(addressBus); }
            else if (operationCycle == 3) { CpuWrite(addressBus, dl); }
            else { Op_DCP(addressBus); CompleteOperation(); }
        }
        static void Op_D7() {
            if (operationCycle < 3) GetAddressZPOffX();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_DCP(addressBus); CompleteOperation(); }
        }
        static void Op_CF() {
            if (operationCycle < 3) GetAddressAbsolute();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_DCP(addressBus); CompleteOperation(); }
        }
        static void Op_DF() {
            if (operationCycle < 5) GetAddressAbsOffX(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { Op_DCP(addressBus); CompleteOperation(); }
        }
        static void Op_DB() {
            if (operationCycle < 5) GetAddressAbsOffY(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { Op_DCP(addressBus); CompleteOperation(); }
        }
        static void Op_C3() {
            if (operationCycle < 5) GetAddressIndOffX();
            else if (operationCycle == 5) { dl = CpuRead(addressBus); }
            else if (operationCycle == 6) { CpuWrite(addressBus, dl); }
            else { Op_DCP(addressBus); CompleteOperation(); }
        }
        static void Op_D3() {
            if (operationCycle < 5) GetAddressIndOffY(false);
            else if (operationCycle == 5) { dl = CpuRead(addressBus); }
            else if (operationCycle == 6) { CpuWrite(addressBus, dl); }
            else { Op_DCP(addressBus); CompleteOperation(); }
        }

        // === ISC ===
        static void Op_E7() {
            if (operationCycle < 2) GetAddressZeroPage();
            else if (operationCycle == 2) { dl = CpuRead(addressBus); }
            else if (operationCycle == 3) { CpuWrite(addressBus, dl); }
            else { Op_ISC(addressBus); CompleteOperation(); }
        }
        static void Op_F7() {
            if (operationCycle < 3) GetAddressZPOffX();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_ISC(addressBus); CompleteOperation(); }
        }
        static void Op_EF() {
            if (operationCycle < 3) GetAddressAbsolute();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { Op_ISC(addressBus); CompleteOperation(); }
        }
        static void Op_FF() {
            if (operationCycle < 5) GetAddressAbsOffX(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { Op_ISC(addressBus); CompleteOperation(); }
        }
        static void Op_FB() {
            if (operationCycle < 5) GetAddressAbsOffY(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { Op_ISC(addressBus); CompleteOperation(); }
        }
        static void Op_E3() {
            if (operationCycle < 5) GetAddressIndOffX();
            else if (operationCycle == 5) { dl = CpuRead(addressBus); }
            else if (operationCycle == 6) { CpuWrite(addressBus, dl); }
            else { Op_ISC(addressBus); CompleteOperation(); }
        }
        static void Op_F3() {
            if (operationCycle < 5) GetAddressIndOffY(false);
            else if (operationCycle == 5) { dl = CpuRead(addressBus); }
            else if (operationCycle == 6) { CpuWrite(addressBus, dl); }
            else { Op_ISC(addressBus); CompleteOperation(); }
        }

        // === ANC ===
        static void Op_ANC() {
            GetImmediate();
            r_A = (byte)(r_A & dl); flagC = (byte)((r_A & 0x80) >> 7); SetNZ(r_A);
            CompleteOperation();
        }

        // === ALR ===
        static void Op_4B() {
            GetImmediate();
            r_A = (byte)(r_A & dl); flagC = (byte)(r_A & 1); r_A >>= 1; SetNZ(r_A);
            CompleteOperation();
        }

        // === ARR ===
        static void Op_6B() {
            GetImmediate();
            r_A = (byte)(r_A & dl);
            { byte oc = flagC; flagC = (byte)(r_A & 1); r_A = (byte)((r_A >> 1) | (oc << 7)); }
            SetNZ(r_A);
            flagC = (byte)((r_A & 0x40) >> 6);
            flagV = (byte)((((r_A >> 5) ^ (r_A >> 6)) & 1));
            CompleteOperation();
        }

        // === SBX ===
        static void Op_CB() {
            GetImmediate();
            { int tmp = (r_A & r_X) - dl; flagC = (tmp >= 0) ? (byte)1 : (byte)0; r_X = (byte)tmp; SetNZ(r_X); }
            CompleteOperation();
        }

        // === ANE ===
        static void Op_8B() {
            GetImmediate();
            r_A = (byte)((r_A | 0xFF) & r_X & dl);
            SetNZ(r_A); CompleteOperation();
        }

        // === LXA ===
        static void Op_AB() {
            GetImmediate();
            r_A = (byte)((r_A | 0xFF) & dl); r_X = r_A; SetNZ(r_X);
            CompleteOperation();
        }

        // === LAE ===
        static void Op_BB() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(true); break;
                case 4:
                    dl = CpuRead(addressBus); r_A = (byte)(dl & r_SP); r_X = r_A; r_SP = r_A; SetNZ(r_A);
                    CompleteOperation(); break; }
        }

        // === SHA (Ind),Y ===
        static void Op_93() {
            switch (operationCycle) { case 1: case 2: case 3: case 4: GetAddressIndOffY(false); break;
                case 5:
                    if ((temporaryAddress & 0xFF00) != (addressBus & 0xFF00))
                        addressBus = (ushort)((byte)addressBus | (((addressBus >> 8) & r_X) << 8));
                    if (ignoreH) H = 0xFF;
                    CpuWrite(addressBus, (byte)(r_A & (r_X | 0xF5) & H));
                    CompleteOperation(); break; }
        }

        // === SHA Abs,Y ===
        static void Op_9F() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(false); break;
                case 4:
                    if ((temporaryAddress & 0xFF00) != (addressBus & 0xFF00))
                        addressBus = (ushort)((byte)addressBus | (((addressBus >> 8) & r_X) << 8));
                    if (ignoreH) H = 0xFF;
                    CpuWrite(addressBus, (byte)(r_A & (r_X | 0xF5) & H));
                    CompleteOperation(); break; }
        }

        // === SHY Abs,X ===
        static void Op_9C() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffX(false); break;
                case 4:
                    if ((temporaryAddress & 0xFF00) != (addressBus & 0xFF00))
                        addressBus = (ushort)((byte)addressBus | (((addressBus >> 8) & r_Y) << 8));
                    if (ignoreH) H = 0xFF;
                    CpuWrite(addressBus, (byte)(r_Y & H));
                    CompleteOperation(); break; }
        }

        // === SHX Abs,Y ===
        static void Op_9E() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(false); break;
                case 4:
                    if ((temporaryAddress & 0xFF00) != (addressBus & 0xFF00))
                        addressBus = (ushort)((byte)addressBus | (((addressBus >> 8) & r_X) << 8));
                    if (ignoreH) H = 0xFF;
                    CpuWrite(addressBus, (byte)(r_X & H));
                    CompleteOperation(); break; }
        }

        // === SHS Abs,Y ===
        static void Op_9B() {
            switch (operationCycle) { case 1: case 2: case 3: GetAddressAbsOffY(false); break;
                case 4:
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
            delegate*<void>[] t =
            {
                &Op_00                , &Op_01                , &Op_HLT               , &Op_03                , &Op_DOP_ZP            , &Op_05                , &Op_06                , &Op_07                ,  // 0x00-0x07
                &Op_08                , &Op_09                , &Op_0A                , &Op_ANC               , &Op_0C                , &Op_0D                , &Op_0E                , &Op_0F                ,  // 0x08-0x0f
                &Op_10                , &Op_11                , &Op_HLT               , &Op_13                , &Op_DOP_ZPX           , &Op_15                , &Op_16                , &Op_17                ,  // 0x10-0x17
                &Op_18                , &Op_19                , &Op_NOP               , &Op_1B                , &Op_TOP_AbsX          , &Op_1D                , &Op_1E                , &Op_1F                ,  // 0x18-0x1f
                &Op_20                , &Op_21                , &Op_HLT               , &Op_23                , &Op_24                , &Op_25                , &Op_26                , &Op_27                ,  // 0x20-0x27
                &Op_28                , &Op_29                , &Op_2A                , &Op_ANC               , &Op_2C                , &Op_2D                , &Op_2E                , &Op_2F                ,  // 0x28-0x2f
                &Op_30                , &Op_31                , &Op_HLT               , &Op_33                , &Op_DOP_ZPX           , &Op_35                , &Op_36                , &Op_37                ,  // 0x30-0x37
                &Op_38                , &Op_39                , &Op_NOP               , &Op_3B                , &Op_TOP_AbsX          , &Op_3D                , &Op_3E                , &Op_3F                ,  // 0x38-0x3f
                &Op_40                , &Op_41                , &Op_HLT               , &Op_43                , &Op_DOP_ZP            , &Op_45                , &Op_46                , &Op_47                ,  // 0x40-0x47
                &Op_48                , &Op_49                , &Op_4A                , &Op_4B                , &Op_4C                , &Op_4D                , &Op_4E                , &Op_4F                ,  // 0x48-0x4f
                &Op_50                , &Op_51                , &Op_HLT               , &Op_53                , &Op_DOP_ZPX           , &Op_55                , &Op_56                , &Op_57                ,  // 0x50-0x57
                &Op_58                , &Op_59                , &Op_NOP               , &Op_5B                , &Op_TOP_AbsX          , &Op_5D                , &Op_5E                , &Op_5F                ,  // 0x58-0x5f
                &Op_60                , &Op_61                , &Op_HLT               , &Op_63                , &Op_DOP_ZP            , &Op_65                , &Op_66                , &Op_67                ,  // 0x60-0x67
                &Op_68                , &Op_69                , &Op_6A                , &Op_6B                , &Op_6C                , &Op_6D                , &Op_6E                , &Op_6F                ,  // 0x68-0x6f
                &Op_70                , &Op_71                , &Op_HLT               , &Op_73                , &Op_DOP_ZPX           , &Op_75                , &Op_76                , &Op_77                ,  // 0x70-0x77
                &Op_78                , &Op_79                , &Op_NOP               , &Op_7B                , &Op_TOP_AbsX          , &Op_7D                , &Op_7E                , &Op_7F                ,  // 0x78-0x7f
                &Op_DOP_Imm           , &Op_81                , &Op_DOP_Imm           , &Op_83                , &Op_84                , &Op_85                , &Op_86                , &Op_87                ,  // 0x80-0x87
                &Op_88                , &Op_DOP_Imm           , &Op_8A                , &Op_8B                , &Op_8C                , &Op_8D                , &Op_8E                , &Op_8F                ,  // 0x88-0x8f
                &Op_90                , &Op_91                , &Op_HLT               , &Op_93                , &Op_94                , &Op_95                , &Op_96                , &Op_97                ,  // 0x90-0x97
                &Op_98                , &Op_99                , &Op_9A                , &Op_9B                , &Op_9C                , &Op_9D                , &Op_9E                , &Op_9F                ,  // 0x98-0x9f
                &Op_A0                , &Op_A1                , &Op_A2                , &Op_A3                , &Op_A4                , &Op_A5                , &Op_A6                , &Op_A7                ,  // 0xa0-0xa7
                &Op_A8                , &Op_A9                , &Op_AA                , &Op_AB                , &Op_AC                , &Op_AD                , &Op_AE                , &Op_AF                ,  // 0xa8-0xaf
                &Op_B0                , &Op_B1                , &Op_HLT               , &Op_B3                , &Op_B4                , &Op_B5                , &Op_B6                , &Op_B7                ,  // 0xb0-0xb7
                &Op_B8                , &Op_B9                , &Op_BA                , &Op_BB                , &Op_BC                , &Op_BD                , &Op_BE                , &Op_BF                ,  // 0xb8-0xbf
                &Op_C0                , &Op_C1                , &Op_DOP_Imm           , &Op_C3                , &Op_C4                , &Op_C5                , &Op_C6                , &Op_C7                ,  // 0xc0-0xc7
                &Op_C8                , &Op_C9                , &Op_CA                , &Op_CB                , &Op_CC                , &Op_CD                , &Op_CE                , &Op_CF                ,  // 0xc8-0xcf
                &Op_D0                , &Op_D1                , &Op_HLT               , &Op_D3                , &Op_DOP_ZPX           , &Op_D5                , &Op_D6                , &Op_D7                ,  // 0xd0-0xd7
                &Op_D8                , &Op_D9                , &Op_NOP               , &Op_DB                , &Op_TOP_AbsX          , &Op_DD                , &Op_DE                , &Op_DF                ,  // 0xd8-0xdf
                &Op_E0                , &Op_E1                , &Op_DOP_Imm           , &Op_E3                , &Op_E4                , &Op_E5                , &Op_E6                , &Op_E7                ,  // 0xe0-0xe7
                &Op_E8                , &Op_E9_SBC_Imm        , &Op_NOP               , &Op_E9_SBC_Imm        , &Op_EC                , &Op_ED                , &Op_EE                , &Op_EF                ,  // 0xe8-0xef
                &Op_F0                , &Op_F1                , &Op_HLT               , &Op_F3                , &Op_DOP_ZPX           , &Op_F5                , &Op_F6                , &Op_F7                ,  // 0xf0-0xf7
                &Op_F8                , &Op_F9                , &Op_NOP               , &Op_FB                , &Op_TOP_AbsX          , &Op_FD                , &Op_FE                , &Op_FF                  // 0xf8-0xff
            };
            Array.Copy(t, opFnPtrs, 256);
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
