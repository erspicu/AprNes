using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Avalonia.Input;
using AprNes;
using AprNesAvalonia.Platform;

namespace AprNesAvalonia;

/// <summary>
/// Wraps NesCore: manages emulator thread, exposes frame buffer copy and input.
/// Video: NesCore.ScreenBuf1x (uint* ARGB = 0xFFRRGGBB, little-endian → Bgra8888 bytes)
/// </summary>
public sealed unsafe class EmulatorEngine : IDisposable
{
    // ── State ─────────────────────────────────────────────────────────────
    private Thread?  _thread;
    private volatile bool _running;
    private volatile bool _romLoaded;
    private byte[]? _romBytes;
    private byte[]? _biosBytes;     // FDS BIOS (null for cartridge ROMs)
    private string  _romFileName = "";
    private string  _romFilePath = ""; // full path for SRAM derivation

    // ── Platform backends ────────────────────────────────────────────────
    private readonly IAudioBackend   _audio   = PlatformFactory.CreateAudioBackend();
    private readonly IGamepadBackend _gamepad = PlatformFactory.CreateGamepadBackend();

    // ── Video (Lock-Free Double Buffering) ───────────────────────────────
    private IntPtr _bufferA;
    private IntPtr _bufferB;
    private IntPtr _backBuffer;
    private IntPtr _frontBuffer;
    private int _bufferSize;
    private readonly object _resizeLock = new();
    private int _pendingFrame;          // interlocked flag: 1 = new frame available
    private int _frameCounter;          // raw frame count since last FPS query
    // ── Render pipeline (two-stage filter engine) ────────────────────────
    private readonly RenderPipeline _pipeline = new();
    private bool _pipelineActive;       // true if filters are configured
    private bool _analogMode;           // true = read from AnalogScreenBuf
    private int _outputW = 256, _outputH = 240;

    /// <summary>Front buffer pointer — safe to read from Render Thread while Emu Thread writes to back buffer.</summary>
    public IntPtr CurrentFrontBuffer => _frontBuffer;

    /// <summary>Fired on the UI thread when a new frame is ready.</summary>
    public event Action? FrameReady;

    /// <summary>Fired on the UI thread when output resolution changes.</summary>
    public event Action<int, int>? ResolutionChanged;

    /// <summary>Current output width in pixels.</summary>
    public int OutputW => _outputW;
    /// <summary>Current output height in pixels.</summary>
    public int OutputH => _outputH;

    // ── FPS limiting (mirrors original VideoOutputDeal) ─────────────────
    private readonly Stopwatch _fpsStopWatch = new();
    private double _fpsDeadline = 0;
    private const double NES_FRAME_SECONDS = 1.0 / 60.0988;

    // ── Key map: Avalonia Key → NES button index (0-7) ────────────────────
    private readonly Dictionary<Key, byte> _keymap = new()
    {
        { Key.Z,     0 }, // A
        { Key.X,     1 }, // B
        { Key.S,     2 }, // SELECT
        { Key.A,     3 }, // START
        { Key.Up,    4 },
        { Key.Down,  5 },
        { Key.Left,  6 },
        { Key.Right, 7 },
    };

    // Windows VK code → Avalonia Key (for loading from INI)
    private static readonly Dictionary<int, Key> _vkMap = new()
    {
        { 37, Key.Left },  { 38, Key.Up },  { 39, Key.Right }, { 40, Key.Down },
        { 13, Key.Return }, { 32, Key.Space },
        { 65, Key.A },  { 66, Key.B },  { 67, Key.C },  { 68, Key.D },  { 69, Key.E },
        { 70, Key.F },  { 71, Key.G },  { 72, Key.H },  { 73, Key.I },  { 74, Key.J },
        { 75, Key.K },  { 76, Key.L },  { 77, Key.M },  { 78, Key.N },  { 79, Key.O },
        { 80, Key.P },  { 81, Key.Q },  { 82, Key.R },  { 83, Key.S },  { 84, Key.T },
        { 85, Key.U },  { 86, Key.V },  { 87, Key.W },  { 88, Key.X },  { 89, Key.Y },
        { 90, Key.Z },
    };

