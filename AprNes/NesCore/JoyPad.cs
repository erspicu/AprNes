using System.Runtime.CompilerServices;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        static byte* P1_joypad_status;
        static byte P1_StrobeState = 0;

        static byte* P2_joypad_status;
        static byte P2_StrobeState = 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public void P1_ButtonPress(byte v)
        {
            if (v > 7) return;
            P1_joypad_status[v] = 0x41;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public void P1_ButtonUnPress(byte v)
        {
            if (v > 7) return;
            P1_joypad_status[v] = 0x40;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public void P2_ButtonPress(byte v)
        {
            if (v > 7) return;
            P2_joypad_status[v] = 0x41;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public void P2_ButtonUnPress(byte v)
        {
            if (v > 7) return;
            P2_joypad_status[v] = 0x40;
        }

        static byte P1_r = 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public byte gamepad_r_4016()
        {
            if (P1_StrobeState < 8) P1_r = P1_joypad_status[P1_StrobeState];
            else P1_r = 1; // After 8 buttons, shift register returns D0=1 (NES hardware)
            P1_StrobeState++;
            if (P1_StrobeState == 24) P1_StrobeState = 0;
            return (byte)((P1_r & 0x1F) | (cpubus & 0xE0)); // upper 3 bits are CPU open bus
        }

        static byte P2_r = 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public byte gamepad_r_4017()
        {
            if (P2_StrobeState < 8) P2_r = P2_joypad_status[P2_StrobeState];
            else P2_r = 1; // After 8 buttons, shift register returns D0=1 (NES hardware)
            P2_StrobeState++;
            if (P2_StrobeState == 24) P2_StrobeState = 0;
            return (byte)((P2_r & 0x1F) | (cpubus & 0xE0)); // upper 3 bits are CPU open bus
        }

        static byte P1_LastWrite = 0;
        static int strobeWritePending = 0;
        static byte strobeWriteValue = 0;

        // Deferred $4016 write processing: OUT pins update at start of PUT cycles (Mesen2 model)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void processStrobeWrite()
        {
            if (strobeWritePending > 0 && --strobeWritePending == 0)
            {
                if ((P1_LastWrite & 1) == 1 && (strobeWriteValue & 1) == 0)
                {
                    P1_StrobeState = 0;
                    P2_StrobeState = 0; // $4016 strobe resets both controllers
                }
                P1_LastWrite = strobeWriteValue;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public void gamepad_w_4016(byte val)
        {
            strobeWriteValue = val;
            strobeWritePending = (cpuCycleCount & 1) == 0 ? 1 : 2;
        }
    }
}
