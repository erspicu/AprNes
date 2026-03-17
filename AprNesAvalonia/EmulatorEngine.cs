using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Avalonia.Input;
using AprNes;

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
    private string  _romFileName = "";

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

    // ── Public API ─────────────────────────────────────────────────────────
    public bool IsRunning   => _running;
    public bool IsRomLoaded => _romLoaded;

    /// <summary>Apply keyboard mapping from INI (Windows VK codes).</summary>
    public void ApplyKeyMap(int vkA, int vkB, int vkSelect, int vkStart,
                            int vkUp, int vkDown, int vkLeft, int vkRight)
    {
        _keymap.Clear();
        void Map(int vk, byte btn) { if (_vkMap.TryGetValue(vk, out var k)) _keymap[k] = btn; }
        Map(vkA,      0); Map(vkB,     1); Map(vkSelect, 2); Map(vkStart, 3);
        Map(vkUp,     4); Map(vkDown,  5); Map(vkLeft,   6); Map(vkRight, 7);
    }

    /// <summary>Apply audio settings: set NesCore flags and open/close WaveOut.</summary>
    public void ApplyAudioSettings(bool enabled, int volume)
    {
        NesCore.AudioEnabled = enabled;
        NesCore.Volume       = volume;
        WaveOutPlayer.CloseAudio();
        if (enabled && _running)
            WaveOutPlayer.OpenAudio();
    }

    /// <summary>Load and initialise a ROM. Returns true on success.</summary>
    public bool LoadRom(string path)
    {
        Stop();
        NesCore.AccuracyOptA = true;

        byte[] rom;
        try   { rom = File.ReadAllBytes(path); }
        catch { return false; }

        if (!NesCore.init(rom)) return false;
        _romLoaded   = true;
        _romBytes    = rom;
        _romFileName = path;
        NesCore.rom_file_name = Path.GetFileNameWithoutExtension(path);

        NesCore.VideoOutput -= OnVideoOutput;
        NesCore.VideoOutput += OnVideoOutput;
        return true;
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
            WaveOutPlayer.OpenAudio();
    }

    /// <summary>Stop the emulator loop and wait for thread to exit.</summary>
    public void Stop()
    {
        WaveOutPlayer.CloseAudio();
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
        NesCore.VideoOutput -= OnVideoOutput;
    }
}
