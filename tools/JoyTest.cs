// JoyTest.cs — DirectInput + XInput diagnostic tool for AprNes
// Build (run in this folder or any folder):
//   csc /target:winexe /platform:x64 /r:System.Windows.Forms.dll /r:System.Drawing.dll JoyTest.cs
// Or run build_joytest.bat

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// ── Structs ──────────────────────────────────────────────────────────────────
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct DIDEVICEINSTANCE_MIN
{
    public uint dwSize;
    public Guid guidInstance;
    public Guid guidProduct;
    public uint dwDevType;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string tszInstanceName;
}

[StructLayout(LayoutKind.Sequential)]
struct DIJOYSTATE
{
    public int lX, lY, lZ;
    public int lRx, lRy, lRz;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]  public int[]  rglSlider;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]  public uint[] rgdwPOV;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbButtons;
}

[StructLayout(LayoutKind.Sequential)] struct DIPROPHEADER { public uint dwSize, dwHeaderSize, dwObj, dwHow; }
[StructLayout(LayoutKind.Sequential)] struct DIPROPRANGE   { public DIPROPHEADER diph; public int lMin, lMax; }

[StructLayout(LayoutKind.Explicit, Size = 24)]
struct DIOBJECTDATAFORMAT
{
    [FieldOffset(0)]  public IntPtr pguid;
    [FieldOffset(8)]  public uint   dwOfs;
    [FieldOffset(12)] public uint   dwType;
    [FieldOffset(16)] public uint   dwFlags;
}

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

[StructLayout(LayoutKind.Sequential)] struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

// XInput
[StructLayout(LayoutKind.Explicit)]
struct XINPUT_GAMEPAD
{
    [FieldOffset(0)]  public ushort wButtons;
    [FieldOffset(2)]  public byte   bLeftTrigger;
    [FieldOffset(3)]  public byte   bRightTrigger;
    [FieldOffset(4)]  public short  sThumbLX;
    [FieldOffset(6)]  public short  sThumbLY;
    [FieldOffset(8)]  public short  sThumbRX;
    [FieldOffset(10)] public short  sThumbRY;
}
[StructLayout(LayoutKind.Sequential)]
struct XINPUT_STATE { public uint dwPacketNumber; public XINPUT_GAMEPAD Gamepad; }

