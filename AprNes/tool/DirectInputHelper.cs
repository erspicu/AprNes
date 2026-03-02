using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace NativeTools
{
    // ──────────────────────────────────────────────────────────────────────────
    // Structs
    // ──────────────────────────────────────────────────────────────────────────

    // Minimal prefix of DIDEVICEINSTANCEW — only the fields we need
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DIDEVICEINSTANCE_MIN
    {
        public uint dwSize;
        public Guid guidInstance;
        public Guid guidProduct;
        public uint dwDevType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string tszInstanceName;
    }

    // DIJOYSTATE — 80 bytes, matches native layout exactly
    [StructLayout(LayoutKind.Sequential)]
    struct DIJOYSTATE
    {
        public int lX, lY, lZ;
        public int lRx, lRy, lRz;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]  public int[]  rglSlider;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]  public uint[] rgdwPOV;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbButtons;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DIPROPHEADER
    {
        public uint dwSize;
        public uint dwHeaderSize;
        public uint dwObj;
        public uint dwHow;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DIPROPRANGE
    {
        public DIPROPHEADER diph;
        public int lMin;
        public int lMax;
    }

    // Explicit layout with fixed Size to guarantee native-matching size on x64:
    //   IntPtr(8) + 3×uint(12) + 4-byte trailing pad = 24 bytes
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct DIOBJECTDATAFORMAT
    {
        [FieldOffset(0)]  public IntPtr pguid;
        [FieldOffset(8)]  public uint   dwOfs;
        [FieldOffset(12)] public uint   dwType;
        [FieldOffset(16)] public uint   dwFlags;
    }

    // 5×uint(20) + 4-byte pad + IntPtr(8) = 32 bytes
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    struct DIDATAFORMAT
    {
        [FieldOffset(0)]  public uint   dwSize;
        [FieldOffset(4)]  public uint   dwObjSize;
        [FieldOffset(8)]  public uint   dwFlags;
        [FieldOffset(12)] public uint   dwDataSize;
        [FieldOffset(16)] public uint   dwNumObjs;
        [FieldOffset(24)] public IntPtr rgodf;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVINFO_DATA
    {
        public uint    cbSize;
        public Guid    ClassGuid;
        public uint    DevInst;
        public IntPtr  Reserved;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // COM interfaces
    // ──────────────────────────────────────────────────────────────────────────

    delegate int DIEnumDevicesCallback(IntPtr lpddi, IntPtr pvRef);

    [ComImport]
    [Guid("BF798031-483A-4DA2-AA99-5D64ED369700")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDirectInput8W
    {
        [PreserveSig] int CreateDevice(ref Guid rguid,
            [MarshalAs(UnmanagedType.Interface)] out IDirectInputDevice8W device, IntPtr outer);
        [PreserveSig] int EnumDevices(uint devType, DIEnumDevicesCallback callback,
            IntPtr pvRef, uint flags);
        [PreserveSig] int GetDeviceStatus(ref Guid guidInstance);
        [PreserveSig] int RunControlPanel(IntPtr hwnd, uint flags);
        [PreserveSig] int Initialize(IntPtr hinst, uint version);
        [PreserveSig] int FindDevice(ref Guid guidClass,
            [MarshalAs(UnmanagedType.LPWStr)] string name, out Guid guidOut);
        [PreserveSig] int _stub_EnumDevicesBySemantics(IntPtr a, IntPtr b, IntPtr c, IntPtr d, uint e);
        [PreserveSig] int _stub_ConfigureDevices(IntPtr a, IntPtr b, uint c, IntPtr d);
    }

    [ComImport]
    [Guid("54D41081-DC15-4833-A41B-748F73A38179")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDirectInputDevice8W
    {
        [PreserveSig] int GetCapabilities(IntPtr lpDIDevCaps);
        [PreserveSig] int EnumObjects(IntPtr lpCallback, IntPtr pvRef, uint dwFlags);
        [PreserveSig] int GetProperty(IntPtr rguidProp, IntPtr pdiph);
        [PreserveSig] int SetProperty(IntPtr rguidProp, IntPtr pdiph);
        [PreserveSig] int Acquire();
        [PreserveSig] int Unacquire();
        [PreserveSig] int GetDeviceState(uint cbData, IntPtr lpvData);
        [PreserveSig] int GetDeviceData(uint cbObjectData, IntPtr rgdod, ref uint pdwInOut, uint dwFlags);
        [PreserveSig] int SetDataFormat(IntPtr lpdf);
        [PreserveSig] int SetEventNotification(IntPtr hEvent);
        [PreserveSig] int SetCooperativeLevel(IntPtr hwnd, uint dwFlags);
        [PreserveSig] int GetObjectInfo(IntPtr pdidoi, uint dwObj, uint dwHow);
        [PreserveSig] int GetDeviceInfo(IntPtr pdidi);
        [PreserveSig] int RunControlPanel(IntPtr hwnd, uint dwFlags);
        [PreserveSig] int Initialize(IntPtr hinst, uint dwVersion, ref Guid rguid);
        [PreserveSig] int _stub_CreateEffect(IntPtr a, IntPtr b, IntPtr c, IntPtr d);
        [PreserveSig] int _stub_EnumEffects(IntPtr a, IntPtr b, uint c);
        [PreserveSig] int _stub_GetEffectInfo(IntPtr a, IntPtr b);
        [PreserveSig] int _stub_GetForceFeedbackState(IntPtr a);
        [PreserveSig] int _stub_SendForceFeedbackCommand(uint a);
        [PreserveSig] int _stub_EnumCreatedEffectObjects(IntPtr a, IntPtr b, uint c);
        [PreserveSig] int _stub_Escape(IntPtr a);
        [PreserveSig] int Poll();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helper info struct (avoids C# 7 value tuples)
    // ──────────────────────────────────────────────────────────────────────────
    struct DiDeviceInfo
    {
        public Guid   GuidInstance;
        public Guid   GuidProduct;
        public string Name;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DirectInput operations
    // ──────────────────────────────────────────────────────────────────────────
    static class DirectInputNative
    {
        static readonly Guid IID_IDirectInput8W = new Guid("BF798031-483A-4DA2-AA99-5D64ED369700");

        const uint DI8DEVCLASS_GAMECTRL = 4;
        const uint DIEDFL_ATTACHEDONLY  = 0x00000001;
        const uint DISCL_NONEXCLUSIVE   = 0x00000002;
        const uint DISCL_BACKGROUND     = 0x00000008;
        const uint DIPH_BYOFFSET        = 3;
        static readonly IntPtr DIPROP_RANGE = (IntPtr)4;

        [DllImport("dinput8.dll")]
        static extern int DirectInput8Create(IntPtr hinst, uint dwVersion, ref Guid riidltf,
            [MarshalAs(UnmanagedType.Interface)] out IDirectInput8W ppvOut, IntPtr punkOuter);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string enumerator,
            IntPtr hwnd, uint flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool SetupDiEnumDeviceInfo(IntPtr devInfoSet, uint memberIdx,
            ref SP_DEVINFO_DATA devInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool SetupDiGetDeviceInstanceId(IntPtr devInfoSet,
            ref SP_DEVINFO_DATA devInfoData, StringBuilder deviceInstanceId,
            uint deviceInstanceIdSize, out uint requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr devInfoSet);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string moduleName);

        // Returns a set of (PID<<16|VID) values for XInput devices (identified by "IG_" in path)
        static HashSet<uint> GetXInputVidPids()
        {
            var result = new HashSet<uint>();
            IntPtr hDevInfo = SetupDiGetClassDevs(IntPtr.Zero, "HID", IntPtr.Zero,
                0x00000002u | 0x00000004u); // DIGCF_PRESENT | DIGCF_ALLCLASSES
            if (hDevInfo == new IntPtr(-1)) return result;
            try
            {
                SP_DEVINFO_DATA devInfo = new SP_DEVINFO_DATA();
                devInfo.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                for (uint idx = 0; SetupDiEnumDeviceInfo(hDevInfo, idx, ref devInfo); idx++)
                {
                    StringBuilder sb = new StringBuilder(512);
                    uint reqSize;
                    if (!SetupDiGetDeviceInstanceId(hDevInfo, ref devInfo, sb,
                            (uint)sb.Capacity, out reqSize)) continue;
                    string path = sb.ToString().ToUpperInvariant();
                    if (!path.Contains("IG_")) continue;
                    int vidIdx = path.IndexOf("VID_");
                    int pidIdx = path.IndexOf("PID_");
                    if (vidIdx < 0 || pidIdx < 0) continue;
                    uint vid, pid;
                    if (!uint.TryParse(path.Substring(vidIdx + 4, 4),
                            System.Globalization.NumberStyles.HexNumber, null, out vid)) continue;
                    if (!uint.TryParse(path.Substring(pidIdx + 4, 4),
                            System.Globalization.NumberStyles.HexNumber, null, out pid)) continue;
                    result.Add((pid << 16) | vid);
                }
            }
            finally { SetupDiDestroyDeviceInfoList(hDevInfo); }
            return result;
        }

        public static IDirectInput8W CreateDirectInput()
        {
            IntPtr hinst = GetModuleHandle(null);
            Guid iid = IID_IDirectInput8W;
            IDirectInput8W di;
            int hr = DirectInput8Create(hinst, 0x0800, ref iid, out di, IntPtr.Zero);
            if (hr != 0) throw new COMException("DirectInput8Create failed", hr);
            return di;
        }

        // Enumerate non-XInput game controllers
        public static List<DiDeviceInfo> EnumJoysticks(IDirectInput8W di)
        {
            HashSet<uint> xInputVidPids = GetXInputVidPids();
            List<DiDeviceInfo> devices = new List<DiDeviceInfo>();
            DIEnumDevicesCallback cb = (lpddi, pvRef) =>
            {
                DIDEVICEINSTANCE_MIN inst = (DIDEVICEINSTANCE_MIN)Marshal.PtrToStructure(
                    lpddi, typeof(DIDEVICEINSTANCE_MIN));
                // guidProduct.Data1 encodes (PID<<16|VID) for joystick devices
                uint data1 = BitConverter.ToUInt32(inst.guidProduct.ToByteArray(), 0);
                if (!xInputVidPids.Contains(data1))
                {
                    DiDeviceInfo info = new DiDeviceInfo
                    {
                        GuidInstance = inst.guidInstance,
                        GuidProduct  = inst.guidProduct,
                        Name         = inst.tszInstanceName ?? string.Empty
                    };
                    devices.Add(info);
                }
                return 1; // DIENUM_CONTINUE
            };
            di.EnumDevices(DI8DEVCLASS_GAMECTRL, cb, IntPtr.Zero, DIEDFL_ATTACHEDONLY);
            GC.KeepAlive(cb);
            return devices;
        }

        // Open, configure and acquire a device
        public static IDirectInputDevice8W OpenDevice(IDirectInput8W di,
            Guid guidInstance, IntPtr hwnd)
        {
            Guid gInst = guidInstance;
            IDirectInputDevice8W device;
            if (di.CreateDevice(ref gInst, out device, IntPtr.Zero) != 0) return null;
            if (device.SetDataFormat(DfDIJoystick.pFormat) != 0)
            { Marshal.ReleaseComObject(device); return null; }
            if (device.SetCooperativeLevel(hwnd, DISCL_NONEXCLUSIVE | DISCL_BACKGROUND) != 0)
            { Marshal.ReleaseComObject(device); return null; }
            // Normalize all axis ranges to [0, 65535]; ignore failure for missing axes
            foreach (uint off in new uint[] { 0, 4, 8, 12, 16, 20, 24, 28 })
                SetAxisRange(device, off, 0, 65535);
            device.Acquire();
            return device;
        }

        static void SetAxisRange(IDirectInputDevice8W device, uint offset, int min, int max)
        {
            DIPROPRANGE r = new DIPROPRANGE
            {
                diph = new DIPROPHEADER
                {
                    dwSize       = (uint)Marshal.SizeOf(typeof(DIPROPRANGE)),
                    dwHeaderSize = (uint)Marshal.SizeOf(typeof(DIPROPHEADER)),
                    dwObj        = offset,
                    dwHow        = DIPH_BYOFFSET
                },
                lMin = min, lMax = max
            };
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(DIPROPRANGE)));
            try
            {
                Marshal.StructureToPtr(r, ptr, false);
                device.SetProperty(DIPROP_RANGE, ptr);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        // Poll a device and return its state; re-acquires automatically on loss
        public static bool PollDevice(IDirectInputDevice8W device, out DIJOYSTATE state)
        {
            state = DefaultState();
            device.Poll();
            IntPtr ptr = Marshal.AllocHGlobal(80); // sizeof(DIJOYSTATE)
            try
            {
                int hr = device.GetDeviceState(80, ptr);
                if (hr != 0)
                {
                    device.Acquire();
                    hr = device.GetDeviceState(80, ptr);
                }
                if (hr != 0) return false;
                state = (DIJOYSTATE)Marshal.PtrToStructure(ptr, typeof(DIJOYSTATE));
                return true;
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        public static DIJOYSTATE DefaultState()
        {
            return new DIJOYSTATE
            {
                rglSlider  = new int[2],
                rgdwPOV    = new uint[] { 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu },
                rgbButtons = new byte[32]
            };
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // c_dfDIJoystick — static DIDATAFORMAT for DIJOYSTATE (built once at startup)
    // ──────────────────────────────────────────────────────────────────────────
    static class DfDIJoystick
    {
        const uint DIDFT_AXIS        = 0x00000003;
        const uint DIDFT_BUTTON      = 0x0000000C;
        const uint DIDFT_POV         = 0x00000010;
        const uint DIDFT_ANYINSTANCE = 0x00FFFF00;
        const uint DIDFT_OPTIONAL    = 0x80000000;
        const uint DIDF_ABSAXIS      = 0x00000001;

        public static readonly IntPtr pFormat;

        static DfDIJoystick()
        {
            // Axis / POV GUIDs pinned in unmanaged heap (never freed — app lifetime)
            IntPtr pgX   = AllocGuid(new Guid("A36D02E0-C9F3-11CF-BFC7-444553540000")); // X
            IntPtr pgY   = AllocGuid(new Guid("A36D02E1-C9F3-11CF-BFC7-444553540000")); // Y
            IntPtr pgZ   = AllocGuid(new Guid("A36D02E2-C9F3-11CF-BFC7-444553540000")); // Z
            IntPtr pgRx  = AllocGuid(new Guid("A36D02F4-C9F3-11CF-BFC7-444553540000")); // Rx
            IntPtr pgRy  = AllocGuid(new Guid("A36D02F5-C9F3-11CF-BFC7-444553540000")); // Ry
            IntPtr pgRz  = AllocGuid(new Guid("A36D02E3-C9F3-11CF-BFC7-444553540000")); // Rz
            IntPtr pgSl  = AllocGuid(new Guid("A36D02E4-C9F3-11CF-BFC7-444553540000")); // Slider
            IntPtr pgPov = AllocGuid(new Guid("A36D02F2-C9F3-11CF-BFC7-444553540000")); // POV

            uint req  = DIDFT_AXIS | DIDFT_ANYINSTANCE;
            uint optA = DIDFT_OPTIONAL | DIDFT_AXIS   | DIDFT_ANYINSTANCE;
            uint optB = DIDFT_OPTIONAL | DIDFT_BUTTON | DIDFT_ANYINSTANCE;
            uint optP = DIDFT_OPTIONAL | DIDFT_POV    | DIDFT_ANYINSTANCE;

            List<DIOBJECTDATAFORMAT> objs = new List<DIOBJECTDATAFORMAT>
            {
                new DIOBJECTDATAFORMAT { pguid=pgX,   dwOfs= 0, dwType=req,  dwFlags=0 }, // X axis
                new DIOBJECTDATAFORMAT { pguid=pgY,   dwOfs= 4, dwType=req,  dwFlags=0 }, // Y axis
                new DIOBJECTDATAFORMAT { pguid=pgZ,   dwOfs= 8, dwType=optA, dwFlags=0 }, // Z
                new DIOBJECTDATAFORMAT { pguid=pgRx,  dwOfs=12, dwType=optA, dwFlags=0 }, // Rx
                new DIOBJECTDATAFORMAT { pguid=pgRy,  dwOfs=16, dwType=optA, dwFlags=0 }, // Ry
                new DIOBJECTDATAFORMAT { pguid=pgRz,  dwOfs=20, dwType=optA, dwFlags=0 }, // Rz
                new DIOBJECTDATAFORMAT { pguid=pgSl,  dwOfs=24, dwType=optA, dwFlags=0 }, // Slider0
                new DIOBJECTDATAFORMAT { pguid=pgSl,  dwOfs=28, dwType=optA, dwFlags=0 }, // Slider1
                new DIOBJECTDATAFORMAT { pguid=pgPov, dwOfs=32, dwType=optP, dwFlags=0 }, // POV0
                new DIOBJECTDATAFORMAT { pguid=pgPov, dwOfs=36, dwType=optP, dwFlags=0 }, // POV1
                new DIOBJECTDATAFORMAT { pguid=pgPov, dwOfs=40, dwType=optP, dwFlags=0 }, // POV2
                new DIOBJECTDATAFORMAT { pguid=pgPov, dwOfs=44, dwType=optP, dwFlags=0 }, // POV3
            };
            // 32 optional buttons at offsets 48–79
            for (int i = 0; i < 32; i++)
                objs.Add(new DIOBJECTDATAFORMAT
                    { pguid = IntPtr.Zero, dwOfs = (uint)(48 + i), dwType = optB, dwFlags = 0 });

            int objSize = Marshal.SizeOf(typeof(DIOBJECTDATAFORMAT)); // 24 on x64
            IntPtr pObjArr = Marshal.AllocHGlobal(objs.Count * objSize);
            for (int i = 0; i < objs.Count; i++)
                Marshal.StructureToPtr(objs[i], IntPtr.Add(pObjArr, i * objSize), false);

            DIDATAFORMAT fmt = new DIDATAFORMAT
            {
                dwSize    = (uint)Marshal.SizeOf(typeof(DIDATAFORMAT)), // 32 on x64
                dwObjSize = (uint)objSize,
                dwFlags   = DIDF_ABSAXIS,
                dwDataSize = 80,  // sizeof(DIJOYSTATE)
                dwNumObjs  = (uint)objs.Count,
                rgodf      = pObjArr
            };
            pFormat = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(DIDATAFORMAT)));
            Marshal.StructureToPtr(fmt, pFormat, false);
        }

        static IntPtr AllocGuid(Guid g)
        {
            IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Guid)));
            Marshal.StructureToPtr(g, p, false);
            return p;
        }
    }
}
