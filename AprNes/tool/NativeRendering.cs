using System;
using System.Drawing;
using System.Runtime.InteropServices;
using NativeTools;

namespace WINAPIGDI
{
    public static class NativeGDI
    {
        public const int DIB_RGB_COLORS = 0;
        public const int DIB_PAL_COLORS = 1;

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
            hOldObject = NativeMethods.SelectObject(hdcSrc, hBitmap);

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

            if (hOldObject != IntPtr.Zero) NativeMethods.SelectObject(hdcSrc, hOldObject);
            if (hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(hBitmap);
            if (hdcDest != IntPtr.Zero) grDest.ReleaseHdc(hdcDest);
            if (hdcSrc != IntPtr.Zero) grSrc.ReleaseHdc(hdcSrc);
            try { _Bitmap.Dispose(); }
            catch { }
        }

        public unsafe static void DrawImageHighSpeedtoDevice()
        {
            NativeMethods.SetDIBitsToDevice(hdcDest, loc_x ,loc_y, (uint)w, (uint)h, 0, 0, 0, (uint)h, data_ptr, ref info, DIB_RGB_COLORS);
        }
    }
}
