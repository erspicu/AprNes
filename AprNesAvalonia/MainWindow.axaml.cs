using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
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
        new PixelSize(256, 240),
        new Vector(96, 96),
        PixelFormats.Bgra8888,
        AlphaFormat.Unpremul);

    private readonly DispatcherTimer _fpsTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    // State tracking for toggle menus
    private bool _soundEnabled;
    private bool _ultraAnalogEnabled;
    private bool _limitFPS;
    private bool _accuracyOptA = true;
    private bool _isFullscreen;
    private string _currentRegion = "NTSC";

    // Recent ROMs
    private readonly List<string> _recentROMs = new();
    private const int MaxRecentROMs = 10;

    public MainWindow()
    {
        InitializeComponent();

        // Load INI settings
        string iniPath = Path.Combine(AppContext.BaseDirectory, "configure", "AprNes.ini");
        _ini = new IniFile(iniPath);
        ApplyIniSettings();

        // Canvas
        GameCanvas.Source = _writeableBitmap;

        // Frame-ready
        _emu.FrameReady += OnFrameReady;

        // FPS display
        _fpsTimer.Tick += (_, _) => StatusFps.Text = _emu.TakeFrameCount().ToString();
        _fpsTimer.Start();

        // Keyboard input
        KeyDown += OnKeyDown;
        KeyUp   += OnKeyUp;

        // Drag & drop
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);

        Closing += (_, _) =>
        {
            _emu.Dispose();        // Stop + SaveSRam + cleanup
            _writeableBitmap.Dispose();
        };

        InitRecentROMs();
        UpdateMenuStates();
    }

    // ═══ Settings ═══════════════════════════════════════════════════════════

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

        _accuracyOptA = _ini.GetBool("AccuracyOptA", true);
        _limitFPS     = _ini.GetBool("LimitFPS",     false);
        _soundEnabled = _ini.GetBool("Sound",        false);
        _currentRegion = _ini.Get("Region", "NTSC");

        AprNes.NesCore.AccuracyOptA = _accuracyOptA;
        AprNes.NesCore.LimitFPS     = _limitFPS;
        ApplyRegionToNesCore(_currentRegion);

        _emu.ApplyAudioSettings(_soundEnabled, _ini.GetInt("Volume", 80));

        ApplyLanguage(_ini.Get("Lang", LangHelper.CurrentLang));
        UpdateMenuStates();
    }

    private string L(string key, string def) => LangHelper.Get(LangHelper.CurrentLang, key, def);

    private void ApplyLanguage(string lang)
    {
        LangHelper.CurrentLang = lang;
        if (!LangHelper.Loaded) return;

        // Menu bar headers
        MenuFile.Header      = L("menu_file",      "_File");
        MenuEmulation.Header = L("menu_emulation", "_Emulation");
        MenuView.Header      = L("menu_view",      "_View");
        MenuTools.Header     = L("menu_tools",     "_Tools");
        MenuHelp.Header      = L("menu_help",      "_Help");

        // File menu
        MenuOpenRom.Header    = L("menu_open_rom", "Open ROM");
        MenuRecentRoms.Header = L("menu_recent",   "Recent ROMs");
        MenuExit.Header       = L("menu_exit",     "Exit");

        // Emulation menu
        MenuSoftReset.Header = L("menu_soft_reset", "Soft Reset");
        MenuHardReset.Header = L("menu_hard_reset", "Hard Reset");
        MenuRegion.Header    = L("menu_region",     "Region");

        // Tools menu
        MenuScreenshot.Header     = L("menu_screenshot",      "Screenshot");
        MenuRomInfo.Header        = L("menu_rom_info",        "ROM Info");
        MenuRecord.Header         = L("menu_record",          "Record");
        MenuRecordVideo.Header    = L("menu_record_video",    "Record Video");
        MenuRecordAudio.Header    = L("menu_record_audio",    "Record Audio");
        MenuRecordSettings.Header = L("menu_record_settings", "Record Settings");
        MenuConfiguration.Header  = L("menu_configuration",   "Configuration");

        // View menu
        MenuFullscreen.Header = L("menu_fullscreen", "Fullscreen");

        // Help menu
        MenuShortcuts.Header = L("menu_shortcuts", "Keyboard Shortcuts");
        MenuAbout.Header     = L("menu_about",     "About");

        // Context menu
        CtxOpenRom.Header    = L("menu_open_rom",      "Open ROM");
        CtxSoftReset.Header  = L("menu_soft_reset",    "Soft Reset");
        CtxHardReset.Header  = L("menu_hard_reset",    "Hard Reset");
        CtxConfig.Header     = L("menu_configuration", "Configuration");
        CtxRomInfo.Header    = L("menu_rom_info",      "ROM Info");
        CtxFullscreen.Header = L("menu_fullscreen",    "Fullscreen");
        CtxExit.Header       = L("menu_exit",          "Exit");

        // Status bar
        if (!_emu.IsRomLoaded)
            StatusRomName.Text = L("status_no_rom", "No ROM loaded");
        StatusFpsLabel.Text = L("status_fps", "FPS:") + " ";
    }

    private void UpdateMenuStates()
    {
        string limitText = L("menu_limit_fps", "Limit FPS");
        string optaText  = L("menu_accuracy_opta", "AccuracyOptA");
        MenuLimitFPS.Header     = _limitFPS     ? "✓ " + limitText : "  " + limitText;
        MenuAccuracyOptA.Header = _accuracyOptA ? "✓ " + optaText  : "  " + optaText;

        string soundText = _soundEnabled ? L("menu_sound_on", "Sound: ON") : L("menu_sound_off", "Sound: OFF");
        MenuSound.Header = soundText;
        CtxSound.Header  = soundText;

        string analogText = _ultraAnalogEnabled
            ? L("menu_ultra_analog_on", "Ultra Analog: ON")
            : L("menu_ultra_analog_off", "Ultra Analog: OFF");
        MenuUltraAnalog.Header = analogText;
        CtxUltraAnalog.Header  = analogText;

        // Region radio — mark current
        MenuRegionNTSC.Header  = _currentRegion == "NTSC"  ? "● NTSC"  : "  NTSC";
        MenuRegionPAL.Header   = _currentRegion == "PAL"   ? "● PAL"   : "  PAL";
        MenuRegionDendy.Header = _currentRegion == "Dendy" ? "● Dendy" : "  Dendy";

        StatusRegion.Text = _currentRegion;
    }

    // ═══ Frame rendering ════════════════════════════════════════════════════

    private unsafe void OnFrameReady()
    {
        var src = _emu.FrameBuffer;
        int size = src.Length;
        fixed (byte* p = src)
        using (var fb = _writeableBitmap.Lock())
            Buffer.MemoryCopy(p, fb.Address.ToPointer(), size, size);
        GameCanvas.InvalidateVisual();
    }

    // ═══ Keyboard ═══════════════════════════════════════════════════════════

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Shift+P = screenshot
        if (e.KeyModifiers == KeyModifiers.Shift && e.Key == Key.P)
        {
            CaptureScreen();
            e.Handled = true;
            return;
        }
        // F11 = fullscreen
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
        // Escape = exit fullscreen
        if (e.Key == Key.Escape && _isFullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
        _emu.KeyDown(e.Key);
    }

    private void OnKeyUp(object? sender, KeyEventArgs e) => _emu.KeyUp(e.Key);

    // ═══ Drag & Drop ════════════════════════════════════════════════════════

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.Data.GetFiles();
        if (files == null) return;

        foreach (var item in files)
        {
            var path = item.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) continue;
            if (!path.EndsWith(".nes", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".fds", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;

            LoadAndStartRom(path);
            break;
        }
    }

    // ═══ ROM loading helper ═════════════════════════════════════════════════

    private void LoadAndStartRom(string path)
    {
        _emu.Stop();
        if (!_emu.LoadRom(path)) return;

        StatusRomName.Text = Path.GetFileName(path);
        _emu.Start();
        AddRecentROM(path);
    }

    // ═══ Fullscreen ═════════════════════════════════════════════════════════

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        if (_isFullscreen)
        {
            WindowState = WindowState.FullScreen;
            // Scale canvas to fill
            GameCanvas.Width  = double.NaN;
            GameCanvas.Height = double.NaN;
            GameCanvas.Stretch = Avalonia.Media.Stretch.Uniform;
            GameBorder.Margin = new Thickness(0);
        }
        else
        {
            WindowState = WindowState.Normal;
            GameCanvas.Width  = 256;
            GameCanvas.Height = 240;
            GameCanvas.Stretch = Avalonia.Media.Stretch.Fill;
            GameBorder.Margin = new Thickness(0);
        }
    }

    // ═══ Screenshot ═════════════════════════════════════════════════════════

    private volatile bool _capturing;

    private async void CaptureScreen()
    {
        if (!_emu.IsRunning || _capturing) return;
        _capturing = true;
        _emu.Pause();

        string message;
        try
        {
            string dir = _ini.Get("CaptureScreenPath",
                         Path.Combine(AppContext.BaseDirectory, "Screenshot"));
            Directory.CreateDirectory(dir);

            string stamp    = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
            string filePath = Path.Combine(dir, $"Screen-{stamp}.png");

            var src = _emu.FrameBuffer;
            unsafe
            {
                fixed (byte* p = src)
                {
                    var bmp = new Bitmap(
                        PixelFormats.Bgra8888, AlphaFormat.Unpremul,
                        (nint)p,
                        new PixelSize(256, 240),
                        new Vector(96, 96),
                        256 * 4);
                    using var fs = File.Create(filePath);
                    bmp.Save(fs);
                    bmp.Dispose();
                }
            }
            message = filePath + " " + L("dlg_screenshot_saved", "save!");
        }
        catch (Exception ex) { message = L("dlg_screenshot_failed", "Screenshot failed:") + " " + ex.Message; }

        _emu.Resume();
        _capturing = false;
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
                Margin = new Thickness(16), Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Button    { Content = "OK",
                                   HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                                   Padding = new Thickness(20, 4) }
                }
            }
        };
        ((win.Content as StackPanel)!.Children[1] as Button)!.Click += (_, _) => win.Close();
        await win.ShowDialog(this);
    }

    // ═══ Menu: File ═════════════════════════════════════════════════════════

    private async void MenuOpenRom_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L("dlg_open_rom", "Open ROM"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(L("dlg_nes_rom", "NES ROM")) { Patterns = ["*.nes", "*.fds", "*.zip"] },
                              new FilePickerFileType(L("dlg_all_files", "All Files")) { Patterns = ["*"] }]
        });
        if (files.Count == 0) return;

        string path = files[0].TryGetLocalPath() ?? string.Empty;
        if (string.IsNullOrEmpty(path)) return;

        LoadAndStartRom(path);
    }

    private void MenuExit_Click(object? sender, RoutedEventArgs e) => Close();

    // ═══ Menu: Emulation ════════════════════════════════════════════════════

    private void MenuSoftReset_Click(object? sender, RoutedEventArgs e)
    {
        if (!_emu.IsRomLoaded) return;
        AprNes.NesCore.SoftReset();
        if (!_emu.IsRunning) _emu.Start();
    }

    private void MenuHardReset_Click(object? sender, RoutedEventArgs e)
    {
        if (!_emu.IsRomLoaded) return;
        if (!_emu.HardReset()) return;
        _emu.Start();
    }

    private void MenuRegion_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not string region) return;
        if (_currentRegion == region) return;

        _currentRegion = region;
        ApplyRegionToNesCore(region);
        _ini.Set("Region", region);
        _ini.Save();
        UpdateMenuStates();

        // Region change requires hard reset (like AprNes WinForms)
        if (_emu.IsRunning)
        {
            _emu.HardReset();
            _emu.Start();
        }
    }

    private static void ApplyRegionToNesCore(string region)
    {
        AprNes.NesCore.Region = region switch
        {
            "PAL"   => AprNes.NesCore.RegionType.PAL,
            "Dendy" => AprNes.NesCore.RegionType.Dendy,
            _       => AprNes.NesCore.RegionType.NTSC,
        };
    }

    private void MenuLimitFPS_Click(object? sender, RoutedEventArgs e)
    {
        _limitFPS = !_limitFPS;
        AprNes.NesCore.LimitFPS = _limitFPS;
        _ini.Set("LimitFPS", _limitFPS ? "1" : "0");
        _ini.Save();
        UpdateMenuStates();
    }

    private void MenuAccuracyOptA_Click(object? sender, RoutedEventArgs e)
    {
        _accuracyOptA = !_accuracyOptA;
        AprNes.NesCore.AccuracyOptA = _accuracyOptA;
        _ini.Set("AccuracyOptA", _accuracyOptA ? "1" : "0");
        _ini.Save();
        UpdateMenuStates();
    }

    // ═══ Menu: View ═════════════════════════════════════════════════════════

    private void MenuFullscreen_Click(object? sender, RoutedEventArgs e) => ToggleFullscreen();

    private void MenuSound_Click(object? sender, RoutedEventArgs e)
    {
        _soundEnabled = !_soundEnabled;
        _emu.ApplyAudioSettings(_soundEnabled, _ini.GetInt("Volume", 80));
        _ini.Set("Sound", _soundEnabled ? "1" : "0");
        _ini.Save();
        UpdateMenuStates();
    }

    private void MenuUltraAnalog_Click(object? sender, RoutedEventArgs e)
    {
        _ultraAnalogEnabled = !_ultraAnalogEnabled;
        // TODO: apply Ultra Analog to NesCore
        UpdateMenuStates();
    }

    // ═══ Menu: Tools ════════════════════════════════════════════════════════

    private void MenuScreenshot_Click(object? sender, RoutedEventArgs e) => CaptureScreen();

    private async void MenuRomInfo_Click(object? sender, RoutedEventArgs e)
    {
        var dlg = new RomInfoWindow();
        dlg.SetInfo(_emu.GetRomInfo());
        await dlg.ShowDialog(this);
    }

    private void MenuRecordVideo_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: toggle video recording
    }

    private void MenuRecordAudio_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: toggle audio recording
    }

    private void MenuRecordSettings_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: open record settings dialog
    }

    private async void MenuConfiguration_Click(object? sender, RoutedEventArgs e)
    {
        bool wasRunning = _emu.IsRunning;
        if (wasRunning) _emu.Pause();

        var dlg = new ConfigWindow(_ini);
        await dlg.ShowDialog(this);

        ApplyIniSettings();
        if (wasRunning) _emu.Resume();
    }

    // ═══ Menu: Help ═════════════════════════════════════════════════════════

    private async void MenuShortcuts_Click(object? sender, RoutedEventArgs e)
    {
        string shortcuts = L("shortcuts_text",
            "Ctrl+O          Open ROM\nCtrl+R          Soft Reset\nCtrl+Shift+P    Screenshot\nF11             Fullscreen\nEscape          Exit Fullscreen\nCtrl+W          Exit")
            .Replace("\\n", "\n");
        await ShowMessageBox(shortcuts);
    }

    private async void MenuAbout_Click(object? sender, RoutedEventArgs e)
    {
        await new AboutWindow().ShowDialog(this);
    }

    // ═══ Recent ROMs ═════════════════════════════════════════════════════════

    private void InitRecentROMs()
    {
        string raw = _ini.Get("RecentROMs", "");
        if (!string.IsNullOrEmpty(raw))
        {
            foreach (string p in raw.Split('|'))
                if (!string.IsNullOrEmpty(p))
                    _recentROMs.Add(p);
        }
        BuildRecentROMsMenu();
    }

    private void BuildRecentROMsMenu()
    {
        MenuRecentRoms.Items.Clear();

        if (_recentROMs.Count == 0)
        {
            var empty = new MenuItem { Header = L("menu_recent_empty", "(empty)"), IsEnabled = false };
            MenuRecentRoms.Items.Add(empty);
            return;
        }

        foreach (string path in _recentROMs)
        {
            var item = new MenuItem { Header = Path.GetFileName(path), Tag = path };
            // Tooltip via attached property not needed; Tag carries full path
            item.Click += RecentROM_Click;
            MenuRecentRoms.Items.Add(item);
        }
    }

    private void AddRecentROM(string fullPath)
    {
        _recentROMs.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
        _recentROMs.Insert(0, fullPath);
        if (_recentROMs.Count > MaxRecentROMs)
            _recentROMs.RemoveRange(MaxRecentROMs, _recentROMs.Count - MaxRecentROMs);

        BuildRecentROMsMenu();
        _ini.Set("RecentROMs", string.Join("|", _recentROMs));
        _ini.Save();
    }

    private void RemoveRecentROM(string fullPath)
    {
        _recentROMs.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
        BuildRecentROMsMenu();
        _ini.Set("RecentROMs", string.Join("|", _recentROMs));
        _ini.Save();
    }

    private async void RecentROM_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not string path) return;
        if (!File.Exists(path))
        {
            await ShowMessageBox(L("file_not_found", "File not found, removed from list."));
            RemoveRecentROM(path);
            return;
        }
        LoadAndStartRom(path);
    }
}
