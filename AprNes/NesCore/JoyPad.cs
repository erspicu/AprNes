using System.Runtime.CompilerServices;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        // TriCNES controller model: 8-bit shift register + 2-cycle deferred shift
        // Button layout: bit 7=A (MSB, read first), bit 6=B, ..., bit 0=Right (LSB, read last)

        // Current button state (set by UI thread, loaded into shift register during strobe)
        static byte P1_Port = 0;
        static byte P2_Port = 0;

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public void P1_ButtonPress(byte v)
        {
            if (v > 7) return;
            P1_Port |= (byte)(0x80 >> v);  // bit 7=button 0 (A), bit 0=button 7 (Right)
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public void P1_ButtonUnPress(byte v)
        {
            if (v > 7) return;
            P1_Port &= (byte)~(0x80 >> v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public void P2_ButtonPress(byte v)
        {
            if (v > 7) return;
            P2_Port |= (byte)(0x80 >> v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public void P2_ButtonUnPress(byte v)
        {
            if (v > 7) return;
            P2_Port &= (byte)~(0x80 >> v);
        }

        // TriCNES: read from shift register (MSB → D0), set 2-cycle shift delay
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public byte gamepad_r_4016()
        {
            byte d0 = (byte)((P1_ShiftRegister & 0x80) == 0 ? 0 : 1);
            P1_ShiftCounter = 2;
            controllerStrobed = false;  // allows rapid A-button streaming while strobed
            return (byte)(d0 | (cpubus & 0xE0)); // D0 = button, D5-D7 = CPU open bus
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public byte gamepad_r_4017()
        {
            byte d0 = (byte)((P2_ShiftRegister & 0x80) == 0 ? 0 : 1);
            P2_ShiftCounter = 2;
            controllerStrobed = false;
            return (byte)(d0 | (cpubus & 0xE0));
        }

        // TriCNES: shift processing in APU step (every CPU cycle)
        // Called from apu_step() — handles both strobing and deferred shift
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ProcessControllerShift()
        {
            if (!controllerStrobing)
            {
                // Not strobing: process deferred shifts
                if (P1_ShiftCounter > 0)
                {
                    P1_ShiftCounter--;
                    if (P1_ShiftCounter == 0)
                    {
                        P1_ShiftRegister <<= 1;
                        P1_ShiftRegister |= 1; // fill bit 0 with 1 (open bus / pull-up)
                    }
                }
                if (P2_ShiftCounter > 0)
                {
                    P2_ShiftCounter--;
                    if (P2_ShiftCounter == 0)
                    {
                        P2_ShiftRegister <<= 1;
                        P2_ShiftRegister |= 1;
                    }
                }
            }
            else
            {
                // Strobing: reset shift counters (TriCNES behavior)
                P1_ShiftCounter = 0;
                P2_ShiftCounter = 0;
            }
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
                    P1_ShiftRegister = P1_Port;
                    P2_ShiftRegister = P2_Port;
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
