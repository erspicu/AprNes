using System.Runtime.CompilerServices;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        static byte* P1_joypad_status;
        static byte P1_StrobeState = 0;

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

        static byte P1_r = 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public byte gamepad_r_4016()
        {
            if (P1_StrobeState < 8) P1_r = P1_joypad_status[P1_StrobeState];
            else P1_r = 1; // After 8 buttons, shift register returns D0=1 (NES hardware)
            P1_StrobeState++;
            if (P1_StrobeState == 24) P1_StrobeState = 0;
            return P1_r;
        }

        static byte P1_LastWrite = 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public void gamepad_w_4016(byte val)
        {
            if (P1_LastWrite == 1 && val == 0) P1_StrobeState = 0;
            P1_LastWrite = val;
        }
    }
}
