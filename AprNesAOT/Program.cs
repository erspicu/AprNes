// AprNesAOT – Win32 Native AOT front-end
// Mirrors the feature set of AprNesUI (WinForms) without using WinForms or System.Drawing.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using LangTool;

namespace AprNes
{
    unsafe class Program
    {
        // ── Win32 message constants ────────────────────────────────────────────
        const uint WM_DESTROY   = 0x0002;
        const uint WM_PAINT     = 0x000F;
        const uint WM_KEYDOWN   = 0x0100;
        const uint WM_KEYUP     = 0x0101;
        const uint WM_COMMAND   = 0x0111;
        const uint WM_TIMER     = 0x0113;
        const uint WM_DROPFILES = 0x0233;

        // Window style
        const uint CS_HREDRAW          = 0x0002;
        const uint CS_VREDRAW          = 0x0001;
        const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
        const uint WS_EX_ACCEPTFILES   = 0x00000010;
        const int  SW_SHOW             = 5;
        const uint IDC_ARROW           = 32512;
        const int  BI_RGB              = 0;
        const int  DIB_RGB_COLORS      = 0;

        // Menu flags
        const uint MF_STRING    = 0x0000;
        const uint MF_POPUP     = 0x0010;
        const uint MF_SEPARATOR = 0x0800;
        const uint MF_CHECKED   = 0x0008;
        const uint MF_UNCHECKED = 0x0000;
        const uint MF_BYCOMMAND = 0x0000;
        const uint MF_GRAYED    = 0x0001;

        // DrawText flags
        const uint DT_CENTER     = 0x00000001;
        const uint DT_VCENTER    = 0x00000004;
        const uint DT_SINGLELINE = 0x00000020;

        // Timer ID
        const nint TIMER_FPS = 1;

        // ── Menu item IDs ──────────────────────────────────────────────────────
        // File
        const int IDM_FILE_OPEN      = 1001;
        const int IDM_FILE_EXIT      = 1002;
        const int IDM_FILE_SOFTRESET = 1003;
        const int IDM_FILE_HARDRESET = 1004;
        // Options
        const int IDM_OPT_SOUND    = 2001;
        const int IDM_OPT_LIMITFPS = 2002;
        const int IDM_VOL_10       = 2010;
        const int IDM_VOL_30       = 2011;
        const int IDM_VOL_50       = 2012;
        const int IDM_VOL_70       = 2013;
        const int IDM_VOL_90       = 2014;
        const int IDM_VOL_100      = 2015;
        // Language  (3000 + index)
        const int IDM_LANG_BASE    = 3000;
        // Help
        const int IDM_HELP_ROMINFO = 4001;
        const int IDM_HELP_ABOUT   = 4002;

        // ── Win32 structs ──────────────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        struct WNDCLASSEX
        {
            public uint  cbSize, style;
            public nint  lpfnWndProc;
            public int   cbClsExtra, cbWndExtra;
            public nint  hInstance, hIcon, hCursor, hbrBackground, lpszMenuName, lpszClassName, hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public int ptX, ptY; }

        [StructLayout(LayoutKind.Sequential)]
        unsafe struct PAINTSTRUCT
        {
            public nint hdc; public int fErase;
            public int rcLeft, rcTop, rcRight, rcBottom;
            public int fRestore, fIncUpdate;
            public fixed byte rgbReserved[32];
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth, biHeight;
            public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage;
            public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OPENFILENAME
        {
            public uint lStructSize; public nint hwndOwner, hInstance;
            public nint lpstrFilter, lpstrCustomFilter;
            public uint nMaxCustFilter, nFilterIndex;
            public nint lpstrFile; public uint nMaxFile;
            public nint lpstrFileTitle; public uint nMaxFileTitle;
            public nint lpstrInitialDir, lpstrTitle;
            public uint Flags; public ushort nFileOffset, nFileExtension;
            public nint lpstrDefExt, lCustData, lpfnHook, lpTemplateName, pvReserved;
            public uint dwReserved, FlagsEx;
        }

