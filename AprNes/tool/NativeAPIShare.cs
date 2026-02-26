using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace NativeTools
{
    static class NativeMethods
    {
        //REF http://www.cnblogs.com/kingthy/archive/2009/03/25/1421838.html
        //REF https://yal.cc/c-sharp-joystick-tracking-via-winmm-dll/
        [DllImport("winmm.dll")]
        public static extern Int32 joyGetPos(Int32 uJoyID, ref JOYINFO pji);
        [DllImport("winmm.dll")]
        public static extern int joyGetDevCaps(IntPtr uJoyID, ref JOYCAPS pjc, int cbjc);

        // XInput — Xbox 手把支援 (xinput1_4.dll, Windows 8+)
        [DllImport("xinput1_4.dll")]
        public static extern uint XInputGetState(uint dwUserIndex, ref XINPUT_STATE pState);


        //for rendering native api
        [DllImport("gdi32.dll", EntryPoint = "SelectObject")]
        public static extern System.IntPtr SelectObject([In()] System.IntPtr hdc, [In()] System.IntPtr h);

        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject([In()] System.IntPtr ho);

        [DllImport("gdi32.dll")]
        public static extern int SetDIBitsToDevice(IntPtr hdc, int XDest, int YDest, uint dwWidth, uint dwHeight, int XSrc, int YSrc, uint uStartScan, uint cScanLines, IntPtr lpvBits, [In] ref BITMAPINFO lpbmi, uint fuColorUse);

    }

    [StructLayoutAttribute(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        /// <summary>
        /// A BITMAPINFOHEADER structure that contains information about the dimensions of color format.
        /// </summary>
        public BITMAPINFOHEADER bmiHeader;

        /// <summary>
        /// An array of RGBQUAD. The elements of the array that make up the color table.
        /// </summary>
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 1, ArraySubType = UnmanagedType.Struct)]
        public RGBQUAD[] bmiColors;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct RGBQUAD
    {
        public byte rgbBlue;
        public byte rgbGreen;
        public byte rgbRed;
        public byte rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public BitmapCompressionMode biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;

        public void Init()
        {
            biSize = (uint)Marshal.SizeOf(this);
        }
    }

    public enum BitmapCompressionMode : uint
    {
        BI_RGB = 0,
        BI_RLE8 = 1,
        BI_RLE4 = 2,
        BI_BITFIELDS = 3,
        BI_JPEG = 4,
        BI_PNG = 5
    }

    //--

    [StructLayout(LayoutKind.Sequential)]
    public struct JOYCAPS
    {
        public ushort wMid;
        public ushort wPid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public int wXmin;
        public int wXmax;
        public int wYmin;
        public int wYmax;
        public int wZmin;
        public int wZmax;
        public int wNumButtons;
        public int wPeriodMin;
        public int wPeriodMax;
        public int wRmin;
        public int wRmax;
        public int wUmin;
        public int wUmax;
        public int wVmin;
        public int wVmax;
        public int wCaps;
        public int wMaxAxes;
        public int wNumAxes;
        public int wMaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szRegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szOEMVxD;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOYINFO
    {
        public Int32 wXpos; // Current X-coordinate.
        public Int32 wYpos; // Current Y-coordinate.
        public Int32 wZpos; // Current Z-coordinate.
        public Int32 wButtons; // Current state of joystick buttons.
    }

    //custom define
    public struct DeviceJoyInfo
    {
        public int ButtonCount;
        public int ID;
        public int Button_old;
        public int Way_X_old;
        public int Way_Y_old;
    }

    // XInput structs
    [StructLayout(LayoutKind.Explicit)]
    public struct XINPUT_GAMEPAD
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
    public struct XINPUT_STATE
    {
        public uint          dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    public struct joystickEvent
    {
        public int event_type; //0:方向鍵觸發 1:一般按鈕觸發
        public int joystick_id;//發生於哪個遊戲手把
        public int button_id;//如果是一般按鈕觸發,發生在哪顆按鈕
        public int button_event;//0:鬆開 1:壓下
        public int way_type; //0:x方向鍵盤 1:y方向鍵盤
        public int way_value;
        public joystickEvent(int _event_type, int _joystick_id, int _button_id, int _button_event, int _way_type, int _way_value)
        {
            event_type = _event_type;
            joystick_id = _joystick_id;
            button_id = _button_id;
            button_event = _button_event;
            way_type = _way_type;
            way_value = _way_value;
        }
    }
}
