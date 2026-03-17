using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AprNesAvalonia.Views;

namespace AprNesAvalonia;

public partial class MainWindow : Window
{
    private readonly EmulatorEngine _emu = new();
    private readonly IniFile _ini;
    private Bitmap? _lastFrame;

    // FPS display timer
    private readonly DispatcherTimer _fpsTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();

        // Load INI settings
        string iniPath = Path.Combine(AppContext.BaseDirectory, "AprNes.ini");
        _ini = new IniFile(iniPath);
        ApplyIniSettings();

        // Wire frame-ready event
        _emu.FrameReady += OnFrameReady;

        // FPS display
        _fpsTimer.Tick += (_, _) => FpsLabel.Text = _emu.TakeFrameCount().ToString();
        _fpsTimer.Start();

        // Keyboard input
        KeyDown += OnKeyDown;
        KeyUp   += OnKeyUp;

        Closing += (_, _) => _emu.Dispose();
    }

    // ── Settings ───────────────────────────────────────────────────────────
    private void ApplyIniSettings()
    {
        _emu.ApplyKeyMap(
            _ini.GetInt("key_A",      90),
            _ini.GetInt("key_B",      88),
            _ini.GetInt("key_SELECT", 83),
            _ini.GetInt("key_START",  65),
            _ini.GetInt("key_UP",     38),
            _ini.GetInt("key_DOWN",   40),
            _ini.GetInt("key_LEFT",   37),
            _ini.GetInt("key_RIGHT",  39));

        AprNes.NesCore.AccuracyOptA = _ini.GetBool("AccuracyOptA", true);
        AprNes.NesCore.LimitFPS     = _ini.GetBool("LimitFPS",    false);
    }

    // ── Frame rendering ────────────────────────────────────────────────────
    private unsafe void OnFrameReady()
    {
        var src = _emu.FrameBuffer;
        fixed (byte* p = src)
        {
            var newFrame = new Bitmap(
                PixelFormats.Bgra8888,
                AlphaFormat.Unpremul,
                (nint)p,
                new Avalonia.PixelSize(256, 240),
                new Avalonia.Vector(96, 96),
                256 * 4);
            var old = _lastFrame;
            _lastFrame = newFrame;
            GameCanvas.Source = newFrame;
            old?.Dispose();
        }
    }

    // ── Keyboard ───────────────────────────────────────────────────────────
    private new void OnKeyDown(object? sender, KeyEventArgs e) { _emu.KeyDown(e.Key); }
    private new void OnKeyUp  (object? sender, KeyEventArgs e) { _emu.KeyUp(e.Key); }

    // ── Toolbar buttons ────────────────────────────────────────────────────
    private async void BtnOpen_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "開啟遊戲",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("NES ROM") { Patterns = ["*.nes"] },
                              new FilePickerFileType("All Files") { Patterns = ["*"] }]
        });
        if (files.Count == 0) return;

        string path = files[0].TryGetLocalPath() ?? string.Empty;
        if (string.IsNullOrEmpty(path)) return;

        _emu.Stop();
        if (!_emu.LoadRom(path))
        {
            RomInfoLabel.Text = "ROM 載入失敗";
            return;
        }
        RomInfoLabel.Text = Path.GetFileName(path);
        _emu.Start();
    }

    private void BtnReset_Click(object? sender, RoutedEventArgs e)
    {
        if (!_emu.IsRomLoaded) return;
        AprNes.NesCore.SoftReset();
        if (!_emu.IsRunning) _emu.Start();
    }

    private async void BtnConfig_Click(object? sender, RoutedEventArgs e)
    {
        bool wasPaused = _emu.IsRunning;
        if (wasPaused) _emu.Pause();

        var dlg = new ConfigWindow();
        await dlg.ShowDialog(this);

        ApplyIniSettings();
        if (wasPaused) _emu.Resume();
    }

    private async void RomInfoLabel_Click(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        var dlg = new RomInfoWindow();
        if (AprNes.NesCore.rom_file_name != "")
        {
            string info = $"File : {AprNes.NesCore.rom_file_name}\r\n" +
                          $"Mapper : {AprNes.NesCore.RomMapper}\r\n" +
                          $"PRG-ROM : {AprNes.NesCore.RomPrgCount} × 16KB\r\n" +
                          $"CHR-ROM : {AprNes.NesCore.RomChrCount} × 8KB\r\n" +
                          $"Mirror : {(AprNes.NesCore.RomHorizMirror ? "Horizontal" : "Vertical")}";
            dlg.SetInfo(info);
        }
        await dlg.ShowDialog(this);
    }

    private async void AboutLabel_Click(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        await new AboutWindow().ShowDialog(this);
    }
}
