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

    // ── Video ──────────────────────────────────────────────────────────────
    // Pre-allocated 256×240 BGRA8888 frame buffer (written on emulator thread, read on UI thread)
    private readonly byte[] _frameBuffer = new byte[256 * 240 * 4];
    private int _pendingFrame;          // interlocked flag: 1 = new frame available
    private int _frameCounter;          // raw frame count since last FPS query

    /// <summary>Fired on the UI thread when a new frame is ready in FrameBuffer.</summary>
    public event Action? FrameReady;

    /// <summary>Latest rendered frame (Bgra8888, 256×240). Access only after FrameReady fires.</summary>
    public ReadOnlySpan<byte> FrameBuffer => _frameBuffer;

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
        // Running on the emulator thread. Copy ScreenBuf1x → managed buffer.
        // NesCore.ScreenBuf1x is uint* ARGB (0xFFRRGGBB).
        // In little-endian memory each uint is stored BB GG RR FF = Bgra8888. ✓
        fixed (byte* dst = _frameBuffer)
            Buffer.MemoryCopy(NesCore.ScreenBuf1x, dst, _frameBuffer.Length, _frameBuffer.Length);

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
        _gamepadRunning = false;
        _gamepadThread?.Join(500);
        _gamepadThread = null;
        _gamepad.Shutdown();
        NesCore.VideoOutput -= OnVideoOutput;
    }
}
