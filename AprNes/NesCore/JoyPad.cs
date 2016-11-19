namespace AprNes
{
    unsafe public partial class NesCore
    {
        byte* P1_joypad_status;
        byte P1_StrobeState = 0;

        public void P1_ButtonPress(byte v)
        {
            if (v > 7) return;
            P1_joypad_status[v] = 0x41;
        }

        public void P1_ButtonUnPress(byte v)
        {
            if (v > 7) return;
            P1_joypad_status[v] = 0x40;
        }

        byte P1_r = 0;
        public byte gamepad_r_4016()
        {
            if (P1_StrobeState < 8) P1_r = P1_joypad_status[P1_StrobeState];
            else if (P1_StrobeState >= 8 && P1_StrobeState < 19) P1_r = 0;
            else if (P1_StrobeState == 19) P1_r = 1;
            else P1_r = 0;
            P1_StrobeState++;
            if (P1_StrobeState == 24) P1_StrobeState = 0;
            return P1_r;
        }

        byte P1_LastWrite = 0;
        public void gamepad_w_4016(byte val)
        {
            if (P1_LastWrite == 1 && val == 0)
                P1_StrobeState = 0;
            P1_LastWrite = val;
        }
    }
}