        // ── P/Invoke – window / message ────────────────────────────────────────
        [DllImport("user32.dll", SetLastError=true)] static extern ushort RegisterClassExW(ref WNDCLASSEX wc);
        [DllImport("user32.dll", SetLastError=true)] static extern nint CreateWindowExW(uint exStyle, nint cls, nint title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
        [DllImport("user32.dll")] static extern int  ShowWindow(nint hWnd, int n);
        [DllImport("user32.dll")] static extern int  UpdateWindow(nint hWnd);
        [DllImport("user32.dll")] static extern int  GetMessageW(ref MSG m, nint hWnd, uint min, uint max);
        [DllImport("user32.dll")] static extern int  TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] static extern nint DispatchMessageW(ref MSG m);
        [DllImport("user32.dll")] static extern nint DefWindowProcW(nint hWnd, uint msg, nint w, nint l);
        [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
        [DllImport("user32.dll")] static extern int  DestroyWindow(nint hWnd);
        [DllImport("user32.dll")] static extern int  InvalidateRect(nint hWnd, nint lpRect, int erase);
        [DllImport("user32.dll")] static extern nint BeginPaint(nint hWnd, ref PAINTSTRUCT ps);
        [DllImport("user32.dll")] static extern int  EndPaint(nint hWnd, ref PAINTSTRUCT ps);
        [DllImport("user32.dll")] static extern nint LoadCursorW(nint inst, nint name);
        [DllImport("user32.dll")] static extern int  GetClientRect(nint hWnd, ref RECT rc);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int  MessageBoxW(nint hWnd, string text, string cap, uint type);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int  DrawTextW(nint hdc, string s, int n, ref RECT rc, uint fmt);
        [DllImport("user32.dll")] static extern nint SetBkMode(nint hdc, int mode);
        [DllImport("user32.dll")] static extern uint SetTextColor(nint hdc, uint color);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool SetWindowTextW(nint hWnd, string text);
        [DllImport("user32.dll")] static extern nint SetTimer(nint hWnd, nint id, uint ms, nint fn);
        [DllImport("user32.dll")] static extern bool KillTimer(nint hWnd, nint id);

        // ── P/Invoke – menu ────────────────────────────────────────────────────
        [DllImport("user32.dll")] static extern nint CreateMenu();
        [DllImport("user32.dll")] static extern nint CreatePopupMenu();
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool AppendMenuW(nint hMenu, uint flags, nint id, string text);
        [DllImport("user32.dll")] static extern bool SetMenu(nint hWnd, nint hMenu);
        [DllImport("user32.dll")] static extern uint CheckMenuItem(nint hMenu, uint id, uint check);
        [DllImport("user32.dll")] static extern bool CheckMenuRadioItem(nint hMenu, uint first, uint last, uint check, uint flags);
        [DllImport("user32.dll")] static extern bool EnableMenuItem(nint hMenu, uint id, uint enable);

        // ── P/Invoke – GDI / shell ─────────────────────────────────────────────
        [DllImport("kernel32.dll")] static extern nint GetModuleHandleW(nint name);
        [DllImport("gdi32.dll")]    static extern int  SetDIBitsToDevice(nint hdc, int xDest, int yDest, uint dw, uint dh, int xs, int ys, uint start, uint lines, void* bits, ref BITMAPINFOHEADER bmi, uint use);
        [DllImport("shell32.dll")] static extern void DragAcceptFiles(nint hWnd, int accept);
        [DllImport("shell32.dll")] static extern uint DragQueryFileW(nint hDrop, uint i, nint buf, uint cch);
        [DllImport("shell32.dll")] static extern void DragFinish(nint hDrop);
        [DllImport("comdlg32.dll", CharSet=CharSet.Unicode)] static extern bool GetOpenFileNameW(ref OPENFILENAME ofn);

        // ── Delegate (prevent GC collection) ──────────────────────────────────
        delegate nint WndProcDelegate(nint hWnd, uint msg, nint w, nint l);
        static WndProcDelegate _wndProcDelegate;

        // ── State ──────────────────────────────────────────────────────────────
        static nint _hWnd;
        static nint _hMenuBar;
        static nint _hOptMenu;   // options popup (for sound/fps toggles)
        static nint _hVolMenu;   // volume submenu
        static nint _hLangMenu;  // language submenu
        static nint _hFileMenu;  // file submenu (for enabling reset items)
        static BITMAPINFOHEADER _bih;

        static Thread        _emuThread;
        static volatile bool _running;
        static byte[]        _lastRomBytes;  // saved for hard reset

        static Dictionary<string, string> _cfg     = new();
        static string                     _cfgFile;
        static int[]                      _keyMap  = new int[8]; // A B SEL START UP DOWN LEFT RIGHT
        static string[]                   _langKeys = Array.Empty<string>();

        // FPS
        static volatile int _frameCount;
        static int          _fps;
        static Stopwatch    _fpsLimitSw = new();

        // ── Entry point ────────────────────────────────────────────────────────
        static void Main(string[] args)
        {
            _cfgFile = Path.Combine(AppContext.BaseDirectory, "AprNes.ini");
            LangINI.init();
            LoadConfig();

            _bih = new BITMAPINFOHEADER
            {
                biSize        = (uint)sizeof(BITMAPINFOHEADER),
                biWidth       = 256,
                biHeight      = -240,   // top-down
                biPlanes      = 1,
                biBitCount    = 32,
                biCompression = (uint)BI_RGB,
            };

            NesCore.VideoOutput += (s, e) =>
            {
                _frameCount++;
                if (NesCore.LimitFPS)
                {
                    long need = 16 - _fpsLimitSw.ElapsedMilliseconds;
                    if (need > 0) Thread.Sleep((int)need);
                    _fpsLimitSw.Restart();
                }
                InvalidateRect(_hWnd, nint.Zero, 0);
            };
            NesCore.OnError = msg => MessageBoxW(_hWnd, msg, "AprNes AOT – Error", 0x10);

            nint hInstance = GetModuleHandleW(nint.Zero);
            _wndProcDelegate = WndProc;
            nint wndProcPtr  = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);

            fixed (char* clsName = "AprNesAOT\0")
            fixed (char* title   = "AprNes AOT\0")
            {
                var wc = new WNDCLASSEX
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

                _hWnd = CreateWindowExW(
                    WS_EX_ACCEPTFILES, (nint)clsName, (nint)title,
                    WS_OVERLAPPEDWINDOW,
                    100, 100,
                    256 * 2 + 16, 240 * 2 + 39,
                    nint.Zero, nint.Zero, hInstance, nint.Zero);
            }

            if (_hWnd == nint.Zero)
            {
                MessageBoxW(nint.Zero, "CreateWindowEx failed", "Fatal", 0x10);
                return;
            }

            BuildMenu();
            DragAcceptFiles(_hWnd, 1);
            WaveOutPlayer.OpenAudio();
            SetTimer(_hWnd, TIMER_FPS, 1000, nint.Zero);

            if (LangINI.LangFileMissing)
                MessageBoxW(_hWnd,
                    "AprNesLang.ini not found.\nUI will use default text.",
                    "Language file missing", 0x30);

            ShowWindow(_hWnd, SW_SHOW);
            UpdateWindow(_hWnd);

            MSG msg = default;
            while (GetMessageW(ref msg, nint.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }

            WaveOutPlayer.CloseAudio();
        }

        // ── Build menu bar ─────────────────────────────────────────────────────
        static void BuildMenu()
        {
            _hMenuBar  = CreateMenu();
            _hFileMenu = CreatePopupMenu();
            _hOptMenu  = CreatePopupMenu();

            // ── File ──────────────────────────────────────────────────────────
            AppendMenuW(_hFileMenu, MF_STRING,    (nint)IDM_FILE_OPEN,      "開啟 ROM (&O)");
            AppendMenuW(_hFileMenu, MF_SEPARATOR, nint.Zero,                null);
            AppendMenuW(_hFileMenu, MF_STRING | MF_GRAYED, (nint)IDM_FILE_SOFTRESET, "軟重置 (&S)");
            AppendMenuW(_hFileMenu, MF_STRING | MF_GRAYED, (nint)IDM_FILE_HARDRESET, "硬重置 (&H)");
            AppendMenuW(_hFileMenu, MF_SEPARATOR, nint.Zero,                null);
            AppendMenuW(_hFileMenu, MF_STRING,    (nint)IDM_FILE_EXIT,      "結束 (&X)");
            AppendMenuW(_hMenuBar,  MF_POPUP, _hFileMenu, "檔案 (&F)");

            // ── Options ───────────────────────────────────────────────────────
            uint soundFlag = MF_STRING | (NesCore.AudioEnabled ? MF_CHECKED : MF_UNCHECKED);
            AppendMenuW(_hOptMenu, soundFlag, (nint)IDM_OPT_SOUND, "音效 (&S)");

            _hVolMenu = CreatePopupMenu();
            AppendMenuW(_hVolMenu, MF_STRING, (nint)IDM_VOL_10,  "10%");
            AppendMenuW(_hVolMenu, MF_STRING, (nint)IDM_VOL_30,  "30%");
            AppendMenuW(_hVolMenu, MF_STRING, (nint)IDM_VOL_50,  "50%");
            AppendMenuW(_hVolMenu, MF_STRING, (nint)IDM_VOL_70,  "70%");
            AppendMenuW(_hVolMenu, MF_STRING, (nint)IDM_VOL_90,  "90%");
            AppendMenuW(_hVolMenu, MF_STRING, (nint)IDM_VOL_100, "100%");
            AppendMenuW(_hOptMenu, MF_POPUP, _hVolMenu, "音量 (&V)");
            UpdateVolumeCheck();

            AppendMenuW(_hOptMenu, MF_SEPARATOR, nint.Zero, null);
            uint fpsFlag = MF_STRING | (NesCore.LimitFPS ? MF_CHECKED : MF_UNCHECKED);
            AppendMenuW(_hOptMenu, fpsFlag, (nint)IDM_OPT_LIMITFPS, "限制 FPS (&F)");
            AppendMenuW(_hMenuBar, MF_POPUP, _hOptMenu, "選項 (&O)");

            // ── Language ──────────────────────────────────────────────────────
            if (LangINI.LangLoadOK && LangINI.lang_map.Count > 0)
            {
                _hLangMenu = CreatePopupMenu();
                _langKeys  = new string[LangINI.lang_map.Count];
                int idx = 0;
                string curLang = _cfg.GetValueOrDefault("Lang", "en-us");
                foreach (var kv in LangINI.lang_map)
                {
                    _langKeys[idx] = kv.Key;
                    uint lf = MF_STRING | (kv.Key == curLang ? MF_CHECKED : MF_UNCHECKED);
                    AppendMenuW(_hLangMenu, lf, (nint)(IDM_LANG_BASE + idx), kv.Key + "  " + kv.Value);
                    idx++;
                }
                AppendMenuW(_hMenuBar, MF_POPUP, _hLangMenu, "語言 (&L)");
            }

            // ── Help ──────────────────────────────────────────────────────────
            nint hHelp = CreatePopupMenu();
            AppendMenuW(hHelp, MF_STRING | MF_GRAYED, (nint)IDM_HELP_ROMINFO, "ROM 資訊 (&I)");
            AppendMenuW(hHelp, MF_SEPARATOR, nint.Zero, null);
            AppendMenuW(hHelp, MF_STRING, (nint)IDM_HELP_ABOUT, "關於 (&A)");
            AppendMenuW(_hMenuBar, MF_POPUP, hHelp, "說明 (&H)");

            SetMenu(_hWnd, _hMenuBar);
        }

        // Enable / disable ROM-dependent menu items
        static void UpdateRomMenuItems(bool hasRom)
        {
            uint flag = hasRom ? 0x0000u : MF_GRAYED; // MF_ENABLED=0, MF_GRAYED=1, MF_BYCOMMAND=0
            EnableMenuItem(_hMenuBar, IDM_FILE_SOFTRESET, flag | MF_BYCOMMAND);
            EnableMenuItem(_hMenuBar, IDM_FILE_HARDRESET, flag | MF_BYCOMMAND);
            EnableMenuItem(_hMenuBar, IDM_HELP_ROMINFO,   flag | MF_BYCOMMAND);
            // Redraw the menu bar
            SetMenu(_hWnd, _hMenuBar);
        }

        static void UpdateVolumeCheck()
        {
            int vol = NesCore.Volume;
            int checkId = vol <= 15  ? IDM_VOL_10 :
                          vol <= 40  ? IDM_VOL_30 :
                          vol <= 60  ? IDM_VOL_50 :
                          vol <= 80  ? IDM_VOL_70 :
                          vol <= 95  ? IDM_VOL_90 : IDM_VOL_100;
            CheckMenuRadioItem(_hVolMenu,
                (uint)IDM_VOL_10, (uint)IDM_VOL_100,
                (uint)checkId, MF_BYCOMMAND);
        }

        // ── Window procedure ───────────────────────────────────────────────────
        static nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
        {
            switch (msg)
            {
                // ── Paint ──────────────────────────────────────────────────────
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
                                dst[(y*2+0)*dstW + x*2+0] = c;
                                dst[(y*2+0)*dstW + x*2+1] = c;
                                dst[(y*2+1)*dstW + x*2+0] = c;
                                dst[(y*2+1)*dstW + x*2+1] = c;
                            }
                        BITMAPINFOHEADER bih2x = _bih;
                        bih2x.biWidth  = dstW;
                        bih2x.biHeight = -dstH;
                        SetDIBitsToDevice(hdc, 0, 0, (uint)dstW, (uint)dstH, 0, 0, 0, (uint)dstH, dst, ref bih2x, (uint)DIB_RGB_COLORS);
                        Marshal.FreeHGlobal((nint)dst);
                    }
                    else
                    {
                        RECT rc = default;
                        GetClientRect(hWnd, ref rc);
                        SetBkMode(hdc, 1); // TRANSPARENT
                        SetTextColor(hdc, 0x00888888);
                        DrawTextW(hdc, "拖曳 .nes ROM 至此，或使用 檔案 > 開啟 ROM", -1, ref rc, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
                    }

                    EndPaint(hWnd, ref ps);
                    return nint.Zero;
                }

                // ── Menu commands ──────────────────────────────────────────────
                case WM_COMMAND:
                {
                    int id = (int)(wParam & 0xFFFF);
                    HandleCommand(hWnd, id);
                    return nint.Zero;
                }

                // ── Keyboard ───────────────────────────────────────────────────
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

                // ── Drag & drop ────────────────────────────────────────────────
                case WM_DROPFILES:
                {
                    nint hDrop = wParam;
                    uint len = DragQueryFileW(hDrop, 0, nint.Zero, 0);
                    if (len > 0)
                    {
                        char[] buf = new char[(int)len + 1];
                        fixed (char* p = buf)
                            DragQueryFileW(hDrop, 0, (nint)p, len + 1);
                        DragFinish(hDrop);
                        LoadRom(new string(buf, 0, (int)len));
                    }
                    else DragFinish(hDrop);
                    return nint.Zero;
                }

                // ── FPS timer ──────────────────────────────────────────────────
                case WM_TIMER:
                    if (wParam == TIMER_FPS)
                    {
                        _fps = _frameCount;
                        _frameCount = 0;
                        string titleStr = _running
                            ? $"AprNes AOT  [{Path.GetFileName(NesCore.rom_file_name)}]  FPS: {_fps}"
                            : "AprNes AOT";
                        SetWindowTextW(hWnd, titleStr);
                    }
                    return nint.Zero;

                // ── Destroy ────────────────────────────────────────────────────
                case WM_DESTROY:
                    KillTimer(hWnd, TIMER_FPS);
                    StopEmulation();
                    WaveOutPlayer.CloseAudio();
                    PostQuitMessage(0);
                    return nint.Zero;
            }
            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        // ── Command dispatcher ─────────────────────────────────────────────────
        static void HandleCommand(nint hWnd, int id)
        {
            // File
            if (id == IDM_FILE_OPEN)      { OpenRomDialog(); return; }
            if (id == IDM_FILE_EXIT)      { DestroyWindow(hWnd); return; }
            if (id == IDM_FILE_SOFTRESET) { DoSoftReset(); return; }
            if (id == IDM_FILE_HARDRESET) { DoHardReset(); return; }

            // Options – Sound toggle
            if (id == IDM_OPT_SOUND)
            {
                NesCore.AudioEnabled = !NesCore.AudioEnabled;
                CheckMenuItem(_hOptMenu, (uint)IDM_OPT_SOUND,
                    (NesCore.AudioEnabled ? MF_CHECKED : MF_UNCHECKED) | MF_BYCOMMAND);
                _cfg["Sound"] = NesCore.AudioEnabled ? "1" : "0";
                SaveConfig();
                return;
            }

            // Options – FPS limit toggle
            if (id == IDM_OPT_LIMITFPS)
            {
                NesCore.LimitFPS = !NesCore.LimitFPS;
                CheckMenuItem(_hOptMenu, (uint)IDM_OPT_LIMITFPS,
                    (NesCore.LimitFPS ? MF_CHECKED : MF_UNCHECKED) | MF_BYCOMMAND);
                _cfg["LimitFPS"] = NesCore.LimitFPS ? "1" : "0";
                SaveConfig();
                return;
            }

            // Volume
            if (id >= IDM_VOL_10 && id <= IDM_VOL_100)
            {
                int vol = id switch {
                    IDM_VOL_10  => 10,
                    IDM_VOL_30  => 30,
                    IDM_VOL_50  => 50,
                    IDM_VOL_70  => 70,
                    IDM_VOL_90  => 90,
                    _           => 100,
                };
                NesCore.Volume = vol;
                UpdateVolumeCheck();
                _cfg["Volume"] = vol.ToString();
                SaveConfig();
                return;
            }

            // Language
            if (id >= IDM_LANG_BASE && id < IDM_LANG_BASE + _langKeys.Length)
            {
                int idx = id - IDM_LANG_BASE;
                string newLang = _langKeys[idx];
                _cfg["Lang"] = newLang;
                SaveConfig();
                // Update checkmark
                CheckMenuRadioItem(_hLangMenu,
                    (uint)IDM_LANG_BASE, (uint)(IDM_LANG_BASE + _langKeys.Length - 1),
                    (uint)id, MF_BYCOMMAND);
                MessageBoxW(_hWnd, $"語言已切換為 {newLang}\n重新啟動後完全生效。", "語言設定", 0x40);
                return;
            }

            // Help
            if (id == IDM_HELP_ROMINFO) { ShowRomInfo(); return; }
            if (id == IDM_HELP_ABOUT)   { ShowAbout();   return; }
        }

