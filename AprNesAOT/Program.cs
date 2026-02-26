using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using LangTool;

namespace AprNes
{
    unsafe class Program
    {
        // ── Win32 constants ────────────────────────────────────────────────────
        const uint WM_DESTROY   = 0x0002;
        const uint WM_PAINT     = 0x000F;
        const uint WM_KEYDOWN   = 0x0100;
        const uint WM_KEYUP     = 0x0101;
        const uint WM_DROPFILES = 0x0233;

        const uint CS_HREDRAW   = 0x0002;
        const uint CS_VREDRAW   = 0x0001;
        const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
        const uint WS_EX_ACCEPTFILES   = 0x00000010;
        const int  SW_SHOW      = 5;
        const uint IDC_ARROW    = 32512;
        const int  DIB_RGB_COLORS = 0;
        const int  BI_RGB       = 0;

        // VK codes
        const int VK_LEFT  = 0x25;
        const int VK_UP    = 0x26;
        const int VK_RIGHT = 0x27;
        const int VK_DOWN  = 0x28;
        const int VK_Z     = 0x5A;
        const int VK_X     = 0x58;
        const int VK_A     = 0x41;
        const int VK_S     = 0x53;

        // ── Win32 structs ──────────────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        struct WNDCLASSEX
        {
            public uint      cbSize;
            public uint      style;
            public nint      lpfnWndProc;
            public int       cbClsExtra;
            public int       cbWndExtra;
            public nint      hInstance;
            public nint      hIcon;
            public nint      hCursor;
            public nint      hbrBackground;
            public nint      lpszMenuName;
            public nint      lpszClassName;
            public nint      hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSG
        {
            public nint  hwnd;
            public uint  message;
            public nint  wParam;
            public nint  lParam;
            public uint  time;
            public int   ptX, ptY;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PAINTSTRUCT
        {
            public nint  hdc;
            public int   fErase;
            public int   rcLeft, rcTop, rcRight, rcBottom;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] rgbReserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public uint  biSize;
            public int   biWidth;
            public int   biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint  biCompression;
            public uint  biSizeImage;
            public int   biXPelsPerMeter;
            public int   biYPelsPerMeter;
            public uint  biClrUsed;
            public uint  biClrImportant;
        }

        // ── P/Invoke ───────────────────────────────────────────────────────────
        [DllImport("user32.dll", SetLastError = true)]
        static extern ushort RegisterClassExW(ref WNDCLASSEX lpWndClass);

        [DllImport("user32.dll", SetLastError = true)]
        static extern nint CreateWindowExW(
            uint dwExStyle, nint lpClassName, nint lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight,
            nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

        [DllImport("user32.dll")] static extern int  ShowWindow(nint hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern int  UpdateWindow(nint hWnd);
        [DllImport("user32.dll")] static extern int  GetMessageW(ref MSG lpMsg, nint hWnd, uint min, uint max);
        [DllImport("user32.dll")] static extern int  TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll")] static extern nint DispatchMessageW(ref MSG lpMsg);
        [DllImport("user32.dll")] static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);
        [DllImport("user32.dll")] static extern void PostQuitMessage(int nExitCode);
        [DllImport("user32.dll")] static extern int  DestroyWindow(nint hWnd);
        [DllImport("user32.dll")] static extern int  InvalidateRect(nint hWnd, nint lpRect, int bErase);
        [DllImport("user32.dll")] static extern nint BeginPaint(nint hWnd, ref PAINTSTRUCT lpPaint);
        [DllImport("user32.dll")] static extern int  EndPaint(nint hWnd, ref PAINTSTRUCT lpPaint);
        [DllImport("user32.dll")] static extern nint LoadCursorW(nint hInstance, nint lpCursorName);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int MessageBoxW(nint hWnd, string lpText, string lpCaption, uint uType);

        [DllImport("kernel32.dll")]
        static extern nint GetModuleHandleW(nint lpModuleName);

        [DllImport("gdi32.dll")]
        static extern int SetDIBitsToDevice(
            nint hdc, int xDest, int yDest, uint dwWidth, uint dwHeight,
            int xSrc, int ySrc, uint uStartScan, uint cScanLines,
            void* lpvBits, ref BITMAPINFOHEADER lpbmi, uint fuColorUse);

        [DllImport("shell32.dll")] static extern void DragAcceptFiles(nint hWnd, int fAccept);
        [DllImport("shell32.dll")] static extern uint DragQueryFileW(nint hDrop, uint iFile, nint lpszFile, uint cch);
        [DllImport("shell32.dll")] static extern void DragFinish(nint hDrop);

        // ── Delegate for WndProc (kept alive to prevent GC) ───────────────────
        delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);
        static WndProcDelegate _wndProcDelegate;

        // ── State ──────────────────────────────────────────────────────────────
        static nint          _hWnd;
        static Thread        _emuThread;
        static volatile bool _running;
        static BITMAPINFOHEADER _bih;

        // ── Entry point ────────────────────────────────────────────────────────
        static void Main(string[] args)
        {
            LangINI.init();

            // Bitmap header for 256×240 32-bpp (bottom-up → negative height = top-down)
            _bih = new BITMAPINFOHEADER
            {
                biSize        = (uint)sizeof(BITMAPINFOHEADER),
                biWidth       = 256,
                biHeight      = -240,   // negative = top-down
                biPlanes      = 1,
                biBitCount    = 32,
                biCompression = (uint)BI_RGB,
            };

            // Wire NesCore events
            NesCore.VideoOutput += (s, e) => InvalidateRect(_hWnd, nint.Zero, 0);
            NesCore.OnError = msg => MessageBoxW(_hWnd, msg, "AprNes AOT - Error", 0x10);

            nint hInstance = GetModuleHandleW(nint.Zero);

            // Register window class
            _wndProcDelegate = WndProc;
            nint wndProcPtr  = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);

            fixed (char* clsName = "AprNesAOT\0")
            fixed (char* title   = "AprNes AOT\0")
            {
                WNDCLASSEX wc = new WNDCLASSEX
                {
                    cbSize        = (uint)sizeof(WNDCLASSEX),
                    style         = CS_HREDRAW | CS_VREDRAW,
                    lpfnWndProc   = wndProcPtr,
                    hInstance     = hInstance,
                    hCursor       = LoadCursorW(nint.Zero, (nint)IDC_ARROW),
                    hbrBackground = (nint)6, // COLOR_WINDOW+1
                    lpszClassName = (nint)clsName,
                };
                RegisterClassExW(ref wc);

                const int scale = 2;
                _hWnd = CreateWindowExW(
                    WS_EX_ACCEPTFILES,
                    (nint)clsName, (nint)title,
                    WS_OVERLAPPEDWINDOW,
                    100, 100,
                    256 * scale + 16,
                    240 * scale + 39,
                    nint.Zero, nint.Zero, hInstance, nint.Zero);
            }

            if (_hWnd == nint.Zero)
            {
                MessageBoxW(nint.Zero, "CreateWindowEx failed", "Error", 0x10);
                return;
            }

            DragAcceptFiles(_hWnd, 1);
            WaveOutPlayer.OpenAudio();

            ShowWindow(_hWnd, SW_SHOW);
            UpdateWindow(_hWnd);

            // Message loop
            MSG msg = default;
            while (GetMessageW(ref msg, nint.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }

            WaveOutPlayer.CloseAudio();
        }

        // ── Window procedure ───────────────────────────────────────────────────
        static nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
        {
            switch (msg)
            {
                case WM_PAINT:
                {
                    PAINTSTRUCT ps = default;
                    nint hdc = BeginPaint(hWnd, ref ps);
                    if (NesCore.ScreenBuf1x != null)
                    {
                        const int scale = 2;
                        // stretch-blit 256×240 → 512×480 using StretchDIBits would require extra API;
                        // use SetDIBitsToDevice at 1:1 then rely on window client area for now.
                        // For 2x, we do a simple manual scale to a temp buffer.
                        uint* src = NesCore.ScreenBuf1x;
                        int dstW = 256 * scale, dstH = 240 * scale;
                        uint* dst = (uint*)Marshal.AllocHGlobal(dstW * dstH * 4);
                        for (int y = 0; y < 240; y++)
                            for (int x = 0; x < 256; x++)
                            {
                                uint c = src[y * 256 + x];
                                dst[(y * 2 + 0) * dstW + x * 2 + 0] = c;
                                dst[(y * 2 + 0) * dstW + x * 2 + 1] = c;
                                dst[(y * 2 + 1) * dstW + x * 2 + 0] = c;
                                dst[(y * 2 + 1) * dstW + x * 2 + 1] = c;
                            }
                        BITMAPINFOHEADER bih2x = _bih;
                        bih2x.biWidth  = dstW;
                        bih2x.biHeight = -dstH;
                        SetDIBitsToDevice(hdc,
                            0, 0, (uint)dstW, (uint)dstH,
                            0, 0, 0, (uint)dstH,
                            dst, ref bih2x, (uint)DIB_RGB_COLORS);
                        Marshal.FreeHGlobal((nint)dst);
                    }
                    EndPaint(hWnd, ref ps);
                    return nint.Zero;
                }

                case WM_KEYDOWN:
                {
                    byte btn = VkToButton((int)wParam);
                    if (btn != 0xFF) NesCore.P1_ButtonPress(btn);
                    return nint.Zero;
                }

                case WM_KEYUP:
                {
                    byte btn = VkToButton((int)wParam);
                    if (btn != 0xFF) NesCore.P1_ButtonUnPress(btn);
                    return nint.Zero;
                }

                case WM_DROPFILES:
                {
                    nint hDrop = wParam;
                    // query file name length
                    uint len = DragQueryFileW(hDrop, 0, nint.Zero, 0);
                    if (len > 0)
                    {
                        char[] buf = new char[(int)len + 1];
                        fixed (char* p = buf)
                            DragQueryFileW(hDrop, 0, (nint)p, len + 1);
                        string path = new string(buf, 0, (int)len);
                        DragFinish(hDrop);
                        LoadRom(path);
                    }
                    else
                    {
                        DragFinish(hDrop);
                    }
                    return nint.Zero;
                }

                case WM_DESTROY:
                    StopEmulation();
                    PostQuitMessage(0);
                    return nint.Zero;
            }
            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        // ── Key mapping ────────────────────────────────────────────────────────
        static byte VkToButton(int vk)
        {
            switch (vk)
            {
                case VK_Z:     return 0; // A
                case VK_X:     return 1; // B
                case VK_A:     return 2; // Select
                case VK_S:     return 3; // Start
                case VK_UP:    return 4;
                case VK_DOWN:  return 5;
                case VK_LEFT:  return 6;
                case VK_RIGHT: return 7;
                default:       return 0xFF;
            }
        }

        // ── ROM loading ────────────────────────────────────────────────────────
        static void LoadRom(string path)
        {
            StopEmulation();
            try
            {
                byte[] rom = File.ReadAllBytes(path);
                NesCore.rom_file_name = path;
                if (!NesCore.init(rom))
                    return;
                _running = true;
                NesCore.exit = false;
                _emuThread = new Thread(() =>
                {
                    try { NesCore.run(); }
                    catch (Exception) { }
                });
                _emuThread.IsBackground = true;
                _emuThread.Start();
            }
            catch (Exception ex)
            {
                MessageBoxW(_hWnd, ex.Message, "Load Error", 0x10);
            }
        }

        static void StopEmulation()
        {
            if (_running)
            {
                _running = false;
                NesCore.exit = true;
                _emuThread?.Join(2000);
                _emuThread = null;
            }
        }
    }
}
