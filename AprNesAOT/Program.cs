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
        const uint WM_COMMAND   = 0x0111;

        const uint CS_HREDRAW   = 0x0002;
        const uint CS_VREDRAW   = 0x0001;
        const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
        const uint WS_EX_ACCEPTFILES   = 0x00000010;
        const int  SW_SHOW      = 5;
        const uint IDC_ARROW    = 32512;
        const int  DIB_RGB_COLORS = 0;
        const int  BI_RGB       = 0;

        // Menu item IDs
        const int IDM_FILE_OPEN = 1001;
        const int IDM_FILE_EXIT = 1002;

        // VK codes
        const int VK_LEFT  = 0x25;
        const int VK_UP    = 0x26;
        const int VK_RIGHT = 0x27;
        const int VK_DOWN  = 0x28;
        const int VK_Z     = 0x5A;
        const int VK_X     = 0x58;
        const int VK_A     = 0x41;
        const int VK_S     = 0x53;

        // DrawText flags
        const uint DT_CENTER    = 0x00000001;
        const uint DT_VCENTER   = 0x00000004;
        const uint DT_SINGLELINE= 0x00000020;

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
        unsafe struct PAINTSTRUCT
        {
            public nint  hdc;
            public int   fErase;
            public int   rcLeft, rcTop, rcRight, rcBottom;
            public int   fRestore;
            public int   fIncUpdate;
            public fixed byte rgbReserved[32];
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int left, top, right, bottom; }

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

        // OPENFILENAME for GetOpenFileNameW
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OPENFILENAME
        {
            public uint    lStructSize;
            public nint    hwndOwner;
            public nint    hInstance;
            public nint    lpstrFilter;
            public nint    lpstrCustomFilter;
            public uint    nMaxCustFilter;
            public uint    nFilterIndex;
            public nint    lpstrFile;
            public uint    nMaxFile;
            public nint    lpstrFileTitle;
            public uint    nMaxFileTitle;
            public nint    lpstrInitialDir;
            public nint    lpstrTitle;
            public uint    Flags;
            public ushort  nFileOffset;
            public ushort  nFileExtension;
            public nint    lpstrDefExt;
            public nint    lCustData;
            public nint    lpfnHook;
            public nint    lpTemplateName;
            public nint    pvReserved;
            public uint    dwReserved;
            public uint    FlagsEx;
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
        [DllImport("user32.dll")] static extern int  GetClientRect(nint hWnd, ref RECT lpRect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int DrawTextW(nint hdc, string lpchText, int nCount, ref RECT lpRect, uint uFormat);
        [DllImport("user32.dll")] static extern nint SetBkMode(nint hdc, int mode);
        [DllImport("user32.dll")] static extern uint SetTextColor(nint hdc, uint crColor);

        // Menu API
        [DllImport("user32.dll")] static extern nint CreateMenu();
        [DllImport("user32.dll")] static extern nint CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool AppendMenuW(nint hMenu, uint uFlags, nint uIDNewItem, string lpNewItem);
        [DllImport("user32.dll")] static extern bool SetMenu(nint hWnd, nint hMenu);

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

        // Open file dialog
        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
        static extern bool GetOpenFileNameW(ref OPENFILENAME lpofn);

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

            // ── Build menu bar ─────────────────────────────────────────────────
            nint hMenuBar  = CreateMenu();
            nint hFileMenu = CreatePopupMenu();
            AppendMenuW(hFileMenu, 0x0000, (nint)IDM_FILE_OPEN, "開啟 ROM (&O)\tCtrl+O");
            AppendMenuW(hFileMenu, 0x0800, nint.Zero, null);   // separator
            AppendMenuW(hFileMenu, 0x0000, (nint)IDM_FILE_EXIT, "結束 (&X)");
            AppendMenuW(hMenuBar,  0x0010, hFileMenu, "檔案 (&F)");
            SetMenu(_hWnd, hMenuBar);

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
                    if (_running && NesCore.ScreenBuf1x != null)
                    {
                        const int scale = 2;
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
                    else
                    {
                        // No ROM loaded — show hint text
                        RECT rc = default;
                        GetClientRect(hWnd, ref rc);
                        SetBkMode(hdc, 1); // TRANSPARENT
                        SetTextColor(hdc, 0x00888888);
                        DrawTextW(hdc,
                            "拖曳 .nes 檔案至此，或使用 檔案 > 開啟 ROM",
                            -1, ref rc,
                            DT_CENTER | DT_VCENTER | DT_SINGLELINE);
                    }
                    EndPaint(hWnd, ref ps);
                    return nint.Zero;
                }

                case WM_COMMAND:
                {
                    int id = (int)(wParam & 0xFFFF);
                    if (id == IDM_FILE_OPEN) OpenRomDialog();
                    else if (id == IDM_FILE_EXIT) DestroyWindow(hWnd);
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
                    else DragFinish(hDrop);
                    return nint.Zero;
                }

                case WM_DESTROY:
                    StopEmulation();
                    WaveOutPlayer.CloseAudio();
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

        // ── Open ROM via dialog ────────────────────────────────────────────────
        static void OpenRomDialog()
        {
            const uint OFN_FILEMUSTEXIST  = 0x00001000;
            const uint OFN_PATHMUSTEXIST  = 0x00000800;
            const uint OFN_HIDEREADONLY   = 0x00000004;

            char[] fileBuffer = new char[260];
            fileBuffer[0] = '\0';

            // Filter string: "NES ROM (*.nes)\0*.nes\0All Files\0*.*\0\0"
            string filter = "NES ROM (*.nes)\0*.nes\0All Files\0*.*\0\0";
            fixed (char* pFilter = filter)
            fixed (char* pFile   = fileBuffer)
            {
                OPENFILENAME ofn = new OPENFILENAME
                {
                    lStructSize = (uint)sizeof(OPENFILENAME),
                    hwndOwner   = _hWnd,
                    lpstrFilter = (nint)pFilter,
                    lpstrFile   = (nint)pFile,
                    nMaxFile    = (uint)fileBuffer.Length,
                    Flags       = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_HIDEREADONLY,
                };
                if (GetOpenFileNameW(ref ofn))
                {
                    string path = new string(pFile).TrimEnd('\0');
                    if (!string.IsNullOrEmpty(path))
                        LoadRom(path);
                }
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
                InvalidateRect(_hWnd, nint.Zero, 1);
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
