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

            st.Stop();
            Console.WriteLine("init joypad infor : " + st.ElapsedMilliseconds + " ms");
        }

        public List<joystickEvent> joy_event_captur()
        {
            List<joystickEvent> event_list = new List<joystickEvent>();
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
            return event_list;
        }
    }
}