    // ── Gamepad polling thread ────────────────────────────────────────────
    private Thread? _gamepadThread;
    private volatile bool _gamepadRunning;

    // ── Public API ─────────────────────────────────────────────────────────
    public bool IsRunning   => _running;
    public bool IsRomLoaded => _romLoaded;
    public string RomFilePath => _romFilePath;

    /// <summary>Access to the gamepad backend (for config capture).</summary>
    public IGamepadBackend Gamepad => _gamepad;

    /// <summary>Apply keyboard mapping from INI (Windows VK codes).</summary>
    public void ApplyKeyMap(int vkA, int vkB, int vkSelect, int vkStart,
                            int vkUp, int vkDown, int vkLeft, int vkRight)
    {
        _keymap.Clear();
        void Map(int vk, byte btn) { if (_vkMap.TryGetValue(vk, out var k)) _keymap[k] = btn; }
        Map(vkA,      0); Map(vkB,     1); Map(vkSelect, 2); Map(vkStart, 3);
        Map(vkUp,     4); Map(vkDown,  5); Map(vkLeft,   6); Map(vkRight, 7);
    }

    /// <summary>Apply audio settings: set NesCore flags and open/close audio backend.</summary>
    public void ApplyAudioSettings(bool enabled, int volume)
    {
        NesCore.AudioEnabled = enabled;
        NesCore.Volume       = volume;
        _audio.Close();
        if (enabled && _running)
            _audio.Open();
    }

    /// <summary>Initialize gamepad backend with the window handle and load mapping from INI.</summary>
    public void InitGamepad(IntPtr windowHandle, IniFile ini)
    {
        _gamepad.Initialize(windowHandle);
        _gamepad.LoadMapping(ini);

        if (!_gamepadRunning && _gamepad.IsAvailable)
        {
            _gamepadRunning = true;
            _gamepadThread = new Thread(GamepadPollLoop) { IsBackground = true, Name = "GamepadPoll" };
            _gamepadThread.Start();
        }
    }

    /// <summary>Reload gamepad mapping (after config change).</summary>
    public void ReloadGamepadMapping(IniFile ini)
    {
        _gamepad.LoadMapping(ini);
    }

    /// <summary>Apply render settings: configure filter pipeline or analog mode, resize buffer.
    /// Must be called from UI thread. If emulator is running, caller should Pause() first.</summary>
    public void ApplyRenderSettings(ResizeFilter s1Filter, int s1Scale,
                                     ResizeFilter s2Filter, int s2Scale,
                                     bool scanline, bool analogEnabled, int analogSize)
    {
        // Safety: if running, detach video output and wait for emu thread to fully stop
        // (mirrors AprNes WinForms: _event.Reset → while(!emuWaiting) Sleep)
        bool wasAttached = false;
        if (_running)
        {
            NesCore.VideoOutput -= OnVideoOutput;
            wasAttached = true;
            NesCore._event.Reset();
            // Spin until emu thread confirms it's blocked on _event.WaitOne()
            while (!NesCore.emuWaiting)
                Thread.Sleep(1);
        }

        _analogMode = analogEnabled;

        int newW, newH;
        if (analogEnabled)
        {
            newW = 256 * analogSize;
            newH = 210 * analogSize;
            _pipelineActive = false;

            // Analog NesCore buffer management (mirrors AprNes WinForms ApplyRenderSettings)
            NesCore.SyncAnalogConfig();
            int neededPx = NesCore.Crt_DstW * NesCore.Crt_DstH;
            if (NesCore.AnalogScreenBuf == null || NesCore.AnalogBufSize != neededPx)
            {
                if (NesCore.AnalogScreenBuf != null)
                    { Marshal.FreeHGlobal((IntPtr)NesCore.AnalogScreenBuf); NesCore.AnalogScreenBuf = null; }
                if (NesCore.AnalogScreenBufBack != null)
                    { Marshal.FreeHGlobal((IntPtr)NesCore.AnalogScreenBufBack); NesCore.AnalogScreenBufBack = null; }

                NesCore.AnalogBufSize       = neededPx;
                NesCore.AnalogScreenBuf     = (uint*)Marshal.AllocHGlobal(sizeof(uint) * neededPx);
                NesCore.AnalogScreenBufBack = (uint*)Marshal.AllocHGlobal(sizeof(uint) * neededPx);
            }
            NesCore.SyncAnalogConfig();
            NesCore.Ntsc_Init();
            NesCore.Crt_Init();
        }
        else
        {
            _pipeline.Configure(s1Filter, s1Scale, s2Filter, s2Scale, scanline);
            newW = _pipeline.OutputW;
            newH = _pipeline.OutputH;
            _pipelineActive = _pipeline.HasFilters;
        }

        bool resChanged = newW != _outputW || newH != _outputH;
        _outputW = newW;
        _outputH = newH;

        // Allocate/resize double buffers — emu thread is confirmed stopped
        int needed = newW * newH * 4;
        lock (_resizeLock)
        {
            _frontBuffer = IntPtr.Zero;

            if (_bufferSize != needed)
            {
                if (_bufferA != IntPtr.Zero) Marshal.FreeHGlobal(_bufferA);
                if (_bufferB != IntPtr.Zero) Marshal.FreeHGlobal(_bufferB);

                _bufferA = Marshal.AllocHGlobal(needed);
                _bufferB = Marshal.AllocHGlobal(needed);
                _bufferSize = needed;
            }

            new Span<byte>((void*)_bufferA, needed).Clear();
            new Span<byte>((void*)_bufferB, needed).Clear();

            _backBuffer  = _bufferA;
            _frontBuffer = _bufferB;
        }

        // Init pipeline buffers
        if (_pipelineActive && NesCore.ScreenBuf1x != null)
            _pipeline.Init(NesCore.ScreenBuf1x);
        else
            _pipeline.FreeMem();

        if (resChanged)
            ResolutionChanged?.Invoke(newW, newH);

        // Re-attach video output and resume emu thread
        if (wasAttached)
        {
            NesCore.VideoOutput += OnVideoOutput;
            NesCore._event.Set();
        }
    }

