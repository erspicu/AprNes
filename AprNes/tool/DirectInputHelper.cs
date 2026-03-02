using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace NativeTools
{
    // ──────────────────────────────────────────────────────────────────────────
    // Native structs
    // ──────────────────────────────────────────────────────────────────────────

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

    // DIJOYSTATE — 80 bytes
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

    // x64: 8+4+4+4 = 20, pad to 24
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct DIOBJECTDATAFORMAT
    {
        [FieldOffset(0)]  public IntPtr pguid;
        [FieldOffset(8)]  public uint   dwOfs;
        [FieldOffset(12)] public uint   dwType;
        [FieldOffset(16)] public uint   dwFlags;
    }

    // x64: 4+4+4+4+4 = 20, pad to 24, then IntPtr at 24 → size 32
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
        public uint   cbSize;
        public Guid   ClassGuid;
        public uint   DevInst;
        public IntPtr Reserved;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helper info struct
    // ──────────────────────────────────────────────────────────────────────────
    struct DiDeviceInfo
    {
        public Guid   GuidInstance;
        public Guid   GuidProduct;
        public string Name;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Vtable slot indices
    // IDirectInput8W vtable (IUnknown: QI=0 AddRef=1 Release=2 then DI methods)
    // IDirectInputDevice8W vtable (same IUnknown prefix)
    // ──────────────────────────────────────────────────────────────────────────
    static class DI8Slot
    {
        public const int Release      = 2;
        public const int CreateDevice = 3;
        public const int EnumDevices  = 4;
    }

    static class DI8DevSlot
    {
        public const int Release             = 2;
        public const int SetProperty         = 6;
        public const int Acquire             = 7;
        public const int GetDeviceState      = 9;
        public const int SetDataFormat       = 11;
        public const int SetCooperativeLevel = 13;
        public const int Poll                = 25;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Vtable delegate types — CallingConvention.StdCall for COM
    // ──────────────────────────────────────────────────────────────────────────
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate uint   VtReleaseFn(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int    VtDI8CreateDeviceFn(IntPtr self, ref Guid rguid, out IntPtr ppDevice, IntPtr punkOuter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int    VtDI8EnumDevicesFn(IntPtr self, uint devType, IntPtr callback, IntPtr pvRef, uint flags);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int    VtDI8DevSetDataFormatFn(IntPtr self, IntPtr lpdf);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int    VtDI8DevSetCoopLevelFn(IntPtr self, IntPtr hwnd, uint flags);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int    VtDI8DevSetPropertyFn(IntPtr self, IntPtr rguidProp, IntPtr pdiph);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int    VtDI8DevAcquireFn(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int    VtDI8DevPollFn(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int    VtDI8DevGetStateFn(IntPtr self, uint cbData, IntPtr lpvData);

    // Callback passed to EnumDevices (must be stdcall; return 1=continue, 0=stop)
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int    DiEnumDevicesNativeCb(IntPtr lpddi, IntPtr pvRef);

    // ──────────────────────────────────────────────────────────────────────────
    // Raw vtable COM dispatch helpers
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
            out IntPtr ppvOut, IntPtr punkOuter);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string moduleName);

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

        // Read vtable slot and return a callable delegate
        static T GetVtMethod<T>(IntPtr comPtr, int slot) where T : class
        {
            IntPtr vtable = Marshal.ReadIntPtr(comPtr);
            IntPtr fnPtr  = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer(fnPtr, typeof(T)) as T;
        }

        // Build set of (PID<<16|VID) for XInput devices (contain "IG_" in HID path)
        static HashSet<uint> GetXInputVidPids()
        {
            var result  = new HashSet<uint>();
            IntPtr hDev = SetupDiGetClassDevs(IntPtr.Zero, "HID", IntPtr.Zero,
                0x00000002u | 0x00000004u); // DIGCF_PRESENT | DIGCF_ALLCLASSES
            if (hDev == new IntPtr(-1)) return result;
            try
            {
                SP_DEVINFO_DATA d = new SP_DEVINFO_DATA();
                d.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                for (uint i = 0; SetupDiEnumDeviceInfo(hDev, i, ref d); i++)
                {
                    StringBuilder sb = new StringBuilder(512);
                    uint req;
                    if (!SetupDiGetDeviceInstanceId(hDev, ref d, sb, (uint)sb.Capacity, out req))
                        continue;
                    string path = sb.ToString().ToUpperInvariant();
                    if (!path.Contains("IG_")) continue;
                    int vi = path.IndexOf("VID_"), pi = path.IndexOf("PID_");
                    if (vi < 0 || pi < 0) continue;
                    uint vid, pid;
                    if (!uint.TryParse(path.Substring(vi + 4, 4),
                            System.Globalization.NumberStyles.HexNumber, null, out vid)) continue;
                    if (!uint.TryParse(path.Substring(pi + 4, 4),
                            System.Globalization.NumberStyles.HexNumber, null, out pid)) continue;
                    result.Add((pid << 16) | vid);
                }
            }
            finally { SetupDiDestroyDeviceInfoList(hDev); }
            return result;
        }

        // ── Public API ───────────────────────────────────────────────────────

        public static IntPtr CreateDirectInput()
        {
            IntPtr hinst = GetModuleHandle(null);
            Guid   iid   = IID_IDirectInput8W;
            IntPtr pDI;
            int hr = DirectInput8Create(hinst, 0x0800, ref iid, out pDI, IntPtr.Zero);
            if (hr != 0 || pDI == IntPtr.Zero)
                throw new Exception("DirectInput8Create failed: 0x" + hr.ToString("X8"));
            return pDI;
        }

        public static List<DiDeviceInfo> EnumJoysticks(IntPtr pDI)
        {
            HashSet<uint>     xipVidPids = GetXInputVidPids();
            List<DiDeviceInfo> devices   = new List<DiDeviceInfo>();

            // Keep callback delegate alive until after EnumDevices returns
            DiEnumDevicesNativeCb cb = (lpddi, pvRef) =>
            {
                try
                {
                    DIDEVICEINSTANCE_MIN inst = (DIDEVICEINSTANCE_MIN)
                        Marshal.PtrToStructure(lpddi, typeof(DIDEVICEINSTANCE_MIN));
                    // guidProduct.Data1 encodes (PID<<16|VID) for HID devices
                    uint data1 = BitConverter.ToUInt32(inst.guidProduct.ToByteArray(), 0);
                    if (!xipVidPids.Contains(data1))
                        devices.Add(new DiDeviceInfo
                        {
                            GuidInstance = inst.guidInstance,
                            GuidProduct  = inst.guidProduct,
                            Name         = inst.tszInstanceName ?? string.Empty
                        });
                }
                catch { }
                return 1; // DIENUM_CONTINUE
            };

            IntPtr cbPtr = Marshal.GetFunctionPointerForDelegate(cb);
            GetVtMethod<VtDI8EnumDevicesFn>(pDI, DI8Slot.EnumDevices)
                (pDI, DI8DEVCLASS_GAMECTRL, cbPtr, IntPtr.Zero, DIEDFL_ATTACHEDONLY);
            GC.KeepAlive(cb);
            return devices;
        }

        public static IntPtr OpenDevice(IntPtr pDI, Guid guidInstance, IntPtr hwnd)
        {
            Guid   gInst = guidInstance;
            IntPtr pDev;
            if (GetVtMethod<VtDI8CreateDeviceFn>(pDI, DI8Slot.CreateDevice)
                    (pDI, ref gInst, out pDev, IntPtr.Zero) != 0 || pDev == IntPtr.Zero)
                return IntPtr.Zero;

            if (GetVtMethod<VtDI8DevSetDataFormatFn>(pDev, DI8DevSlot.SetDataFormat)
                    (pDev, DfDIJoystick.pFormat) != 0)
            { ReleaseDevice(pDev); return IntPtr.Zero; }

            if (GetVtMethod<VtDI8DevSetCoopLevelFn>(pDev, DI8DevSlot.SetCooperativeLevel)
                    (pDev, hwnd, DISCL_NONEXCLUSIVE | DISCL_BACKGROUND) != 0)
            { ReleaseDevice(pDev); return IntPtr.Zero; }

            // Normalize all axis ranges to [0, 65535] (ignore failure for missing axes)
            foreach (uint off in new uint[] { 0, 4, 8, 12, 16, 20, 24, 28 })
                SetAxisRange(pDev, off, 0, 65535);

            GetVtMethod<VtDI8DevAcquireFn>(pDev, DI8DevSlot.Acquire)(pDev);
            return pDev;
        }

        static void SetAxisRange(IntPtr pDev, uint offset, int min, int max)
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
                GetVtMethod<VtDI8DevSetPropertyFn>(pDev, DI8DevSlot.SetProperty)
                    (pDev, DIPROP_RANGE, ptr);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        public static bool PollDevice(IntPtr pDev, out DIJOYSTATE state)
        {
            state = DefaultState();
            GetVtMethod<VtDI8DevPollFn>(pDev, DI8DevSlot.Poll)(pDev);
            IntPtr ptr = Marshal.AllocHGlobal(80); // sizeof(DIJOYSTATE)
            try
            {
                int hr = GetVtMethod<VtDI8DevGetStateFn>(pDev, DI8DevSlot.GetDeviceState)
                             (pDev, 80, ptr);
                if (hr != 0)
                {
                    GetVtMethod<VtDI8DevAcquireFn>(pDev, DI8DevSlot.Acquire)(pDev);
                    hr = GetVtMethod<VtDI8DevGetStateFn>(pDev, DI8DevSlot.GetDeviceState)
                             (pDev, 80, ptr);
                }
                if (hr != 0) return false;
                state = (DIJOYSTATE)Marshal.PtrToStructure(ptr, typeof(DIJOYSTATE));
                return true;
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        public static void ReleaseDevice(IntPtr pDev)
        {
            if (pDev != IntPtr.Zero)
                GetVtMethod<VtReleaseFn>(pDev, DI8DevSlot.Release)(pDev);
        }

        public static void ReleaseDI(IntPtr pDI)
        {
            if (pDI != IntPtr.Zero)
                GetVtMethod<VtReleaseFn>(pDI, DI8Slot.Release)(pDI);
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
            // Axis/POV GUIDs pinned in unmanaged heap for app lifetime
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
                new DIOBJECTDATAFORMAT { pguid=pgX,   dwOfs= 0, dwType=req,  dwFlags=0 },
                new DIOBJECTDATAFORMAT { pguid=pgY,   dwOfs= 4, dwType=req,  dwFlags=0 },
                new DIOBJECTDATAFORMAT { pguid=pgZ,   dwOfs= 8, dwType=optA, dwFlags=0 },
                new DIOBJECTDATAFORMAT { pguid=pgRx,  dwOfs=12, dwType=optA, dwFlags=0 },
                new DIOBJECTDATAFORMAT { pguid=pgRy,  dwOfs=16, dwType=optA, dwFlags=0 },
                new DIOBJECTDATAFORMAT { pguid=pgRz,  dwOfs=20, dwType=optA, dwFlags=0 },
                new DIOBJECTDATAFORMAT { pguid=pgSl,  dwOfs=24, dwType=optA, dwFlags=0 },
                new DIOBJECTDATAFORMAT { pguid=pgSl,  dwOfs=28, dwType=optA, dwFlags=0 },
                new DIOBJECTDATAFORMAT { pguid=pgPov, dwOfs=32, dwType=optP, dwFlags=0 },
                new DIOBJECTDATAFORMAT { pguid=pgPov, dwOfs=36, dwType=optP, dwFlags=0 },
                new DIOBJECTDATAFORMAT { pguid=pgPov, dwOfs=40, dwType=optP, dwFlags=0 },
                new DIOBJECTDATAFORMAT { pguid=pgPov, dwOfs=44, dwType=optP, dwFlags=0 },
            };
            for (int i = 0; i < 32; i++)
                objs.Add(new DIOBJECTDATAFORMAT
                    { pguid = IntPtr.Zero, dwOfs = (uint)(48 + i), dwType = optB, dwFlags = 0 });

            int    objSize  = Marshal.SizeOf(typeof(DIOBJECTDATAFORMAT));
            IntPtr pObjArr  = Marshal.AllocHGlobal(objs.Count * objSize);
            for (int i = 0; i < objs.Count; i++)
                Marshal.StructureToPtr(objs[i], IntPtr.Add(pObjArr, i * objSize), false);

            DIDATAFORMAT fmt = new DIDATAFORMAT
            {
                dwSize     = (uint)Marshal.SizeOf(typeof(DIDATAFORMAT)),
                dwObjSize  = (uint)objSize,
                dwFlags    = DIDF_ABSAXIS,
                dwDataSize = 80,
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
