using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NativeTools
{
    class joystick
    {
        List<DeviceJoyInfo> joyinfo_list = new List<DeviceJoyInfo>();
        JOYCAPS joycap = new JOYCAPS();
        JOYINFO js = new JOYINFO();
        int JOYCAPS_size;
        public int PeriodMin = 0;

        // XInput state — player index 0-3, device ID = XI_ID_BASE + index
        const int XI_ID_BASE = 1000;
        const short XI_DEADZONE = 7849; // XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE
        // wButtons bitmasks: A, B, X, Y, LB, RB, Start, Back → button IDs 1-8
        static readonly int[] XI_BTN_MASK = { 0x1000, 0x2000, 0x4000, 0x8000, 0x0100, 0x0200, 0x0010, 0x0020 };
        XINPUT_STATE[] xi_states    = new XINPUT_STATE[4];
        bool[]         xi_connected = new bool[4];
        bool           xi_available = true;

        unsafe public void Init()
        {
            Stopwatch st = new Stopwatch();
            st.Restart();

            PeriodMin = 0;
            joyinfo_list.Clear();

            JOYCAPS_size = Marshal.SizeOf(typeof(JOYCAPS));

            for (int i = 0; i < 256; i++)
            {
                if (NativeMethods.joyGetDevCaps((IntPtr)i, ref joycap, JOYCAPS_size) == 0)
                {
                    DeviceJoyInfo info = new DeviceJoyInfo();

                    //set id
                    info.ID = i;

                    //check joyex
                    if (NativeMethods.joyGetPos(i, ref js) == 0)
                    {
                        info.Way_X_old = js.wXpos;
                        info.Way_Y_old = js.wYpos;
                    }
                    else continue; //裝置功能失效

                    //set button count
                    info.ButtonCount = joycap.wNumButtons;

                    info.Button_old = 0;

                    if (joycap.wPeriodMin > PeriodMin)
                        PeriodMin = joycap.wPeriodMin;

                    joyinfo_list.Add(info);
                }
            }
            //取出所有目前連線遊戲手把中最慢的PeriodMin然後+2ms
            PeriodMin += 2;

            // XInput 初始掃描 (Xbox / XInput 手把)
            xi_available = true;
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    XINPUT_STATE s = new XINPUT_STATE();
                    xi_connected[i] = (NativeMethods.XInputGetState((uint)i, ref s) == 0);
                    xi_states[i] = s;
                    if (xi_connected[i])
                        Console.WriteLine("XInput player " + i + " connected");
                }
            }
            catch { xi_available = false; }

            st.Stop();
            Console.WriteLine("init joypad infor : " + st.ElapsedMilliseconds + " ms");
        }

        public List<joystickEvent> joy_event_captur()
        {
            List<joystickEvent> event_list = new List<joystickEvent>();

            // WinMM polling
            for (int i_button = 0; i_button < joyinfo_list.Count(); i_button++)
            {
                DeviceJoyInfo button_inf = joyinfo_list[i_button];
                int button_id = button_inf.ID;
                int button_count = button_inf.ButtonCount;

                NativeMethods.joyGetPos(button_id, ref js);

                int button_now = js.wButtons;
                int X_now = js.wXpos;
                int Y_now = js.wYpos;
                int button_old = button_inf.Button_old;
                int X_old = button_inf.Way_X_old;
                int Y_old = button_inf.Way_Y_old;

                button_inf.Button_old = button_now;
                button_inf.Way_X_old = X_now;
                button_inf.Way_Y_old = Y_now;

                joyinfo_list[i_button] = button_inf;
                if (button_old != button_now || button_now != 0)
                {
                    for (int i = 0; i < button_count; i++)
                    {
                        if ((button_now & 1) != 0)
                            event_list.Add(new joystickEvent(1, button_inf.ID, i + 1, 1, 0, 0));
                        else
                        {
                            if ((button_now & 1) != (button_old & 1))
                                event_list.Add(new joystickEvent(1, button_inf.ID, i + 1, 0, 0, 0));
                        }
                        button_now >>= 1;
                        button_old >>= 1;
                    }
                }

                if (X_old != X_now || (X_now != 32767 && X_now != 32511 && X_now != 32254))
                    event_list.Add(new joystickEvent(0, button_inf.ID, 0, 0, 0, X_now));

                if (Y_old != Y_now || (Y_now != 32767 && Y_now != 32511 && Y_now != 32254))
                    event_list.Add(new joystickEvent(0, button_inf.ID, 0, 0, 1, Y_now));
            }

            // XInput polling (Xbox / XInput 手把)
            if (xi_available)
            {
                try
                {
                    for (int i = 0; i < 4; i++)
                    {
                        XINPUT_STATE cur = new XINPUT_STATE();
                        bool nowConn = (NativeMethods.XInputGetState((uint)i, ref cur) == 0);
                        if (!nowConn) { xi_connected[i] = false; continue; }
                        if (!xi_connected[i]) { xi_connected[i] = true; xi_states[i] = cur; } // 剛連線，以當前狀態為基準
                        XINPUT_STATE prev = xi_states[i];
                        xi_states[i] = cur;

                        int id = XI_ID_BASE + i;

                        // 一般按鈕 (A=1, B=2, X=3, Y=4, LB=5, RB=6, Start=7, Back=8)
                        for (int b = 0; b < XI_BTN_MASK.Length; b++)
                        {
                            bool nowP = (cur.Gamepad.wButtons & XI_BTN_MASK[b]) != 0;
                            bool preP = (prev.Gamepad.wButtons & XI_BTN_MASK[b]) != 0;
                            if (nowP)
                                event_list.Add(new joystickEvent(1, id, b + 1, 1, 0, 0));
                            else if (preP)
                                event_list.Add(new joystickEvent(1, id, b + 1, 0, 0, 0));
                        }

                        // D-Pad → X/Y way events (0=left/up, 32767=center, 65535=right/down)
                        int dpad  = cur.Gamepad.wButtons  & 0x000F;
                        int dpadP = prev.Gamepad.wButtons & 0x000F;
                        int xNow  = ((dpad  & 0x0008) != 0) ? 65535 : ((dpad  & 0x0004) != 0) ? 0 : 32767;
                        int xPrev = ((dpadP & 0x0008) != 0) ? 65535 : ((dpadP & 0x0004) != 0) ? 0 : 32767;
                        int yNow  = ((dpad  & 0x0002) != 0) ? 65535 : ((dpad  & 0x0001) != 0) ? 0 : 32767;
                        int yPrev = ((dpadP & 0x0002) != 0) ? 65535 : ((dpadP & 0x0001) != 0) ? 0 : 32767;
                        if (xNow != xPrev || xNow != 32767)
                            event_list.Add(new joystickEvent(0, id, 0, 0, 0, xNow));
                        if (yNow != yPrev || yNow != 32767)
                            event_list.Add(new joystickEvent(0, id, 0, 0, 1, yNow));

                        // Left analog stick → X/Y way events (D-Pad 優先；中立時才送 analog)
                        short lx = cur.Gamepad.sThumbLX, lxP = prev.Gamepad.sThumbLX;
                        short ly = cur.Gamepad.sThumbLY, lyP = prev.Gamepad.sThumbLY;
                        int axNow  = (Math.Abs(lx)  < XI_DEADZONE) ? 32767 : (lx  + 32768) & 0xFFFF;
                        int axPrev = (Math.Abs(lxP) < XI_DEADZONE) ? 32767 : (lxP + 32768) & 0xFFFF;
                        int ayNow  = (Math.Abs(ly)  < XI_DEADZONE) ? 32767 : 65535 - ((ly  + 32768) & 0xFFFF);
                        int ayPrev = (Math.Abs(lyP) < XI_DEADZONE) ? 32767 : 65535 - ((lyP + 32768) & 0xFFFF);
                        if (xNow == 32767 && (axNow != axPrev || axNow != 32767))
                            event_list.Add(new joystickEvent(0, id, 0, 0, 0, axNow));
                        if (yNow == 32767 && (ayNow != ayPrev || ayNow != 32767))
                            event_list.Add(new joystickEvent(0, id, 0, 0, 1, ayNow));
                    }
                }
                catch { xi_available = false; }
            }

            return event_list;
        }
    }
}