    private void GamepadPollLoop()
    {
        while (_gamepadRunning)
        {
            Thread.Sleep(10);
            if (_running)
                _gamepad.Poll();
        }
    }

    /// <summary>Load and initialise a ROM (supports .nes, .fds, .zip). Returns true on success.</summary>
    public bool LoadRom(string path)
    {
        Stop();
        SaveSRam(); // save previous ROM's battery RAM

        byte[] rom;
        try
        {
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                rom = null;
                using var archive = ZipFile.OpenRead(path);
                foreach (var entry in archive.Entries)
                {
                    string ext = Path.GetExtension(entry.FullName).ToLowerInvariant();
                    if (ext == ".nes" || ext == ".fds")
                    {
                        using var ms = new MemoryStream();
                        using (var s = entry.Open()) s.CopyTo(ms);
                        rom = ms.ToArray();
                        _romFileName = entry.Name;
                        break;
                    }
                }
                if (rom == null) return false;
            }
            else
            {
                rom = File.ReadAllBytes(path);
                _romFileName = Path.GetFileName(path);
            }
        }
        catch { return false; }

        _romFilePath = path;
        _romBytes = rom;
        NesCore.rom_file_name = SRamBaseName(path);
        NesCore.exit = false;

        bool initOk;
        if (NesCore.IsFdsFile(rom))
        {
            byte[] bios = NesCore.LoadAndValidateFdsBios(AppContext.BaseDirectory);
            if (bios == null) return false;
            _biosBytes = bios;
            initOk = NesCore.initFDS(bios, rom);
        }
        else
        {
            _biosBytes = null;
            initOk = NesCore.init(rom);
        }

        if (!initOk) return false;
        _romLoaded = true;
        LoadSRam();

