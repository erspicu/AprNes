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
    private readonly WriteableBitmap _writeableBitmap = new WriteableBitmap(
        new Avalonia.PixelSize(256, 240),
        new Avalonia.Vector(96, 96),
        PixelFormats.Bgra8888,
        AlphaFormat.Unpremul);

    // FPS display timer
    private readonly DispatcherTimer _fpsTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();

        // Load INI settings
        string iniPath = Path.Combine(AppContext.BaseDirectory, "AprNes.ini");
        _ini = new IniFile(iniPath);
        ApplyIniSettings();

        // Set canvas source once — WriteableBitmap auto-invalidates on Lock/Unlock
        GameCanvas.Source = _writeableBitmap;

        // Wire frame-ready event
        _emu.FrameReady += OnFrameReady;

        // FPS display
        _fpsTimer.Tick += (_, _) => FpsLabel.Text = _emu.TakeFrameCount().ToString();
        _fpsTimer.Start();

        // Keyboard input
        KeyDown += OnKeyDown;
        KeyUp   += OnKeyUp;

        Closing += (_, _) => { _emu.Dispose(); _writeableBitmap.Dispose(); };
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

        _emu.ApplyAudioSettings(
            _ini.GetBool("Sound",  false),
            _ini.GetInt ("Volume", 80));

        ApplyLanguage(_ini.Get("Lang", "zh-tw"));
    }

    private void ApplyLanguage(string lang)
    {
        if (!LangHelper.Loaded) return;
        BtnOpen.Content   = LangHelper.Get(lang, "rom",     "開啟遊戲");
        BtnReset.Content  = LangHelper.Get(lang, "reset",   "重置");
        BtnConfig.Content = LangHelper.Get(lang, "setting", "功能設定");
        RomInfoLabel.Text = LangHelper.Get(lang, "rominfo", "ROM資訊");
        AboutLabel.Text   = LangHelper.Get(lang, "about",   "關於");
    }

    // ── Frame rendering ────────────────────────────────────────────────────
    private unsafe void OnFrameReady()
    {
        var src = _emu.FrameBuffer;
        int size = src.Length;
        fixed (byte* p = src)
        using (var fb = _writeableBitmap.Lock())
            Buffer.MemoryCopy(p, fb.Address.ToPointer(), size, size);
        GameCanvas.InvalidateVisual();
    }

    // ── Keyboard ───────────────────────────────────────────────────────────
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Shift && e.Key == Key.P)
        {
            CaptureScreen();
            e.Handled = true;
            return;
        }
        _emu.KeyDown(e.Key);
    }
    private void OnKeyUp(object? sender, KeyEventArgs e) { _emu.KeyUp(e.Key); }

    // ── Screenshot (Shift+P) ───────────────────────────────────────────────
    private volatile bool _capturing = false;
    private async void CaptureScreen()
    {
        if (!_emu.IsRunning) return;
        if (_capturing) return;
        _capturing = true;

        _emu.Pause();

        string message;
        try
        {
            string dir = _ini.Get("CaptureScreenPath", Path.Combine(AppContext.BaseDirectory, "Screenshot"));
            Directory.CreateDirectory(dir);

            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
            string filePath = Path.Combine(dir, $"Screen-{stamp}.png");

            var src = _emu.FrameBuffer;
            unsafe
            {
                fixed (byte* p = src)
                {
                    var bmp = new Bitmap(
                        PixelFormats.Bgra8888, AlphaFormat.Unpremul,
                        (nint)p,
                        new Avalonia.PixelSize(256, 240),
                        new Avalonia.Vector(96, 96),
                        256 * 4);
                    using var fs = File.Create(filePath);
                    bmp.Save(fs);
                    bmp.Dispose();
                }
            }
            message = filePath + " save!";
        }
        catch (Exception ex)
        {
            message = "截圖失敗: " + ex.Message;
        }

        _emu.Resume();
        _capturing = false;

        // Show message box (mirrors original MessageBox.Show)
        await ShowMessageBox(message);
    }

    private async System.Threading.Tasks.Task ShowMessageBox(string message)
    {
        var win = new Window
        {
            Title = "AprNes",
            Width = 420, SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Button    { Content = "確定", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                                    Padding = new Avalonia.Thickness(20, 4) }
                }
            }
        };
        // Wire OK button to close
        var btn = ((StackPanel)win.Content).Children[1] as Button;
        btn!.Click += (_, _) => win.Close();
        await win.ShowDialog(this);
    }

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
            // keep label as-is on failure
            return;
        }
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

        var dlg = new ConfigWindow(_ini);
        await dlg.ShowDialog(this);

        ApplyIniSettings();
        if (wasPaused) _emu.Resume();
    }

    private async void RomInfoLabel_Click(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        var dlg = new RomInfoWindow();
        dlg.SetInfo(_emu.GetRomInfo());
        await dlg.ShowDialog(this);
    }

    private async void AboutLabel_Click(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        await new AboutWindow().ShowDialog(this);
    }
}
