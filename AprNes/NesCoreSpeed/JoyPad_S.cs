using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AprNes
{
    unsafe public partial class NesCoreSpeed
    {
        static byte* P1_joypad_status_S;
        static byte P1_StrobeState_S = 0;
        static byte P1_LastWrite_S = 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public void P1_ButtonPress_S(byte v)
        {
            if (v > 7) return;
            P1_joypad_status_S[v] = 0x41;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public void P1_ButtonUnPress_S(byte v)
        {
            if (v > 7) return;
            P1_joypad_status_S[v] = 0x40;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte gamepad_r_4016_S()
        {
            byte val;
            if (P1_StrobeState_S < 8) val = P1_joypad_status_S[P1_StrobeState_S];
            else val = 1;
            P1_StrobeState_S++;
            if (P1_StrobeState_S == 24) P1_StrobeState_S = 0;
            return (byte)(val & 0x1F);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void gamepad_w_4016_S(byte val)
        {
            if ((P1_LastWrite_S & 1) == 1 && (val & 1) == 0) P1_StrobeState_S = 0;
            P1_LastWrite_S = val;
        }
    }
}