// ── Vtable delegates ──────────────────────────────────────────────────────────
[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint   VtReleaseFn(IntPtr self);
[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int    VtDI8CreateDeviceFn(IntPtr self, ref Guid rguid, out IntPtr ppDevice, IntPtr punk);
[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int    VtDI8EnumDevicesFn(IntPtr self, uint devType, IntPtr cb, IntPtr pvRef, uint flags);
[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int    VtDI8DevSetDataFormatFn(IntPtr self, IntPtr lpdf);
[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int    VtDI8DevSetCoopLevelFn(IntPtr self, IntPtr hwnd, uint flags);
[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int    VtDI8DevSetPropertyFn(IntPtr self, IntPtr rguidProp, IntPtr pdiph);
[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int    VtDI8DevAcquireFn(IntPtr self);
[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int    VtDI8DevPollFn(IntPtr self);
[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int    VtDI8DevGetStateFn(IntPtr self, uint cbData, IntPtr lpvData);
[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int    DiEnumDevicesNativeCb(IntPtr lpddi, IntPtr pvRef);

// ── DfDIJoystick (DIDATAFORMAT for DIJOYSTATE) ────────────────────────────────
static class DfDIJoystick
{
    const uint DIDFT_AXIS = 0x00000003, DIDFT_BUTTON = 0x0000000C, DIDFT_POV = 0x00000010;
    const uint DIDFT_ANYINSTANCE = 0x00FFFF00, DIDFT_OPTIONAL = 0x80000000;
    const uint DIDF_ABSAXIS = 0x00000001;
    public static readonly IntPtr pFormat;
    static DfDIJoystick()
    {
        IntPtr pgX=G(new Guid("A36D02E0-C9F3-11CF-BFC7-444553540000")), pgY=G(new Guid("A36D02E1-C9F3-11CF-BFC7-444553540000")),
               pgZ=G(new Guid("A36D02E2-C9F3-11CF-BFC7-444553540000")), pgRx=G(new Guid("A36D02F4-C9F3-11CF-BFC7-444553540000")),
               pgRy=G(new Guid("A36D02F5-C9F3-11CF-BFC7-444553540000")), pgRz=G(new Guid("A36D02E3-C9F3-11CF-BFC7-444553540000")),
               pgSl=G(new Guid("A36D02E4-C9F3-11CF-BFC7-444553540000")), pgPov=G(new Guid("A36D02F2-C9F3-11CF-BFC7-444553540000"));
        uint req=DIDFT_AXIS|DIDFT_ANYINSTANCE, optA=DIDFT_OPTIONAL|DIDFT_AXIS|DIDFT_ANYINSTANCE,
             optB=DIDFT_OPTIONAL|DIDFT_BUTTON|DIDFT_ANYINSTANCE, optP=DIDFT_OPTIONAL|DIDFT_POV|DIDFT_ANYINSTANCE;
        var o = new List<DIOBJECTDATAFORMAT>
        {
            O(pgX,0,req),O(pgY,4,req),O(pgZ,8,optA),O(pgRx,12,optA),O(pgRy,16,optA),O(pgRz,20,optA),O(pgSl,24,optA),O(pgSl,28,optA),
            O(pgPov,32,optP),O(pgPov,36,optP),O(pgPov,40,optP),O(pgPov,44,optP),
        };
        for (int i=0;i<32;i++) o.Add(new DIOBJECTDATAFORMAT{pguid=IntPtr.Zero,dwOfs=(uint)(48+i),dwType=optB});
        int sz=Marshal.SizeOf(typeof(DIOBJECTDATAFORMAT)); IntPtr arr=Marshal.AllocHGlobal(o.Count*sz);
        for (int i=0;i<o.Count;i++) Marshal.StructureToPtr(o[i],IntPtr.Add(arr,i*sz),false);
        DIDATAFORMAT f=new DIDATAFORMAT{dwSize=(uint)Marshal.SizeOf(typeof(DIDATAFORMAT)),dwObjSize=(uint)sz,dwFlags=DIDF_ABSAXIS,dwDataSize=80,dwNumObjs=(uint)o.Count,rgodf=arr};
        pFormat=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(DIDATAFORMAT))); Marshal.StructureToPtr(f,pFormat,false);
    }
    static IntPtr G(Guid g){IntPtr p=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Guid)));Marshal.StructureToPtr(g,p,false);return p;}
    static DIOBJECTDATAFORMAT O(IntPtr pg,uint ofs,uint type){return new DIOBJECTDATAFORMAT{pguid=pg,dwOfs=ofs,dwType=type};}
}

// ── DI vtable helper ──────────────────────────────────────────────────────────
static class DI
{
    static readonly Guid IID_IDirectInput8W = new Guid("BF798031-483A-4DA2-AA99-5D64ED369700");
    const uint DI8DEVCLASS_GAMECTRL=4, DIEDFL_ATTACHEDONLY=1, DISCL_NONEXCLUSIVE=2, DISCL_BACKGROUND=8;
    static readonly IntPtr DIPROP_RANGE = (IntPtr)4;
    const uint DIPH_BYOFFSET = 3;

    [DllImport("dinput8.dll")]   static extern int DirectInput8Create(IntPtr hinst,uint ver,ref Guid iid,out IntPtr ppv,IntPtr unk);
    [DllImport("kernel32.dll")]  static extern IntPtr GetModuleHandle(string m);
    [DllImport("setupapi.dll",CharSet=CharSet.Auto,SetLastError=true)] static extern IntPtr SetupDiGetClassDevs(IntPtr cg,string en,IntPtr hw,uint fl);
    [DllImport("setupapi.dll",CharSet=CharSet.Auto,SetLastError=true)] static extern bool SetupDiEnumDeviceInfo(IntPtr s,uint i,ref SP_DEVINFO_DATA d);
    [DllImport("setupapi.dll",CharSet=CharSet.Auto,SetLastError=true)] static extern bool SetupDiGetDeviceInstanceId(IntPtr s,ref SP_DEVINFO_DATA d,StringBuilder buf,uint sz,out uint req);
    [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [DllImport("xinput1_4.dll")] public static extern uint XInputGetState(uint idx, ref XINPUT_STATE st);

    static T V<T>(IntPtr p,int slot) where T:class
    { IntPtr vt=Marshal.ReadIntPtr(p); return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt,slot*IntPtr.Size),typeof(T)) as T; }

    static HashSet<uint> XInputVidPids()
    {
        var r=new HashSet<uint>(); IntPtr h=SetupDiGetClassDevs(IntPtr.Zero,"HID",IntPtr.Zero,6);
        if(h==new IntPtr(-1)) return r;
        try {
            SP_DEVINFO_DATA d=new SP_DEVINFO_DATA{cbSize=(uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA))};
            for(uint i=0;SetupDiEnumDeviceInfo(h,i,ref d);i++){
                StringBuilder sb=new StringBuilder(512); uint req;
                if(!SetupDiGetDeviceInstanceId(h,ref d,sb,(uint)sb.Capacity,out req)) continue;
                string p=sb.ToString().ToUpperInvariant(); if(!p.Contains("IG_")) continue;
                int vi=p.IndexOf("VID_"),pi=p.IndexOf("PID_"); if(vi<0||pi<0) continue;
                uint vid,pid;
                if(uint.TryParse(p.Substring(vi+4,4),System.Globalization.NumberStyles.HexNumber,null,out vid)&&
                   uint.TryParse(p.Substring(pi+4,4),System.Globalization.NumberStyles.HexNumber,null,out pid))
                    r.Add((pid<<16)|vid);
            }
        } finally { SetupDiDestroyDeviceInfoList(h); }
        return r;
    }

    public static IntPtr CreateDI()
    {
        Guid iid=IID_IDirectInput8W; IntPtr p;
        int hr=DirectInput8Create(GetModuleHandle(null),0x0800,ref iid,out p,IntPtr.Zero);
        if(hr!=0||p==IntPtr.Zero) throw new Exception("DirectInput8Create failed 0x"+hr.ToString("X8"));
        return p;
    }

    public struct DevInfo { public Guid inst; public Guid prod; public string name; }

    public static List<DevInfo> EnumJoysticks(IntPtr pDI)
    {
        var xip=XInputVidPids();
        var devs=new List<DevInfo>();
        DiEnumDevicesNativeCb cb=(lpddi,_)=>{
            try {
                var inst=(DIDEVICEINSTANCE_MIN)Marshal.PtrToStructure(lpddi,typeof(DIDEVICEINSTANCE_MIN));
                uint d1=BitConverter.ToUInt32(inst.guidProduct.ToByteArray(),0);
                if(!xip.Contains(d1)) devs.Add(new DevInfo{inst=inst.guidInstance,prod=inst.guidProduct,name=inst.tszInstanceName??""});
            } catch {}
            return 1;
        };
        IntPtr cbp=Marshal.GetFunctionPointerForDelegate(cb);
        V<VtDI8EnumDevicesFn>(pDI,4)(pDI,DI8DEVCLASS_GAMECTRL,cbp,IntPtr.Zero,DIEDFL_ATTACHEDONLY);
        GC.KeepAlive(cb);
        return devs;
    }

    public static IntPtr OpenDevice(IntPtr pDI, Guid guid, IntPtr hwnd)
    {
        IntPtr pDev; Guid g=guid;
        if(V<VtDI8CreateDeviceFn>(pDI,3)(pDI,ref g,out pDev,IntPtr.Zero)!=0||pDev==IntPtr.Zero) return IntPtr.Zero;
        if(V<VtDI8DevSetDataFormatFn>(pDev,11)(pDev,DfDIJoystick.pFormat)!=0){ReleaseDevice(pDev);return IntPtr.Zero;}
        if(V<VtDI8DevSetCoopLevelFn>(pDev,13)(pDev,hwnd,DISCL_NONEXCLUSIVE|DISCL_BACKGROUND)!=0){ReleaseDevice(pDev);return IntPtr.Zero;}
        foreach(uint off in new uint[]{0,4,8,12,16,20,24,28}) SetAxisRange(pDev,off,0,65535);
        V<VtDI8DevAcquireFn>(pDev,7)(pDev);
        return pDev;
    }

    static void SetAxisRange(IntPtr pDev,uint ofs,int mn,int mx)
    {
        DIPROPRANGE r=new DIPROPRANGE{diph=new DIPROPHEADER{
            dwSize=(uint)Marshal.SizeOf(typeof(DIPROPRANGE)),dwHeaderSize=(uint)Marshal.SizeOf(typeof(DIPROPHEADER)),
            dwObj=ofs,dwHow=DIPH_BYOFFSET},lMin=mn,lMax=mx};
        IntPtr ptr=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(DIPROPRANGE)));
        try { Marshal.StructureToPtr(r,ptr,false); V<VtDI8DevSetPropertyFn>(pDev,6)(pDev,DIPROP_RANGE,ptr); }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    public static bool Poll(IntPtr pDev, out DIJOYSTATE st)
    {
        st=new DIJOYSTATE{rglSlider=new int[2],rgdwPOV=new uint[]{0xFFFFFFFF,0xFFFFFFFF,0xFFFFFFFF,0xFFFFFFFF},rgbButtons=new byte[32]};
        V<VtDI8DevPollFn>(pDev,25)(pDev);
        IntPtr ptr=Marshal.AllocHGlobal(80);
        try {
            int hr=V<VtDI8DevGetStateFn>(pDev,9)(pDev,80,ptr);
            if(hr!=0){V<VtDI8DevAcquireFn>(pDev,7)(pDev);hr=V<VtDI8DevGetStateFn>(pDev,9)(pDev,80,ptr);}
            if(hr!=0) return false;
            st=(DIJOYSTATE)Marshal.PtrToStructure(ptr,typeof(DIJOYSTATE));
            return true;
        } finally { Marshal.FreeHGlobal(ptr); }
    }

    public static void ReleaseDevice(IntPtr pDev){if(pDev!=IntPtr.Zero)V<VtReleaseFn>(pDev,2)(pDev);}
}

// ── Main Form ─────────────────────────────────────────────────────────────────
class JoyTestForm : Form
{
    // DI state
    IntPtr _pDI = IntPtr.Zero;
    struct DIDev { public int ID; public string Name; public IntPtr Ptr; public DIJOYSTATE Prev; }
    List<DIDev> _diDevs = new List<DIDev>();

    // XI state
    XINPUT_STATE[] _xiPrev = new XINPUT_STATE[4];
    bool[] _xiConn = new bool[4];

    // UI
    ListBox   _log;
    Label     _rawLabel;
    Button    _clearBtn;
    Button    _reinitBtn;
    Label     _devLabel;
    Thread    _pollThread;
    bool      _running = true;
    const int MAX_LOG = 300;

    public JoyTestForm()
    {
        Text = "JoyTest — DirectInput + XInput Diagnostic";
        Size = new Size(820, 600);
        MinimumSize = new Size(600, 400);
        Font = new Font("Consolas", 9f);

        var devPanel = new Panel { Dock = DockStyle.Top, Height = 80, Padding = new Padding(4) };
        _devLabel = new Label { Dock = DockStyle.Fill, Text = "Initializing...", AutoSize = false };
        devPanel.Controls.Add(_devLabel);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(4,2,4,2), FlowDirection = FlowDirection.LeftToRight };
        _clearBtn  = new Button { Text = "Clear Log",    AutoSize = true };
        _reinitBtn = new Button { Text = "Re-init Joy",  AutoSize = true };
        _clearBtn.Click  += (s,e) => _log.Items.Clear();
        _reinitBtn.Click += (s,e) => InitJoy();
        btnPanel.Controls.Add(_clearBtn);
        btnPanel.Controls.Add(_reinitBtn);

        _rawLabel = new Label { Dock = DockStyle.Bottom, Height = 52, Font = new Font("Consolas", 8f), Text = "Raw state will appear here when a device is active..." };

        _log = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true,
                             IntegralHeight = false, SelectionMode = SelectionMode.None };

        Controls.Add(_log);
        Controls.Add(_rawLabel);
        Controls.Add(btnPanel);
        Controls.Add(devPanel);

        Shown += (s,e) => { InitJoy(); StartPoll(); };
        FormClosing += (s,e) => { _running = false; };
    }

    void AddLog(string msg)
    {
        if (InvokeRequired) { Invoke(new Action<string>(AddLog), msg); return; }
        if (_log.Items.Count >= MAX_LOG) _log.Items.RemoveAt(0);
        _log.Items.Add(msg);
        _log.TopIndex = _log.Items.Count - 1;
    }

    void UpdateRaw(string msg)
    {
        if (InvokeRequired) { Invoke(new Action<string>(UpdateRaw), msg); return; }
        _rawLabel.Text = msg;
    }

    void UpdateDevLabel(string msg)
    {
        if (InvokeRequired) { Invoke(new Action<string>(UpdateDevLabel), msg); return; }
        _devLabel.Text = msg;
    }

    void InitJoy()
    {
        // Release existing
        foreach (var d in _diDevs) DI.ReleaseDevice(d.Ptr);
        _diDevs.Clear();
        if (_pDI != IntPtr.Zero) { /* leave _pDI alive for re-init */ }

        var sb = new StringBuilder();
        sb.AppendLine("[DirectInput Devices]");
        try
        {
            if (_pDI == IntPtr.Zero) _pDI = DI.CreateDI();
            var devList = DI.EnumJoysticks(_pDI);
            if (devList.Count == 0) sb.AppendLine("  (none found)");
            int id = 0;
            foreach (var info in devList)
            {
                IntPtr pDev = DI.OpenDevice(_pDI, info.inst, Handle);
                DIJOYSTATE blank = new DIJOYSTATE
                {
                    rglSlider=new int[2], rgdwPOV=new uint[]{0xFFFFFFFF,0xFFFFFFFF,0xFFFFFFFF,0xFFFFFFFF}, rgbButtons=new byte[32]
                };
                _diDevs.Add(new DIDev { ID=id, Name=info.name, Ptr=pDev, Prev=blank });
                sb.AppendLine("  [" + id + "] " + info.name + (pDev==IntPtr.Zero ? " (OPEN FAILED)" : " OK"));
                id++;
            }
        }
        catch (Exception ex) { sb.AppendLine("  ERROR: " + ex.Message); }

        sb.AppendLine("[XInput Players]");
        try
        {
            for (int i = 0; i < 4; i++)
            {
                XINPUT_STATE st = new XINPUT_STATE();
                _xiConn[i] = (DI.XInputGetState((uint)i, ref st) == 0);
                _xiPrev[i] = st;
                if (_xiConn[i]) sb.AppendLine("  Player " + i + ": connected");
            }
            if (!Array.Exists(_xiConn, x => x)) sb.AppendLine("  (none connected)");
        }
        catch { sb.AppendLine("  (XInput not available)"); }

        UpdateDevLabel(sb.ToString().TrimEnd());
        AddLog("=== Joystick re-initialized ===");
    }

    void StartPoll()
    {
        _pollThread = new Thread(PollLoop) { IsBackground = true, Name = "JoyPoll" };
        _pollThread.Start();
    }

    void PollLoop()
    {
        while (_running)
        {
            Thread.Sleep(10);
            PollDI();
            PollXI();
        }
    }

    void PollDI()
    {
        var rawLines = new StringBuilder();
        for (int idx = 0; idx < _diDevs.Count; idx++)
        {
            DIDev dev = _diDevs[idx];
            if (dev.Ptr == IntPtr.Zero) continue;

            DIJOYSTATE st;
            if (!DI.Poll(dev.Ptr, out st)) continue;

            DIJOYSTATE prev = dev.Prev;
            dev.Prev = st;
            _diDevs[idx] = dev;

            // Raw state for first device
            if (idx == 0)
            {
                var btns = new StringBuilder();
                for (int b = 0; b < 32; b++) if ((st.rgbButtons[b] & 0x80) != 0) btns.Append(b+1 + " ");
                rawLines.AppendLine(string.Format("[DI:{0}] lX={1,6} lY={2,6} lZ={3,6} | lRx={4,6} lRy={5,6} lRz={6,6}",
                    dev.ID, st.lX, st.lY, st.lZ, st.lRx, st.lRy, st.lRz));
                rawLines.AppendLine(string.Format("       POV={0,10} | Buttons: {1}", st.rgdwPOV[0], btns.Length>0?btns.ToString().Trim():"(none)"));
            }

            // Button events
            for (int b = 0; b < 32; b++)
            {
                bool now  = (st.rgbButtons[b]   & 0x80) != 0;
                bool prv  = (prev.rgbButtons[b]  & 0x80) != 0;
                if (now != prv)
                    AddLog(string.Format("[DI:{0}] event_type=1 BUTTON {1} {2}", dev.ID, b+1, now?"PRESS":"RELEASE"));
            }

            // Axis events (with noise filter ±256)
            const int CENTER = 32767, NOISE = 256;
            int xN=(Math.Abs(st.lX-CENTER)<NOISE)?CENTER:st.lX;    int xP=(Math.Abs(prev.lX-CENTER)<NOISE)?CENTER:prev.lX;
            int yN=(Math.Abs(st.lY-CENTER)<NOISE)?CENTER:st.lY;    int yP=(Math.Abs(prev.lY-CENTER)<NOISE)?CENTER:prev.lY;
            if (xP!=xN||xN!=CENTER) AddLog(string.Format("[DI:{0}] event_type=0 AXIS   X={1,6}  (raw={2})", dev.ID, xN, st.lX));
            if (yP!=yN||yN!=CENTER) AddLog(string.Format("[DI:{0}] event_type=0 AXIS   Y={1,6}  (raw={2})", dev.ID, yN, st.lY));

            // POV
            if (st.rgdwPOV[0] != prev.rgdwPOV[0])
            {
                string pStr = st.rgdwPOV[0] == 0xFFFFFFFF ? "CENTER" : (st.rgdwPOV[0] / 100.0).ToString("F1") + "°";
                int povX, povY; PovToXY(st.rgdwPOV[0], out povX, out povY);
                AddLog(string.Format("[DI:{0}] event_type=0 POV    {1} → X={2,6} Y={3,6}", dev.ID, pStr, povX, povY));
            }
        }

        if (rawLines.Length > 0) UpdateRaw(rawLines.ToString().TrimEnd());
    }

    static void PovToXY(uint pov, out int x, out int y)
    {
        x = 32767; y = 32767;
        if (pov == 0xFFFFFFFFu) return;
        int d = (int)pov;
        if      (d >= 4500  && d <= 13500) x = 65535;
        else if (d >= 22500 && d <= 31500) x = 0;
        if      (d <= 4500  || d >= 31500) y = 0;
        else if (d >= 13500 && d <= 22500) y = 65535;
    }

    void PollXI()
    {
        bool xi = false;
        for (int i = 0; i < 4; i++)
        {
            XINPUT_STATE cur = new XINPUT_STATE();
            bool conn;
            try { conn = (DI.XInputGetState((uint)i, ref cur) == 0); } catch { break; }
            if (!conn) { _xiConn[i] = false; continue; }
            if (!_xiConn[i]) { _xiConn[i] = true; _xiPrev[i] = cur; }
            XINPUT_STATE prv = _xiPrev[i];
            _xiPrev[i] = cur;
            xi = true;

            int id = 1000 + i;
            // Buttons A B X Y LB RB Start Back
            int[] masks = { 0x1000,0x2000,0x4000,0x8000,0x0100,0x0200,0x0010,0x0020 };
            string[] names = { "A","B","X","Y","LB","RB","Start","Back" };
            for (int b = 0; b < masks.Length; b++)
            {
                bool now = (cur.Gamepad.wButtons & masks[b]) != 0;
                bool prv2 = (prv.Gamepad.wButtons & masks[b]) != 0;
                if (now != prv2) AddLog(string.Format("[XI:{0}] event_type=1 BUTTON {1} {2}", id, names[b], now?"PRESS":"RELEASE"));
            }
            // D-Pad
            int dN=cur.Gamepad.wButtons&0xF, dP=prv.Gamepad.wButtons&0xF;
            int xN=((dN&8)!=0)?65535:((dN&4)!=0)?0:32767;
            int xPr=((dP&8)!=0)?65535:((dP&4)!=0)?0:32767;
            int yN=((dN&2)!=0)?65535:((dN&1)!=0)?0:32767;
            int yPr=((dP&2)!=0)?65535:((dP&1)!=0)?0:32767;
            if (xN!=xPr||xN!=32767) AddLog(string.Format("[XI:{0}] event_type=0 DPAD   X={1,6}", id, xN));
            if (yN!=yPr||yN!=32767) AddLog(string.Format("[XI:{0}] event_type=0 DPAD   Y={1,6}", id, yN));

            // Raw for first XI device
            if (xi)
            {
                xi = false;
                UpdateRaw(string.Format("[XI:{0}] Btns=0x{1:X4}  LX={2,6} LY={3,6}  RX={4,6} RY={5,6}  LT={6} RT={7}",
                    id, cur.Gamepad.wButtons, cur.Gamepad.sThumbLX, cur.Gamepad.sThumbLY,
                    cur.Gamepad.sThumbRX, cur.Gamepad.sThumbRY, cur.Gamepad.bLeftTrigger, cur.Gamepad.bRightTrigger));
            }
        }
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new JoyTestForm());
    }
}
