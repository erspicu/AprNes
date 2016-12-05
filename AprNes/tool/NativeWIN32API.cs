using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace NativeWIN32API
{
    public static class NativeGDI
    {
        static IntPtr hdcDest = IntPtr.Zero;
        static IntPtr hdcSrc = IntPtr.Zero;
        static IntPtr hBitmap = IntPtr.Zero;
        static IntPtr hOldObject = IntPtr.Zero;
        static Graphics grSrc;
        static Graphics grDest;

        static int w, h;
        static Bitmap _Bitmap;
        static IntPtr data_ptr;
        static BITMAPINFO info;

        static int loc_x=0;
        static int loc_y=0;

        public unsafe static void initHighSpeed(Graphics _grDest, int width, int height, uint* data , int dx , int dy )
        {

            loc_x = dx;
            loc_y = dy;

            freeHighSpeed();

            w = width;
            h = height;
            _Bitmap = new Bitmap(width, height);
            grSrc = Graphics.FromImage(_Bitmap);
            grDest = _grDest;

            hdcDest = grDest.GetHdc();
            hdcSrc = grSrc.GetHdc();

            hBitmap = _Bitmap.GetHbitmap();
            hOldObject = SelectObject(hdcSrc, hBitmap);

            info = new BITMAPINFO();
            info.bmiHeader = new BITMAPINFOHEADER();
            info.bmiHeader.biSize = (uint)Marshal.SizeOf(info.bmiHeader);
            info.bmiHeader.biWidth = w;
            //http://www.tech-archive.net/Archive/Development/microsoft.public.win32.programmer.gdi/2006-02/msg00157.html
            info.bmiHeader.biHeight = -h;
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;
            info.bmiHeader.biCompression = BitmapCompressionMode.BI_RGB;
            info.bmiHeader.biSizeImage = (uint)(w * h * 4);
            data_ptr = (IntPtr)data;
        }

        public unsafe static void freeHighSpeed()
        {

            if (hOldObject != IntPtr.Zero) SelectObject(hdcSrc, hOldObject);
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            if (hdcDest != IntPtr.Zero) grDest.ReleaseHdc(hdcDest);
            if (hdcSrc != IntPtr.Zero) grSrc.ReleaseHdc(hdcSrc);
            try { _Bitmap.Dispose(); }
            catch { }
        }

        public unsafe static void DrawImageHighSpeedtoDevice()
        {
            SetDIBitsToDevice(hdcDest, loc_x ,loc_y, (uint)w, (uint)h, 0, 0, 0, (uint)h, data_ptr, ref info, DIB_RGB_COLORS);
        }

        public static void DrawImage(Graphics grDest, Bitmap grSrcBitmap)
        {
            grSrc = Graphics.FromImage(grSrcBitmap);

            hdcDest = grDest.GetHdc();
            hdcSrc = grSrc.GetHdc();
            hBitmap = grSrcBitmap.GetHbitmap();
            hOldObject = SelectObject(hdcSrc, hBitmap);

            BitBlt(hdcDest, 0, 0, grSrcBitmap.Width, grSrcBitmap.Height, hdcSrc, 0, 0, 0x00CC0020U);

            if (hOldObject != IntPtr.Zero)
                SelectObject(hdcSrc, hOldObject);
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);
            if (hdcDest != IntPtr.Zero)
                grDest.ReleaseHdc(hdcDest);
            if (hdcSrc != IntPtr.Zero)
                grSrc.ReleaseHdc(hdcSrc);

        }

        [DllImport("gdi32.dll", EntryPoint = "SelectObject")]
        public static extern System.IntPtr SelectObject([In()] System.IntPtr hdc, [In()] System.IntPtr h);

        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject([In()] System.IntPtr ho);

        [DllImport("gdi32.dll", EntryPoint = "BitBlt")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BitBlt([In()] System.IntPtr hdc, int x, int y, int cx, int cy, [In()] System.IntPtr hdcSrc, int x1, int y1, uint rop);


        [DllImport("gdi32.dll")]
        static extern int SetDIBitsToDevice(IntPtr hdc, int XDest, int YDest, uint dwWidth, uint dwHeight, int XSrc, int YSrc, uint uStartScan, uint cScanLines, byte[] lpvBits, [In] ref BITMAPINFO lpbmi, uint fuColorUse);

        [DllImport("gdi32.dll")]
        static extern int SetDIBitsToDevice(IntPtr hdc, int XDest, int YDest, uint dwWidth, uint dwHeight, int XSrc, int YSrc, uint uStartScan, uint cScanLines, IntPtr lpvBits, [In] ref BITMAPINFO lpbmi, uint fuColorUse);


        public const int DIB_RGB_COLORS = 0;
        public const int DIB_PAL_COLORS = 1;

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

    }
}
