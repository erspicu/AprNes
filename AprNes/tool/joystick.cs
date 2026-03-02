using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NativeTools
{
    class joystick
    {
        // DirectInput devices (non-XInput game controllers)
        struct DiDevice
        {
            public int       ID;
            public IntPtr    Device;
            public DIJOYSTATE PrevState;
        }
        List<DiDevice> _diDevices = new List<DiDevice>();
        IntPtr _di;

        public int PeriodMin = 10; // ms between polls

        // XInput state — player index 0-3, device ID = XI_ID_BASE + index
        const int XI_ID_BASE = 1000;
        const short XI_DEADZONE = 7849; // XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE
        // wButtons bitmasks: A, B, X, Y, LB, RB, Start, Back → button IDs 1-8
        static readonly int[] XI_BTN_MASK = { 0x1000, 0x2000, 0x4000, 0x8000, 0x0100, 0x0200, 0x0010, 0x0020 };
        XINPUT_STATE[] xi_states    = new XINPUT_STATE[4];
        bool[]         xi_connected = new bool[4];
        bool           xi_available = true;

        public void Init(IntPtr hwnd)
        {
            Stopwatch st = new Stopwatch();
            st.Restart();

            _diDevices.Clear();
            int nextId = 0;

            // DirectInput — enumerate non-XInput game controllers
            try
            {
                _di = DirectInputNative.CreateDirectInput();
                foreach (DiDeviceInfo info in DirectInputNative.EnumJoysticks(_di))
                {
                    IntPtr dev = DirectInputNative.OpenDevice(_di, info.GuidInstance, hwnd);
                    if (dev == IntPtr.Zero) continue;
                    _diDevices.Add(new DiDevice
                    {
                        ID        = nextId++,
                        Device    = dev,
                        PrevState = DirectInputNative.DefaultState()
                    });
                    Console.WriteLine("DirectInput device " + (nextId - 1) + ": " + info.Name);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("DirectInput init error: " + ex.Message);
            }

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

        // Convert DirectInput POV value (hundredths of degree) to normalised X/Y (0/32767/65535)
        static void PovToXY(uint pov, out int x, out int y)
        {
            x = 32767; y = 32767;
            if (pov == 0xFFFFFFFFu) return;
            int deg = (int)pov; // 0–35900
            if      (deg >= 4500  && deg <= 13500) x = 65535; // E
            else if (deg >= 22500 && deg <= 31500) x = 0;     // W
            if      (deg <= 4500  || deg >= 31500) y = 0;     // N
            else if (deg >= 13500 && deg <= 22500) y = 65535; // S
        }

        // Snap analog axis value to 3-state digital: 0 (low), 32767 (center), 65535 (high).
        // Ensures exact values regardless of DIPROP_RANGE success or hardware quirks.
        static int SnapAxisDigital(int v)
        {
            if (v < 16384) return 0;
            if (v > 49151) return 65535;
            return 32767;
        }

        public List<joystickEvent> joy_event_captur()
        {
            List<joystickEvent> event_list = new List<joystickEvent>();

            // DirectInput polling
            for (int idx = 0; idx < _diDevices.Count; idx++)
            {
                DiDevice dev = _diDevices[idx];
                DIJOYSTATE state;
                if (!DirectInputNative.PollDevice(dev.Device, out state)) continue;

                DIJOYSTATE prev = dev.PrevState;
                dev.PrevState = state;
                _diDevices[idx] = dev;

                int id = dev.ID;

                // Buttons (up to 32)
                for (int b = 0; b < 32; b++)
                {
                    bool nowP  = (state.rgbButtons[b] & 0x80) != 0;
                    bool prevP = (prev.rgbButtons[b]  & 0x80) != 0;
                    if (nowP)
                        event_list.Add(new joystickEvent(1, id, b + 1, 1, 0, 0));
                    else if (prevP)
                        event_list.Add(new joystickEvent(1, id, b + 1, 0, 0, 0));
                }

                // Main X/Y axes — normalize near-center noise to exact 32767, then snap to digital
                const int AXIS_CENTER = 32767;
                const int AXIS_NOISE  = 256; // suppress hardware jitter at rest
                int xNow  = SnapAxisDigital((Math.Abs(state.lX - AXIS_CENTER) < AXIS_NOISE) ? AXIS_CENTER : state.lX);
                int yNow  = SnapAxisDigital((Math.Abs(state.lY - AXIS_CENTER) < AXIS_NOISE) ? AXIS_CENTER : state.lY);
                int xPrev = SnapAxisDigital((Math.Abs(prev.lX  - AXIS_CENTER) < AXIS_NOISE) ? AXIS_CENTER : prev.lX);
                int yPrev = SnapAxisDigital((Math.Abs(prev.lY  - AXIS_CENTER) < AXIS_NOISE) ? AXIS_CENTER : prev.lY);
                if (xPrev != xNow || xNow != AXIS_CENTER)
                    event_list.Add(new joystickEvent(0, id, 0, 0, 0, xNow));
                if (yPrev != yNow || yNow != AXIS_CENTER)
                    event_list.Add(new joystickEvent(0, id, 0, 0, 1, yNow));

                // POV hat → X/Y events (only when main axes are neutral)
                int povX, povY, prevPovX, prevPovY;
                PovToXY(state.rgdwPOV[0], out povX, out povY);
                PovToXY(prev.rgdwPOV[0],  out prevPovX, out prevPovY);
                if (xNow == 32767 && (prevPovX != povX || povX != 32767))
                    event_list.Add(new joystickEvent(0, id, 0, 0, 0, povX));
                if (yNow == 32767 && (prevPovY != povY || povY != 32767))
                    event_list.Add(new joystickEvent(0, id, 0, 0, 1, povY));
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
                        int axNow  = (Math.Abs(lx)  < XI_DEADZONE) ? 32767 : SnapAxisDigital((lx  + 32768) & 0xFFFF);
                        int axPrev = (Math.Abs(lxP) < XI_DEADZONE) ? 32767 : SnapAxisDigital((lxP + 32768) & 0xFFFF);
                        int ayNow  = (Math.Abs(ly)  < XI_DEADZONE) ? 32767 : SnapAxisDigital(65535 - ((ly  + 32768) & 0xFFFF));
                        int ayPrev = (Math.Abs(lyP) < XI_DEADZONE) ? 32767 : SnapAxisDigital(65535 - ((lyP + 32768) & 0xFFFF));
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