        // ── Key mapping (loaded from config) ──────────────────────────────────
        static byte VkToButton(int vk)
        {
            for (int i = 0; i < 8; i++)
                if (_keyMap[i] == vk) return (byte)i;
            return 0xFF;
        }

        // ── Open file dialog ───────────────────────────────────────────────────
        static void OpenRomDialog()
        {
            const uint OFN_FILEMUSTEXIST = 0x00001000;
            const uint OFN_PATHMUSTEXIST = 0x00000800;
            const uint OFN_HIDEREADONLY  = 0x00000004;

            char[] fileBuf = new char[260];
            string filter  = "NES ROM (*.nes)\0*.nes\0All Files\0*.*\0\0";
            fixed (char* pFilter = filter)
            fixed (char* pFile   = fileBuf)
            {
                var ofn = new OPENFILENAME
                {
                    lStructSize = (uint)sizeof(OPENFILENAME),
                    hwndOwner   = _hWnd,
                    lpstrFilter = (nint)pFilter,
                    lpstrFile   = (nint)pFile,
                    nMaxFile    = (uint)fileBuf.Length,
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
                _lastRomBytes        = rom;
                NesCore.rom_file_name = path;
                if (!NesCore.init(rom))
                {
                    MessageBoxW(_hWnd, "ROM 初始化失敗（不支援的 Mapper？）", "Load Error", 0x10);
                    return;
                }
                _running       = true;
                NesCore.exit   = false;
                NesCore._event.Set();
                _emuThread = new Thread(() => { try { NesCore.run(); } catch { } })
                {
                    IsBackground = true,
                    Name         = "NesCore"
                };
                _emuThread.Start();
                _fpsLimitSw.Restart();

                UpdateRomMenuItems(true);
                InvalidateRect(_hWnd, nint.Zero, 1);
            }
            catch (Exception ex)
            {
                MessageBoxW(_hWnd, ex.Message, "Load Error", 0x10);
            }
        }

        // ── Soft reset ────────────────────────────────────────────────────────
        static void DoSoftReset()
        {
            if (!_running) return;
            NesCore._event.Reset();
            while (NesCore.screen_lock) Thread.Sleep(1);
            NesCore.SoftReset();
            NesCore._event.Set();
        }

        // ── Hard reset ────────────────────────────────────────────────────────
        static void DoHardReset()
        {
            if (!_running || _lastRomBytes == null) return;
            string savedName = NesCore.rom_file_name;
            StopEmulation();
            NesCore.exit          = false;
            NesCore.rom_file_name = savedName;
            if (!NesCore.init(_lastRomBytes)) return;
            _running = true;
            NesCore._event.Set();
            _emuThread = new Thread(() => { try { NesCore.run(); } catch { } })
            {
                IsBackground = true,
                Name         = "NesCore"
            };
            _emuThread.Start();
        }

        // ── Stop emulation thread ─────────────────────────────────────────────
        static void StopEmulation()
        {
            if (_running)
            {
                _running       = false;
                NesCore.exit   = true;
                NesCore._event.Set(); // unblock if waiting
                _emuThread?.Join(2000);
                _emuThread = null;
            }
        }

        // ── ROM info dialog ────────────────────────────────────────────────────
        static void ShowRomInfo()
        {
            if (!_running) return;
            string mirror  = NesCore.RomHorizMirror ? "水平" : "垂直";
            string battery = NesCore.HasBattery ? "有" : "無";
            string info =
                $"檔案 : {Path.GetFileName(NesCore.rom_file_name)}\r\n" +
                $"Mapper : {NesCore.RomMapper}\r\n" +
                $"PRG-ROM : {NesCore.RomPrgCount} × 16 KB\r\n" +
                $"CHR-ROM : {NesCore.RomChrCount} × 8 KB\r\n" +
                $"鏡像 : {mirror}\r\n" +
                $"電池 : {battery}";
            MessageBoxW(_hWnd, info, "ROM 資訊", 0x40);
        }

        // ── About dialog ──────────────────────────────────────────────────────
        static void ShowAbout()
        {
            MessageBoxW(_hWnd,
                "AprNes AOT\r\n\r\n" +
                "NES Emulator – Native AOT build (.NET 8)\r\n" +
                "Shares NesCore with the original AprNes (WinForms) project.\r\n\r\n" +
                "Controls (default):\r\n" +
                "  A=Z  B=X  SELECT=S  START=A\r\n" +
                "  ↑↓←→ = arrow keys",
                "關於 AprNes AOT", 0x40);
        }

        // ── Config load ────────────────────────────────────────────────────────
        static void LoadConfig()
        {
            // Defaults
            _cfg["key_A"]       = "90";   // Z
            _cfg["key_B"]       = "88";   // X
            _cfg["key_SELECT"]  = "83";   // S
            _cfg["key_START"]   = "65";   // A
            _cfg["key_UP"]      = "38";
            _cfg["key_DOWN"]    = "40";
            _cfg["key_LEFT"]    = "37";
            _cfg["key_RIGHT"]   = "39";
            _cfg["Sound"]       = "1";
            _cfg["Volume"]      = "70";
            _cfg["LimitFPS"]    = "0";
            _cfg["Lang"]        = "en-us";
            _cfg["ScreenSize"]  = "2";

            if (File.Exists(_cfgFile))
            {
                foreach (string line in File.ReadAllLines(_cfgFile))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) _cfg[line[..eq]] = line[(eq+1)..];
                }
            }
            else
            {
                SaveConfig(); // create with defaults
            }

            // Apply key map
            string[] keyNames = { "key_A","key_B","key_SELECT","key_START","key_UP","key_DOWN","key_LEFT","key_RIGHT" };
            for (int i = 0; i < 8; i++)
                _keyMap[i] = int.TryParse(_cfg.GetValueOrDefault(keyNames[i], "0"), out int k) ? k : 0;

            // Apply audio
            NesCore.AudioEnabled = _cfg.GetValueOrDefault("Sound",    "1") == "1";
            NesCore.Volume       = int.TryParse(_cfg.GetValueOrDefault("Volume", "70"), out int v) ? Math.Clamp(v, 0, 100) : 70;
            NesCore.LimitFPS     = _cfg.GetValueOrDefault("LimitFPS", "0") == "1";
        }

        // ── Config save ────────────────────────────────────────────────────────
        static void SaveConfig()
        {
            try
            {
                var lines = new System.Text.StringBuilder();
                foreach (var kv in _cfg)
                    lines.AppendLine($"{kv.Key}={kv.Value}");
                File.WriteAllText(_cfgFile, lines.ToString());
            }
            catch { }
        }
    }
}