        NesCore.VideoOutput -= OnVideoOutput;
        NesCore.VideoOutput += OnVideoOutput;
        return true;
    }

    /// <summary>Hard reset (power cycle): re-init current ROM from scratch.</summary>
    public bool HardReset()
    {
        if (!_romLoaded || _romBytes == null) return false;
        Stop();
        SaveSRam();

        NesCore.exit = false;
        NesCore.rom_file_name = SRamBaseName(_romFilePath);

        bool initOk;
        if (_biosBytes != null && NesCore.IsFdsFile(_romBytes))
            initOk = NesCore.initFDS(_biosBytes, _romBytes);
        else
            initOk = NesCore.init(_romBytes);

        if (!initOk) return false;
        LoadSRam();

        NesCore.VideoOutput -= OnVideoOutput;
        NesCore.VideoOutput += OnVideoOutput;
        return true;
    }

    // ── SRAM (battery-backed RAM) ────────────────────────────────────────
    private static string SRamBaseName(string path) =>
        string.IsNullOrEmpty(path) ? "" : path.Remove(path.Length - Path.GetExtension(path).Length);

    private string SRamPath() => SRamBaseName(_romFilePath) + ".sav";

    public void SaveSRam()
    {
        if (!_romLoaded || !NesCore.HasBattery) return;
        try { File.WriteAllBytes(SRamPath(), NesCore.DumpSRam()); }
        catch { /* ignore write errors */ }
    }

    public void LoadSRam()
    {
        if (!NesCore.HasBattery) return;
        string path = SRamPath();
        if (File.Exists(path))
        {
            try { NesCore.LoadSRam(File.ReadAllBytes(path)); }
            catch { }
        }
    }

    /// <summary>Start (or resume) the emulator loop.</summary>
    public void Start()
    {
        if (!_romLoaded || _running) return;

        // Re-init pipeline with current ScreenBuf1x (may have changed after ROM load/reset)
        if (_pipelineActive)
            _pipeline.Init(NesCore.ScreenBuf1x);

        _running = true;
        _fpsDeadline = 0;
        _fpsStopWatch.Reset();
        NesCore.exit = false;
        NesCore._event.Set();
        _thread = new Thread(NesCore.run) { IsBackground = true, Name = "NesCore" };
        _thread.Start();

        if (NesCore.AudioEnabled)
            _audio.Open();
    }

    /// <summary>Stop the emulator loop and wait for thread to exit.</summary>
    public void Stop()
    {
        _audio.Close();
        if (!_running) return;
        _running = false;
        NesCore.exit = true;
        NesCore._event.Set();   // unblock if paused
        _thread?.Join(2000);
        _thread = null;
    }

    public void Pause()  { NesCore._event.Reset(); }
    public void Resume() { NesCore._event.Set();   }

    public void KeyDown(Key k) { if (_running && _keymap.TryGetValue(k, out var b)) NesCore.P1_ButtonPress(b); }
    public void KeyUp  (Key k) { if (_running && _keymap.TryGetValue(k, out var b)) NesCore.P1_ButtonUnPress(b); }

    /// <summary>Returns frames rendered since last call (for FPS display).</summary>
    public int TakeFrameCount() => Interlocked.Exchange(ref _frameCounter, 0);

    /// <summary>Returns iNES header info string, same format as original AprNes.</summary>
    public string GetRomInfo()
    {
        if (_romBytes == null || _romBytes.Length < 16) return "No ROM loaded.";
        if (!(_romBytes[0] == 'N' && _romBytes[1] == 'E' && _romBytes[2] == 'S' && _romBytes[3] == 0x1a))
            return "Bad Magic Number! (not a valid NES ROM)";

        var sb = new StringBuilder();
        sb.AppendLine("FileName : " + Path.GetFileName(_romFileName));
        sb.AppendLine("iNes Header");
        byte prg = _romBytes[4]; sb.AppendLine("PRG-ROM count : " + prg);
        byte chr = _romBytes[5]; sb.AppendLine("CHR-ROM count : " + chr);
        byte ctrl1 = _romBytes[6], ctrl2 = _romBytes[7];
        sb.AppendLine((ctrl1 & 1) != 0 ? "vertical mirroring" : "horizontal mirroring");
        sb.AppendLine("battery-backed RAM : " + ((ctrl1 & 2) != 0 ? "yes" : "no"));
        sb.AppendLine("trainer : "            + ((ctrl1 & 4) != 0 ? "yes" : "no"));
        sb.AppendLine("fourscreen mirroring : " + ((ctrl1 & 8) != 0 ? "yes" : "no"));

        int mapper;
        bool iNesV2 = false;
        if ((ctrl2 & 0xf) != 0)
        {
            if ((ctrl2 & 0xc) == 8)
            {
                iNesV2 = true;
                mapper = ((ctrl1 & 0xf0) >> 4) | (ctrl2 & 0xf0);
                sb.AppendLine("Nes header 2.0 version!");
            }
            else
            {
                mapper = (ctrl1 & 0xf0) >> 4;
                sb.AppendLine("Old style Mapper info!");
            }
        }
        else
            mapper = ((ctrl1 & 0xf0) >> 4) | (ctrl2 & 0xf0);

        sb.AppendLine("Mapper number : " + mapper);
        if (iNesV2 && _romBytes.Length > 8)
            sb.AppendLine("RAM banks count : " + _romBytes[8]);
        return sb.ToString();
    }

    // ── Private ────────────────────────────────────────────────────────────
    private void OnVideoOutput(object? sender, EventArgs e)
    {
        int copyBytes = _outputW * _outputH * 4;

        lock (_resizeLock)
        {
            if (_bufferSize < copyBytes || _backBuffer == IntPtr.Zero) return;

            void* dst = (void*)_backBuffer;

            if (_analogMode && NesCore.AnalogScreenBuf != null)
            {
                Buffer.MemoryCopy(NesCore.AnalogScreenBuf, dst, copyBytes, copyBytes);
            }
            else if (_pipelineActive)
            {
                if (!_pipeline.IsInitialized && NesCore.ScreenBuf1x != null)
                    _pipeline.Init(NesCore.ScreenBuf1x);

                if (_pipeline.IsInitialized)
                {
                    _pipeline.Process();
                    Buffer.MemoryCopy(_pipeline.OutputPtr, dst, copyBytes, copyBytes);
                }
                else
                {
                    int baseBytes = 256 * 240 * 4;
                    Buffer.MemoryCopy(NesCore.ScreenBuf1x, dst, baseBytes, baseBytes);
                }
            }
            else
            {
                Buffer.MemoryCopy(NesCore.ScreenBuf1x, dst, copyBytes, copyBytes);
            }

            // Lock-free atomic swap: front ↔ back
            _backBuffer = Interlocked.Exchange(ref _frontBuffer, _backBuffer);
        }

        Interlocked.Increment(ref _frameCounter);

        // FPS limiting (mirrors original AprNes VideoOutputDeal)
        if (NesCore.LimitFPS)
        {
            if (!_fpsStopWatch.IsRunning) _fpsStopWatch.Restart();
            double now = _fpsStopWatch.Elapsed.TotalSeconds;
            if (_fpsDeadline < now)
                _fpsDeadline = now + NES_FRAME_SECONDS;
            while (_fpsDeadline - _fpsStopWatch.Elapsed.TotalSeconds > 0.001)
                Thread.Sleep(1);
            while (_fpsStopWatch.Elapsed.TotalSeconds < _fpsDeadline) { }
            _fpsDeadline += NES_FRAME_SECONDS;
        }

        // Signal UI thread via Avalonia dispatcher
        if (Interlocked.Exchange(ref _pendingFrame, 1) == 0)
            Avalonia.Threading.Dispatcher.UIThread.Post(FireFrameReady, Avalonia.Threading.DispatcherPriority.Render);
    }

    private void FireFrameReady()
    {
        Interlocked.Exchange(ref _pendingFrame, 0);
        FrameReady?.Invoke();
    }

    public void Dispose()
    {
        Stop();
        SaveSRam();
        _pipeline.Dispose();
        _gamepadRunning = false;
        _gamepadThread?.Join(500);
        _gamepadThread = null;
        _gamepad.Shutdown();
        NesCore.VideoOutput -= OnVideoOutput;

        lock (_resizeLock)
        {
            if (_bufferA != IntPtr.Zero) Marshal.FreeHGlobal(_bufferA);
            if (_bufferB != IntPtr.Zero) Marshal.FreeHGlobal(_bufferB);
            _bufferA = _bufferB = _backBuffer = _frontBuffer = IntPtr.Zero;
            _bufferSize = 0;
        }
    }
}
