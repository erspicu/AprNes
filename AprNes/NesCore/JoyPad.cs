using System.Runtime.CompilerServices;
using System.Threading;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        // TriCNES controller model: 8-bit shift register + 2-cycle deferred shift
        // Button layout: bit 7=A (MSB, read first), bit 6=B, ..., bit 0=Right (LSB, read last)

        // Current button state (written by UI thread + DirectInput polling thread
        // concurrently). Promoted to int so Interlocked.Or/And can do lock-free
        // atomic RMW — protects against the read-modify-write race that would
        // otherwise cause occasional "lost update" (stuck or dropped buttons)
        // when keyboard and gamepad events land within a few nanoseconds of
        // each other. Reader path (P1_ShiftRegister = (byte)P1_Port at strobe)
        // stays lock-free — single-int load is atomic per ECMA-335.
        static int P1_Port = 0;
        static int P2_Port = 0;

        // 8-bit parallel-to-serial shift registers
        // MSB is read first; after shift left, bit 0 is filled with 1
        static byte P1_ShiftRegister = 0;
        static byte P2_ShiftRegister = 0;

        // 2-cycle delay counters (TriCNES: Controller1ShiftCounter/Controller2ShiftCounter)
        // Set to 2 on read, decremented in APU step; shift occurs when counter reaches 0
        static byte P1_ShiftCounter = 0;
        static byte P2_ShiftCounter = 0;

        // Strobe state (TriCNES: APU_ControllerPortsStrobing / APU_ControllerPortsStrobed)
        static bool controllerStrobing = false;   // $4016 bit 0 — while true, shift registers reload
        static bool controllerStrobed = false;     // Whether strobe has been processed this frame

        // Lock-free atomic OR / AND. On .NET 5+ (AprNesAvalonia) uses the direct
        // Interlocked.Or/And intrinsics — a single LOCK OR / LOCK AND instruction
        // (~5-10 cycles). On .NET Framework 4.8.1 (AprNes NetFx) falls back to a
        // CompareExchange spin loop (~15-25 cycles uncontended). In both cases the
        // contention rate for controller input is essentially zero, so typical cost
        // is a single atomic RMW.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AtomicOrInt(ref int location, int value)
        {
#if NET5_0_OR_GREATER
            Interlocked.Or(ref location, value);
#else
            int orig, updated;
            do { orig = location; updated = orig | value; }
            while (Interlocked.CompareExchange(ref location, updated, orig) != orig);
#endif
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AtomicAndInt(ref int location, int value)
        {
#if NET5_0_OR_GREATER
            Interlocked.And(ref location, value);
#else
            int orig, updated;
            do { orig = location; updated = orig & value; }
            while (Interlocked.CompareExchange(ref location, updated, orig) != orig);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public void P1_ButtonPress(byte v)
        {
            if (v > 7) return;
            AtomicOrInt(ref P1_Port, 0x80 >> v);  // bit 7=button 0 (A), bit 0=button 7 (Right)
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public void P1_ButtonUnPress(byte v)
        {
            if (v > 7) return;
            AtomicAndInt(ref P1_Port, ~(0x80 >> v));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public void P2_ButtonPress(byte v)
        {
            if (v > 7) return;
            AtomicOrInt(ref P2_Port, 0x80 >> v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public void P2_ButtonUnPress(byte v)
        {
            if (v > 7) return;
            AtomicAndInt(ref P2_Port, ~(0x80 >> v));
        }

        // TriCNES: read from shift register (MSB → D0), set 2-cycle shift delay
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public byte gamepad_r_4016()
        {
            P1_ShiftCounter = 2;
            controllerStrobed = false;
            return (byte)((P1_ShiftRegister >> 7) | (cpubus & 0xE0));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public byte gamepad_r_4017()
        {
            P2_ShiftCounter = 2;
            controllerStrobed = false;
            return (byte)((P2_ShiftRegister >> 7) | (cpubus & 0xE0));
        }

        // TriCNES: shift processing in APU step (every CPU cycle)
        // Called from apu_step() — handles both strobing and deferred shift
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ProcessControllerShift()
        {
            if (controllerStrobing)
            {
                P1_ShiftCounter = 0;
                P2_ShiftCounter = 0;
                return;
            }

            if (P1_ShiftCounter != 0 && --P1_ShiftCounter == 0)
                P1_ShiftRegister = (byte)((P1_ShiftRegister << 1) | 1);

            if (P2_ShiftCounter != 0 && --P2_ShiftCounter == 0)
                P2_ShiftRegister = (byte)((P2_ShiftRegister << 1) | 1);
        }

        // TriCNES: strobe reload in APU GET cycle (transition to PUT)
        // Called from apu_step() GET cycle block
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ProcessControllerStrobe()
        {
            if (controllerStrobing)
            {
                if (!controllerStrobed)
                {
                    controllerStrobed = true;
                    // Load shift registers from current button state
                    P1_ShiftRegister = (byte)P1_Port;
                    P2_ShiftRegister = (byte)P2_Port;
                }
            }
            else
            {
                controllerStrobed = false;
            }
        }

        // $4016 write — set strobe flag (TriCNES: immediate, not deferred)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public void gamepad_w_4016(byte val)
        {
            controllerStrobing = (val & 1) != 0;
            if (!controllerStrobing)
                controllerStrobed = false;
        }
    }
}
